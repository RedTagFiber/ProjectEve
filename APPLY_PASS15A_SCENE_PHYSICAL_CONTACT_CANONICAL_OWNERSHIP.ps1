$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$setupPath  = Join-Path $root 'DATA\ProjectEveDatabaseSetup.cs'
$scenePath  = Join-Path $root 'Scene\SceneSpatialInteractionService.cs'
$playerPath = Join-Path $root 'World\PlayerWorldPresenceService.cs'
$worldPath  = Join-Path $root 'World\WorldOccupancyService.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

foreach ($p in @($setupPath,$scenePath,$playerPath,$worldPath)) {
    if (-not (Test-Path $p)) { throw ('Required file not found: ' + $p) }
}

$setup  = Get-Content $setupPath -Raw
$scene  = Get-Content $scenePath -Raw
$player = Get-Content $playerPath -Raw
$world  = Get-Content $worldPath -Raw

if ($setup -match '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+ScenePhysicalContact') {
    throw 'ProjectEveDatabaseSetup already owns ScenePhysicalContact. Stop; Pass 15A may already be applied.'
}
if ($scene -notmatch '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+ScenePhysicalContact') {
    throw 'SceneSpatialInteractionService ScenePhysicalContact schema block not found.'
}
if ($player -notmatch '(?i)UPDATE\s+ScenePhysicalContact') {
    throw 'PlayerWorldPresenceService direct ScenePhysicalContact update not found.'
}
if ($world -notmatch '(?i)UPDATE\s+ScenePhysicalContact') {
    throw 'WorldOccupancyService direct ScenePhysicalContact update not found.'
}

function Find-MethodBlock {
    param([string]$Text,[string]$SignatureRegex)

    $m = [regex]::Match(
        $Text,
        $SignatureRegex,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if (-not $m.Success) {
        throw ('Could not find method: ' + $SignatureRegex)
    }

    $open = $Text.IndexOf('{', $m.Index)
    if ($open -lt 0) { throw 'Opening brace not found.' }

    $depth=0
    $close=-1
    for ($i=$open; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') { $depth++ }
        elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { $close=$i; break }
        }
    }
    if ($close -lt 0) { throw 'Closing brace not found.' }

    return @{
        Match=$m
        Open=$open
        Close=$close
        Text=$Text.Substring($m.Index,($close-$m.Index)+1)
    }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass15A' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$rels=@(
    'DATA\ProjectEveDatabaseSetup.cs',
    'Scene\SceneSpatialInteractionService.cs',
    'World\PlayerWorldPresenceService.cs',
    'World\WorldOccupancyService.cs'
)

foreach ($rel in $rels) {
    $src=Join-Path $root $rel
    $bak=Join-Path $backupRoot $rel
    $arc=Join-Path $archiveRoot ($rel -replace '\.cs$','.pre-pass15a.cs')
    New-Item -ItemType Directory -Path (Split-Path $bak -Parent) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $arc -Parent) -Force | Out-Null
    Copy-Item $src $bak -Force
    Copy-Item $src $arc -Force
}

# ----------------------------------------------------------------------
# 1. Canonical schema ownership -> ProjectEveDatabaseSetup (MAIN DB for now)
# ----------------------------------------------------------------------
$ensureAll = Find-MethodBlock -Text $setup -SignatureRegex 'public\s+static\s+void\s+EnsureAll\s*\(\s*\)\s*\{'
if ($ensureAll.Text -match 'EnsureScenePhysicalContactSchema\s*\(') {
    throw 'EnsureAll already calls EnsureScenePhysicalContactSchema.'
}

$setup = $setup.Insert($ensureAll.Close, @'

            EnsureScenePhysicalContactSchema();
'@)

$classClose=$setup.LastIndexOf('}')
if ($classClose -lt 0) { throw 'Could not find ProjectEveDatabaseSetup class closing brace.' }

$schemaMethod=@'

        private static void EnsureScenePhysicalContactSchema()
        {
            // ScenePhysicalContact remains in MAIN temporarily because the active
            // scene spatial/presence stack still shares MAIN scene state.
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={MainDatabasePath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ScenePhysicalContact
                (
                    SceneId TEXT NOT NULL,
                    CharacterAKey TEXT NOT NULL,
                    CharacterBKey TEXT NOT NULL,
                    InitiatorCharacterKey TEXT NOT NULL,
                    ContactKind TEXT NOT NULL DEFAULT 'none',
                    State TEXT NOT NULL DEFAULT 'none',
                    ReactionState TEXT NOT NULL DEFAULT 'unknown',
                    StartedGameTime TEXT NOT NULL,
                    UpdatedGameTime TEXT NOT NULL,
                    UpdatedRealUtc TEXT NOT NULL,
                    PRIMARY KEY(SceneId,CharacterAKey,CharacterBKey)
                );

                CREATE INDEX IF NOT EXISTS IX_ScenePhysicalContact_Active
                    ON ScenePhysicalContact(SceneId,State,CharacterAKey,CharacterBKey);
                """;

            cmd.ExecuteNonQuery();
        }

'@
$setup=$setup.Insert($classClose,$schemaMethod)

# ----------------------------------------------------------------------
# 2. Remove ScenePhysicalContact schema ownership from SceneSpatial service.
#    Keep SceneSpatialInteractionEvent schema there for now.
# ----------------------------------------------------------------------
$contactSchemaPattern='(?s)\s*CREATE TABLE IF NOT EXISTS ScenePhysicalContact\s*\(.*?\);\s*CREATE INDEX IF NOT EXISTS IX_ScenePhysicalContact_Active\s*ON ScenePhysicalContact\(SceneId,State,CharacterAKey,CharacterBKey\);\s*'
$m=[regex]::Match($scene,$contactSchemaPattern,[System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
if (-not $m.Success) {
    throw 'Could not safely isolate ScenePhysicalContact schema block in SceneSpatialInteractionService.'
}
$scene=$scene.Remove($m.Index,$m.Length)

# ----------------------------------------------------------------------
# 3. Add one public break-contact gateway to SceneSpatialInteractionService.
#    This keeps all runtime ScenePhysicalContact SQL in one source file.
# ----------------------------------------------------------------------
$insertAnchor='    private PairRow UpsertPendingContact('
$anchorIndex=$scene.IndexOf($insertAnchor)
if ($anchorIndex -lt 0) {
    throw 'Could not find SceneSpatialInteractionService insertion anchor.'
}

$breakGateway=@'
    /// <summary>
    /// Canonical runtime gateway for interrupting physical contact when a player
    /// or NPC leaves a shared scene.
    /// </summary>
    public static void BreakContactsForCharacter(
        string sceneId,
        string characterKey,
        DateTimeOffset gameTime)
    {
        if (string.IsNullOrWhiteSpace(sceneId) ||
            string.IsNullOrWhiteSpace(characterKey))
            return;

        ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();

        using var conn = new SqliteConnection(
            "Data Source=" + ProjectEve.Data.ProjectEveDatabaseSetup.MainDatabasePath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE ScenePhysicalContact
SET State='broken',
    ReactionState='interrupted',
    UpdatedGameTime=$game,
    UpdatedRealUtc=$real
WHERE SceneId=$scene
  AND State IN ('pending','active','hesitant','frozen')
  AND (CharacterAKey=$character OR CharacterBKey=$character);";

        cmd.Parameters.AddWithValue("$game", gameTime.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$character", characterKey);
        cmd.ExecuteNonQuery();
    }

'@

$scene=$scene.Insert($anchorIndex,$breakGateway)

# ----------------------------------------------------------------------
# 4. Route PlayerWorldPresenceService through the scene gateway.
# ----------------------------------------------------------------------
$playerMethod=Find-MethodBlock -Text $player -SignatureRegex 'private\s+void\s+BreakPlayerContactsLocked\s*\(\s*string\s+sceneId\s*,\s*string\s+playerId\s*\)\s*\{'

$newPlayerMethod=@'
private void BreakPlayerContactsLocked(
        string sceneId,
        string playerId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            return;

        try
        {
            ProjectEve.Scene.SceneSpatialInteractionService.BreakContactsForCharacter(
                sceneId,
                "player:" + playerId,
                _clock.Now);
        }
        catch
        {
            // Scene contact state is best-effort during development/migration.
        }
    }
'@

$player=$player.Substring(0,$playerMethod.Match.Index)+$newPlayerMethod+$player.Substring($playerMethod.Close+1)

# ----------------------------------------------------------------------
# 5. Route WorldOccupancyService through the same scene gateway.
# ----------------------------------------------------------------------
$worldMethod=Find-MethodBlock -Text $world -SignatureRegex 'private\s+void\s+BreakSceneContacts\s*\(\s*string\s+sceneId\s*,\s*string\s+characterKey\s*,\s*DateTimeOffset\s+gameTime\s*\)\s*\{'

$newWorldMethod=@'
private void BreakSceneContacts(
        string sceneId,
        string characterKey,
        DateTimeOffset gameTime)
    {
        try
        {
            ProjectEve.Scene.SceneSpatialInteractionService.BreakContactsForCharacter(
                sceneId,
                characterKey,
                gameTime);
        }
        catch
        {
            // Scene contact state is best-effort during development/migration.
        }
    }
'@

$world=$world.Substring(0,$worldMethod.Match.Index)+$newWorldMethod+$world.Substring($worldMethod.Close+1)

# ----------------------------------------------------------------------
# Safety checks.
# ----------------------------------------------------------------------
if ($player -match '(?i)\bUPDATE\s+ScenePhysicalContact\b' -or
    $player -match '(?i)\bINSERT\s+INTO\s+ScenePhysicalContact\b' -or
    $player -match '(?i)\bDELETE\s+FROM\s+ScenePhysicalContact\b') {
    throw 'Safety check failed: PlayerWorldPresenceService still writes ScenePhysicalContact directly.'
}

if ($world -match '(?i)\bUPDATE\s+ScenePhysicalContact\b' -or
    $world -match '(?i)\bINSERT\s+INTO\s+ScenePhysicalContact\b' -or
    $world -match '(?i)\bDELETE\s+FROM\s+ScenePhysicalContact\b') {
    throw 'Safety check failed: WorldOccupancyService still writes ScenePhysicalContact directly.'
}

if ($scene -match '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+ScenePhysicalContact') {
    throw 'Safety check failed: SceneSpatialInteractionService still creates ScenePhysicalContact.'
}

if ($setup -notmatch '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+ScenePhysicalContact') {
    throw 'Safety check failed: ProjectEveDatabaseSetup does not create ScenePhysicalContact.'
}

Set-Content $setupPath $setup -Encoding UTF8
Set-Content $scenePath $scene -Encoding UTF8
Set-Content $playerPath $player -Encoding UTF8
Set-Content $worldPath $world -Encoding UTF8

$reportRoot='D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 15A - SCENE PHYSICAL CONTACT CANONICAL OWNERSHIP
=====================================================

CLASSIFICATION:
ScenePhysicalContact is objective physical scene state.

TEMPORARY DATABASE LOCATION:
project_eve.db (MAIN)

WHY IT REMAINS MAIN FOR NOW:
SceneSpatialInteractionService currently shares scene presence/spatial state
through the MAIN scene stack. Moving only ScenePhysicalContact to locations.db
would split one active scene transaction domain across databases prematurely.

SCHEMA OWNER:
DATA\ProjectEveDatabaseSetup.cs

RUNTIME WRITE OWNER:
Scene\SceneSpatialInteractionService.cs

ROUTED WRITERS:
- World\PlayerWorldPresenceService.cs
- World\WorldOccupancyService.cs

Both now call:
SceneSpatialInteractionService.BreakContactsForCharacter(...)

READ OWNER:
SceneSpatialInteractionService.cs

FUTURE:
When ScenePresence / shared scene state is migrated as a complete unit,
ScenePhysicalContact can move with that scene domain into locations.db.

NO DATA DELETION:
- no tables dropped
- no rows deleted
- no scene contacts deleted
- no NPC purge
'@ | Set-Content (Join-Path $reportRoot 'PASS15A_SCENE_PHYSICAL_CONTACT_CANONICAL_OWNERSHIP.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 15A ScenePhysicalContact ownership applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'Database: MAIN (temporary, intentional)'
Write-Host 'Schema owner: ProjectEveDatabaseSetup'
Write-Host 'Runtime writer: SceneSpatialInteractionService'
Write-Host 'PlayerWorldPresenceService direct writes: 0 expected'
Write-Host 'WorldOccupancyService direct writes: 0 expected'
Write-Host ''
Write-Host 'No database rows, tables, scene contacts, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
