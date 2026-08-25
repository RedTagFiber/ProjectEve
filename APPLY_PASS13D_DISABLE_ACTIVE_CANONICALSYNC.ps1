$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$programPath = Join-Path $root 'Program.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $programPath)) {
    throw 'Program.cs was not found.'
}

$text = Get-Content $programPath -Raw

$patterns = @(
    'ProjectEveCanonicalSync.SyncAll(); // 4DB VERIFY SYNC',
    'ProjectEveCanonicalSync.SyncAll(); // 4DB STARTUP SYNC',
    'ProjectEveCanonicalSync.SyncAll(); // 4DB NPC SAVE SYNC'
)

foreach ($p in $patterns) {
    if ($text -notlike ('*' + $p + '*')) {
        throw ('Expected call not found: ' + $p)
    }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass13D' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$backupFile = Join-Path $backupRoot 'Program.cs'
$archiveFile = Join-Path $archiveRoot 'Program.pre-pass13d-canonical-sync.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null

Copy-Item $programPath $backupFile -Force
Copy-Item $programPath $archiveFile -Force

$text = $text.Replace(
    'ProjectEveCanonicalSync.SyncAll(); // 4DB VERIFY SYNC',
    '// Canonical DBs are now written directly by their owning repositories; legacy SyncAll disabled.')

$text = $text.Replace(
    'ProjectEveCanonicalSync.SyncAll(); // 4DB STARTUP SYNC',
    '// Canonical DBs are now written directly by their owning repositories; legacy SyncAll disabled.')

$text = $text.Replace(
    'ProjectEveCanonicalSync.SyncAll(); // 4DB NPC SAVE SYNC',
    '// Canonical DBs are now written directly by their owning repositories; legacy SyncAll disabled.')

if ($text -match 'ProjectEveCanonicalSync\.SyncAll\s*\(') {
    throw 'Safety check failed: Program.cs still calls ProjectEveCanonicalSync.SyncAll().'
}

Set-Content $programPath $text -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 13D - DISABLE ACTIVE PROJECTEVECANONICALSYNC CALLS
======================================================

FOUND ACTIVE CALL SITES:
- Program.cs verify path
- Program.cs startup path
- Program.cs NPC-save path

ACTION:
Removed all three active calls to ProjectEveCanonicalSync.SyncAll().

WHY:
The canonical DBs now have direct owners/gateways:
- current NPC state -> CharacterRepository
- traits -> NpcTraitRepository
- relationships -> RelationshipRepository
- personal memories -> MemoryDatabase
- history -> history services/repositories
- locations -> location services/repositories

ProjectEveCanonicalSync remains in source temporarily as an inert migration
bridge. It is NOT archived in this pass.

NO DATA DELETION:
- no tables dropped
- no rows deleted
- no memories deleted
- no relationships deleted
- no NPCs deleted

NEXT:
Build and verify. If clean, archive ProjectEveCanonicalSync.cs in a later pass.
'@ | Set-Content (Join-Path $reportRoot 'PASS13D_DISABLE_ACTIVE_CANONICAL_SYNC.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 13D active CanonicalSync calls disabled.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'Removed active SyncAll calls from:'
Write-Host '  verify'
Write-Host '  startup'
Write-Host '  NPC save'
Write-Host ''
Write-Host 'ProjectEveCanonicalSync.cs remains in place but is no longer called by Program.cs.'
Write-Host 'No database rows, tables, memories, relationships, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
