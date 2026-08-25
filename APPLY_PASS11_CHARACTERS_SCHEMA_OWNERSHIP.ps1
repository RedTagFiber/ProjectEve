$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'Program.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $path)) {
    throw 'Program.cs was not found.'
}

$text = Get-Content $path -Raw

# Safety checks from the local preflight.
if ($text -notmatch '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+Characters') {
    throw 'Program.cs no longer creates Characters. Stop; Pass 11 may already be applied.'
}
if ($text -notmatch 'ProjectEveDatabaseSetup') {
    throw 'Program.cs does not reference ProjectEveDatabaseSetup. Stop.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass11' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp
$backupFile  = Join-Path $backupRoot 'Program.cs'
$archiveFile = Join-Path $archiveRoot 'Program.pre-characters-schema-owner.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null
Copy-Item $path $backupFile -Force
Copy-Item $path $archiveFile -Force

# Remove only the Characters CREATE TABLE block inside Program.cs.
$createPattern = '(?s)\s*Execute\s*\(\s*conn\s*,\s*"""\s*CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+Characters\b.*?"""\s*\)\s*;'

$m = [regex]::Match(
    $text,
    $createPattern,
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

if (-not $m.Success) {
    throw 'Could not safely find the Characters CREATE TABLE Execute block.'
}

$text = $text.Remove($m.Index, $m.Length)

# Remove Program-owned ALTER/EnsureColumn ownership for Characters.
# These are schema mutations too, so they belong with ProjectEveDatabaseSetup.
$lines = $text -split "`r?`n"
$filtered = New-Object System.Collections.Generic.List[string]

$removedEnsureColumns = 0

foreach ($line in $lines) {
    if ($line -match '^\s*EnsureColumn\s*\(\s*conn\s*,\s*"Characters"\s*,') {
        $removedEnsureColumns++
        continue
    }
    $filtered.Add($line)
}

if ($removedEnsureColumns -eq 0) {
    throw 'No Characters EnsureColumn calls were found. Stop before writing.'
}

$text = ($filtered -join [Environment]::NewLine)

# Ensure Program delegates canonical schema before doing any Program-local table work.
$methodPattern = 'static\s+void\s+EnsureCoreTables\s*\(\s*\)\s*[^{]*\{'
$mm = [regex]::Match(
    $text,
    $methodPattern,
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

if (-not $mm.Success) {
    throw 'Could not find EnsureCoreTables().'
}

$brace = $text.IndexOf('{', $mm.Index)
if ($brace -lt 0) {
    throw 'Could not find EnsureCoreTables opening brace.'
}

$insertion = @'

        // Canonical Characters schema is owned by ProjectEveDatabaseSetup.
        ProjectEveDatabaseSetup.EnsureAll();

'@

# Don't duplicate if already present right at method start from a prior manual edit.
$lookAheadLen = [Math]::Min(300, $text.Length - $brace - 1)
$lookAhead = $text.Substring($brace + 1, $lookAheadLen)

if ($lookAhead -notmatch 'ProjectEveDatabaseSetup\.EnsureAll\(\)') {
    $text = $text.Insert($brace + 1, $insertion)
}

# Post-change safety checks.
if ($text -match '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+Characters') {
    throw 'Safety check failed: Program.cs still creates Characters.'
}
if ($text -match 'EnsureColumn\s*\(\s*conn\s*,\s*"Characters"\s*,') {
    throw 'Safety check failed: Program.cs still alters Characters.'
}

Set-Content $path $text -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@"
PASS 11 - CHARACTERS SCHEMA OWNERSHIP
=====================================

CANONICAL SCHEMA OWNER:
DATA\ProjectEveDatabaseSetup.cs

PROGRAM.CS:
- no longer CREATEs Characters
- no longer ALTERs/EnsureColumn's Characters
- delegates schema initialization to ProjectEveDatabaseSetup.EnsureAll()

RUNTIME WRITERS LEFT ACTIVE:
- Program.cs
- World\SmallTown\Population\FamilyFriendWebSystem.cs

Those writer responsibilities are intentionally NOT changed in Pass 11.
They will be classified separately after schema ownership is clean.

READERS LEFT ACTIVE:
- Program.cs
- Characters\Base\CharacterRepository.cs
- Memory\MemoryDatabase.cs
- World\WorldOccupancyService.cs
- World\SmallTown\HumanEvents\HumanEventScheduler.cs
- World\SmallTown\Population\FamilyFriendWebSystem.cs

Characters rows/data were not deleted.
Tables were not dropped.
NPCs were not deleted.

Removed Program EnsureColumn calls: $removedEnsureColumns
"@ | Set-Content (Join-Path $reportRoot 'PASS11_CHARACTERS_SCHEMA_OWNER.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 11 Characters schema ownership cleanup applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'KEEP schema owner: DATA\ProjectEveDatabaseSetup.cs'
Write-Host ('Removed Program Characters EnsureColumn calls: ' + $removedEnsureColumns)
Write-Host 'Program runtime character writes were NOT changed.'
Write-Host ''
Write-Host 'No database rows, tables, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
