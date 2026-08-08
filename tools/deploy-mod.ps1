<#
.SYNOPSIS
  Deploys one content variant into <ModRoot>/lib/<version>/ plus the loader at the mod
  root, then regenerates extractionrun-variants.manifest. Called from the content project's
  build target (dev loop) and from build-variants.ps1 (multi-version bundle).

.PARAMETER ContentDll
  Path to the built ExtractionRun.Content.

.PARAMETER LoaderDll
  Path to the built <modId>.dll loader.

.PARAMETER ModRoot
  Mod install folder (the one containing <modId>.json).

.PARAMETER Sts2DataDir
  Used to auto-detect the game version from ../release_info.json when -VariantTarget
  is empty (dev loop). Ignored when -VariantTarget is provided.

.PARAMETER VariantTarget
  Explicit compat target (e.g. "0.110.1"). Recommended for snapshot builds.
#>
param(
    [Parameter(Mandatory = $true)][string]$ContentDll,
    [Parameter(Mandatory = $true)][string]$LoaderDll,
    [Parameter(Mandatory = $true)][string]$ModRoot,
    [Parameter(Mandatory = $false)][string]$Sts2DataDir = '',
    [Parameter(Mandatory = $false)][string]$VariantTarget = ''
)

$ErrorActionPreference = 'Stop'

# ── Mod-specific names (replace when adopting this template) ──────
$modId = 'ExtractionRun'
$contentAssembly = 'ExtractionRun.Content.dll'
$loaderAssembly = "$modId.dll"
$variantManifest = 'extractionrun-variants.manifest'

if ([string]::IsNullOrWhiteSpace($VariantTarget)) {
    $gameRoot = if (-not [string]::IsNullOrWhiteSpace($Sts2DataDir)) { Split-Path $Sts2DataDir -Parent } else { '' }
    $releaseInfo = if (-not [string]::IsNullOrWhiteSpace($gameRoot)) { Join-Path $gameRoot 'release_info.json' } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($releaseInfo) -and (Test-Path $releaseInfo)) {
        $info = Get-Content $releaseInfo -Raw | ConvertFrom-Json
        $VariantTarget = ([string]$info.version).Trim()
        if ($VariantTarget -like 'v*') { $VariantTarget = $VariantTarget.Substring(1) }
    }
}

if ([string]::IsNullOrWhiteSpace($VariantTarget)) {
    throw "Could not determine variant target version. Pass -VariantTarget or point -Sts2DataDir at the game install."
}

# Deploy content variant.
$variantDir = Join-Path $ModRoot "lib/$VariantTarget"
New-Item -ItemType Directory -Force -Path $variantDir | Out-Null
Copy-Item -Force $ContentDll (Join-Path $variantDir $contentAssembly)
[System.IO.File]::WriteAllText((Join-Path $variantDir 'compat-target.txt'), $VariantTarget, (New-Object System.Text.UTF8Encoding($false)))

# Deploy the game-facing loader at the mod root.
if (Test-Path $LoaderDll) {
    Copy-Item -Force $LoaderDll (Join-Path $ModRoot $loaderAssembly)
    Write-Host "Deployed loader -> $ModRoot/$loaderAssembly"
} else {
    Write-Warning "Loader DLL not found at '$LoaderDll'; the mod will not load without it."
}

# Regenerate the variants manifest from whatever lib/*/ folders exist.
& (Join-Path $PSScriptRoot 'generate-variants-manifest.ps1') -ModRoot $ModRoot -VariantAssembly $contentAssembly -ManifestName $variantManifest
Write-Host "Deployed $modId content variant $VariantTarget -> $ModRoot/lib/$VariantTarget"
