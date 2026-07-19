using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
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
        private static readonly AutoSellRuntimeLease<Plugin, AutoSellBehaviour> RuntimeLease =
            new AutoSellRuntimeLease<Plugin, AutoSellBehaviour>();

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
        private AutoSellBehaviour? _behaviour;
        private bool _ownsRuntimeLease;

        public override void Load()
        {
            if (!RuntimeLease.TryAcquire(this))
            {
                throw new InvalidOperationException(
                    "FarmTogether2.AutoSellMod already owns the process-wide runtime lease.");
            }

            _ownsRuntimeLease = true;
            Log = base.Log;

            try
            {
                RuntimeCompatibilityResult compatibility = RuntimeCompatibility.VerifyCurrentGame(
                    Paths.GameRootPath,
                    Paths.GameDataPath);
                if (!compatibility.IsCompatible)
                {
                    Log.LogError(
                        $"[autosell] Plugin disabled: runtime compatibility check failed: {compatibility.Message}. " +
                        $"Expected Steam build {RuntimeCompatibility.SupportedSteamBuild}.");
                    if (!TryReleaseRuntimeLease())
                        throw new InvalidOperationException("Could not release the unused AutoSell runtime lease.");
                    return;
                }

                Log.LogInfo($"[autosell] Runtime compatibility check passed: {compatibility.Message}.");

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

                if (!RuntimeLease.TryBeginComponentCreation(this))
                    throw new InvalidOperationException("Could not begin AutoSell behaviour creation.");

                try
                {
                    _behaviour = AddComponent<AutoSellBehaviour>();
                }
                finally
                {
                    RuntimeLease.EndComponentCreation(this);
                }

                if (_behaviour == null
                    || !RuntimeLease.TryConfirmComponent(
                        this,
                        _behaviour,
                        out AutoSellBehaviour ownedBehaviour,
                        static (registered, returned) =>
                            registered.Pointer != IntPtr.Zero
                            && registered.Pointer == returned.Pointer))
                    throw new InvalidOperationException("AutoSell behaviour ownership could not be confirmed.");

                _behaviour = ownedBehaviour;
                _behaviour.Activate();
                Log.LogInfo(
                    $"Plugin {MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} " +
                    $"({MyPluginInfo.PLUGIN_GUID}) is loaded.");
            }
            catch
            {
                if (_ownsRuntimeLease && !TryReleaseRuntimeLease())
                {
                    Log.LogError(
                        "[autosell] Load failed and the runtime component could not be cleaned up. " +
                        "The process-wide lease remains held to prevent a duplicate component.");
                }

                throw;
            }
        }

        public override bool Unload()
        {
            if (!_ownsRuntimeLease)
                return true;

            bool released = TryReleaseRuntimeLease();
            if (!released)
            {
                base.Log.LogError(
                    "[autosell] Unload could not fully detach and destroy the runtime component. " +
                    "The process-wide lease remains held.");
            }

            return released;
        }

        internal static bool TryRegisterCreatingBehaviour(AutoSellBehaviour behaviour)
        {
            return RuntimeLease.TryRegisterCreatingComponent(behaviour);
        }

        private bool TryReleaseRuntimeLease()
        {
            bool released = RuntimeLease.TryCleanupAndRelease(
                this,
                behaviour =>
                {
                    if (!behaviour.Shutdown())
                        return false;

                    try
                    {
                        UnityEngine.Object.Destroy(behaviour);
                        return true;
                    }
                    catch (Exception exception)
                    {
                        Log.LogError(
                            $"[autosell] Could not destroy runtime component: " +
                            $"{exception.GetType().Name}: {exception.Message}");
                        return false;
                    }
                });

            if (released)
            {
                _behaviour = null;
                _ownsRuntimeLease = false;
            }

            return released;
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
        private const double SuccessEventTimeoutSeconds = 15.0;

        private sealed class SellCandidate
        {
            internal readonly TownShopInstance Shop;
            internal readonly int ResourceSlotIndex;
            internal readonly FarmResourceType ResourceType;
            internal readonly long AmountPerInteraction;
            internal readonly int GoodIndex;

            internal SellCandidate(
                TownShopInstance shop,
                int resourceSlotIndex,
                FarmResourceType resourceType,
                long amountPerInteraction,
                int goodIndex)
            {
                Shop = shop;
                ResourceSlotIndex = resourceSlotIndex;
                ResourceType = resourceType;
                AmountPerInteraction = amountPerInteraction;
                GoodIndex = goodIndex;
            }
        }

        private sealed class PendingSaleDetails
        {
            internal PendingSaleDetails(
                long amountPerInteraction,
                uint interactionCount,
                long currentAmount,
                long maxValue,
                long targetAmount,
                float triggerRatio)
            {
                AmountPerInteraction = amountPerInteraction;
                InteractionCount = interactionCount;
                CurrentAmount = currentAmount;
                MaxValue = maxValue;
                TargetAmount = targetAmount;
                TriggerRatio = triggerRatio;
            }

            internal long AmountPerInteraction { get; }
            internal uint InteractionCount { get; }
            internal long CurrentAmount { get; }
            internal long MaxValue { get; }
            internal long TargetAmount { get; }
            internal float TriggerRatio { get; }
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
        private readonly AutoSellDispatcher<SellCandidate> _dispatcher =
            new AutoSellDispatcher<SellCandidate>();
        private readonly AutoSellPendingTracker<FarmResourceType, PendingSaleDetails> _pendingSales =
            new AutoSellPendingTracker<FarmResourceType, PendingSaleDetails>();
        private readonly AutoSellAttemptCoordinator<FarmResourceType, PendingSaleDetails> _attemptCoordinator;
        private readonly AutoSellRuntimeGate _runtimeGate = new AutoSellRuntimeGate();
        private FarmData? _subscribedFarm;
        private LocalPlayer? _subscribedPlayer;
        private AutoSellSessionIdentity? _sessionIdentity;
        private long _sessionGeneration;
        private FarmData.ActionPerformedHandler? _townActionPerformedHandler;
        private Action<
            Vector3,
            ActionPerformedType,
            ulong,
            FarmMoney,
            Il2CppSystem.Collections.Generic.List<FarmResource>,
            bool>? _townActionCallback;
        private bool _subscriptionMayBeAttached;

        public AutoSellBehaviour(IntPtr ptr) : base(ptr)
        {
            _attemptCoordinator =
                new AutoSellAttemptCoordinator<FarmResourceType, PendingSaleDetails>(_pendingSales);
        }

        private void Awake()
        {
            _runtimeGate.Deactivate();

            if (Plugin.TryRegisterCreatingBehaviour(this))
                return;

            try
            {
                UnityEngine.Object.Destroy(this);
            }
            catch
            {
                // The component remains inactive if Unity cannot destroy an unexpected instance.
            }
        }

        [HideFromIl2Cpp]
        internal void Activate()
        {
            _runtimeGate.Activate();
        }

        [HideFromIl2Cpp]
        internal bool Shutdown()
        {
            _runtimeGate.Deactivate();
            _attemptCoordinator.Clear();
            _dispatcher.Clear();
            _popupUntil = 0.0f;
            _popupDetail = "";
            _popupSubtext = "";

            try
            {
                DetachFarmSubscription();
                return true;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(
                    "[autosell] Could not detach town action handler during shutdown",
                    exception);
                return false;
            }
        }

        private void Update()
        {
            try
            {
                if (!_runtimeGate.CanRun)
                    return;

                float now = Time.realtimeSinceStartup;
                if (now < _nextCheck)
                    return;

                _nextCheck = now + Plugin.ScanInterval;

                AutoSellSessionReadStatus sessionStatus = TryGetCurrentSession(
                        out FarmData farm,
                        out LocalPlayer player,
                        out AutoSellSessionIdentity sessionIdentity,
                        out Il2CppSystem.Collections.Generic.List<TownSlot>? slots);
                if (sessionStatus == AutoSellSessionReadStatus.NoActiveSession)
                {
                    ResetSessionForLifecycleChange();
                    return;
                }
                if (sessionStatus == AutoSellSessionReadStatus.IdentityUnavailable)
                    return;

                if (!Plugin.Enabled.Value)
                {
                    ResetChangedSessionWhileDisabled(sessionIdentity);
                    return;
                }

                EnsureFarmSubscription(farm, player, sessionIdentity);
                if (slots == null)
                    return;

                ScanAndSell(farm, player, slots);
            }
            catch (Exception e)
            {
                LogCallbackFailure("[autosell] Update callback failed", e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                Shutdown();
            }
            catch (Exception exception)
            {
                LogCallbackFailure("[autosell] Destroy callback failed", exception);
            }
        }

        [HideFromIl2Cpp]
        private static AutoSellSessionReadStatus TryGetCurrentSession(
            out FarmData farm,
            out LocalPlayer player,
            out AutoSellSessionIdentity identity,
            out Il2CppSystem.Collections.Generic.List<TownSlot>? slots)
        {
            farm = null!;
            player = null!;
            identity = default;
            slots = null;

            if (!StageScript.HasInstance)
                return AutoSellSessionReadStatus.NoActiveSession;

            StageScript stage = StageScript.Instance;
            if (stage == null)
                return AutoSellSessionReadStatus.IdentityUnavailable;
            if (!stage.IsLoaded || !stage.HasLocalPlayer)
                return AutoSellSessionReadStatus.NoActiveSession;

            player = stage.LocalPlayer;
            farm = FarmData.CurrentFarm;
            if (player == null || farm == null)
                return AutoSellSessionReadStatus.IdentityUnavailable;

            IntPtr stagePointer = stage.Pointer;
            IntPtr farmPointer = farm.Pointer;
            IntPtr playerPointer = player.Pointer;
            if (stagePointer == IntPtr.Zero
                || farmPointer == IntPtr.Zero
                || playerPointer == IntPtr.Zero)
                return AutoSellSessionReadStatus.IdentityUnavailable;

            identity = new AutoSellSessionIdentity(
                stagePointer,
                stage.GetInstanceID(),
                farmPointer,
                playerPointer,
                player.GetInstanceID());

            var townData = farm.TownData;
            if (townData != null)
                slots = townData.Slots;

            return AutoSellSessionReadStatus.Available;
        }

        [HideFromIl2Cpp]
        private void ScanAndSell(
            FarmData farm,
            LocalPlayer player,
            Il2CppSystem.Collections.Generic.List<TownSlot> slots)
        {
            ProcessPendingTimeouts(Time.realtimeSinceStartup);

            ClearScanFlags();
            _dispatcher.Clear();

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
                    CollectCandidatesFromShop(farm, player, shop, slotIndex);
                }
                catch (Exception e)
                {
                    WarnThrottled($"[autosell] Shop scan failed at town slot {slotIndex}: {e}");
                }
            }

            _dispatcher.ExecuteCandidates(
                candidate => TrySellCandidate(farm, player, candidate),
                e => WarnThrottled($"[autosell] Candidate sale failed: {e.GetType().Name}: {e.Message}"));

            LogResourcesWithoutShop(farm);
        }

        private void CollectCandidatesFromShop(
            FarmData farm,
            LocalPlayer player,
            TownShopInstance shop,
            int townSlotIndex)
        {
            TownShopDefinition definition = shop.Definition;
            if (definition == null || definition.ShopResources == null)
                return;

            bool canScanShop = AutoSellShopAccessPolicy.CanScanShop(
                player.Permissions == PlayerPermissions.Full,
                () =>
                {
                    FailedAction failReason;
                    return farm.IsTownShopOpen(player, definition, out failReason);
                },
                e => WarnThrottled($"[autosell] Shop open check failed at town slot {townSlotIndex}: {e}"));
            if (!canScanShop)
                return;

            _dispatcher.CollectOffers(
                definition.ShopResources.Count,
                resourceSlotIndex => CollectOfferFromShop(shop, definition, resourceSlotIndex),
                e => WarnThrottled($"[autosell] Shop offer scan failed at town slot {townSlotIndex}: {e}"));
        }

        [HideFromIl2Cpp]
        private AutoSellOffer<SellCandidate>? CollectOfferFromShop(
            TownShopInstance shop,
            TownShopDefinition definition,
            int resourceSlotIndex)
        {
            var shopResource = definition.ShopResources[resourceSlotIndex];
            if (shopResource == null)
                return null;

            FarmResource tradeResource = shopResource.Resource;
            FarmResourceType resourceType = tradeResource.Type;
            MarkOfferedByOpenShop(resourceType);

            FarmMoney money = shop.GetSellMoney_Resource(resourceSlotIndex);
            int priority = AutoSellPolicy.GetCurrencyPriority(
                money.Coins,
                money.Bills,
                money.Medals);

            return new AutoSellOffer<SellCandidate>(
                new SellCandidate(
                    shop,
                    resourceSlotIndex,
                    resourceType,
                    tradeResource.Amount,
                    shopResource.GoodIndex),
                priority);
        }

        [HideFromIl2Cpp]
        private void TrySellCandidate(
            FarmData farm,
            LocalPlayer player,
            SellCandidate candidate)
        {
            double now = Time.realtimeSinceStartup;
            AutoSellAttemptOutcome outcome = _attemptCoordinator.TryPrepareAndDispatch(
                _sessionGeneration,
                candidate.ResourceType,
                now,
                SuccessEventTimeoutSeconds,
                () => PrepareSaleAttempt(farm, candidate),
                prepared => candidate.Shop.SellResources(
                    player,
                    candidate.ResourceSlotIndex,
                    prepared.Payload.InteractionCount),
                out PreparedAutoSellAttempt<PendingSaleDetails> prepared,
                out PendingSale<FarmResourceType, PendingSaleDetails>? confirmed);

            if (outcome == AutoSellAttemptOutcome.BlockedByPendingRequest
                || outcome == AutoSellAttemptOutcome.BlockedByActiveDispatch)
            {
                Plugin.Debug(
                    outcome == AutoSellAttemptOutcome.BlockedByPendingRequest
                        ? $"[autosell] {candidate.ResourceType} already has an unresolved sale request; " +
                            "skipping another request for this resource."
                        : $"[autosell] A native sale dispatch is already active; " +
                            $"skipping {candidate.ResourceType} until it returns.");
                return;
            }

            if (outcome != AutoSellAttemptOutcome.Dispatched)
                return;

            if (confirmed.HasValue)
            {
                CompleteConfirmedSale(confirmed.Value);
            }
            else if (_pendingSales.IsPending(candidate.ResourceType))
            {
                Plugin.Debug(
                    $"[autosell] Submitted {prepared.Payload.InteractionCount} " +
                    $"{candidate.ResourceType} trade(s) for {prepared.ExpectedResourceAmount} resources; " +
                    "no synchronous matching town action event was observed, so the request remains uncertain.");
            }
        }

        [HideFromIl2Cpp]
        private PreparedAutoSellAttempt<PendingSaleDetails>? PrepareSaleAttempt(
            FarmData farm,
            SellCandidate candidate)
        {
            FarmResourceStorage storage = farm.GetResource(candidate.ResourceType);
            if (storage == null)
                return null;

            long maxValue = storage.MaxValue;
            long currentAmount = storage.Amount;
            if (maxValue <= 0 || currentAmount <= 0)
                return null;

            float triggerRatio = Plugin.NormalizedTriggerRatio;
            double currentRatio = currentAmount / (double)maxValue;
            if (currentRatio < triggerRatio)
                return null;

            if (Plugin.IsResourceExcluded(candidate.ResourceType))
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but is excluded by config.");
                return null;
            }

            long amountPerInteraction = candidate.AmountPerInteraction;
            if (amountPerInteraction <= 0)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but shop trade amount is {amountPerInteraction}.");
                return null;
            }

            long targetAmount = (long)Math.Floor(maxValue * (double)triggerRatio);
            long excessAmount = currentAmount - targetAmount;
            bool forceOneTrade = excessAmount < amountPerInteraction
                && Plugin.SellOneTradeWhenFull.Value
                && currentAmount >= maxValue
                && currentAmount >= amountPerInteraction;

            if (excessAmount < amountPerInteraction && !forceOneTrade)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but excess {excessAmount} is less than one trade ({amountPerInteraction}).");
                return null;
            }

            if (forceOneTrade)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is full at {currentAmount}/{maxValue}; selling one trade even though excess {excessAmount} is less than one trade ({amountPerInteraction}).");
            }

            uint remainingUses = candidate.Shop.GetRemainingUses(candidate.GoodIndex);
            if (remainingUses == 0)
            {
                Plugin.Debug($"[autosell] {candidate.ResourceType} is {currentAmount}/{maxValue} ({currentRatio:P0}) but shop good index {candidate.GoodIndex} has 0 remaining uses.");
                return null;
            }

            uint interactionCount = AutoSellPolicy.CalculateInteractionCount(
                currentAmount,
                maxValue,
                triggerRatio,
                amountPerInteraction,
                remainingUses,
                Plugin.SellOneTradeWhenFull.Value);
            if (interactionCount == 0)
                return null;

            long requestedAmount = checked(amountPerInteraction * (long)interactionCount);
            FarmMoney currentMoneyPerInteraction =
                candidate.Shop.GetSellMoney_Resource(candidate.ResourceSlotIndex);
            var currentMoneySignature = new AutoSellMoneySignature(
                currentMoneyPerInteraction.Coins,
                currentMoneyPerInteraction.Bills,
                currentMoneyPerInteraction.Medals);
            currentMoneySignature.ValidateMultiplicationRange(interactionCount);
            AutoSellMoneySignature expectedMoney = AutoSellNativeMoneyProjection.Project(
                interactionCount,
                multiplier =>
                {
                    FarmMoney nativeExpectedMoney = currentMoneyPerInteraction * multiplier;
                    return new AutoSellMoneySignature(
                        nativeExpectedMoney.Coins,
                        nativeExpectedMoney.Bills,
                        nativeExpectedMoney.Medals);
                });
            var details = new PendingSaleDetails(
                amountPerInteraction,
                interactionCount,
                currentAmount,
                maxValue,
                targetAmount,
                triggerRatio);
            return new PreparedAutoSellAttempt<PendingSaleDetails>(
                requestedAmount,
                expectedMoney,
                details);
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

        [HideFromIl2Cpp]
        private void EnsureFarmSubscription(
            FarmData farm,
            LocalPlayer player,
            AutoSellSessionIdentity sessionIdentity)
        {
            if (_runtimeGate.CanProcessTownActions && IsSameSession(sessionIdentity))
                return;

            ResetSessionForLifecycleChange();
            AdvanceSessionGeneration();

            _townActionCallback = OnTownActionPerformed;
            _townActionPerformedHandler = _townActionCallback;
            _subscribedFarm = farm;
            _subscribedPlayer = player;
            _sessionIdentity = sessionIdentity;
            _subscriptionMayBeAttached = true;
            _runtimeGate.DisableTownActionCallbacks();

            farm.OnTownActionPerformed += _townActionPerformedHandler;
            _runtimeGate.EnableTownActionCallbacks();
        }

        [HideFromIl2Cpp]
        private void DetachFarmSubscription()
        {
            if (_subscriptionMayBeAttached
                && _subscribedFarm != null
                && _townActionPerformedHandler != null)
            {
                _subscribedFarm.OnTownActionPerformed -= _townActionPerformedHandler;
            }

            _subscriptionMayBeAttached = false;
            _runtimeGate.DisableTownActionCallbacks();
            _subscribedFarm = null;
            _subscribedPlayer = null;
            _sessionIdentity = null;
            _townActionPerformedHandler = null;
            _townActionCallback = null;
        }

        [HideFromIl2Cpp]
        private void ResetSessionForLifecycleChange()
        {
            _runtimeGate.DisableTownActionCallbacks();
            _attemptCoordinator.Clear();
            DetachFarmSubscription();
        }

        [HideFromIl2Cpp]
        private void ResetChangedSessionWhileDisabled(AutoSellSessionIdentity sessionIdentity)
        {
            if (_subscribedFarm == null && !_subscriptionMayBeAttached)
                return;

            if (!_runtimeGate.CanProcessTownActions || !IsSameSession(sessionIdentity))
                ResetSessionForLifecycleChange();
        }

        [HideFromIl2Cpp]
        private bool IsSameSession(AutoSellSessionIdentity sessionIdentity)
        {
            return _subscribedFarm != null
                && _subscribedPlayer != null
                && _sessionIdentity.HasValue
                && _sessionIdentity.Value.Equals(sessionIdentity);
        }

        [HideFromIl2Cpp]
        private void AdvanceSessionGeneration()
        {
            if (_sessionGeneration == long.MaxValue)
                throw new InvalidOperationException("AutoSell session generation overflowed Int64.");

            _sessionGeneration++;
        }

        [HideFromIl2Cpp]
        private void ProcessPendingTimeouts(double now)
        {
            IReadOnlyList<PendingSale<FarmResourceType, PendingSaleDetails>> timedOut =
                _pendingSales.CollectNewTimeouts(now);
            for (int index = 0; index < timedOut.Count; index++)
            {
                PendingSale<FarmResourceType, PendingSaleDetails> pending = timedOut[index];
                Plugin.Log.LogWarning(
                    $"[autosell] No synchronous matching success event for {pending.Resource} after " +
                    $"{SuccessEventTimeoutSeconds:F0}s. Its state is now uncertain, so AutoSell will " +
                    "not issue another request for this resource until the farm/player session is reset.");
            }
        }

        [HideFromIl2Cpp]
        private void OnTownActionPerformed(
            Vector3 worldPosition,
            ActionPerformedType actionType,
            ulong xp,
            FarmMoney money,
            Il2CppSystem.Collections.Generic.List<FarmResource> resources,
            bool decreaseResources)
        {
            try
            {
                if (!_runtimeGate.CanProcessTownActions)
                    return;

                AutoSellSessionReadStatus sessionStatus = TryGetCurrentSession(
                        out FarmData farm,
                        out LocalPlayer player,
                        out AutoSellSessionIdentity sessionIdentity,
                        out _);
                if (sessionStatus == AutoSellSessionReadStatus.NoActiveSession)
                {
                    ResetSessionForLifecycleChange();
                    return;
                }
                if (sessionStatus == AutoSellSessionReadStatus.IdentityUnavailable)
                    return;
                if (!IsSameSession(sessionIdentity))
                {
                    ResetSessionForLifecycleChange();
                    return;
                }

                HandleTownActionPerformed(actionType, money, resources, decreaseResources);
            }
            catch (Exception exception)
            {
                LogCallbackFailure("[autosell] Town action callback failed", exception);
            }
        }

        [HideFromIl2Cpp]
        private void HandleTownActionPerformed(
            ActionPerformedType actionType,
            FarmMoney money,
            Il2CppSystem.Collections.Generic.List<FarmResource> resources,
            bool decreaseResources)
        {
            if (actionType != ActionPerformedType.Exchange
                || !decreaseResources
                || resources == null
                || resources.Count == 0)
            {
                return;
            }

            var totals = new Dictionary<FarmResourceType, long>();
            for (int index = 0; index < resources.Count; index++)
            {
                FarmResource resource = resources[index];
                if (resource.Amount <= 0)
                    continue;

                totals.TryGetValue(resource.Type, out long currentTotal);
                if (resource.Amount > long.MaxValue - currentTotal)
                {
                    LogCallbackFailure(
                        $"[autosell] Ignoring malformed town action success event for {resource.Type}: " +
                        "resource amount overflowed Int64.");
                    return;
                }

                totals[resource.Type] = currentTotal + resource.Amount;
            }

            foreach (KeyValuePair<FarmResourceType, long> total in totals)
            {
                var signature = new AutoSellDispatchSignature<FarmResourceType>(
                    _sessionGeneration,
                    total.Key,
                    total.Value,
                    new AutoSellMoneySignature(money.Coins, money.Bills, money.Medals));
                _attemptCoordinator.TryObserveCallback(signature);
            }
        }

        private static void LogCallbackFailure(string message)
        {
            try
            {
                Plugin.Log.LogWarning(message);
            }
            catch
            {
                // Never propagate logging failures through an IL2CPP callback boundary.
            }
        }

        private static void LogCallbackFailure(string context, Exception exception)
        {
            try
            {
                Plugin.Log.LogWarning(
                    $"{context}: {exception.GetType().Name}: {exception.Message}");
            }
            catch
            {
                // Exception formatting and logging must remain inside the callback boundary.
            }
        }

        [HideFromIl2Cpp]
        private void CompleteConfirmedSale(
            PendingSale<FarmResourceType, PendingSaleDetails> confirmed)
        {
            PendingSaleDetails details = confirmed.Payload;
            long currentAmount = Math.Max(0, details.CurrentAmount - confirmed.ExpectedResourceAmount);
            FarmResourceStorage? storage = _subscribedFarm?.GetResource(confirmed.Resource);
            if (storage != null)
                currentAmount = storage.Amount;

            string logMessage =
                $"Sold {confirmed.ExpectedResourceAmount} {confirmed.Resource} with " +
                $"{details.InteractionCount} interaction(s), earned {FormatMoneyPlain(confirmed.ExpectedMoney)}. " +
                $"{details.CurrentAmount}/{details.MaxValue} -> {currentAmount}/{details.MaxValue}.";
            string popupDetail =
                $"{confirmed.Resource}: -{confirmed.ExpectedResourceAmount}  {FormatMoneyPlain(confirmed.ExpectedMoney)}";
            string popupSubtext =
                $"{details.InteractionCount} trade(s) x {details.AmountPerInteraction} | " +
                $"Storage {details.CurrentAmount}->{currentAmount}/{details.MaxValue} | " +
                $"Trigger {FormatRatio(details.TriggerRatio)}";
            MarkSold(confirmed.Resource);
            ShowPopup(logMessage, popupDetail, popupSubtext);
            Plugin.Debug($"[autosell] {logMessage} Target {details.TargetAmount}.");
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

        private static string FormatMoneyPlain(AutoSellMoneySignature money)
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
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastWarnAt < 10.0f)
                    return;

                _lastWarnAt = now;
                Plugin.Log.LogWarning(message);
            }
            catch
            {
                // Diagnostics must never escape through a Unity or IL2CPP callback boundary.
            }
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
            try
            {
                if (!_runtimeGate.CanRun)
                    return;
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
                LogCallbackFailure(
                    "[autosell] Sell popup disabled this session because IMGUI is unavailable",
                    e);
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
            try
            {
                Plugin.Log.LogInfo(
                    $"[autosell] popup IMGUI capabilities: color={_canColor} " +
                    $"texture={_canTexture} style={_canStyle}");
            }
            catch
            {
                // The capability probe result remains usable even if logging is unavailable.
            }
        }
    }
}
