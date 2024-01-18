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

namespace BuyableHauntedMasks
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class BuyableHauntedMasks : BaseUnityPlugin
    {
        private const string modGUID = "MegaPiggy.BuyableHauntedMasks";
        private const string modName = "Buyable Haunted Masks";
        private const string modVersion = "1.0.0";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableHauntedMasks Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public List<Item> AllItems => Resources.FindObjectsOfTypeAll<Item>().Concat(UnityEngine.Object.FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)).ToList();
        public Item Comedy => AllItems.FirstOrDefault(item => item.name.Contains("Comedy"));
        public Item Tragedy => AllItems.FirstOrDefault(item => item.name.Contains("Tragedy"));
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
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        private Item MakeNonScrap(Item original, int price)
        {
            Item clone = Object.Instantiate<Item>(original);
            clone.name = "Buyable" + original.name;
            clone.isScrap = false;
            clone.creditsWorth = price;
            return clone;
        }

        private TerminalNode CreateInfoNode(string name, string description)
        {
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            node.clearPreviousText = true;
            node.name = name + "InfoNode";
            node.displayText = description + "\n\n";
            return node;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AddComedy();
            AddTragedy();
        }

        private void AddComedy()
        {
            if (Comedy == null) return;
            if (ComedyClone == null) ComedyClone = MakeNonScrap(Comedy, ComedyMaskPrice);
            Items.RegisterShopItem(ComedyClone, price: ComedyMaskPrice, itemInfo: CreateInfoNode("ComedyMask", "Haunted mask. It has a 65% chance to turn you into zombie every 5 seconds."));
            LoggerInstance.LogInfo($"Comedy Mask added to Shop for {ComedyMaskPrice} credits");
        }

        private void AddTragedy()
        {
            if (Tragedy == null) return;
            if (TragedyClone == null) TragedyClone = MakeNonScrap(Tragedy, TragedyMaskPrice);
            Items.RegisterShopItem(TragedyClone, price: TragedyMaskPrice, itemInfo: CreateInfoNode("TragedyMask", "Haunted mask. It has a 65% chance to turn you into zombie every 5 seconds. You cannot take this off once you put it on."));
            LoggerInstance.LogInfo($"Tragedy Mask added to Shop for {TragedyMaskPrice} credits");
        }
    }
}