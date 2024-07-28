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
        private const string modVersion = "1.3.0";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableHauntedMasks Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public static StartOfRound StartOfRound => StartOfRound.Instance;
        public static List<Item> AllItems => StartOfRound.allItemsList.itemsList.ToList();
        public static Item Comedy => AllItems.FirstOrDefault(item => item.name.Equals("ComedyMask") && item.spawnPrefab != null);
        public static Item Tragedy => AllItems.FirstOrDefault(item => item.name.Equals("TragedyMask") && item.spawnPrefab != null);
        public static ClonedItem ComedyClone { get; private set; }
        public static ClonedItem TragedyClone { get; private set; }


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
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        public class ClonedItem : Item
        {
            public Item original;
        }

        private static ClonedItem CloneNonScrap(Item original, int price)
        {
            ClonedItem clone = ScriptableObject.CreateInstance<ClonedItem>();
            DontDestroyOnLoad(clone);
            clone.original = original;
            var prefab = NetworkPrefabs.CloneNetworkPrefab(original.spawnPrefab, "Buyable" + original.name);
            prefab.AddComponent<Unflagger>();
            DontDestroyOnLoad(prefab);
            CopyFields(original, clone);
            prefab.GetComponent<GrabbableObject>().itemProperties = clone;
            clone.spawnPrefab = prefab;
            clone.name = "Buyable" + original.name;
            clone.creditsWorth = price;
            clone.isScrap = false;
            return clone;
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
            if (StartOfRound == null) return;
            if (AllItems == null) return;
            if (Comedy == null) return;
            if (ComedyClone != null) return;
            ComedyClone = CloneNonScrap(Comedy, ComedyMaskPrice);
            AddComedyToShop();
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
            if (StartOfRound == null) return;
            if (AllItems == null) return;
            if (Tragedy == null) return;
            if (TragedyClone != null) return;
            TragedyClone = CloneNonScrap(Tragedy, TragedyMaskPrice);
            AddTragedyToShop();
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
                    UpdateComedyPrice();
                    UpdateTragedyPrice();
                }
                else
                {
                    LoggerInstance.LogInfo("Connected to server, requesting settings");
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("BuyableHauntedMasks_OnReceiveConfigSync", OnReceiveSync);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("BuyableHauntedMasks_OnRequestConfigSync", 0, new FastBufferWriter(0, Allocator.Temp), NetworkDelivery.Reliable);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(StartOfRound), "Awake")]
            public static void Awake()
            {
                LoggerInstance.LogWarning("Start of round awake");
                CloneComedy();
                CloneTragedy();
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