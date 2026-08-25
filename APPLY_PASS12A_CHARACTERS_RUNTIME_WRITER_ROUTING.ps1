$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'
$webPath  = Join-Path $root 'World\SmallTown\Population\FamilyFriendWebSystem.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $repoPath)) { throw 'Missing Characters\Base\CharacterRepository.cs' }
if (-not (Test-Path $webPath))  { throw 'Missing World\SmallTown\Population\FamilyFriendWebSystem.cs' }

$repo = Get-Content $repoPath -Raw
$web  = Get-Content $webPath -Raw

# Safety checks based on the exact local Pass 12 preflight.
if ($web -notmatch 'private\s+static\s+void\s+UpdateCharacterMaterializationTier\s*\(\s*int\s+npcId\s*,\s*int\s+webTier\s*\)') {
    throw 'Could not find UpdateCharacterMaterializationTier(int npcId, int webTier).'
}
if ($web -notmatch '(?i)UPDATE\s+Characters') {
    throw 'FamilyFriendWebSystem no longer directly updates Characters. Stop.'
}
if ($repo -match '\bLowerMaterializationTier\s*\(') {
    throw 'CharacterRepository.LowerMaterializationTier already exists. Stop; Pass 12A may already be applied.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass12A' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$repoBackup = Join-Path $backupRoot 'Characters\Base\CharacterRepository.cs'
$webBackup  = Join-Path $backupRoot 'World\SmallTown\Population\FamilyFriendWebSystem.cs'
$repoArchive = Join-Path $archiveRoot 'Characters\Base\CharacterRepository.pre-pass12a.cs'
$webArchive  = Join-Path $archiveRoot 'World\SmallTown\Population\FamilyFriendWebSystem.pre-pass12a.cs'

foreach ($p in @($repoBackup,$webBackup,$repoArchive,$webArchive)) {
    New-Item -ItemType Directory -Path (Split-Path $p -Parent) -Force | Out-Null
}
Copy-Item $repoPath $repoBackup -Force
Copy-Item $webPath  $webBackup -Force
Copy-Item $repoPath $repoArchive -Force
Copy-Item $webPath  $webArchive -Force

# -------------------------------------------------------------------------
# 1. Add canonical Characters runtime gateway for the materialization Tier.
# -------------------------------------------------------------------------
$insertAnchor = '        public static void PrintCharacterSheet(SimCharacter eve)'
$anchorIndex = $repo.IndexOf($insertAnchor)

if ($anchorIndex -lt 0) {
    throw 'Could not find CharacterRepository insertion anchor near PrintCharacterSheet.'
}

$repoMethod = @'
        /// <summary>
        /// Canonical runtime gateway for lowering Characters.Tier.
        /// Lower numeric tiers represent more materialized / important NPCs.
        /// Relationship systems may request a lower tier, but they do not write
        /// the Characters table directly.
        /// </summary>
        public static void LowerMaterializationTier(int npcId, int requestedTier)
        {
            if (npcId <= 0)
                return;

            requestedTier = Math.Clamp(requestedTier, 1, 5);

            EnsureDataDir();
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Characters
                SET Tier = CASE
                    WHEN Tier IS NULL THEN $tier
                    WHEN $tier < Tier THEN $tier
                    ELSE Tier
                END,
                UpdatedRealAt = CURRENT_TIMESTAMP
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue("$tier", requestedTier);
            cmd.Parameters.AddWithValue("$id", npcId);
            cmd.ExecuteNonQuery();
        }

'@

$repo = $repo.Insert($anchorIndex, $repoMethod)

# -------------------------------------------------------------------------
# 2. Replace FamilyFriendWebSystem's direct SQL writer with repository call.
#    Find the exact method block by brace matching.
# -------------------------------------------------------------------------
$methodMatch = [regex]::Match(
    $web,
    'private\s+static\s+void\s+UpdateCharacterMaterializationTier\s*\(\s*int\s+npcId\s*,\s*int\s+webTier\s*\)\s*\{',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

if (-not $methodMatch.Success) {
    throw 'Could not locate UpdateCharacterMaterializationTier method block.'
}

$openBrace = $web.IndexOf('{', $methodMatch.Index)
$depth = 0
$closeBrace = -1

for ($i = $openBrace; $i -lt $web.Length; $i++) {
    if ($web[$i] -eq '{') { $depth++ }
    elseif ($web[$i] -eq '}') {
        $depth--
        if ($depth -eq 0) {
            $closeBrace = $i
            break
        }
    }
}

if ($closeBrace -lt 0) {
    throw 'Could not find UpdateCharacterMaterializationTier closing brace.'
}

$replacement = @'
private static void UpdateCharacterMaterializationTier(int npcId, int webTier)
    {
        // Family/friend relationship truth lives in the relationships DB.
        // Characters.Tier is current NPC materialization truth in the MAIN DB,
        // so all runtime writes route through CharacterRepository.
        ProjectEve.Characters.Base.CharacterRepository.LowerMaterializationTier(
            npcId,
            webTier);
    }
'@

$web = $web.Substring(0, $methodMatch.Index) + $replacement + $web.Substring($closeBrace + 1)

# Safety: FamilyFriendWebSystem must no longer directly update Characters.
if ($web -match '(?i)UPDATE\s+Characters') {
    throw 'Safety check failed: FamilyFriendWebSystem still contains UPDATE Characters.'
}
if ($repo -notmatch '\bLowerMaterializationTier\s*\(') {
    throw 'Safety check failed: repository gateway was not added.'
}

Set-Content $repoPath $repo -Encoding UTF8
Set-Content $webPath  $web  -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 12A - CHARACTERS RUNTIME WRITER ROUTING
============================================

CANONICAL CURRENT NPC TABLE:
Characters in project_eve.db

CANONICAL RUNTIME GATEWAY:
Characters\Base\CharacterRepository.cs

ROUTED:
World\SmallTown\Population\FamilyFriendWebSystem.cs
  BEFORE: directly UPDATE Characters SET Tier...
  AFTER:  CharacterRepository.LowerMaterializationTier(...)

WHY:
Family/friend relationship state belongs in relationships.db.
It may influence NPC materialization importance, but it should not own a direct
writer to the canonical Characters table.

PROGRAM.CS:
The three remaining direct Characters writes in Program.cs are intentionally
left unchanged in Pass 12A. They are currently seeder/bootstrap writes:
- identity stub INSERT
- folder metadata UPDATE
- core/full NPC INSERT

They are classified TEMPORARY SEEDER-ONLY and will be routed separately after
the runtime writer path is clean.

NO DATA DELETION:
- no table drops
- no row deletes
- no NPC purge
'@ | Set-Content (Join-Path $reportRoot 'PASS12A_CHARACTERS_RUNTIME_WRITER_ROUTING.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 12A Characters runtime writer routing applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'FamilyFriendWebSystem no longer writes Characters directly.'
Write-Host 'Canonical runtime gateway: CharacterRepository.LowerMaterializationTier'
Write-Host ''
Write-Host 'Program.cs seeder/bootstrap writes were NOT changed.'
Write-Host 'No database rows, tables, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
