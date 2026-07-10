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
        internal static ConfigEntry<int> ConfigSchemaVersion = null!;
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
            TriggerRatio = Config.Bind("Sell", "TriggerRatio", 0.80f,
                "Sell resource excess when Amount / MaxValue is at or above this ratio. Clamped to 0.01..0.999.");
            ExcludedResources = Config.Bind("Sell", "ExcludedResources", "GoldNugget",
                "Comma/semicolon/space separated FarmResourceType names that should never be auto-sold. Event and EventB are allowed by default for the Event Shack.");
            ConfigSchemaVersion = Config.Bind("Migration", "ConfigSchemaVersion", 0,
                "Internal config migration version. Do not edit manually.");
            MigrateLegacyExcludedResources();
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
                    value = 0.80f;
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

        private void MigrateLegacyExcludedResources()
        {
            ExclusionMigrationDecision decision = AutoSellPolicy.DecideExclusionMigration(
                ExcludedResources.Value,
                ConfigSchemaVersion.Value);
            bool configChanged = false;

            if (decision.ExcludedResourcesChanged)
            {
                ExcludedResources.Value = decision.ExcludedResources;
                _excludedResourcesRaw = "";
                _excludedResourcesCache = null;
                configChanged = true;
                Log.LogInfo("[autosell] Migrated legacy ExcludedResources default so Event Shack resources can be sold.");
            }

            if (ConfigSchemaVersion.Value != decision.MigrationVersion)
            {
                ConfigSchemaVersion.Value = decision.MigrationVersion;
                configChanged = true;
            }

            if (configChanged)
                Config.Save();
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
        private sealed class SellCandidate
        {
            internal readonly TownShopInstance Shop;
            internal readonly int ResourceSlotIndex;
            internal readonly FarmResourceType ResourceType;
            internal readonly long AmountPerInteraction;
            internal readonly int GoodIndex;
            internal readonly FarmMoney MoneyPerInteraction;
            internal readonly int CurrencyPriority;
            internal readonly int DiscoveryOrder;

            internal SellCandidate(
                TownShopInstance shop,
                int resourceSlotIndex,
                FarmResourceType resourceType,
                long amountPerInteraction,
                int goodIndex,
                FarmMoney moneyPerInteraction,
                int currencyPriority,
                int discoveryOrder)
            {
                Shop = shop;
                ResourceSlotIndex = resourceSlotIndex;
                ResourceType = resourceType;
                AmountPerInteraction = amountPerInteraction;
                GoodIndex = goodIndex;
                MoneyPerInteraction = moneyPerInteraction;
                CurrencyPriority = currencyPriority;
                DiscoveryOrder = discoveryOrder;
            }
        }

        private float _nextCheck;
        private float _lastWarnAt = -999f;
        private float _popupUntil;
        // Set once if even a basic IMGUI label throws (e.g. "Method unstripping failed" after a game
        // update) so we stop re-throwing every frame. The sell logic does not depend on this.
        private static bool _guiUnsupported;
        // One-time probe of which IMGUI primitives Il2CppInterop could actually unstrip on this build,
        // so we render the popup with only the parts that work (the 2026-06-15 update broke GUI.set_color).
        private static bool _guiProbed;
        private static bool _canColor;     // GUI.color get/set works
        private static bool _canTexture;   // GUI.DrawTexture works
        private static bool _canStyle;     // new GUIStyle(GUI.skin.label) works
        private string _popupDetail = "";
        private string _popupSubtext = "";
        private readonly bool[] _offeredByOpenShop = new bool[(int)FarmResourceType.Count];
        private readonly bool[] _soldThisScan = new bool[(int)FarmResourceType.Count];
        private readonly List<SellCandidate> _sellCandidates = new List<SellCandidate>();

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
            _sellCandidates.Clear();
            int discoveryOrder = 0;

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
                    CollectCandidatesFromShop(farm, player, shop, ref discoveryOrder);
                }
                catch (Exception e)
                {
                    WarnThrottled($"[autosell] Shop scan failed: {e.GetType().Name}: {e.Message}");
                }
            }

            _sellCandidates.Sort(CompareSellCandidates);

            for (int i = 0; i < _sellCandidates.Count; i++)
            {
                try
                {
                    TrySellCandidate(farm, player, _sellCandidates[i]);
                }
                catch (Exception e)
                {
                    WarnThrottled($"[autosell] Candidate sale failed: {e.GetType().Name}: {e.Message}");
                }
            }

            LogResourcesWithoutShop(farm);
        }

        private void CollectCandidatesFromShop(
            FarmData farm,
            LocalPlayer player,
            TownShopInstance shop,
            ref int discoveryOrder)
        {
            TownShopDefinition definition = shop.Definition;
            if (definition == null || definition.ShopResources == null)
                return;

            FailedAction failReason;
            if (!farm.IsTownShopOpen(player, definition, out failReason))
                return;

            for (int resourceSlotIndex = 0;
                 resourceSlotIndex < definition.ShopResources.Count;
                 resourceSlotIndex++)
            {
                var shopResource = definition.ShopResources[resourceSlotIndex];
                if (shopResource == null)
                    continue;

                FarmResource tradeResource = shopResource.Resource;
                FarmResourceType resourceType = tradeResource.Type;
                MarkOfferedByOpenShop(resourceType);

                FarmMoney money = shop.GetSellMoney_Resource(resourceSlotIndex);
                int priority = AutoSellPolicy.GetCurrencyPriority(
                    money.Coins,
                    money.Bills,
                    money.Medals);

                _sellCandidates.Add(new SellCandidate(
                    shop,
                    resourceSlotIndex,
                    resourceType,
                    tradeResource.Amount,
                    shopResource.GoodIndex,
                    money,
                    priority,
                    discoveryOrder++));
            }
        }

        private static int CompareSellCandidates(SellCandidate left, SellCandidate right)
        {
            return AutoSellPolicy.CompareOffers(
                left.CurrencyPriority,
                left.DiscoveryOrder,
                right.CurrencyPriority,
                right.DiscoveryOrder);
        }

        private void TrySellCandidate(
            FarmData farm,
            LocalPlayer player,
            SellCandidate candidate)
        {
            FarmResourceStorage storage = farm.GetResource(candidate.ResourceType);
            if (storage == null)
                return;

            long maxValue = storage.MaxValue;
            long currentAmount = storage.Amount;
            if (maxValue <= 0 || currentAmount <= 0)
                return;

            float triggerRatio = Plugin.NormalizedTriggerRatio;
            double currentRatio = currentAmount / (double)maxValue;
            if (currentRatio < triggerRatio)
                return;

            if (Plugin.IsResourceExcluded(candidate.ResourceType))
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but is excluded by config.");
                return;
            }

            long amountPerInteraction = candidate.AmountPerInteraction;
            if (amountPerInteraction <= 0)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but shop trade amount is {amountPerInteraction}.");
                return;
            }

            long targetAmount = (long)Math.Floor(maxValue * (double)triggerRatio);
            long excessAmount = currentAmount - targetAmount;
            bool forceOneTrade = excessAmount < amountPerInteraction
                && Plugin.SellOneTradeWhenFull.Value
                && currentAmount >= maxValue;

            if (excessAmount < amountPerInteraction && !forceOneTrade)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but excess {excessAmount} is less than one trade ({amountPerInteraction}).");
                return;
            }

            if (forceOneTrade)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is full at {currentAmount}/{maxValue}; selling one trade even though excess {excessAmount} is less than one trade ({amountPerInteraction}).");
            }

            uint remainingUses = candidate.Shop.GetRemainingUses(candidate.GoodIndex);
            if (remainingUses == 0)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but shop good index {candidate.GoodIndex} has 0 remaining uses.");
                return;
            }

            uint interactionCount = AutoSellPolicy.CalculateInteractionCount(
                currentAmount,
                maxValue,
                triggerRatio,
                amountPerInteraction,
                remainingUses,
                Plugin.SellOneTradeWhenFull.Value);
            if (interactionCount == 0)
                return;

            FarmMoney earnedMoney = candidate.MoneyPerInteraction * (float)interactionCount;
            candidate.Shop.SellResources(player, candidate.ResourceSlotIndex, interactionCount);

            long soldAmount = amountPerInteraction * interactionCount;
            long projectedAmount = Math.Max(0, currentAmount - soldAmount);
            string logMessage = $"Sold {soldAmount} {candidate.ResourceType} with {interactionCount} interaction(s), earned {FormatMoneyPlain(earnedMoney)}. {currentAmount}/{maxValue} -> {projectedAmount}/{maxValue}.";
            string popupDetail = $"{candidate.ResourceType}: -{soldAmount}  {FormatMoneyPlain(earnedMoney)}";
            string popupSubtext = $"{interactionCount} trade(s) x {amountPerInteraction} | Storage {currentAmount}->{projectedAmount}/{maxValue} | Trigger {FormatRatio(triggerRatio)}";
            MarkSold(candidate.ResourceType);
            ShowPopup(logMessage, popupDetail, popupSubtext);
            Plugin.Debug($"[autosell] {logMessage} Target {targetAmount}.");
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

        private static string FormatRatio(float ratio)
        {
            return $"{Mathf.RoundToInt(ratio * 100.0f)}%";
        }

        private void WarnThrottled(string message)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastWarnAt < 10.0f)
                return;

            _lastWarnAt = now;
            Plugin.Log.LogWarning(message);
        }

        private void ShowPopup(string logMessage, string detail, string subtext)
        {
            Plugin.Log.LogInfo($"[autosell] {logMessage}");

            if (!Plugin.ShowSellPopup.Value)
                return;

            _popupDetail = detail;
            _popupSubtext = subtext;
            _popupUntil = Time.realtimeSinceStartup + Plugin.PopupSeconds;
        }

        private void OnGUI()
        {
            if (!Plugin.ShowSellPopup.Value)
                return;
            if (Time.realtimeSinceStartup > _popupUntil)
                return;
            if (string.IsNullOrEmpty(_popupDetail))
                return;
            if (_guiUnsupported)
                return;

            if (!_guiProbed)
                ProbeGuiCapabilities();

            try
            {
                float screenWidth = Screen.width;
                float panelWidth = Mathf.Min(760.0f, screenWidth - 32.0f);
                if (panelWidth < 320.0f)
                    panelWidth = Mathf.Max(240.0f, screenWidth - 16.0f);

                const float panelHeight = 104.0f;
                float x = Mathf.Max(8.0f, (screenWidth - panelWidth) * 0.5f);
                var panelRect = new Rect(x, 72.0f, panelWidth, panelHeight);

                // Decorative box — only if BOTH the texture draw and the colour setter survived unstripping.
                if (_canTexture && _canColor)
                {
                    Color originalColor = GUI.color;
                    try
                    {
                        GUI.color = new Color(0.02f, 0.02f, 0.02f, 0.78f);
                        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
                        GUI.color = new Color(1.0f, 0.72f, 0.18f, 0.96f);
                        GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, 6.0f, panelRect.height), Texture2D.whiteTexture);
                    }
                    finally
                    {
                        GUI.color = originalColor;
                    }
                }

                float textX = panelRect.x + 20.0f;
                float textWidth = panelRect.width - 32.0f;

                // Text is the actual notification. Styled labels if GUIStyle works; otherwise plain labels
                // (default skin) so the message still shows on a build where styling can't be unstripped.
                if (_canStyle)
                {
                    var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                    titleStyle.normal.textColor = new Color(1.0f, 0.82f, 0.36f, 1.0f);
                    var detailStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = true };
                    detailStyle.normal.textColor = Color.white;
                    var subtextStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperLeft, wordWrap = true };
                    subtextStyle.normal.textColor = new Color(0.86f, 0.90f, 0.94f, 1.0f);
                    // No background box is possible (GUI.DrawTexture can't be unstripped on this build), so
                    // give every line a near-black OUTLINE — draws the text in black around an 8-point ring,
                    // then the coloured text on top — so it stays readable over the busy farm background.
                    DrawOutlinedLabel(new Rect(textX, panelRect.y + 8.0f, textWidth, 22.0f), "AUTO SELL MOD", titleStyle);
                    DrawOutlinedLabel(new Rect(textX, panelRect.y + 32.0f, textWidth, 32.0f), _popupDetail, detailStyle);
                    DrawOutlinedLabel(new Rect(textX, panelRect.y + 68.0f, textWidth, 30.0f), _popupSubtext, subtextStyle);
                }
                else
                {
                    GUI.Label(new Rect(textX, panelRect.y + 8.0f, textWidth, 22.0f), "AUTO SELL MOD");
                    GUI.Label(new Rect(textX, panelRect.y + 32.0f, textWidth, 40.0f), _popupDetail);
                    GUI.Label(new Rect(textX, panelRect.y + 74.0f, textWidth, 30.0f), _popupSubtext);
                }
            }
            catch (Exception e)
            {
                // Even basic labels failed to unstrip → disable the popup for this session (no per-frame
                // spam) instead of throwing thousands of times. The auto-sell logic is unaffected.
                _guiUnsupported = true;
                Plugin.Log.LogWarning($"[autosell] Sell popup disabled this session: IMGUI unavailable after game update " +
                                      $"({e.GetType().Name}: {e.Message}). Set ShowSellPopup=false to silence permanently.");
            }
        }

        // Text outline offsets (8-point ring) — fake a readable backdrop without GUI.DrawTexture.
        private static readonly Vector2[] _outlineOffsets =
        {
            new Vector2(-1.6f, -1.6f), new Vector2(0f, -1.8f), new Vector2(1.6f, -1.6f),
            new Vector2(-1.8f, 0f),                            new Vector2(1.8f, 0f),
            new Vector2(-1.6f, 1.6f),  new Vector2(0f, 1.8f),  new Vector2(1.6f, 1.6f),
        };

        // Draw `text` with a near-black outline so it reads over any background. Uses only GUI.Label +
        // GUIStyle.textColor (both unstrip fine on this build); no textures.
        private static void DrawOutlinedLabel(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return;
            Color textColor = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.92f);
            for (int i = 0; i < _outlineOffsets.Length; i++)
                GUI.Label(new Rect(r.x + _outlineOffsets[i].x, r.y + _outlineOffsets[i].y, r.width, r.height), text, style);
            style.normal.textColor = textColor;
            GUI.Label(r, text, style);
        }

        // Probe ONCE which IMGUI primitives Il2CppInterop managed to unstrip on this build. Each call is
        // isolated so one failure (e.g. GUI.set_color after the 2026-06-15 update) doesn't mask the others.
        // Must run inside OnGUI (these are GUI calls); the off-screen DrawTexture probe is harmless.
        private static void ProbeGuiCapabilities()
        {
            _guiProbed = true;
            try { Color c = GUI.color; GUI.color = c; _canColor = true; } catch { _canColor = false; }
            try { GUI.DrawTexture(new Rect(-100f, -100f, 1f, 1f), Texture2D.whiteTexture); _canTexture = true; } catch { _canTexture = false; }
            try { var _ = new GUIStyle(GUI.skin.label); _canStyle = true; } catch { _canStyle = false; }
            Plugin.Log.LogInfo($"[autosell] popup IMGUI capabilities: color={_canColor} texture={_canTexture} style={_canStyle}");
        }
    }
}
