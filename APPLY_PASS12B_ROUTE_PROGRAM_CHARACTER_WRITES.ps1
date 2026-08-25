$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$programPath = Join-Path $root 'Program.cs'
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $programPath)) { throw 'Program.cs was not found.' }
if (-not (Test-Path $repoPath)) { throw 'Characters\Base\CharacterRepository.cs was not found.' }

$program = Get-Content $programPath -Raw
$repo = Get-Content $repoPath -Raw

if ($program -notmatch '(?i)\bINSERT\s+INTO\s+Characters\b') {
    throw 'Program.cs no longer contains INSERT INTO Characters. Stop; Pass 12B may already be applied.'
}
if ($program -notmatch '(?i)\bUPDATE\s+Characters\b') {
    throw 'Program.cs no longer contains UPDATE Characters. Stop; Pass 12B may already be applied.'
}
if ($repo -match '\bSaveIdentityStub\s*\(') {
    throw 'CharacterRepository.SaveIdentityStub already exists. Stop; Pass 12B may already be applied.'
}

function Replace-MethodBody {
    param(
        [string]$Text,
        [string]$SignatureRegex,
        [string]$Replacement
    )

    $m = [regex]::Match(
        $Text,
        $SignatureRegex,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if (-not $m.Success) {
        throw "Could not find method matching: $SignatureRegex"
    }

    $openBrace = $Text.IndexOf('{', $m.Index)
    if ($openBrace -lt 0) { throw 'Could not find opening brace.' }

    $depth = 0
    $closeBrace = -1

    for ($i = $openBrace; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') { $depth++ }
        elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                $closeBrace = $i
                break
            }
        }
    }

    if ($closeBrace -lt 0) { throw 'Could not find closing brace.' }

    return $Text.Substring(0, $m.Index) + $Replacement + $Text.Substring($closeBrace + 1)
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass12B' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$programBackup = Join-Path $backupRoot 'Program.cs'
$repoBackup    = Join-Path $backupRoot 'Characters\Base\CharacterRepository.cs'
$programArchive = Join-Path $archiveRoot 'Program.pre-pass12b.cs'
$repoArchive    = Join-Path $archiveRoot 'Characters\Base\CharacterRepository.pre-pass12b.cs'

foreach ($p in @($programBackup,$repoBackup,$programArchive,$repoArchive)) {
    New-Item -ItemType Directory -Path (Split-Path $p -Parent) -Force | Out-Null
}

Copy-Item $programPath $programBackup -Force
Copy-Item $repoPath $repoBackup -Force
Copy-Item $programPath $programArchive -Force
Copy-Item $repoPath $repoArchive -Force

# ----------------------------------------------------------------------
# Add all canonical Characters write gateways to CharacterRepository.
# ----------------------------------------------------------------------
$anchor = '        /// <summary>' + [Environment]::NewLine + '        /// Canonical runtime gateway for lowering Characters.Tier.'
$anchorIndex = $repo.IndexOf($anchor)

if ($anchorIndex -lt 0) {
    $anchorIndex = $repo.IndexOf('        public static void LowerMaterializationTier')
    if ($anchorIndex -lt 0) {
        throw 'Could not find CharacterRepository insertion anchor before LowerMaterializationTier.'
    }
}

$methods = @'
        /// <summary>
        /// Canonical gateway used by the world seeder to create or refresh a
        /// generated NPC identity row in Characters.
        /// </summary>
        public static void SaveIdentityStub(SimCharacter npc, string batchLabel)
        {
            if (npc == null || npc.Id <= 0)
                return;

            _ = batchLabel; // retained for seeder call compatibility

            EnsureDataDir();
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();

            var folderName = ProjectEve.Data.ProjectEveDatabaseSetup.GetNpcFolderName(
                npc.Id,
                npc.Name ?? "");
            var folderPath = ProjectEve.Data.ProjectEveDatabaseSetup.GetNpcFolderPath(
                npc.Id,
                npc.Name ?? "");
            var npcKey = $"npc_{npc.Id:D6}";
            var status = npc.Tier >= 5 ? "HistoryOnly" : "Draft";

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Characters
                (
                    Id, NpcKey, FolderName, FolderPath, Name, Age, Gender,
                    Occupation, Location, Status, Goal, Need, Fear, Want,
                    PersonalityContext, Hometown, Address, Tier, UpdatedRealAt
                )
                VALUES
                (
                    $id, $npcKey, $folderName, $folderPath, $name, $age, $gender,
                    $occ, $loc, $status, $goal, $need, $fear, $want,
                    $ctx, $home, $addr, $tier, CURRENT_TIMESTAMP
                )
                ON CONFLICT(Id) DO UPDATE SET
                    NpcKey = $npcKey,
                    FolderName = $folderName,
                    FolderPath = $folderPath,
                    Name = $name,
                    Age = $age,
                    Gender = $gender,
                    Occupation = $occ,
                    Location = $loc,
                    Status = $status,
                    Goal = $goal,
                    Need = $need,
                    Fear = $fear,
                    Want = $want,
                    PersonalityContext = $ctx,
                    Hometown = $home,
                    Address = $addr,
                    Tier = $tier,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;

            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$npcKey", npcKey);
            cmd.Parameters.AddWithValue("$folderName", folderName);
            cmd.Parameters.AddWithValue("$folderPath", folderPath);
            cmd.Parameters.AddWithValue("$name", npc.Name ?? "");
            cmd.Parameters.AddWithValue("$age", npc.Age);
            cmd.Parameters.AddWithValue("$gender", npc.Gender ?? "");
            cmd.Parameters.AddWithValue("$occ", npc.Occupation ?? "");
            cmd.Parameters.AddWithValue("$loc", npc.Location ?? "");
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$goal", npc.Goal ?? "");
            cmd.Parameters.AddWithValue("$need", npc.Need ?? "");
            cmd.Parameters.AddWithValue("$fear", npc.Fear ?? "");
            cmd.Parameters.AddWithValue("$want", npc.Want ?? "");
            cmd.Parameters.AddWithValue("$ctx", npc.PersonalityContext ?? "");
            cmd.Parameters.AddWithValue("$home", npc.Hometown ?? "");
            cmd.Parameters.AddWithValue("$addr", npc.HomeAddress ?? "");
            cmd.Parameters.AddWithValue("$tier", npc.Tier);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Canonical gateway for refreshing the stable NPC key and filesystem
        /// folder metadata stored on Characters.
        /// </summary>
        public static void UpdateFolderInfo(int npcId, string npcName)
        {
            if (npcId <= 0)
                return;

            EnsureDataDir();
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();

            var folderName = ProjectEve.Data.ProjectEveDatabaseSetup.GetNpcFolderName(
                npcId,
                npcName);
            var folderPath = ProjectEve.Data.ProjectEveDatabaseSetup.GetNpcFolderPath(
                npcId,
                npcName);
            var npcKey = $"npc_{npcId:D6}";

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Characters
                SET
                    NpcKey = $npcKey,
                    FolderName = $folderName,
                    FolderPath = $folderPath,
                    UpdatedRealAt = CURRENT_TIMESTAMP
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue("$id", npcId);
            cmd.Parameters.AddWithValue("$npcKey", npcKey);
            cmd.Parameters.AddWithValue("$folderName", folderName);
            cmd.Parameters.AddWithValue("$folderPath", folderPath);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Canonical gateway used by bootstrap code for the four core NPC rows.
        /// </summary>
        public static void EnsureCoreRow(
            int id,
            string name,
            int age,
            string gender,
            string occupation,
            string location,
            string goal,
            string need,
            string fear,
            string want,
            string context,
            string hometown,
            string address,
            int tier)
        {
            if (id <= 0)
                return;

            EnsureDataDir();
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();

            var folderName = ProjectEve.Data.ProjectEveDatabaseSetup.GetNpcFolderName(id, name);
            var folderPath = ProjectEve.Data.ProjectEveDatabaseSetup.GetNpcFolderPath(id, name);
            var npcKey = $"npc_{id:D6}";

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Characters
                (
                    Id, NpcKey, FolderName, FolderPath, Name, Age, Gender,
                    Occupation, Location, Status, Goal, Need, Fear, Want,
                    PersonalityContext, Hometown, Address, Tier, UpdatedRealAt
                )
                VALUES
                (
                    $id, $npcKey, $folderName, $folderPath, $name, $age, $gender,
                    $occupation, $location, 'Core', $goal, $need, $fear, $want,
                    $context, $hometown, $address, $tier, CURRENT_TIMESTAMP
                )
                ON CONFLICT(Id) DO UPDATE SET
                    NpcKey = $npcKey,
                    FolderName = $folderName,
                    FolderPath = $folderPath,
                    Name = $name,
                    Age = $age,
                    Gender = $gender,
                    Occupation = $occupation,
                    Location = $location,
                    Status = 'Core',
                    Goal = $goal,
                    Need = $need,
                    Fear = $fear,
                    Want = $want,
                    PersonalityContext = $context,
                    Hometown = $hometown,
                    Address = $address,
                    Tier = $tier,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;

            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$npcKey", npcKey);
            cmd.Parameters.AddWithValue("$folderName", folderName);
            cmd.Parameters.AddWithValue("$folderPath", folderPath);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$age", age);
            cmd.Parameters.AddWithValue("$gender", gender);
            cmd.Parameters.AddWithValue("$occupation", occupation);
            cmd.Parameters.AddWithValue("$location", location);
            cmd.Parameters.AddWithValue("$goal", goal);
            cmd.Parameters.AddWithValue("$need", need);
            cmd.Parameters.AddWithValue("$fear", fear);
            cmd.Parameters.AddWithValue("$want", want);
            cmd.Parameters.AddWithValue("$context", context);
            cmd.Parameters.AddWithValue("$hometown", hometown);
            cmd.Parameters.AddWithValue("$address", address);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.ExecuteNonQuery();

            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureNpcFolders(id, name);
        }

'@

$repo = $repo.Insert($anchorIndex, $methods)

# ----------------------------------------------------------------------
# Replace Program's three direct Characters writer methods with routing
# wrappers. Call sites stay unchanged.
# ----------------------------------------------------------------------
$program = Replace-MethodBody `
    -Text $program `
    -SignatureRegex 'static\s+void\s+SaveNpcIdentityStub\s*\(\s*SimCharacter\s+npc\s*,\s*string\s+batchLabel\s*\)\s*\{' `
    -Replacement @'
static void SaveNpcIdentityStub(SimCharacter npc, string batchLabel)
    {
        CharacterRepository.SaveIdentityStub(npc, batchLabel);
    }
'@

$program = Replace-MethodBody `
    -Text $program `
    -SignatureRegex 'static\s+void\s+UpdateNpcFolderInfo\s*\(\s*int\s+npcId\s*,\s*string\s+npcName\s*\)\s*\{' `
    -Replacement @'
static void UpdateNpcFolderInfo(int npcId, string npcName)
    {
        CharacterRepository.UpdateFolderInfo(npcId, npcName);
    }
'@

$program = Replace-MethodBody `
    -Text $program `
    -SignatureRegex 'static\s+void\s+EnsureCoreNpcRow\s*\(\s*int\s+id\s*,\s*string\s+name\s*,\s*int\s+age\s*,\s*string\s+gender\s*,\s*string\s+occupation\s*,\s*string\s+location\s*,\s*string\s+goal\s*,\s*string\s+need\s*,\s*string\s+fear\s*,\s*string\s+want\s*,\s*string\s+context\s*,\s*string\s+hometown\s*,\s*string\s+address\s*,\s*int\s+tier\s*\)\s*\{' `
    -Replacement @'
static void EnsureCoreNpcRow(
        int id,
        string name,
        int age,
        string gender,
        string occupation,
        string location,
        string goal,
        string need,
        string fear,
        string want,
        string context,
        string hometown,
        string address,
        int tier)
    {
        CharacterRepository.EnsureCoreRow(
            id,
            name,
            age,
            gender,
            occupation,
            location,
            goal,
            need,
            fear,
            want,
            context,
            hometown,
            address,
            tier);
    }
'@

# Final safety check: Program should have zero direct Characters writes.
if ($program -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+Characters\b' -or
    $program -match '(?i)(?:^|[\s;"@])UPDATE\s+Characters\b' -or
    $program -match '(?i)\bDELETE\s+FROM\s+Characters\b') {
    throw 'Safety check failed: Program.cs still contains a direct Characters write.'
}

if ($repo -notmatch '\bSaveIdentityStub\s*\(' -or
    $repo -notmatch '\bUpdateFolderInfo\s*\(' -or
    $repo -notmatch '\bEnsureCoreRow\s*\(') {
    throw 'Safety check failed: one or more CharacterRepository gateways are missing.'
}

Set-Content $programPath $program -Encoding UTF8
Set-Content $repoPath $repo -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 12B - PROGRAM CHARACTERS WRITER ROUTING
============================================

CANONICAL CURRENT NPC TABLE:
Characters in project_eve.db

CANONICAL RUNTIME/SEED WRITE GATEWAY:
Characters\Base\CharacterRepository.cs

PROGRAM.CS DIRECT WRITES ROUTED:
- SaveNpcIdentityStub -> CharacterRepository.SaveIdentityStub
- UpdateNpcFolderInfo -> CharacterRepository.UpdateFolderInfo
- EnsureCoreNpcRow -> CharacterRepository.EnsureCoreRow

RESULT:
Program.cs no longer contains direct INSERT/UPDATE/DELETE SQL for Characters.

CALL SITES:
Existing seeder/bootstrap call sites remain unchanged through thin wrappers.

BEHAVIOR PRESERVED:
- generated NPC upsert behavior
- Draft / HistoryOnly status logic
- core NPC upsert behavior
- folder metadata refresh
- core NPC filesystem folder creation

NO DATA DELETION:
- no table drops
- no row deletes
- no NPC purge
'@ | Set-Content (Join-Path $reportRoot 'PASS12B_PROGRAM_CHARACTERS_WRITER_ROUTING.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 12B Program Characters writer routing applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'Program.cs direct Characters writes: 0 expected'
Write-Host 'Canonical gateway: Characters\Base\CharacterRepository.cs'
Write-Host ''
Write-Host 'No database rows, tables, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
