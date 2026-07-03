#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-Text([string]$relativePath) {
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot $relativePath)
}

function Assert-Contains([string]$text, [string]$pattern, [string]$message) {
    if ($text -notmatch $pattern) {
        throw $message
    }
}

function Assert-NotContains([string]$text, [string]$pattern, [string]$message) {
    if ($text -match $pattern) {
        throw $message
    }
}

$qol = Read-Text 'src/QoLMod/Plugin.cs'
$farmhand = Read-Text 'src/FarmhandSpeedMod/Plugin.cs'
$autoRange = Read-Text 'src/AutoModRangeMod/Plugin.cs'
$autoSell = Read-Text 'src/AutoSellMod/Plugin.cs'
$props = Read-Text 'Directory.Build.props'
$targetsPath = Join-Path $repoRoot 'Directory.Build.targets'

Assert-Contains $qol 'float\.IsNaN\(value\)' 'QoL Clamp must reject NaN before raw float patching.'
Assert-Contains $qol 'float\.IsNaN\(v\)' 'QoL ClampMultiplier must reject NaN.'
Assert-Contains $qol 'PatchFloatResult' 'QoL raw float patch must report success/failure.'
Assert-Contains $qol '_autoInternalClampPatchActive' 'QoL auto field fallback must depend on actual raw patch success.'
Assert-NotContains $qol 'AccessTools\.Property\(typeof\(LocalPlayer\), "autoTractor(Speed|SpeedLerp|StartTime)"' 'QoL auto tractor private fields must not be resolved as properties only.'
Assert-Contains $qol 'AccessTools\.Field\(type, name\)' 'QoL auto tractor private member resolver must check fields.'

Assert-Contains $autoRange 'player\.State != Player\.PlayerState\.AutoTractor' 'AutoModRange must require auto-tractor state before widening.'
Assert-NotContains $autoRange 'manualTractor' 'AutoModRange must not widen manual tractor work paths.'

Assert-Contains $qol 'SharedWorkThrottleBypass\.SetOwner' 'QoL must use shared SkipWorkThrottle ownership.'
Assert-Contains $farmhand 'SharedWorkThrottleBypass\.SetOwner' 'FarmhandSpeed must use shared SkipWorkThrottle ownership.'
Assert-NotContains $qol 'Player\.SkipWorkThrottle = want;' 'QoL must not directly overwrite shared SkipWorkThrottle with its local want.'
Assert-NotContains $farmhand 'Player\.SkipWorkThrottle = want;' 'FarmhandSpeed must not directly overwrite shared SkipWorkThrottle with its local want.'

Assert-Contains $autoSell 'finally\s*\{\s*GUI\.color = originalColor;' 'AutoSell popup must restore GUI.color in a finally block.'

if (-not (Test-Path -LiteralPath $targetsPath)) {
    throw 'Directory.Build.targets must centralize deploy behavior.'
}

$targets = Get-Content -Raw -LiteralPath $targetsPath
Assert-Contains $targets 'DeployToGame' 'Deploy target must be gated by DeployToGame.'
Assert-Contains $props 'Samboy063\.Cpp2IL\.Core' 'Cpp2IL transitive package must be pinned to a resolvable version.'

foreach ($project in @(
    'src/QoLMod/FarmTogether2.QoLMod.csproj',
    'src/AutoSellMod/FarmTogether2.AutoSellMod.csproj',
    'src/FarmhandSpeedMod/FarmTogether2.FarmhandSpeedMod.csproj',
    'src/AutoModRangeMod/FarmTogether2.AutoModRangeMod.csproj'
)) {
    $projectText = Read-Text $project
    Assert-NotContains $projectText '<Target Name="CopyToGame"' "$project must not duplicate CopyToGame target."
}

Write-Host '[review-guards] OK'
