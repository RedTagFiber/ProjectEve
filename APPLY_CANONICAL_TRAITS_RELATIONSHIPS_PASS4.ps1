$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this script from the folder containing ProjectEve.csproj.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass4' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\CanonicalPass4' $stamp
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null

function Backup-Relative([string]$relativePath) {
    $src = Join-Path $root $relativePath
    if (-not (Test-Path $src)) { return }

    $dst = Join-Path $backupRoot $relativePath
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
    Copy-Item $src $dst -Force
}

$required = @(
    'Characters\Base\CharacterRepository.cs',
    'Characters\Traits\State\TraitStateDatabase.cs',
    'DATA\ProjectEveDatabaseSetup.cs',
    'Program.cs'
)

foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $root $r))) {
        throw ('Required file missing: ' + $r)
    }
    Backup-Relative $r
}

$packageRoot = Split-Path $MyInvocation.MyCommand.Path -Parent

$newFiles = @(
    'Characters\Traits\NpcTraitRepository.cs',
    'Characters\Traits\State\TraitStateDatabase.cs',
    'Relationships\RelationshipRepository.cs',
    'DATA\ProjectEveOwnershipVerifier.cs'
)

foreach ($relative in $newFiles) {
    $src = Join-Path $packageRoot $relative
    $dst = Join-Path $root $relative

    if (-not (Test-Path $src)) {
        if (-not (Test-Path $dst)) {
            throw ('Package file missing: ' + $relative)
        }
        continue
    }

    $srcFull = [System.IO.Path]::GetFullPath($src)
    $dstFull = [System.IO.Path]::GetFullPath($dst)

    if (-not $srcFull.Equals($dstFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        Backup-Relative $relative
        New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
        Copy-Item $src $dst -Force
    }
}

$nl = [Environment]::NewLine

# ------------------------------------------------------------
# Database setup remains the ONLY schema owner.
# Add the trait-control schema there.
# ------------------------------------------------------------
$setupPath = Join-Path $root 'DATA\ProjectEveDatabaseSetup.cs'
$setup = Get-Content $setupPath -Raw

if ($setup -notmatch 'CREATE TABLE IF NOT EXISTS NpcTraitControl') {
    $needle = 'CREATE TABLE IF NOT EXISTS NpcEmotionTriggers'
    $idx = $setup.IndexOf($needle)

    if ($idx -lt 0) {
        throw 'Could not find NpcEmotionTriggers in ProjectEveDatabaseSetup.cs.'
    }

    $schema = @'
        CREATE TABLE IF NOT EXISTS NpcTraitControl
        (
            NpcId INTEGER NOT NULL,
            TraitId TEXT NOT NULL,
            Control INTEGER NOT NULL DEFAULT 50,
            LastUpdatedRealAt TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (NpcId, TraitId),
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

'@

    $setup = $setup.Insert($idx, $schema)
    Set-Content $setupPath $setup -Encoding UTF8
}

# ------------------------------------------------------------
# CharacterRepository:
# traits use NpcTraitRepository only;
# relationships use RelationshipRepository only.
# ------------------------------------------------------------
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'
$repo = Get-Content $repoPath -Raw

if ($repo -notmatch 'using\s+ProjectEve\.Characters\.Traits\s*;') {
    $firstUsing = $repo.IndexOf('using ')
    $repo = $repo.Insert($firstUsing, 'using ProjectEve.Characters.Traits;' + $nl)
}

# Replace LoadTraits body.
$loadTraitsPattern = '(?s)private\s+static\s+void\s+LoadTraits\s*\(\s*SqliteConnection\s+conn\s*,\s*SimCharacter\s+npc\s*\)\s*\{.*?\n\s*\}\s*\n\s*public\s+static\s+void\s+SaveTraits'
$loadTraitsReplacement = @'
private static void LoadTraits(SqliteConnection conn, SimCharacter npc)
        {
            npc.Traits ??= new NpcTraits();

            var loaded = NpcTraitRepository.LoadAll(npc.Id);
            if (loaded.Count == 0)
                return;

            var fast = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in loaded)
            {
                if (pair.Key.StartsWith("mid.", StringComparison.OrdinalIgnoreCase))
                    mid[pair.Key] = pair.Value;
                else if (pair.Key.StartsWith("slow.", StringComparison.OrdinalIgnoreCase))
                    slow[pair.Key] = pair.Value;
                else
                    fast[pair.Key] = pair.Value;
            }

            if (fast.Count == 0)
                fast = TraitJsonLoader.BuildFastDefaults(45f);

            npc.Traits.InitializeFromLayers(fast, mid, slow);
        }

        public static void SaveTraits
'@

$newRepo = [regex]::Replace($repo, $loadTraitsPattern, $loadTraitsReplacement, 1)
if ($newRepo -eq $repo) {
    throw 'Could not replace CharacterRepository.LoadTraits.'
}
$repo = $newRepo

# Replace SaveTraits method through the BRAIN separator.
$saveTraitsPattern = '(?s)public\s+static\s+void\s+SaveTraits\s*\(\s*int\s+npcId\s*,\s*NpcTraits\s+traits\s*\)\s*\{.*?\n\s*\}\s*\n\s*//\s*=+\s*\n\s*//\s*BRAIN'
$saveTraitsReplacement = @'
public static void SaveTraits(int npcId, NpcTraits traits)
        {
            if (traits == null)
                return;

            NpcTraitRepository.SaveAll(npcId, traits);
        }

        // ============================================================
        // BRAIN
'@

$newRepo = [regex]::Replace($repo, $saveTraitsPattern, $saveTraitsReplacement, 1)
if ($newRepo -eq $repo) {
    throw 'Could not replace CharacterRepository.SaveTraits.'
}
$repo = $newRepo

# Replace relationship loader.
$relationshipPattern = '(?s)private\s+static\s+void\s+LoadRelationships\s*\(\s*SqliteConnection\s+conn\s*,\s*SimCharacter\s+npc\s*\)\s*\{.*?\n\s*\}\s*\n\s*public\s+static\s+void\s+SaveCharacterState'
$relationshipReplacement = @'
private static void LoadRelationships(SqliteConnection conn, SimCharacter npc)
        {
            npc.Relationships = RelationshipRepository.LoadForSource(npc.Id);
        }

        public static void SaveCharacterState
'@

$newRepo = [regex]::Replace($repo, $relationshipPattern, $relationshipReplacement, 1)
if ($newRepo -eq $repo) {
    throw 'Could not replace CharacterRepository.LoadRelationships.'
}
$repo = $newRepo

Set-Content $repoPath $repo -Encoding UTF8

# ------------------------------------------------------------
# Program:
# - SaveNpcTraitsToStudioTable becomes one call to canonical repository.
# - relationship helpers route to RelationshipRepository.
# - stop Program from creating tables owned by DatabaseSetup.
# ------------------------------------------------------------
$programPath = Join-Path $root 'Program.cs'
$program = Get-Content $programPath -Raw

$traitMethodPattern = '(?s)static\s+void\s+SaveNpcTraitsToStudioTable\s*\(\s*SimCharacter\s+npc\s*\)\s*\{.*?\n\s*\}\s*\n\s*static\s+int\s+ClampTraitValue'
$traitMethodReplacement = @'
static void SaveNpcTraitsToStudioTable(SimCharacter npc)
    {
        if (npc?.Traits == null)
            return;

        CharacterRepository.SaveTraits(npc.Id, npc.Traits);
    }

    static int ClampTraitValue
'@
$updated = [regex]::Replace($program, $traitMethodPattern, $traitMethodReplacement, 1)
if ($updated -eq $program) {
    throw 'Could not replace Program.SaveNpcTraitsToStudioTable.'
}
$program = $updated

$upsertPattern = '(?s)static\s+void\s+UpsertRelationship\s*\(\s*int\s+npcId\s*,\s*string\s+targetName\s*,\s*string\s+relationshipType\s*,\s*int\s+trust\s*,\s*int\s+respect\s*,\s*int\s+affection\s*,\s*int\s+attraction\s*,\s*int\s+tension\s*,\s*string\s+notes\s*\)\s*\{.*?\n\s*\}\s*\n\s*static\s+void\s+UpsertRelationshipIfMissing'
$upsertReplacement = @'
static void UpsertRelationship(
        int npcId,
        string targetName,
        string relationshipType,
        int trust,
        int respect,
        int affection,
        int attraction,
        int tension,
        string notes)
    {
        RelationshipRepository.Upsert(
            npcId,
            targetCharacterId: null,
            targetName,
            relationshipType,
            trust,
            respect,
            affection,
            attraction,
            tension,
            notes);
    }

    static void UpsertRelationshipIfMissing
'@
$updated = [regex]::Replace($program, $upsertPattern, $upsertReplacement, 1)
if ($updated -eq $program) {
    throw 'Could not replace Program.UpsertRelationship.'
}
$program = $updated

$existsPattern = '(?s)static\s+bool\s+RelationshipExists\s*\(\s*int\s+npcId\s*,\s*string\s+targetName\s*,\s*string\s+relationshipType\s*\)\s*\{.*?\n\s*\}\s*\n\s*static\s+void\s+EnsureCoreNpcRows'
$existsReplacement = @'
static bool RelationshipExists(int npcId, string targetName, string relationshipType)
    {
        return RelationshipRepository.Exists(npcId, targetName, relationshipType);
    }

    static void EnsureCoreNpcRows
'@
$updated = [regex]::Replace($program, $existsPattern, $existsReplacement, 1)
if ($updated -eq $program) {
    throw 'Could not replace Program.RelationshipExists.'
}
$program = $updated

# Remove duplicate schema-creation blocks now owned by DatabaseSetup.
$canonicalTables = @(
    'NpcRelationships',
    'NpcAppearanceProfiles',
    'NpcVoiceProfiles',
    'NpcTraitValues',
    'NpcBuildRevisions'
)

foreach ($table in $canonicalTables) {
    $pattern = '(?s)\s*Execute\s*\(\s*conn\s*,\s*"""\s*CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+' +
               [regex]::Escape($table) +
               '\b.*?"""\s*\)\s*;'

    $program = [regex]::Replace($program, $pattern, '', 1)
}

# Add canonical ownership verifier after finance verifier.
if ($program -notmatch 'ProjectEveOwnershipVerifier\.PrintToConsole\(\);') {
    $needle = 'ProjectEveFinanceVerifier.PrintToConsole();'
    $idx = $program.IndexOf($needle)

    if ($idx -ge 0) {
        $program = $program.Insert(
            $idx + $needle.Length,
            $nl + '        ProjectEveOwnershipVerifier.PrintToConsole();'
        )
    }
}

Set-Content $programPath $program -Encoding UTF8

# ------------------------------------------------------------
# Archive the retired legacy TraitStateDatabase implementation copy.
# We keep source backups outside the project.
# ------------------------------------------------------------
$oldTraitBackup = Join-Path $backupRoot 'Characters\Traits\State\TraitStateDatabase.cs'
if (Test-Path $oldTraitBackup) {
    $archiveDest = Join-Path $archiveRoot 'Characters\Traits\State\TraitStateDatabase.legacy.cs'
    New-Item -ItemType Directory -Path (Split-Path $archiveDest -Parent) -Force | Out-Null
    Copy-Item $oldTraitBackup $archiveDest -Force
}

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Canonical Traits + Relationships Pass 4 applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'This pass did NOT delete NPCs or databases.'
Write-Host 'Legacy Traits/Relationships tables remain temporarily for migration only.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host '  dotnet run -- verify'
