using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LethalLib.Modules;
using UnityEngine.SceneManagement;
using BepInEx.Configuration;
using Dissonance;
using System;
using System.Reflection;

namespace BuyableHauntedMasks
{
    [BepInDependency("evaisa.lethallib", "0.13.2")]
    [BepInPlugin(modGUID, modName, modVersion)]
    public class BuyableHauntedMasks : BaseUnityPlugin
    {
        private const string modGUID = "MegaPiggy.BuyableHauntedMasks";
        private const string modName = "Buyable Haunted Masks";
        private const string modVersion = "1.0.3";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableHauntedMasks Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public List<Item> AllItems => Resources.FindObjectsOfTypeAll<Item>().Concat(UnityEngine.Object.FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)).ToList();
        public Item Comedy => AllItems.FirstOrDefault(item => item.name.Equals("ComedyMask"));
        public Item Tragedy => AllItems.FirstOrDefault(item => item.name.Equals("TragedyMask"));
        public Item ComedyClone { get; private set; }
        public Item TragedyClone { get; private set; }


        private ConfigEntry<int> ComedyMaskPriceConfig;
        public int ComedyMaskPrice => ComedyMaskPriceConfig.Value;

        private ConfigEntry<int> TragedyMaskPriceConfig;
        public int TragedyMaskPrice => TragedyMaskPriceConfig.Value;

        private void Awake()
        {
            if (Instance == null) Instance = this;
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

        private Item MakeNonScrap(int price, string name = "")
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

        private void CloneNonScrap(Item original, Item clone, int price)
        {
            DontDestroyOnLoad(original.spawnPrefab);
            CopyFields(original, clone);
            clone.name = "Buyable" + original.name;
            clone.creditsWorth = price;
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

        private TerminalNode CreateInfoNode(string name, string description)
        {
            if (infoNodes.ContainsKey(name)) return infoNodes[name];
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            node.clearPreviousText = true;
            node.name = name + "InfoNode";
            node.displayText = description + "\n\n";
            infoNodes.Add(name, node);
            return node;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CloneComedy();
            CloneTragedy();
        }

        private void CloneComedy()
        {
            if (Comedy == null) return;
            CloneNonScrap(Comedy, ComedyClone, ComedyMaskPrice);
        }

        private void AddComedyToShop()
        {
            Items.RegisterShopItem(ComedyClone, price: ComedyMaskPrice, itemInfo: CreateInfoNode("ComedyMask", "Haunted mask. It has a 65% chance to turn you into zombie every 5 seconds."));
            LoggerInstance.LogInfo($"Comedy Mask added to Shop for {ComedyMaskPrice} credits");
        }

        private void CloneTragedy()
        {
            if (Tragedy == null) return;
            CloneNonScrap(Tragedy, TragedyClone, TragedyMaskPrice);
        }

        private void AddTragedyToShop()
        {
            Items.RegisterShopItem(TragedyClone, price: TragedyMaskPrice, itemInfo: CreateInfoNode("TragedyMask", "Haunted mask. It has a 65% chance to turn you into zombie every 5 seconds. You cannot take this off once you put it on."));
            LoggerInstance.LogInfo($"Tragedy Mask added to Shop for {TragedyMaskPrice} credits");
        }
    }
}