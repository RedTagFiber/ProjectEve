$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$syncRel = 'DATA\ProjectEveCanonicalSync.cs'
$syncPath = Join-Path $root $syncRel

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $syncPath)) {
    throw 'DATA\ProjectEveCanonicalSync.cs was not found. Pass 13E may already be applied.'
}

# Final safety: no compiled source outside the sync file may reference it.
$refs = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*' -and
        $_.FullName -ne $syncPath
    } |
    Select-String -Pattern 'ProjectEveCanonicalSync|SyncAll\s*\(|SyncRelationships\s*\(|SyncHistory\s*\(|SyncLocations\s*\('

if ($refs) {
    Write-Host ''
    Write-Host 'Pass 13E stopped: references still exist.' -ForegroundColor Red
    $refs | ForEach-Object {
        Write-Host ($_.Path + ':' + $_.LineNumber + '  ' + $_.Line.Trim())
    }
    throw 'Remove/inspect remaining CanonicalSync call sites before archiving.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass13E' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$backupFile = Join-Path $backupRoot $syncRel
$archiveFile = Join-Path $archiveRoot $syncRel

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null

Copy-Item $syncPath $backupFile -Force
Move-Item $syncPath $archiveFile -Force

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@"
PASS 13E - ARCHIVE PROJECTEVECANONICALSYNC
=========================================

ARCHIVED ACTIVE SOURCE:
$syncRel

ARCHIVE LOCATION:
$archiveFile

WHY:
Pass 13C found only three external SyncAll call sites.
Pass 13D disabled all three.
Build and verify succeeded with CanonicalSync inactive.

RESULT:
ProjectEveCanonicalSync is no longer part of the active compiled project.

CANONICAL OWNERS REMAIN:
- Characters -> CharacterRepository
- NpcTraitValues -> NpcTraitRepository
- RelationshipStates -> RelationshipRepository
- PersonalMemories -> MemoryDatabase
- schema -> ProjectEveDatabaseSetup

NO DATA DELETION:
- no tables dropped
- no rows deleted
- no memories deleted
- no relationships deleted
- no NPCs deleted
"@ | Set-Content (Join-Path $reportRoot 'PASS13E_ARCHIVE_CANONICAL_SYNC.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 13E ProjectEveCanonicalSync archived.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'ProjectEveCanonicalSync.cs is no longer inside the active project tree.'
Write-Host 'No database rows, tables, memories, relationships, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
