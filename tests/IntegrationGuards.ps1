#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$plugin = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/Plugin.cs')
$attemptCoordinator = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellAttemptCoordinator.cs')
$dispatchObservation = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellDispatchObservation.cs')
$dispatcher = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellDispatcher.cs')
$pendingTracker = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellPendingTracker.cs')
$policy = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellPolicy.cs')
$runtimeGate = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellRuntimeGate.cs')
$runtimeLease = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellRuntimeLease.cs')
$sessionIdentity = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellSessionIdentity.cs')
$runtimeCompatibility = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/RuntimeCompatibility.cs')
$shopAccessPolicy = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/AutoSellShopAccessPolicy.cs')
$guardFailures = [System.Collections.Generic.List[string]]::new()

function Add-GuardFailure([string]$message) {
    [void]$script:guardFailures.Add($message)
}

function Assert-Match([string]$source, [string]$pattern, [string]$message) {
    if ($source -notmatch $pattern) {
        Add-GuardFailure $message
    }
}

function Assert-NoMatch([string]$source, [string]$pattern, [string]$message) {
    if ($source -match $pattern) {
        Add-GuardFailure $message
    }
}

Assert-Match $plugin 'ExcludedResources.*"GoldNugget"' 'AutoSell must enable Event resources by default.'
Assert-Match $plugin 'MigrateLegacyExcludedResources\(\)' 'AutoSell must migrate the legacy default exclusion set.'
Assert-Match $plugin 'ConfigEntry<int>\s+ConfigSchemaVersion' 'AutoSell must persist a config schema migration marker.'
Assert-Match $plugin 'Config\.Bind\("Migration",\s*"ConfigSchemaVersion",\s*0' 'AutoSell schema marker must default to version 0.'
Assert-Match $plugin '(?s)AutoSellPolicy\.DecideExclusionMigration\(\s*ExcludedResources\.Value,\s*ConfigSchemaVersion\.Value\)' 'AutoSell plugin must use the tested exclusion migration decision.'
Assert-Match $dispatcher 'class\s+AutoSellDispatcher<TCandidate>' 'AutoSell must provide a game-independent managed dispatcher.'
Assert-Match $plugin 'AutoSellDispatcher<SellCandidate>\s+_dispatcher' 'AutoSell behaviour must own the tested managed dispatcher.'
Assert-Match $plugin '(?s)private void CollectCandidatesFromShop\(.*?_dispatcher\.CollectOffers\(' 'AutoSell shop collection must be wired through per-offer dispatcher isolation.'
Assert-Match $plugin '(?s)private void ScanAndSell\(.*?_dispatcher\.ExecuteCandidates\(\s*candidate\s*=>\s*TrySellCandidate\(farm,\s*player,\s*candidate\)' 'AutoSell scan must execute sorted candidates through the managed dispatcher.'
Assert-Match $plugin 'AutoSellPolicy\.GetCurrencyPriority' 'AutoSell must rank native FarmMoney through the policy.'
Assert-Match $plugin 'AutoSellPolicy\.CalculateInteractionCount' 'AutoSell must use the tested interaction calculation.'
Assert-Match $plugin 'candidate\.Shop\.SellResources' 'AutoSell must execute the selected native shop candidate.'
Assert-Match $pendingTracker 'class\s+AutoSellPendingTracker<TKey, TPayload>' 'AutoSell must provide a tested per-resource pending request tracker.'
Assert-Match $attemptCoordinator 'TryPrepareAndDispatch' 'AutoSell must provide a tested prepare-and-dispatch coordinator.'
Assert-Match $attemptCoordinator '(?s)_pendingRequests\.TryBegin\(.*?TryExecuteDispatch\(' 'AutoSell must arm pending state only after preparation and before native dispatch.'
Assert-Match $attemptCoordinator '(?s)catch\s*\{.*?_dispatchObservation\.TryAbortDispatch\(signature\);.*?throw;' 'Native dispatch failures must discard callback observations and retain armed pending state.'
Assert-Match $attemptCoordinator '(?s)TryExecuteDispatch\(.*?dispatch,\s*prepared,.*?matchingCallbackObserved.*?TryCommitObservedConfirmation' 'AutoSell may commit a callback observation only after native dispatch returns normally.'
Assert-Match $dispatchObservation 'class\s+AutoSellDispatchObservation<TKey>' 'AutoSell must provide a game-independent synchronous dispatch observer.'
Assert-Match $dispatchObservation '(?s)TryObserveCallback\(.*?_hasActiveDispatch.*?_dispatchEntered.*?_dispatchThreadId != Environment\.CurrentManagedThreadId.*?_activeSignature\.Equals\(signature\)' 'AutoSell must observe callbacks only on the thread that entered the exact active dispatch.'
Assert-Match $dispatchObservation '(?s)TryExecuteDispatch<TState>\(.*?_dispatchEntered = true;.*?_dispatchThreadId = dispatchThreadId;.*?dispatch\(state\);.*?_dispatchThreadId != dispatchThreadId.*?ClearWithoutLock\(\)' 'AutoSell must keep the synchronous callback window inside the native dispatch call.'
Assert-Match $dispatchObservation 'SessionGeneration == other\.SessionGeneration' 'AutoSell dispatch signatures must include the current session generation.'
Assert-Match $dispatchObservation '(?s)ResourceAmount == other\.ResourceAmount.*?Money\.Equals\(other\.Money\)' 'AutoSell dispatch signatures must include the exact resource amount and money.'
Assert-Match $plugin 'EnsureFarmSubscription\(farm,\s*player,\s*sessionIdentity\)' 'AutoSell must subscribe to the game success event for the current farm/player session.'
Assert-Match $plugin 'farm\.OnTownActionPerformed\s*\+=' 'AutoSell must observe successful native town actions.'
Assert-Match $plugin 'ActionPerformedType\.Exchange' 'AutoSell confirmations must be limited to successful local exchange events.'
Assert-Match $plugin '_attemptCoordinator\.TryObserveCallback\(signature\)' 'AutoSell town callbacks must only record matching active-dispatch observations.'
Assert-NoMatch $plugin '_pendingSales\.TryConfirm|TryCommitObservedConfirmation' 'AutoSell callbacks must not clear pending requests directly.'
Assert-Match $plugin '(?s)FarmMoney currentMoneyPerInteraction\s*=\s*candidate\.Shop\.GetSellMoney_Resource\(candidate\.ResourceSlotIndex\);.*?ValidateMultiplicationRange\(interactionCount\);.*?AutoSellNativeMoneyProjection\.Project\(.*?currentMoneyPerInteraction \* multiplier;.*?new AutoSellMoneySignature\(\s*nativeExpectedMoney\.Coins,\s*nativeExpectedMoney\.Bills,\s*nativeExpectedMoney\.Medals\)' 'AutoSell must re-read current money immediately before dispatch, validate its mathematical range, and use the game native Single multiplication result for the confirmation signature.'
Assert-NoMatch $plugin 'candidate\.MoneyPerInteraction|readonly AutoSellMoneySignature MoneyPerInteraction|CheckedMultiply' 'AutoSell must not use stale candidate money or Int64 multiplication for the native confirmation signature.'
Assert-NoMatch $dispatchObservation 'CheckedMultiply' 'The managed dispatch signature must not model FarmMoney native multiplication as checked Int64 arithmetic.'
Assert-Match $plugin 'AutoSellPolicy\.LimitInteractionCountForExecution' 'AutoSell must use the tested online and transport count limit.'
Assert-Match $plugin 'StageParameters\.IsOnline' 'AutoSell must limit online requests separately from synchronous offline requests.'
Assert-NoMatch $plugin 'TryReleaseForRetry|LateSuccessEventGraceSeconds|\bisRetry\b' 'AutoSell must not retry requests whose result is uncertain.'
Assert-Match $pendingTracker 'IsUncertain' 'AutoSell pending state must expose a tested uncertain state after timeout.'
Assert-NoMatch $pendingTracker 'RetryAt|TryReleaseForRetry' 'AutoSell pending state must not have an automatic retry transition.'
Assert-Match $policy 'MaxNativeInteractionCount\s*=\s*\(uint\)short\.MaxValue' 'AutoSell native interaction transport must be capped to the positive signed 16-bit range.'
Assert-Match $policy '(?s)possibleInteractions == 0.*?currentAmount >= maxValue.*?currentAmount >= amountPerInteraction' 'Forced full-storage sales must still require enough resources for one native trade.'
Assert-Match $runtimeCompatibility 'ExpectedGameAssemblySha256' 'AutoSell must fingerprint GameAssembly at runtime.'
Assert-Match $runtimeCompatibility 'ExpectedGlobalMetadataSha256' 'AutoSell must fingerprint IL2CPP metadata at runtime.'
Assert-Match $plugin '(?s)if\s*\(!compatibility\.IsCompatible\).*?return\s*;' 'AutoSell must fail closed before adding its behaviour on an unknown runtime.'
Assert-Match $shopAccessPolicy 'class\s+AutoSellShopAccessPolicy' 'AutoSell must provide a game-independent shop access policy.'
Assert-Match $plugin 'AutoSellShopAccessPolicy\.CanScanShop' 'AutoSell shop collection must use the tested shop access policy.'
Assert-Match $plugin 'player\.Permissions\s*==\s*PlayerPermissions\.Full' 'AutoSell must bypass the native shop-open check only for full permissions.'
Assert-Match $plugin '(?s)CollectCandidatesFromShop\(\s*FarmData farm,\s*LocalPlayer player,\s*TownShopInstance shop,\s*int townSlotIndex\s*\)' 'AutoSell shop collection must carry the town slot index into native-boundary diagnostics.'
Assert-Match $plugin 'Shop open check failed at town slot \{townSlotIndex\}: \{e\}' 'AutoSell must log the slot and full exception for a native shop-open failure.'
Assert-Match $runtimeLease 'TryCleanupAndRelease' 'AutoSell must retain its singleton lease until component cleanup succeeds.'
Assert-Match $runtimeGate '(?s)internal void Deactivate\(\).*?CanProcessTownActions = false;.*?CanRun = false;' 'AutoSell runtime gate must disable callbacks before the runtime becomes inactive.'
Assert-Match $plugin 'public override bool Unload\(\)' 'AutoSell must override BepInEx unload.'
Assert-Match $plugin 'RuntimeLease\.TryAcquire\(this\)' 'AutoSell must acquire a process-wide lease before loading.'
Assert-Match $plugin 'RuntimeLease\.TryBeginComponentCreation\(this\)' 'AutoSell must mark the partial component-creation window.'
Assert-Match $plugin 'TryRegisterCreatingBehaviour\(this\)' 'AutoSell Awake must register a component created before AddComponent returns.'
Assert-Match $plugin '(?s)internal bool Shutdown\(\).*?_runtimeGate\.Deactivate\(\);.*?_attemptCoordinator\.Clear\(\);.*?DetachFarmSubscription\(\)' 'AutoSell shutdown must deactivate, clear transaction state, and detach the native event.'
Assert-Match $plugin '(?s)private void Update\(\)\s*\{\s*try\s*\{.*?if \(!_runtimeGate\.CanRun\)' 'AutoSell Update must put its active gate inside the outermost exception boundary.'
Assert-Match $plugin '(?s)private void OnGUI\(\)\s*\{\s*try\s*\{.*?if \(!_runtimeGate\.CanRun\)' 'AutoSell OnGUI must put its active gate inside the outermost exception boundary.'
Assert-Match $plugin '(?s)private void OnTownActionPerformed\(.*?\)\s*\{\s*try\s*\{.*?if \(!_runtimeGate\.CanProcessTownActions\)' 'AutoSell native callback must put its active gate inside the outermost exception boundary.'
Assert-Match $plugin '(?s)private void OnDestroy\(\)\s*\{\s*try\s*\{' 'AutoSell OnDestroy must retain an outermost exception boundary.'
Assert-Match $plugin '(?s)catch \(Exception (?:e|exception)\)\s*\{\s*LogCallbackFailure\(' 'AutoSell callback boundaries must use non-throwing failure logging.'
Assert-Match $plugin '(?s)private static void LogCallbackFailure\(string context, Exception exception\)\s*\{\s*try\s*\{.*?exception\.GetType\(\)\.Name.*?catch\s*\{' 'AutoSell must format and write callback exceptions inside a non-throwing helper.'
Assert-Match $plugin '(?s)TryGetCurrentSession\(.*?AutoSellSessionReadStatus\.IdentityUnavailable.*?return;' 'AutoSell must fail closed without resetting transaction state when session identity is unavailable.'
Assert-Match $sessionIdentity '_stagePointer == other\._stagePointer' 'AutoSell session identity must include the StageScript native pointer.'
Assert-Match $sessionIdentity '_stageInstanceId == other\._stageInstanceId' 'AutoSell session identity must include the StageScript Unity instance ID.'
Assert-Match $sessionIdentity '_farmPointer == other\._farmPointer' 'AutoSell session identity must include the farm native pointer.'
Assert-Match $sessionIdentity '_playerPointer == other\._playerPointer' 'AutoSell session identity must include the local-player native pointer.'
Assert-Match $sessionIdentity '_playerInstanceId == other\._playerInstanceId' 'AutoSell session identity must include the local-player Unity instance ID.'
Assert-NoMatch $sessionIdentity 'ReferenceEquals' 'Managed wrapper replacement must not look like a session change.'
Assert-Match $plugin '_sessionIdentity\.Value\.Equals\(sessionIdentity\)' 'AutoSell runtime must use the tested native and Unity session identity.'

$migrateLegacyExcludedResources = [regex]::Match(
    $plugin,
    '(?s)private void MigrateLegacyExcludedResources\(\).*?(?=\r?\n\s*internal static HashSet<FarmResourceType> GetExcludedResources\()')
if (-not $migrateLegacyExcludedResources.Success) {
    Add-GuardFailure 'AutoSell integration guard could not isolate MigrateLegacyExcludedResources.'
}
else {
    Assert-Match $migrateLegacyExcludedResources.Value 'ConfigSchemaVersion\.Value\s*=\s*decision\.MigrationVersion\s*;' 'MigrateLegacyExcludedResources must persist the migration decision marker.'
    Assert-Match $migrateLegacyExcludedResources.Value 'if\s*\(\s*configChanged\s*\)\s*Config\.Save\(\)\s*;' 'MigrateLegacyExcludedResources must save config when migration state changes.'
}

$trySellCandidate = [regex]::Match(
    $plugin,
    '(?s)private void TrySellCandidate\(.*?(?=\r?\n\s*private void ClearScanFlags\()')
if (-not $trySellCandidate.Success) {
    Add-GuardFailure 'AutoSell integration guard could not isolate TrySellCandidate.'
}
else {
    Assert-Match $trySellCandidate.Value 'farm\.GetResource\(candidate\.ResourceType\)' 'TrySellCandidate must re-read farm storage for every candidate.'
    Assert-Match $trySellCandidate.Value 'candidate\.Shop\.GetRemainingUses\(candidate\.GoodIndex\)' 'TrySellCandidate must re-read remaining shop uses for every candidate.'
    Assert-Match $trySellCandidate.Value '_attemptCoordinator\.TryPrepareAndDispatch' 'TrySellCandidate must use the tested attempt coordinator.'
    Assert-Match $trySellCandidate.Value '(?s)PrepareSaleAttempt\(farm, candidate\).*?candidate\.Shop\.SellResources' 'TrySellCandidate must prepare fully before native dispatch.'
    Assert-NoMatch $trySellCandidate.Value '_pendingSales\.TryBegin|_pendingSales\.TryCancel' 'TrySellCandidate must not manipulate pending state outside the coordinator.'
    Assert-Match $trySellCandidate.Value '(?s)confirmed\.HasValue.*?CompleteConfirmedSale\(confirmed\.Value\)' 'TrySellCandidate must report success only after the coordinator commits a synchronous observation.'
    Assert-NoMatch $trySellCandidate.Value 'ShowPopup\(' 'TrySellCandidate must not show success before a matching success event.'
    Assert-NoMatch $trySellCandidate.Value 'MarkSold\(' 'TrySellCandidate must not mark a request sold before a matching success event.'
}

$project = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/FarmTogether2.AutoSellMod/FarmTogether2.AutoSellMod.csproj')
$readme = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'README.md')
$modConfig = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'mod.json') | ConvertFrom-Json

if ($project -notmatch '<Version>1\.1\.1</Version>') {
    Add-GuardFailure 'AutoSell repair release must be version 1.1.1.'
}
if ($readme -notmatch 'Loading \[FarmTogether2\.AutoSellMod 1\.1\.1\]') {
    Add-GuardFailure 'README load verification must reference AutoSell version 1.1.1.'
}
if ($readme -notmatch '奖章.*钻石.*金币') {
    Add-GuardFailure 'README must document AutoSell currency priority.'
}
if ($readme -notmatch '活动车') {
    Add-GuardFailure 'README must document Event Shack support.'
}
if ($readme -notmatch '`ExcludedResources`.*`GoldNugget`') {
    Add-GuardFailure 'README must document the new exclusion default.'
}
if ($modConfig.project -cne 'src/FarmTogether2.AutoSellMod/FarmTogether2.AutoSellMod.csproj') {
    Add-GuardFailure 'mod.json must name the standalone AutoSell plugin project.'
}
$expectedGuards = @('tests/RegressionGuards.ps1', 'tests/IntegrationGuards.ps1')
$actualGuards = @($modConfig.guardScripts)
if (Compare-Object $expectedGuards $actualGuards -SyncWindow 0) {
    Add-GuardFailure 'mod.json must run the standalone AutoSell guards in the declared order.'
}

if ($guardFailures.Count -gt 0) {
    $details = ($guardFailures | ForEach-Object { " - $_" }) -join [Environment]::NewLine
    throw "AutoSell integration guards failed:$([Environment]::NewLine)$details"
}

Write-Output '[autosell-integration-guards] OK'
