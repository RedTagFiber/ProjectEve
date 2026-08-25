$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'Memory\MemoryDatabase.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $path)) {
    throw 'Memory\MemoryDatabase.cs was not found.'
}

$text = Get-Content $path -Raw

# Safety: Pass 10 is only for the CURRENT patched MemoryDatabase that owns
# PersonalMemories. Do not touch an old GitHub-style Memories implementation.
if ($text -notmatch '\bPersonalMemories\b') {
    throw 'This local MemoryDatabase does not contain PersonalMemories. Stop and send the file.'
}

$methodMatch = [regex]::Match(
    $text,
    'private\s+void\s+EnsureSchema\s*\(\s*\)\s*\{',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

if (-not $methodMatch.Success) {
    throw 'Could not find private void EnsureSchema().'
}

$openBrace = $text.IndexOf('{', $methodMatch.Index)
if ($openBrace -lt 0) {
    throw 'Could not locate EnsureSchema opening brace.'
}

# Find matching closing brace. SQL strings in this method do not use C# braces;
# if they ever do, this pass stops through its post-replacement safety checks.
$depth = 0
$closeBrace = -1
for ($i = $openBrace; $i -lt $text.Length; $i++) {
    $ch = $text[$i]
    if ($ch -eq '{') {
        $depth++
    }
    elseif ($ch -eq '}') {
        $depth--
        if ($depth -eq 0) {
            $closeBrace = $i
            break
        }
    }
}

if ($closeBrace -lt 0) {
    throw 'Could not locate EnsureSchema closing brace.'
}

$oldMethod = $text.Substring(
    $methodMatch.Index,
    ($closeBrace - $methodMatch.Index) + 1)

# Require the method we are replacing to really be a schema owner.
if ($oldMethod -notmatch '(?i)CREATE\s+TABLE' -or
    $oldMethod -notmatch '\bPersonalMemories\b') {
    throw 'EnsureSchema did not contain the expected PersonalMemories CREATE TABLE. Stop.'
}

$newMethod = @'
private void EnsureSchema()
        {
            // Canonical schema ownership belongs only to ProjectEveDatabaseSetup.
            // MemoryDatabase remains the runtime read/write gateway for personal
            // memories, but it no longer creates or alters the table itself.
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();
        }
'@

$before = $text.Substring(0, $methodMatch.Index)
$after  = $text.Substring($closeBrace + 1)
$newText = $before + $newMethod + $after

# Safety checks before write.
if ($newText -match '(?s)private\s+void\s+EnsureSchema\s*\(\s*\).*?CREATE\s+TABLE') {
    throw 'Replacement safety check failed: EnsureSchema still contains CREATE TABLE.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass10' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp
$backupFile  = Join-Path $backupRoot 'Memory\MemoryDatabase.cs'
$archiveFile = Join-Path $archiveRoot 'Memory\MemoryDatabase.pre-schema-ownership.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null
Copy-Item $path $backupFile -Force
Copy-Item $path $archiveFile -Force

Set-Content $path $newText -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 10 - PERSONAL MEMORY SCHEMA OWNERSHIP
==========================================

CANONICAL SCHEMA OWNER:
DATA\ProjectEveDatabaseSetup.cs

RUNTIME MEMORY GATEWAY:
Memory\MemoryDatabase.cs

CHANGE:
MemoryDatabase no longer CREATEs or ALTERs PersonalMemories.
Its EnsureSchema() now delegates to ProjectEveDatabaseSetup.EnsureAll().

NOT CHANGED:
- MemoryDatabase runtime reads/writes
- ProjectEveCanonicalSync migration bridge
- Existing PersonalMemories data
- Relationships DB contents

WHY:
The post-Pass-9 runtime audit showed PersonalMemories had two schema creators:
1. ProjectEveDatabaseSetup
2. MemoryDatabase

This pass removes duplicate schema ownership without changing memory behavior.
'@ | Set-Content (Join-Path $reportRoot 'PASS10_PERSONAL_MEMORY_SCHEMA_OWNER.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 10 PersonalMemories schema ownership cleanup applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'KEEP schema owner: DATA\ProjectEveDatabaseSetup.cs'
Write-Host 'MemoryDatabase remains runtime memory read/write gateway.'
Write-Host ''
Write-Host 'No database rows, tables, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
