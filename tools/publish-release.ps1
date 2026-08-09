<#
.SYNOPSIS
  Deterministic core of the /release workflow: bumps the manifest version, builds the multi-version
  bundle (+ pck), packages the white-listed mod files into dist/ExtractionRun-<tag>.zip, then commits
  the version bump + CHANGELOG.md and creates the release tag. It does NOT push or touch GitHub — the
  /release command handles the push + `gh release create` after its confirmation gate.

.PARAMETER Tag
  Release version, e.g. "v0.2.0" or "v0.2.0-beta.1". Must be valid semver and not already tagged.

.PARAMETER ApiRoot
  Directory containing one sub-folder per game version, each holding sts2.dll.
  Default: F:\sts2-mod\sts2-versions.

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER Sts2Path
  Game install root. If empty, auto-detected from the Steam registry.

.PARAMETER Versions
  Optional filter: only build these compat targets (e.g. "0.107.1"). Empty = all snapshots.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Tag v0.2.0

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Tag v0.2.0-beta.1 -Versions 0.107.1,0.110.1
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $false)][string]$ApiRoot = 'F:\sts2-mod\sts2-versions',
    [Parameter(Mandatory = $false)][string]$Configuration = 'Release',
    [Parameter(Mandatory = $false)][string]$Sts2Path = '',
    [Parameter(Mandatory = $false)][string[]]$Versions = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$modId = 'ExtractionRun'
$modManifest = Join-Path $repoRoot "$modId.json"
$contentProject = Join-Path $repoRoot "$modId.csproj"
$buildVariants = Join-Path $PSScriptRoot 'build-variants.ps1'
$zipName = "$modId-$Tag.zip"

Push-Location $repoRoot
try {
    # ── Preflight ────────────────────────────────────────────────
    if ($Tag -notmatch '^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Invalid tag '$Tag'. Expected semver like v0.2.0 or v0.2.0-beta.1."
    }
    if (-not (git rev-parse --is-inside-work-tree 2>$null)) { throw 'Not inside a git repository.' }
    if (git tag -l $Tag) { throw "Tag '$Tag' already exists locally." }

    # Working tree must be clean except the two files this workflow manages
    # (the /release command writes CHANGELOG.md before invoking this script).
    foreach ($line in (git status --porcelain)) {
        $path = ($line -replace '^\S+\s+', '').Trim()
        if ($path -ne "$modId.json" -and $path -ne 'CHANGELOG.md') {
            throw "Working tree not clean: unexpected change at '$path'. Commit or stash it before releasing."
        }
    }

    # ── Resolve game path (shared with build-variants.ps1) ──────
    if ([string]::IsNullOrWhiteSpace($Sts2Path)) {
        $uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2868840'
        $installLocation = (Get-ItemProperty -Path $uninstallKey -ErrorAction SilentlyContinue).InstallLocation
        if ([string]::IsNullOrWhiteSpace($installLocation)) {
            throw "Could not auto-detect the game. Pass -Sts2Path <game root>."
        }
        $Sts2Path = $installLocation
    }
    $Sts2Path = (Resolve-Path $Sts2Path).Path
    $modRoot = Join-Path (Join-Path $Sts2Path 'mods') $modId
    $pckPath = Join-Path $modRoot "$modId.pck"

    # ── Bump manifest version (deployed to the mods folder by the build below) ──
    $json = Get-Content $modManifest -Raw
    if ($json -notmatch '"version"\s*:\s*"([^"]*)"') {
        throw "Could not locate the version field in $modManifest"
    }
    $oldVersion = $Matches[1]
    $json = $json -replace '"version"\s*:\s*"[^"]*"', ('"version": "' + $Tag + '"')
    [System.IO.File]::WriteAllText($modManifest, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Bumped $modId.json version: $oldVersion -> $Tag"

    # ── Build: .pck first, then the multi-version bundle ─────────
    # build-variants.ps1 never touches the .pck (PckPackerEnabled=false), so publish must run
    # first or the release ships a stale pck. GodotPublish swallows Godot's exit code, so verify
    # the pck actually refreshed instead of trusting dotnet's exit status.
    $pckBefore = if (Test-Path $pckPath) { (Get-Item $pckPath).LastWriteTimeUtc } else { [datetime]::MinValue }
    Write-Host "`n-- dotnet publish (pck + installed-game variant) --"
    $publishArgs = @('publish', $contentProject, '-c', $Configuration, ('/p:Sts2Path=' + $Sts2Path))
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
    if (-not (Test-Path $pckPath)) { throw "dotnet publish did not produce $pckPath" }
    if ((Get-Item $pckPath).LastWriteTimeUtc -le $pckBefore) {
        throw "$modId.pck was not refreshed by dotnet publish — check the Godot export (GodotPath in Directory.Build.props)."
    }

    Write-Host "`n-- build-variants (multi-version bundle) --"
    & $buildVariants -ApiRoot $ApiRoot -Configuration $Configuration -Sts2Path $Sts2Path -Versions $Versions

    # ── Package: white-list only. The live mods folder also holds the workshop-uploader cache
    #    and older release zips, which must never leak into the archive.
    Write-Host "`n-- packaging --"
    $rootFiles = @("$modId.dll", "$modId.json", "$modId.pck", 'extractionrun-variants.manifest')
    $variantFiles = @('ExtractionRun.Content.dll', 'compat-target.txt')
    $libRoot = Join-Path $modRoot 'lib'

    $distDir = Join-Path $repoRoot 'dist'
    $stageDir = Join-Path $distDir 'stage'
    if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

    $missing = @()
    foreach ($f in $rootFiles) {
        $src = Join-Path $modRoot $f
        if (-not (Test-Path $src)) { $missing += $f; continue }
        Copy-Item -Force $src (Join-Path $stageDir $f)
    }
    $covered = @()
    if (Test-Path $libRoot) {
        foreach ($verDir in (Get-ChildItem -Directory $libRoot | Sort-Object Name)) {
            $verRel = "lib/$($verDir.Name)"
            New-Item -ItemType Directory -Force -Path (Join-Path $stageDir $verRel) | Out-Null
            foreach ($vf in $variantFiles) {
                $src = Join-Path $verDir.FullName $vf
                if (-not (Test-Path $src)) { $missing += "$verRel/$vf"; continue }
                Copy-Item -Force $src (Join-Path $stageDir "$verRel/$vf")
            }
            $covered += (Get-Content (Join-Path $verDir.FullName 'compat-target.txt') -Raw).Trim()
        }
    } else {
        $missing += 'lib/ (no variant folders at all)'
    }
    if ($missing.Count -gt 0) { throw "Bundle incomplete — missing: $($missing -join ', ')" }

    $zipPath = Join-Path $distDir $zipName
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath)

    # Re-open the zip and assert every expected entry is present before discarding the stage.
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $expected = @($rootFiles) + @($covered | ForEach-Object { "lib/$_/ExtractionRun.Content.dll" }) `
            + @($covered | ForEach-Object { "lib/$_/compat-target.txt" })
        $missingInZip = @($expected | Where-Object { $entries -notcontains $_ })
        if ($missingInZip.Count -gt 0) { throw "Zip missing entries: $($missingInZip -join ', ')" }
    } finally {
        $zip.Dispose()
    }
    Remove-Item -Recurse -Force $stageDir
    $zipSize = (Get-Item $zipPath).Length
    Write-Host "Packaged $zipPath ($zipSize bytes)"

    # ── Commit + local tag (no push — outward steps belong to the command's confirm gate) ──
    Write-Host "`n-- commit + tag --"
    $preHead = (git rev-parse HEAD).Trim()
    git add "$modId.json"
    if (Test-Path (Join-Path $repoRoot 'CHANGELOG.md')) { git add 'CHANGELOG.md' }
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { throw "Nothing to commit: version already at $Tag and CHANGELOG.md unchanged." }
    git commit -m "release: $Tag"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)." }
    $commitSha = (git rev-parse HEAD).Trim()
    git tag $Tag
    if ($LASTEXITCODE -ne 0) { throw "git tag failed (exit $LASTEXITCODE)." }

    Write-Host "`n== RELEASE SUMMARY =="
    Write-Host "TAG=$Tag"
    Write-Host "COMMIT=$commitSha"
    Write-Host "ZIP=$zipPath"
    Write-Host "COVERED=$($covered -join ',')"
    Write-Host "== END =="
} finally {
    Pop-Location
}
