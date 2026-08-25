$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$legacyRel = 'DATA\World\Scene\ScenePerceptionService.cs'
$keepRel   = 'Scene\ScenePerceptionService.cs'
$legacy = Join-Path $root $legacyRel
$keep   = Join-Path $root $keepRel

if (-not (Test-Path $legacy)) { throw "Legacy duplicate not found: $legacyRel" }
if (-not (Test-Path $keep))   { throw "Canonical scene service not found: $keepRel" }

$legacyText = Get-Content $legacy -Raw
$keepText   = Get-Content $keep -Raw

# Safety checks from the exact local preflight:
# - Scene copy is the newer implementation with facing-aware visibility.
# - DATA copy is the older implementation.
if ($keepText -notmatch 'FacingVisibilityFactor') {
    throw 'Canonical Scene copy does not contain FacingVisibilityFactor. Stop.'
}
if ($legacyText -match 'FacingVisibilityFactor') {
    throw 'Legacy DATA copy unexpectedly contains FacingVisibilityFactor. Stop.'
}

# Refuse to retire if any compiled source explicitly references the legacy namespace.
$explicitRefs = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -ne $legacy
    } |
    Select-String -Pattern 'ProjectEve\.DATA\.World\.Scene\.ScenePerceptionService|ProjectEve\.DATA\.World\.Scene' -CaseSensitive:$false

if ($explicitRefs) {
    Write-Host ''
    Write-Host 'Explicit references to the legacy DATA scene namespace were found:' -ForegroundColor Red
    $explicitRefs | ForEach-Object {
        Write-Host ('  ' + $_.Path + ':' + $_.LineNumber + '  ' + $_.Line.Trim())
    }
    throw 'Pass 9 stopped before changing source.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass9' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$backupFile  = Join-Path $backupRoot $legacyRel
$archiveFile = Join-Path $archiveRoot $legacyRel

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null

Copy-Item $legacy $backupFile -Force
Copy-Item $legacy $archiveFile -Force

# Retire the duplicate from the active compile tree.
Remove-Item $legacy -Force

# Remove empty directories only if empty.
$legacyDir = Split-Path $legacy -Parent
try {
    if ((Get-ChildItem $legacyDir -Force | Measure-Object).Count -eq 0) {
        Remove-Item $legacyDir -Force
    }
} catch { }

# Architecture note.
$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 9 - SCENE PERCEPTION DUPLICATE RETIREMENT
==============================================

KEEP CANONICAL:
Scene\ScenePerceptionService.cs
Namespace: ProjectEve.Scene

WHY:
The local preflight showed this is the newer implementation.
It includes FacingVisibilityFactor and applies observer facing direction to
visual perception.

RETIRED DUPLICATE:
DATA\World\Scene\ScenePerceptionService.cs
Namespace: ProjectEve.DATA.World.Scene

The retired file duplicated runtime ownership of:
- ActiveScene
- ScenePresence
- SceneBarrier
- ScenePerceptionEvidence

No SQL tables or rows were deleted.
Only the duplicate C# implementation was removed from the active compile tree.

Archive:
D:\ProjectEve\Archive\LegacyCode\<timestamp>\DATA\World\Scene\ScenePerceptionService.cs
'@ | Set-Content (Join-Path $reportRoot 'PASS9_SCENE_PERCEPTION_OWNER.txt') -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 9 duplicate ScenePerceptionService retired.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'KEEP:    Scene\ScenePerceptionService.cs'
Write-Host 'RETIRED: DATA\World\Scene\ScenePerceptionService.cs'
Write-Host ''
Write-Host 'No database rows, tables, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
