# Cutover helper: copies the validated /staging/* tree into the live root.
#
# Migrates:
#   /staging/cardMetadata        -> /cardMetadata
#   /staging/cardMetadataVersion -> /cardMetadataVersion
#   /staging/gameModes           -> /gameModes
#   /staging/legalityCache       -> /legalityCache
#
# OVERWRITES existing prod data at those paths. Does NOT touch:
#   - /lobbies, /staging/lobbies, /discordMessages (ephemeral, irrelevant)
#   - /formats (legacy, kept around for old plugin versions still pointing at prod)
#
# Reads via `firebase database:get`, writes via `firebase database:set`.
# Each path is dumped to a temp file so the round-trip is auditable.
#
# Usage:
#   tools\cutover_to_prod.ps1
#
# Requires:
#   - firebase CLI (`firebase login` already done)
#   - Windows PowerShell 5.1 or later
#   - Run from the repo root

param(
    [string]$ProjectId = "mtga-enhancement-suite",
    [string]$WorkDir   = "$env:TEMP\mtga-cutover"
)

$ErrorActionPreference = "Stop"

# Ensure we're in firebase/ for database:set to find rules
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$firebaseDir = Join-Path $repoRoot "firebase"
if (-not (Test-Path $firebaseDir)) {
    Write-Error "Cannot find firebase/ directory at $firebaseDir"
    exit 1
}
Push-Location $firebaseDir

try {
    if (-not (Test-Path $WorkDir)) {
        New-Item -ItemType Directory -Path $WorkDir | Out-Null
    }
    Write-Host "Cutover working dir: $WorkDir"
    Write-Host ""

    $paths = @(
        "cardMetadata",
        "cardMetadataVersion",
        "gameModes",
        "legalityCache"
    )

    # Phase 1: dump every staging path to disk first. If any export fails,
    # we abort BEFORE touching prod — leaves prod pristine.
    Write-Host "=== Phase 1: exporting /staging/* ==="
    foreach ($p in $paths) {
        $outFile = Join-Path $WorkDir "$p.json"
        Write-Host "  Exporting /staging/$p -> $outFile"
        firebase database:get "/staging/$p" --project $ProjectId -o $outFile
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Export of /staging/$p failed"
            exit 1
        }
        # Sanity check: file should exist and not be `null`
        $size = (Get-Item $outFile).Length
        $content = (Get-Content $outFile -Raw).Trim()
        if ($content -eq "null" -or $size -lt 4) {
            Write-Warning "  /staging/$p is empty/null. Aborting cutover."
            exit 1
        }
        Write-Host "    OK ($size bytes)"
    }
    Write-Host ""

    # Phase 2: confirm before clobbering prod
    Write-Host "=== Phase 2: about to OVERWRITE prod paths ==="
    foreach ($p in $paths) { Write-Host "  /$p" }
    Write-Host ""
    $confirm = Read-Host "Type 'yes' to proceed"
    if ($confirm -ne "yes") {
        Write-Host "Aborted by user. Staging snapshots are still in $WorkDir."
        exit 0
    }
    Write-Host ""

    # Phase 3: write each path to prod.
    # Firebase CLI uses `-f` / `--force` to skip the overwrite prompt
    # (renamed from `--confirm` in older releases).
    Write-Host "=== Phase 3: writing prod ==="
    foreach ($p in $paths) {
        $inFile = Join-Path $WorkDir "$p.json"
        Write-Host "  Writing $inFile -> /$p"
        firebase database:set "/$p" $inFile --project $ProjectId --force
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Write to /$p failed. Other paths may be partially migrated; investigate before retrying."
            exit 1
        }
        Write-Host "    OK"
    }
    Write-Host ""

    Write-Host "=== Cutover complete ==="
    Write-Host "Staging snapshots preserved in $WorkDir for rollback purposes."
    Write-Host "To roll back, re-run with --src prod, --dst the same paths."
}
finally {
    Pop-Location
}
