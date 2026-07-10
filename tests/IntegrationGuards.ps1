#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$plugin = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/AutoSellMod/Plugin.cs')
$dispatcher = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/AutoSellMod/AutoSellDispatcher.cs')

function Assert-Contains([string]$source, [string]$pattern, [string]$message) {
    if ($source -notmatch $pattern) {
        throw $message
    }
}

Assert-Contains $plugin 'ExcludedResources.*"GoldNugget"' 'AutoSell must enable Event resources by default.'
Assert-Contains $plugin 'MigrateLegacyExcludedResources\(\)' 'AutoSell must migrate the legacy default exclusion set.'
Assert-Contains $plugin 'ConfigEntry<int>\s+ConfigSchemaVersion' 'AutoSell must persist a config schema migration marker.'
Assert-Contains $plugin 'Config\.Bind\("Migration",\s*"ConfigSchemaVersion",\s*0' 'AutoSell schema marker must default to version 0.'
Assert-Contains $plugin '(?s)AutoSellPolicy\.DecideExclusionMigration\(\s*ExcludedResources\.Value,\s*ConfigSchemaVersion\.Value\)' 'AutoSell plugin must use the tested exclusion migration decision.'
Assert-Contains $dispatcher 'class\s+AutoSellDispatcher<TCandidate>' 'AutoSell must provide a game-independent managed dispatcher.'
Assert-Contains $plugin 'AutoSellDispatcher<SellCandidate>\s+_dispatcher' 'AutoSell behaviour must own the tested managed dispatcher.'
Assert-Contains $plugin '(?s)private void CollectCandidatesFromShop\(.*?_dispatcher\.CollectOffers\(' 'AutoSell shop collection must be wired through per-offer dispatcher isolation.'
Assert-Contains $plugin '(?s)private void ScanAndSell\(\).*?_dispatcher\.ExecuteCandidates\(\s*candidate\s*=>\s*TrySellCandidate\(farm,\s*player,\s*candidate\)' 'AutoSell scan must execute sorted candidates through the managed dispatcher.'
Assert-Contains $plugin 'AutoSellPolicy\.GetCurrencyPriority' 'AutoSell must rank native FarmMoney through the policy.'
Assert-Contains $plugin 'AutoSellPolicy\.CalculateInteractionCount' 'AutoSell must use the tested interaction calculation.'
Assert-Contains $plugin 'candidate\.Shop\.SellResources' 'AutoSell must execute the selected native shop candidate.'

$trySellCandidate = [regex]::Match(
    $plugin,
    '(?s)private void TrySellCandidate\(.*?(?=\r?\n\s*private void ClearScanFlags\()')
if (-not $trySellCandidate.Success) {
    throw 'AutoSell integration guard could not isolate TrySellCandidate.'
}
Assert-Contains $trySellCandidate.Value 'farm\.GetResource\(candidate\.ResourceType\)' 'TrySellCandidate must re-read farm storage for every candidate.'
Assert-Contains $trySellCandidate.Value 'candidate\.Shop\.GetRemainingUses\(candidate\.GoodIndex\)' 'TrySellCandidate must re-read remaining shop uses for every candidate.'

$project = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/AutoSellMod/FarmTogether2.AutoSellMod.csproj')
$readme = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'README.md')
$deployScript = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'scripts/build-deploy-autosell.ps1')

if ($project -notmatch '<Version>1\.1\.0</Version>') {
    throw 'AutoSell feature release must be version 1.1.0.'
}
if ($deployScript -notmatch 'Loading \[FarmTogether2\.AutoSellMod 1\.1\.0\]') {
    throw 'AutoSell deploy verification must reference version 1.1.0.'
}
if ($readme -notmatch '奖章.*钻石.*金币') {
    throw 'README must document AutoSell currency priority.'
}
if ($readme -notmatch '活动车') {
    throw 'README must document Event Shack support.'
}
if ($readme -notmatch '`ExcludedResources`.*`GoldNugget`') {
    throw 'README must document the new exclusion default.'
}

Write-Host '[autosell-integration-guards] OK'
