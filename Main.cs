using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LethalLib.Modules;
using UnityEngine.SceneManagement;
using BepInEx.Configuration;
using Unity.Netcode;
using System.Reflection;
using System;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;
using Unity.Collections;
using GameNetcodeStuff;

namespace BuyableHauntedMasks
{
    [BepInDependency("evaisa.lethallib", "0.13.2")]
    [BepInPlugin(modGUID, modName, modVersion)]
    public class BuyableHauntedMasks : BaseUnityPlugin
    {
        private const string modGUID = "MegaPiggy.BuyableHauntedMasks";
        private const string modName = "Buyable Haunted Masks";
        private const string modVersion = "1.2.0";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableHauntedMasks Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public static List<Item> AllItems => Resources.FindObjectsOfTypeAll<Item>().Concat(UnityEngine.Object.FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)).ToList();
        public static Item Comedy => AllItems.FirstOrDefault(item => item.name.Equals("ComedyMask"));
        public static Item Tragedy => AllItems.FirstOrDefault(item => item.name.Equals("TragedyMask"));
        public static Item ComedyClone { get; private set; }
        public static GameObject ComedyObjectClone { get; private set; }
        public static Item TragedyClone { get; private set; }
        public static GameObject TragedyObjectClone { get; private set; }


        private static ConfigEntry<int> ComedyMaskPriceConfig;
        public static int ComedyMaskPriceLocal => ComedyMaskPriceConfig.Value;
        internal static int ComedyMaskPriceRemote = -1;
        public static int ComedyMaskPrice => ComedyMaskPriceRemote > -1 ? ComedyMaskPriceRemote : ComedyMaskPriceLocal;

        private static ConfigEntry<int> TragedyMaskPriceConfig;
        public static int TragedyMaskPriceLocal => TragedyMaskPriceConfig.Value;
        internal static int TragedyMaskPriceRemote = -1;
        public static int TragedyMaskPrice => TragedyMaskPriceRemote > -1 ? TragedyMaskPriceRemote : TragedyMaskPriceLocal;
        private static bool IsHost => NetworkManager.Singleton.IsHost;
        private static ulong LocalClientId => NetworkManager.Singleton.LocalClientId;

        private void Awake()
        {
            if (Instance == null)
            {
                DontDestroyOnLoad(this);
                Instance = this;
            }
            harmony.PatchAll();
            ComedyMaskPriceConfig = Config.Bind("Prices", "ComedyMaskPrice", 30, "Credits needed to buy comedy mask");
            TragedyMaskPriceConfig = Config.Bind("Prices", "TragedyMaskPrice", 30, "Credits needed to buy tragedy mask");
            SceneManager.sceneLoaded += OnSceneLoaded;
            ComedyClone = MakeNonScrap(ComedyMaskPrice, "C");
            TragedyClone = MakeNonScrap(TragedyMaskPrice, "T");
            AddComedyToShop();
            AddTragedyToShop();
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        private static Item MakeNonScrap(int price, string name = "")
        {
            Item nonScrap = ScriptableObject.CreateInstance<Item>();
            DontDestroyOnLoad(nonScrap);
            nonScrap.name = "Error";
            nonScrap.itemName = "Error";
            nonScrap.itemId = 6624;
            nonScrap.isScrap = false;
            nonScrap.creditsWorth = price;
            nonScrap.canBeGrabbedBeforeGameStart = true;
            nonScrap.automaticallySetUsingPower = false;
            nonScrap.batteryUsage = 300;
            nonScrap.canBeInspected = false;
            nonScrap.isDefensiveWeapon = true;
            nonScrap.saveItemVariable = true;
            nonScrap.syncGrabFunction = false;
            nonScrap.twoHandedAnimation = true;
            nonScrap.verticalOffset = 0.25f;
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("Cube" + name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(prefab.transform, false);
            cube.GetComponent<MeshRenderer>().sharedMaterial.shader = Shader.Find("HDRP/Lit");
            prefab.AddComponent<BoxCollider>().size = Vector3.one * 2;
            prefab.AddComponent<AudioSource>();
            var prop = prefab.AddComponent<PhysicsProp>();
            prop.itemProperties = nonScrap;
            prop.grabbable = true;
            nonScrap.spawnPrefab = prefab;
            prefab.tag = "PhysicsProp";
            prefab.layer = LayerMask.NameToLayer("Props");
            cube.layer = LayerMask.NameToLayer("Props");
            try
            {
                GameObject scanNode = GameObject.Instantiate<GameObject>(Items.scanNodePrefab, prefab.transform);
                scanNode.name = "ScanNode";
                scanNode.transform.localPosition = new Vector3(0f, 0f, 0f);
                scanNode.transform.localScale *= 2;
                ScanNodeProperties properties = scanNode.GetComponent<ScanNodeProperties>();
                properties.nodeType = 1;
                properties.headerText = "Error";
                properties.subText = $"A mod is incompatible with {modName}";
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e.ToString());
            }
            prefab.transform.localScale = Vector3.one / 2;
            return nonScrap;
        }

        private static GameObject CloneNonScrap(Item original, Item clone, int price)
        {
            GameObject.Destroy(clone.spawnPrefab);
            var prefab = NetworkPrefabs.CloneNetworkPrefab(original.spawnPrefab);
            prefab.AddComponent<Unflagger>();
            DontDestroyOnLoad(prefab);
            CopyFields(original, clone);
            prefab.GetComponent<GrabbableObject>().itemProperties = clone;
            clone.spawnPrefab = prefab;
            clone.name = "Buyable" + original.name;
            clone.creditsWorth = price;
            return prefab;
        }

        public static void CopyFields(Item source, Item destination)
        {
            FieldInfo[] fields = typeof(Item).GetFields();
            foreach (FieldInfo field in fields)
            {
                field.SetValue(destination, field.GetValue(source));
            }
        }

        private static Dictionary<string, TerminalNode> infoNodes = new Dictionary<string, TerminalNode>();

        private static TerminalNode CreateInfoNode(string name, string description)
        {
            if (infoNodes.ContainsKey(name)) return infoNodes[name];
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            node.clearPreviousText = true;
            node.name = name + "InfoNode";
            node.displayText = description + "\n\n";
            infoNodes.Add(name, node);
            return node;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CloneComedy();
            CloneTragedy();
        }

        private static void CloneComedy()
        {
            if (Comedy == null) return;
            if (ComedyObjectClone != null) return;
            ComedyObjectClone = CloneNonScrap(Comedy, ComedyClone, ComedyMaskPrice);
        }

        private static void AddComedyToShop()
        {
            Items.RegisterShopItem(ComedyClone, price: ComedyMaskPrice, itemInfo: CreateInfoNode("ComedyMask", "Haunted mask. It has a 65% chance to turn you into zombie every 5 seconds."));
            LoggerInstance.LogInfo($"Comedy Mask added to Shop for {ComedyMaskPrice} credits");
        }

        private static void UpdateComedyPrice()
        {
            ComedyClone.creditsWorth = ComedyMaskPrice;
            Items.UpdateShopItemPrice(ComedyClone, price: ComedyMaskPrice);
            LoggerInstance.LogInfo($"Comedy Mask price updated to {ComedyMaskPrice} credits");
        }

        private static void CloneTragedy()
        {
            if (Tragedy == null) return;
            if (TragedyObjectClone != null) return;
            TragedyObjectClone = CloneNonScrap(Tragedy, TragedyClone, TragedyMaskPrice);
        }

        private static void AddTragedyToShop()
        {
            Items.RegisterShopItem(TragedyClone, price: TragedyMaskPrice, itemInfo: CreateInfoNode("TragedyMask", "Haunted mask. It has a 65% chance to turn you into zombie every 5 seconds. You cannot take this off once you put it on."));
            LoggerInstance.LogInfo($"Tragedy Mask added to Shop for {TragedyMaskPrice} credits");
        }

        private static void UpdateTragedyPrice()
        {
            TragedyClone.creditsWorth = TragedyMaskPrice;
            Items.UpdateShopItemPrice(TragedyClone, price: TragedyMaskPrice);
            LoggerInstance.LogInfo($"Tragedy Mask price updated to {TragedyMaskPrice} credits");
        }

        public static byte CurrentVersionByte = 1;

        public static void WriteData(FastBufferWriter writer)
        {
            writer.WriteByte(CurrentVersionByte);
            writer.WriteBytes(BitConverter.GetBytes(ComedyMaskPriceLocal));
            writer.WriteBytes(BitConverter.GetBytes(TragedyMaskPriceLocal));
        }

        public static void ReadData(FastBufferReader reader)
        {
            reader.ReadByte(out byte version);
            if (version == CurrentVersionByte)
            {
                var cPriceBytes = new byte[4];
                reader.ReadBytes(ref cPriceBytes, 4);
                ComedyMaskPriceRemote = BitConverter.ToInt32(cPriceBytes, 0);
                UpdateComedyPrice();
                var tPriceBytes = new byte[4];
                reader.ReadBytes(ref tPriceBytes, 4);
                TragedyMaskPriceRemote = BitConverter.ToInt32(tPriceBytes, 0);
                UpdateTragedyPrice();
                LoggerInstance.LogInfo("Host config set successfully");
                return;
            }
            throw new Exception("Invalid version byte");
        }

        public static void OnRequestSync(ulong clientID, FastBufferReader reader)
        {
            if (IsHost)
            {
                LoggerInstance.LogInfo("Sending config to client " + clientID.ToString());
                FastBufferWriter writer = new FastBufferWriter(5, Allocator.Temp, 5);
                try
                {
                    WriteData(writer);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("BuyableHauntedMasks_OnReceiveConfigSync", clientID, writer, NetworkDelivery.Reliable);
                }
                catch (Exception ex)
                {
                    LoggerInstance.LogError($"Failed to send config: {ex}");
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        public static void OnReceiveSync(ulong clientID, FastBufferReader reader)
        {
            LoggerInstance.LogInfo("Received config from host");
            try
            {
                ReadData(reader);
            }
            catch (Exception ex)
            {
                LoggerInstance.LogError($"Failed to receive config: {ex}");
                ComedyMaskPriceRemote = -1;
                TragedyMaskPriceRemote = -1;
            }
        }

        /// <summary>
        /// For what ever reason the hide flags were set to HideAndDontSave, which caused it to not save obviously.
        /// I'm not sure what sets and I don't want to bother finding out when a fix like this is so easy.
        /// </summary>
        internal class Unflagger : MonoBehaviour
        {
            public void Awake()
            {
                gameObject.hideFlags = HideFlags.None;
            }
        }

        [HarmonyPatch]
        internal static class Patches
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
            public static void ServerConnect()
            {
                if (IsHost)
                {
                    LoggerInstance.LogInfo("Started hosting, using local settings");
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("BuyableHauntedMasks_OnRequestConfigSync", OnRequestSync);
                }
                else
                {
                    LoggerInstance.LogInfo("Connected to server, requesting settings");
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("BuyableHauntedMasks_OnReceiveConfigSync", OnReceiveSync);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("BuyableHauntedMasks_OnRequestConfigSync", 0, new FastBufferWriter(0, Allocator.Temp), NetworkDelivery.Reliable);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
            public static void ServerDisconnect()
            {
                LoggerInstance.LogInfo("Server disconnect");
                ComedyMaskPriceRemote = -1;
                TragedyMaskPriceRemote = -1;
            }
        }
    }
}