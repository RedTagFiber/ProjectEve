$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$programPath = Join-Path $root 'Program.cs'
$repoPath = Join-Path $root 'Characters\Traits\NpcTraitRepository.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $programPath)) { throw 'Program.cs was not found.' }
if (-not (Test-Path $repoPath)) { throw 'Characters\Traits\NpcTraitRepository.cs was not found.' }

$program = Get-Content $programPath -Raw
$repo = Get-Content $repoPath -Raw

if ($program -notmatch '(?i)INSERT\s+OR\s+IGNORE\s+INTO\s+NpcTraitValues') {
    throw 'Program.cs no longer contains the legacy NpcTraitValues INSERT OR IGNORE. Stop; Pass 13A may already be applied.'
}
if ($repo -notmatch 'public\s+static\s+void\s+SaveAll\s*\(\s*int\s+npcId\s*,\s*NpcTraits\s+traits\s*\)') {
    throw 'NpcTraitRepository.SaveAll(int npcId, NpcTraits traits) was not found.'
}

# Locate the direct SQL statement in Program.cs.
$hit = [regex]::Match(
    $program,
    '(?i)INSERT\s+OR\s+IGNORE\s+INTO\s+NpcTraitValues')

if (-not $hit.Success) {
    throw 'Could not locate legacy Program trait write.'
}

# Find nearest enclosing static method by scanning backwards.
$prefix = $program.Substring(0, $hit.Index)
$methodMatches = [regex]::Matches(
    $prefix,
    'static\s+(?:void|int|bool|string|[A-Za-z_][A-Za-z0-9_<>,\?\.\[\]]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^)]*\)\s*\{',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

if ($methodMatches.Count -eq 0) {
    throw 'Could not identify the enclosing Program.cs trait-write method.'
}

$method = $methodMatches[$methodMatches.Count - 1]
$methodName = $method.Groups[1].Value
$openBrace = $program.IndexOf('{', $method.Index)

# Find matching closing brace.
$depth = 0
$closeBrace = -1
for ($i = $openBrace; $i -lt $program.Length; $i++) {
    if ($program[$i] -eq '{') { $depth++ }
    elseif ($program[$i] -eq '}') {
        $depth--
        if ($depth -eq 0) {
            $closeBrace = $i
            break
        }
    }
}

if ($closeBrace -lt 0) {
    throw 'Could not find closing brace for the Program trait-write method.'
}

$oldMethod = $program.Substring(
    $method.Index,
    ($closeBrace - $method.Index) + 1)

# Strong safety checks before replacing anything.
if ($oldMethod -notmatch '(?i)NpcTraitValues') {
    throw 'Selected method does not contain NpcTraitValues. Stop.'
}
if ($oldMethod -notmatch '\bnpc\.Traits\b') {
    throw 'Selected method does not reference npc.Traits. Stop.'
}

# Extract original method signature up to opening brace.
$signature = $program.Substring($method.Index, $openBrace - $method.Index).TrimEnd()

# Require a SimCharacter npc parameter so the replacement is semantically safe.
if ($signature -notmatch 'SimCharacter\s+npc') {
    throw ('Trait write method "' + $methodName + '" does not have SimCharacter npc parameter. Stop.')
}

$newMethod = $signature + @'
{
        if (npc == null || npc.Id <= 0 || npc.Traits == null)
            return;

        // Canonical NpcTraitValues runtime write ownership belongs to
        // NpcTraitRepository. Program.cs is seeder/bootstrap orchestration only.
        ProjectEve.Characters.Traits.NpcTraitRepository.SaveAll(
            npc.Id,
            npc.Traits);
    }
'@

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass13A' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$backupFile  = Join-Path $backupRoot 'Program.cs'
$archiveFile = Join-Path $archiveRoot 'Program.pre-pass13a-trait-writer.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null

Copy-Item $programPath $backupFile -Force
Copy-Item $programPath $archiveFile -Force

$program = $program.Substring(0, $method.Index) + $newMethod + $program.Substring($closeBrace + 1)

# Final safety: Program must no longer directly write NpcTraitValues.
if ($program -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+NpcTraitValues\b' -or
    $program -match '(?i)(?:^|[\s;"@])UPDATE\s+NpcTraitValues\b' -or
    $program -match '(?i)\bDELETE\s+FROM\s+NpcTraitValues\b') {
    throw 'Safety check failed: Program.cs still directly writes NpcTraitValues.'
}

Set-Content $programPath $program -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@"
PASS 13A - NPC TRAIT RUNTIME WRITER ROUTING
===========================================

CANONICAL TABLE:
NpcTraitValues in project_eve.db

SCHEMA OWNER:
DATA\ProjectEveDatabaseSetup.cs

CANONICAL RUNTIME WRITE GATEWAY:
Characters\Traits\NpcTraitRepository.cs

ROUTED PROGRAM METHOD:
$methodName

BEFORE:
Program.cs directly INSERT OR IGNORE INTO NpcTraitValues.

AFTER:
Program.cs calls:
NpcTraitRepository.SaveAll(npc.Id, npc.Traits)

RESULT:
Program.cs should have zero direct NpcTraitValues writes.

NO DATA DELETION:
- no table drops
- no row deletes
- no NPC purge
"@ | Set-Content (Join-Path $reportRoot 'PASS13A_NPC_TRAIT_RUNTIME_WRITER_ROUTING.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 13A NpcTraitValues writer routing applied.' -ForegroundColor Green
Write-Host ('Routed Program method: ' + $methodName)
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'Program.cs direct NpcTraitValues writes: 0 expected'
Write-Host 'Canonical gateway: Characters\Traits\NpcTraitRepository.cs'
Write-Host ''
Write-Host 'No database rows, tables, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
