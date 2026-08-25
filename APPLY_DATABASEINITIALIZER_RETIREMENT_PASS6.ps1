$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$relative = 'DATA\DatabaseInitializer.cs'
$target = Join-Path $root $relative
if (-not (Test-Path $target)) {
    throw 'DATA\DatabaseInitializer.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass6' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\CanonicalPass6' $stamp

$backupFile = Join-Path $backupRoot $relative
$archiveFile = Join-Path $archiveRoot 'DATA\DatabaseInitializer.legacy.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null

Copy-Item $target $backupFile -Force
Copy-Item $target $archiveFile -Force

$packageRoot = Split-Path $MyInvocation.MyCommand.Path -Parent
$source = Join-Path $packageRoot $relative

if (Test-Path $source) {
    $srcFull = [System.IO.Path]::GetFullPath($source)
    $dstFull = [System.IO.Path]::GetFullPath($target)

    if (-not $srcFull.Equals($dstFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item $source $target -Force
    }
}
else {
    # If the package was extracted directly over the project, the new file
    # may already be in place. Validate its marker before proceeding.
    $current = Get-Content $target -Raw
    if ($current -notmatch 'Legacy bootstrap compatibility shim') {
        throw 'Pass 6 replacement DatabaseInitializer.cs was not found.'
    }
}

# Source-level sanity check: the active DatabaseInitializer must not contain
# any schema creation or legacy SQL ownership after this pass.
$current = Get-Content $target -Raw

$forbidden = @(
    'CREATE TABLE',
    'INSERT OR REPLACE INTO Characters',
    'DELETE FROM Relationships',
    'INSERT INTO Relationships',
    'INSERT INTO Traits',
    'CREATE TABLE IF NOT EXISTS Traits',
    'CREATE TABLE IF NOT EXISTS Relationships',
    'CREATE TABLE IF NOT EXISTS Memories',
    'CREATE TABLE IF NOT EXISTS Locations'
)

foreach ($token in $forbidden) {
    if ($current.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw ('Legacy ownership token still present in active DatabaseInitializer.cs: ' + $token)
    }
}

# Create a small source-ownership report outside the project tree.
$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

$report = @'
PASS 6 - DATABASE INITIALIZER OWNERSHIP
=======================================

ACTIVE:
DATA\DatabaseInitializer.cs

ROLE:
Compatibility bootstrap shim only.

ALLOWED:
- DatabaseInitializer.DbPath -> canonical ProjectEveDatabaseSetup.MainDatabasePath
- DatabaseInitializer.Initialize() -> ProjectEveDatabaseSetup.EnsureAll()

RETIRED FROM DatabaseInitializer:
- Characters schema ownership
- Appearance schema ownership
- Traits / TraitControl ownership
- Relationships ownership
- Memories ownership
- History ownership
- Location ownership
- MoneyProfile ownership
- JobProfile ownership
- Eve seed writes
- location seed writes
- history-v1 schema creation

LEGACY TABLES:
Existing legacy tables are intentionally NOT dropped in Pass 6.
They remain only as migration leftovers until the final source audit proves
there are no remaining readers/writers.

ARCHIVED LEGACY SOURCE:
D:\ProjectEve\Archive\CanonicalPass6\<timestamp>\DATA\DatabaseInitializer.legacy.cs
'@

$report | Set-Content (Join-Path $reportRoot 'PASS6_DATABASEINITIALIZER_OWNERSHIP.txt') -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'DatabaseInitializer Retirement Pass 6 applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'DatabaseInitializer is now a compatibility shim only.'
Write-Host 'No legacy tables were dropped.'
Write-Host 'No NPCs or databases were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
