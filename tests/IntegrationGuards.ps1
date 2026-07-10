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

Write-Host '[autosell-integration-guards] OK'
