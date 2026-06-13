using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Logic;
using Logic.Definition.Town;
using Logic.Farm;
using Logic.Town;
using Logic.Town.Items;
using UnityEngine;

namespace FarmTogether2.AutoSellMod
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new ManualLogSource Log = null!;

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<float> CheckIntervalSeconds = null!;
        internal static ConfigEntry<float> TriggerRatio = null!;
        internal static ConfigEntry<string> ExcludedResources = null!;
        internal static ConfigEntry<bool> SellOneTradeWhenFull = null!;
        internal static ConfigEntry<bool> ShowSellPopup = null!;
        internal static ConfigEntry<float> SellPopupSeconds = null!;
        internal static ConfigEntry<bool> DebugLog = null!;

        private static string _excludedResourcesRaw = "";
        private static HashSet<FarmResourceType>? _excludedResourcesCache;

        public override void Load()
        {
            Log = base.Log;

            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch for automatic resource selling.");
            CheckIntervalSeconds = Config.Bind("General", "CheckIntervalSeconds", 5.0f,
                "How often to scan inventory and town shops, in real-time seconds.");
            TriggerRatio = Config.Bind("Sell", "TriggerRatio", 0.90f,
                "Sell resource excess when Amount / MaxValue is at or above this ratio. Clamped to 0.01..0.999.");
            ExcludedResources = Config.Bind("Sell", "ExcludedResources", "Event,EventB,GoldNugget",
                "Comma/semicolon/space separated FarmResourceType names that should never be auto-sold.");
            SellOneTradeWhenFull = Config.Bind("Sell", "SellOneTradeWhenFull", true,
                "If storage is full but the excess above TriggerRatio is smaller than one shop trade, sell one trade anyway.");
            ShowSellPopup = Config.Bind("UI", "ShowSellPopup", true,
                "Briefly show an on-screen message when AutoSell sells resources.");
            SellPopupSeconds = Config.Bind("UI", "SellPopupSeconds", 3.0f,
                "How long the AutoSell message stays visible, in real-time seconds.");
            DebugLog = Config.Bind("Debug", "DebugLog", false,
                "Log each automatic sell attempt and skipped invalid excluded resource name.");

            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} ({MyPluginInfo.PLUGIN_GUID}) is loaded.");
            AddComponent<AutoSellBehaviour>();
        }

        internal static float ScanInterval
        {
            get
            {
                float value = CheckIntervalSeconds.Value;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 1.0f)
                    value = 1.0f;
                return value;
            }
        }

        internal static float NormalizedTriggerRatio
        {
            get
            {
                float value = TriggerRatio.Value;
                if (float.IsNaN(value) || float.IsInfinity(value))
                    value = 0.90f;
                return Mathf.Clamp(value, 0.01f, 0.999f);
            }
        }

        internal static float PopupSeconds
        {
            get
            {
                float value = SellPopupSeconds.Value;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.5f)
                    value = 0.5f;
                return Mathf.Min(value, 10.0f);
            }
        }

        internal static HashSet<FarmResourceType> GetExcludedResources()
        {
            string raw = ExcludedResources.Value ?? "";
            if (_excludedResourcesCache != null && raw == _excludedResourcesRaw)
                return _excludedResourcesCache;

            var excluded = new HashSet<FarmResourceType>();
            string[] parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string name = part.Trim();
                if (Enum.TryParse(name, ignoreCase: true, out FarmResourceType resource))
                {
                    excluded.Add(resource);
                }
                else if (DebugLog.Value)
                {
                    Log.LogWarning($"[autosell] Ignoring unknown FarmResourceType in ExcludedResources: '{name}'");
                }
            }

            _excludedResourcesRaw = raw;
            _excludedResourcesCache = excluded;
            return excluded;
        }

        internal static bool IsResourceExcluded(FarmResourceType resource)
        {
            return GetExcludedResources().Contains(resource);
        }

        internal static void Debug(string message)
        {
            if (DebugLog.Value)
                Log.LogInfo(message);
        }
    }

    public sealed class AutoSellBehaviour : MonoBehaviour
    {
        private float _nextCheck;
        private float _lastWarnAt = -999f;
        private float _popupUntil;
        private string _popupText = "";
        private readonly bool[] _offeredByOpenShop = new bool[(int)FarmResourceType.Count];
        private readonly bool[] _soldThisScan = new bool[(int)FarmResourceType.Count];

        public AutoSellBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextCheck)
                return;

            _nextCheck = now + Plugin.ScanInterval;

            if (!Plugin.Enabled.Value)
                return;

            try
            {
                ScanAndSell();
            }
            catch (Exception e)
            {
                WarnThrottled($"[autosell] Scan failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private void ScanAndSell()
        {
            if (!StageScript.HasInstance)
                return;

            StageScript stage = StageScript.Instance;
            if (stage == null || !stage.IsLoaded || !stage.HasLocalPlayer)
                return;

            LocalPlayer player = stage.LocalPlayer;
            FarmData farm = FarmData.CurrentFarm;
            if (player == null || farm == null || farm.TownData == null)
                return;

            var slots = farm.TownData.Slots;
            if (slots == null)
                return;

            ClearScanFlags();

            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                TownSlot slot = slots[slotIndex];
                if (slot == null || slot.IsEmpty || slot.Contents == null)
                    continue;

                TownShopInstance shop = slot.Contents.TryCast<TownShopInstance>();
                if (shop == null)
                    continue;

                try
                {
                    TrySellFromShop(farm, player, shop);
                }
                catch (Exception e)
                {
                    WarnThrottled($"[autosell] Shop scan failed: {e.GetType().Name}: {e.Message}");
                }
            }

            LogResourcesWithoutShop(farm);
        }

        private void TrySellFromShop(FarmData farm, LocalPlayer player, TownShopInstance shop)
        {
            TownShopDefinition definition = shop.Definition;
            if (definition == null || definition.ShopResources == null)
                return;

            FailedAction failReason;
            if (!farm.IsTownShopOpen(player, definition, out failReason))
                return;

            float triggerRatio = Plugin.NormalizedTriggerRatio;

            for (int resourceSlotIndex = 0; resourceSlotIndex < definition.ShopResources.Count; resourceSlotIndex++)
            {
                var shopResource = definition.ShopResources[resourceSlotIndex];
                if (shopResource == null)
                    continue;

                FarmResource tradeResource = shopResource.Resource;
                FarmResourceType resourceType = tradeResource.Type;
                MarkOfferedByOpenShop(resourceType);

                FarmResourceStorage storage = farm.GetResource(resourceType);
                if (storage == null)
                    continue;

                long maxValue = storage.MaxValue;
                long currentAmount = storage.Amount;
                if (maxValue <= 0 || currentAmount <= 0)
                    continue;

                double currentRatio = currentAmount / (double)maxValue;
                if (currentRatio < triggerRatio)
                    continue;

                if (Plugin.IsResourceExcluded(resourceType))
                {
                    Plugin.Debug($"[autosell] {resourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but is excluded by config.");
                    continue;
                }

                long amountPerInteraction = tradeResource.Amount;
                if (amountPerInteraction <= 0)
                {
                    Plugin.Debug($"[autosell] {resourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but shop trade amount is {amountPerInteraction}.");
                    continue;
                }

                long targetAmount = (long)Math.Floor(maxValue * (double)triggerRatio);
                long excessAmount = currentAmount - targetAmount;
                if (excessAmount < amountPerInteraction)
                {
                    if (!Plugin.SellOneTradeWhenFull.Value || currentAmount < maxValue)
                    {
                        Plugin.Debug($"[autosell] {resourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but excess {excessAmount} is less than one trade ({amountPerInteraction}).");
                        continue;
                    }

                    Plugin.Debug($"[autosell] {resourceType} is full at {currentAmount}/{maxValue}; selling one trade even though excess {excessAmount} is less than one trade ({amountPerInteraction}).");
                }

                long possibleInteractions = excessAmount / amountPerInteraction;
                if (possibleInteractions == 0 && Plugin.SellOneTradeWhenFull.Value && currentAmount >= maxValue)
                    possibleInteractions = 1;
                uint interactionCount = ToInteractionCount(possibleInteractions);
                if (interactionCount == 0)
                    continue;

                uint remainingUses = shop.GetRemainingUses(shopResource.GoodIndex);
                if (remainingUses == 0)
                {
                    Plugin.Debug($"[autosell] {resourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but shop good index {shopResource.GoodIndex} has 0 remaining uses.");
                    continue;
                }

                if (interactionCount > remainingUses)
                    interactionCount = remainingUses;
                if (interactionCount == 0)
                    continue;

                FarmMoney earnedMoney = shop.GetSellMoney_Resource(resourceSlotIndex) * (float)interactionCount;

                shop.SellResources(player, resourceSlotIndex, interactionCount);

                long soldAmount = amountPerInteraction * interactionCount;
                string message = $"AutoSell: sold {soldAmount} {resourceType} ({interactionCount} trade(s), {FormatMoneyPlain(earnedMoney)})";
                MarkSold(resourceType);
                ShowPopup(message);
                Plugin.Debug($"[autosell] Sold {soldAmount} {resourceType} with {interactionCount} interaction(s), earned {FormatMoneyPlain(earnedMoney)}. {currentAmount}/{maxValue} -> target {targetAmount}.");
            }
        }

        private void ClearScanFlags()
        {
            for (int i = 0; i < _offeredByOpenShop.Length; i++)
            {
                _offeredByOpenShop[i] = false;
                _soldThisScan[i] = false;
            }
        }

        private void MarkOfferedByOpenShop(FarmResourceType resource)
        {
            int index = (int)resource;
            if (index >= 0 && index < _offeredByOpenShop.Length)
                _offeredByOpenShop[index] = true;
        }

        private void MarkSold(FarmResourceType resource)
        {
            int index = (int)resource;
            if (index >= 0 && index < _soldThisScan.Length)
                _soldThisScan[index] = true;
        }

        private void LogResourcesWithoutShop(FarmData farm)
        {
            if (!Plugin.DebugLog.Value)
                return;

            float triggerRatio = Plugin.NormalizedTriggerRatio;
            for (int i = 0; i < (int)FarmResourceType.Count; i++)
            {
                var resource = (FarmResourceType)i;
                if (Plugin.IsResourceExcluded(resource))
                    continue;
                if (_offeredByOpenShop[i] || _soldThisScan[i])
                    continue;

                FarmResourceStorage storage = farm.GetResource(resource);
                if (storage == null || storage.MaxValue <= 0)
                    continue;

                double currentRatio = storage.Amount / (double)storage.MaxValue;
                if (currentRatio >= triggerRatio)
                {
                    Plugin.Debug($"[autosell] {resource} is {storage.Amount}/{storage.MaxValue} ({currentRatio:P0}) but no open placed town shop offers this resource.");
                }
            }
        }

        private static string FormatMoneyPlain(FarmMoney money)
        {
            var parts = new List<string>();
            if (money.Coins != 0)
                parts.Add($"{money.Coins} Coins");
            if (money.Bills != 0)
                parts.Add($"{money.Bills} Bills");
            if (money.Medals != 0)
                parts.Add($"{money.Medals} Medals");

            return parts.Count == 0 ? "+0" : "+" + string.Join(", ", parts);
        }

        private static uint ToInteractionCount(long value)
        {
            if (value <= 0)
                return 0;
            if (value >= uint.MaxValue)
                return uint.MaxValue;
            return (uint)value;
        }

        private void WarnThrottled(string message)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastWarnAt < 10.0f)
                return;

            _lastWarnAt = now;
            Plugin.Log.LogWarning(message);
        }

        private void ShowPopup(string message)
        {
            Plugin.Log.LogInfo($"[autosell] {message}");

            if (!Plugin.ShowSellPopup.Value)
                return;

            _popupText = message;
            _popupUntil = Time.realtimeSinceStartup + Plugin.PopupSeconds;
        }

        private void OnGUI()
        {
            if (!Plugin.ShowSellPopup.Value)
                return;
            if (Time.realtimeSinceStartup > _popupUntil)
                return;
            if (string.IsNullOrEmpty(_popupText))
                return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(22, 62, 980, 40), _popupText, style);
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20, 60, 980, 40), _popupText, style);
        }
    }
}
