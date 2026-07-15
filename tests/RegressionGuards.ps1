#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-Text([string]$relativePath) {
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot $relativePath)
}

function Assert-Match([string]$text, [string]$pattern, [string]$message) {
    if ($text -notmatch $pattern) {
        throw $message
    }
}

function Assert-NoMatch([string]$text, [string]$pattern, [string]$message) {
    if ($text -match $pattern) {
        throw $message
    }
}

$plugin = Read-Text 'src/FarmTogether2.AutoSellMod/Plugin.cs'
$project = Read-Text 'src/FarmTogether2.AutoSellMod/FarmTogether2.AutoSellMod.csproj'
$testProject = Read-Text 'tests/FarmTogether2.AutoSellMod.Tests/FarmTogether2.AutoSellMod.Tests.csproj'

Assert-Match $plugin 'finally\s*\{\s*GUI\.color = originalColor;' 'AutoSell popup must restore GUI.color in a finally block.'
Assert-Match $project '<Version>1\.1\.3</Version>' 'AutoSell project version must remain 1.1.3.'
Assert-Match $project '<BepInExPluginGuid>com\.abmcar\.farmtogether2\.autosellmod</BepInExPluginGuid>' 'AutoSell plugin GUID changed unexpectedly.'
Assert-NoMatch $project '<Target Name="CopyToGame"' 'AutoSell project must not embed a game deployment target.'
Assert-NoMatch $project 'D:/SteamLibrary|D:\\SteamLibrary' 'AutoSell project must not contain a machine-specific Steam path.'

foreach ($sourceFile in @(
    'AutoSellAttemptCoordinator.cs',
    'AutoSellDispatchObservation.cs',
    'AutoSellDispatcher.cs',
    'AutoSellPendingTracker.cs',
    'AutoSellPolicy.cs',
    'AutoSellRuntimeGate.cs',
    'AutoSellRuntimeLease.cs',
    'AutoSellSessionIdentity.cs',
    'AutoSellShopAccessPolicy.cs',
    'RuntimeCompatibility.cs'
)) {
    $escapedFile = [regex]::Escape($sourceFile)
    Assert-Match $testProject "\.\.\\\.\.\\src\\FarmTogether2\.AutoSellMod\\$escapedFile" "Managed tests must link $sourceFile from the standalone source tree."
}
Assert-NoMatch $testProject '\.\.\\\.\.\\src\\AutoSellMod\\' 'Managed tests must not reference the former monorepo source path.'

Assert-Match $plugin '(?s)\[HideFromIl2Cpp\]\s*private AutoSellOffer<SellCandidate>\? CollectOfferFromShop' 'Managed candidate collection must be hidden from Il2Cpp method injection.'
Assert-Match $plugin '(?s)\[HideFromIl2Cpp\]\s*private void TrySellCandidate' 'Managed candidate execution must be hidden from Il2Cpp method injection.'
Assert-Match $plugin 'public override bool Unload\(\)' 'AutoSell must explicitly clean up its persistent BepInEx component.'
Assert-Match $plugin 'private AutoSellBehaviour\? _behaviour' 'AutoSell plugin must retain the runtime component handle.'
Assert-Match $plugin '(?s)private void Update\(\)\s*\{\s*try\s*\{\s*if \(!_runtimeGate\.CanRun\)' 'AutoSell Update must fail closed inside an outer exception boundary after shutdown.'
Assert-Match $plugin '(?s)private void OnGUI\(\)\s*\{\s*try\s*\{\s*if \(!_runtimeGate\.CanRun\)' 'AutoSell OnGUI must fail closed inside an outer exception boundary after shutdown.'
Assert-NoMatch $plugin 'TryReleaseForRetry|LateSuccessEventGraceSeconds|\bisRetry\b' 'AutoSell must not automatically retry an uncertain request.'

Write-Output '[autosell-regression-guards] OK'
