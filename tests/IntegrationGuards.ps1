#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$plugin = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/AutoSellMod/Plugin.cs')

function Assert-Contains([string]$pattern, [string]$message) {
    if ($plugin -notmatch $pattern) {
        throw $message
    }
}

Assert-Contains 'ExcludedResources.*"GoldNugget"' 'AutoSell must enable Event resources by default.'
Assert-Contains 'MigrateLegacyExcludedResources\(\)' 'AutoSell must migrate the legacy default exclusion set.'
Assert-Contains 'CollectCandidatesFromShop' 'AutoSell must collect shop candidates before selling.'
Assert-Contains '_sellCandidates\.Sort\(CompareSellCandidates\)' 'AutoSell must sort collected candidates.'
Assert-Contains 'AutoSellPolicy\.GetCurrencyPriority' 'AutoSell must rank native FarmMoney through the policy.'
Assert-Contains 'AutoSellPolicy\.CalculateInteractionCount' 'AutoSell must use the tested interaction calculation.'
Assert-Contains 'candidate\.Shop\.SellResources' 'AutoSell must execute the selected native shop candidate.'

$project = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/AutoSellMod/FarmTogether2.AutoSellMod.csproj')
$readme = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'README.md')

if ($project -notmatch '<Version>1\.1\.0</Version>') {
    throw 'AutoSell feature release must be version 1.1.0.'
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
