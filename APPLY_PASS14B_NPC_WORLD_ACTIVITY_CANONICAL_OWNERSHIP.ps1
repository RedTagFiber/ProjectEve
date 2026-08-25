$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$setupPath   = Join-Path $root 'DATA\ProjectEveDatabaseSetup.cs'
$enginePath  = Join-Path $root 'World\SmallTown\Activity\WorldActivityEngine.cs'
$plannerPath = Join-Path $root 'World\SmallTown\Activity\ActivityPlanner.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

foreach ($p in @($setupPath,$enginePath,$plannerPath)) {
    if (-not (Test-Path $p)) { throw ('Required file not found: ' + $p) }
}

$setup   = Get-Content $setupPath -Raw
$engine  = Get-Content $enginePath -Raw
$planner = Get-Content $plannerPath -Raw

if ($setup -match '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+NpcWorldActivity') {
    throw 'ProjectEveDatabaseSetup already owns NpcWorldActivity. Stop; Pass 14B may already be applied.'
}
if ($engine -notmatch '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+NpcWorldActivity') {
    throw 'WorldActivityEngine schema block not found.'
}
if ($planner -notmatch '(?i)INSERT\s+INTO\s+NpcWorldActivity') {
    throw 'ActivityPlanner direct NpcWorldActivity writer not found.'
}

function Find-MethodBlock {
    param(
        [string]$Text,
        [string]$SignatureRegex
    )

    $m = [regex]::Match(
        $Text,
        $SignatureRegex,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if (-not $m.Success) {
        throw ('Could not find method: ' + $SignatureRegex)
    }

    $open = $Text.IndexOf('{', $m.Index)
    if ($open -lt 0) { throw 'Opening brace not found.' }

    $depth = 0
    $close = -1
    for ($i=$open; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') { $depth++ }
        elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                $close = $i
                break
            }
        }
    }

    if ($close -lt 0) { throw 'Closing brace not found.' }

    return @{
        Match = $m
        Open = $open
        Close = $close
        Text = $Text.Substring($m.Index, ($close - $m.Index) + 1)
    }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot  = Join-Path 'D:\ProjectEve\Backups\CanonicalPass14B' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\LegacyCode' $stamp

$rels = @(
    'DATA\ProjectEveDatabaseSetup.cs',
    'World\SmallTown\Activity\WorldActivityEngine.cs',
    'World\SmallTown\Activity\ActivityPlanner.cs'
)

foreach ($rel in $rels) {
    $src = Join-Path $root $rel
    $bak = Join-Path $backupRoot $rel
    $arc = Join-Path $archiveRoot ($rel -replace '\.cs$','.pre-pass14b.cs')
    New-Item -ItemType Directory -Path (Split-Path $bak -Parent) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $arc -Parent) -Force | Out-Null
    Copy-Item $src $bak -Force
    Copy-Item $src $arc -Force
}

# ----------------------------------------------------------------------
# 1. ProjectEveDatabaseSetup becomes the sole schema owner.
#    Add a dedicated schema method and call it from EnsureAll().
# ----------------------------------------------------------------------
$ensureAll = Find-MethodBlock -Text $setup -SignatureRegex 'public\s+static\s+void\s+EnsureAll\s*\(\s*\)\s*\{'

if ($ensureAll.Text -match 'EnsureNpcWorldActivitySchema\s*\(') {
    throw 'EnsureAll already calls EnsureNpcWorldActivitySchema.'
}

$callInsertion = @'

            EnsureNpcWorldActivitySchema();
'@

$setup = $setup.Insert($ensureAll.Close, $callInsertion)

# Re-find EnsureAll after insertion because indexes changed.
$classClose = $setup.LastIndexOf('}')
if ($classClose -lt 0) { throw 'Could not find ProjectEveDatabaseSetup class closing brace.' }

$schemaMethod = @'

        private static void EnsureNpcWorldActivitySchema()
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={MainDatabasePath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcWorldActivity
                (
                    NpcId INTEGER PRIMARY KEY,
                    LocationId TEXT,
                    Activity TEXT,
                    ActivityStartGameTime TEXT,
                    LastWorldTickGameTime TEXT,
                    IsBusy INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_world_activity_location
                    ON NpcWorldActivity(LocationId, Activity);
                """;

            cmd.ExecuteNonQuery();
        }

'@

$setup = $setup.Insert($classClose, $schemaMethod)

# ----------------------------------------------------------------------
# 2. WorldActivityEngine no longer owns schema.
# ----------------------------------------------------------------------
$init = Find-MethodBlock -Text $engine -SignatureRegex 'public\s+static\s+void\s+Initialize\s*\(\s*\)\s*\{'
$newInit = @'
public static void Initialize()
        {
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();
        }
'@
$engine = $engine.Substring(0,$init.Match.Index) + $newInit + $engine.Substring($init.Close + 1)

# Replace private Save with a canonical public gateway preserving start time.
$save = Find-MethodBlock -Text $engine -SignatureRegex 'private\s+static\s+void\s+Save\s*\(\s*int\s+npcId\s*,\s*string\s+location\s*,\s*string\s+activity\s*,\s*DateTime\s+gameTime\s*,\s*bool\s+busy\s*\)\s*\{'

$newSave = @'
private static void Save(int npcId, string location, string activity, DateTime gameTime, bool busy)
        {
            UpsertActivityState(
                npcId,
                location,
                activity,
                gameTime,
                gameTime,
                busy,
                preserveExistingStart: true);
        }

        /// <summary>
        /// Canonical runtime write gateway for NpcWorldActivity.
        /// ActivityPlanner and world-tick code both route physical activity state here.
        /// </summary>
        public static void UpsertActivityState(
            int npcId,
            string locationId,
            string activity,
            DateTime activityStartGameTime,
            DateTime lastWorldTickGameTime,
            bool isBusy,
            bool preserveExistingStart = false)
        {
            if (npcId <= 0)
                return;

            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();

            cmd.CommandText = preserveExistingStart
                ? """
                    INSERT INTO NpcWorldActivity
                    (NpcId, LocationId, Activity, ActivityStartGameTime, LastWorldTickGameTime, IsBusy)
                    VALUES ($npc,$loc,$act,$start,$tick,$busy)
                    ON CONFLICT(NpcId) DO UPDATE SET
                        LocationId=$loc,
                        Activity=$act,
                        LastWorldTickGameTime=$tick,
                        IsBusy=$busy;
                    """
                : """
                    INSERT INTO NpcWorldActivity
                    (NpcId, LocationId, Activity, ActivityStartGameTime, LastWorldTickGameTime, IsBusy)
                    VALUES ($npc,$loc,$act,$start,$tick,$busy)
                    ON CONFLICT(NpcId) DO UPDATE SET
                        LocationId=$loc,
                        Activity=$act,
                        ActivityStartGameTime=$start,
                        LastWorldTickGameTime=$tick,
                        IsBusy=$busy;
                    """;

            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$loc", locationId ?? "");
            cmd.Parameters.AddWithValue("$act", activity ?? "idle");
            cmd.Parameters.AddWithValue("$start", activityStartGameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$tick", lastWorldTickGameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$busy", isBusy ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
'@

$engine = $engine.Substring(0,$save.Match.Index) + $newSave + $engine.Substring($save.Close + 1)

# ----------------------------------------------------------------------
# 3. ActivityPlanner delegates physical state persistence to engine.
# ----------------------------------------------------------------------
$apply = Find-MethodBlock -Text $planner -SignatureRegex 'public\s+static\s+void\s+ApplyCurrentPlanToWorld\s*\(\s*int\s+npcId\s*,\s*DateTime\s+gameTime\s*\)\s*\{'

$newApply = @'
public static void ApplyCurrentPlanToWorld(
            int npcId,
            DateTime gameTime)
        {
            Initialize();

            PlannedActivity? plan = GetCurrentPlan(
                npcId,
                gameTime);

            if (plan == null)
                return;

            // Planner owns intent/plan selection only.
            // WorldActivityEngine owns canonical physical activity state persistence.
            WorldActivityEngine.UpsertActivityState(
                npcId,
                plan.LocationId ?? "",
                plan.ActivityId ?? "idle",
                plan.StartGameTime,
                gameTime,
                plan.IsBusy,
                preserveExistingStart: false);
        }
'@

$planner = $planner.Substring(0,$apply.Match.Index) + $newApply + $planner.Substring($apply.Close + 1)

# ----------------------------------------------------------------------
# Safety checks.
# ----------------------------------------------------------------------
if ($engine -match '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+NpcWorldActivity') {
    throw 'Safety check failed: WorldActivityEngine still creates NpcWorldActivity.'
}

if ($planner -match '(?i)\bINSERT\s+INTO\s+NpcWorldActivity\b' -or
    $planner -match '(?i)(?:^|[\s;"@])UPDATE\s+NpcWorldActivity\b' -or
    $planner -match '(?i)\bDELETE\s+FROM\s+NpcWorldActivity\b') {
    throw 'Safety check failed: ActivityPlanner still writes NpcWorldActivity directly.'
}

if ($setup -notmatch '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+NpcWorldActivity') {
    throw 'Safety check failed: ProjectEveDatabaseSetup does not create NpcWorldActivity.'
}

if ($engine -notmatch '\bUpsertActivityState\s*\(') {
    throw 'Safety check failed: WorldActivityEngine gateway missing.'
}

Set-Content $setupPath $setup -Encoding UTF8
Set-Content $enginePath $engine -Encoding UTF8
Set-Content $plannerPath $planner -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 14B - NPC WORLD ACTIVITY CANONICAL OWNERSHIP
=================================================

CANONICAL TABLE:
NpcWorldActivity in project_eve.db

SCHEMA OWNER:
DATA\ProjectEveDatabaseSetup.cs

RUNTIME WRITE OWNER:
World\SmallTown\Activity\WorldActivityEngine.cs

PLANNER ROLE:
World\SmallTown\Activity\ActivityPlanner.cs
- plans activities
- reads NpcActivityPlan
- delegates current physical state to WorldActivityEngine
- no direct NpcWorldActivity write

BEHAVIOR PRESERVED:
- planner can explicitly set ActivityStartGameTime from plan.StartGameTime
- world tick updates location/activity/busy/tick without resetting existing start time
- same physical state table remains in MAIN DB

NO DATA DELETION:
- no table drop
- no row delete
- no NPC purge
'@ | Set-Content (Join-Path $reportRoot 'PASS14B_NPC_WORLD_ACTIVITY_CANONICAL_OWNERSHIP.txt') -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 14B NpcWorldActivity canonical ownership applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'Schema owner: ProjectEveDatabaseSetup'
Write-Host 'Runtime writer: WorldActivityEngine.UpsertActivityState'
Write-Host 'ActivityPlanner direct NpcWorldActivity writes: 0 expected'
Write-Host ''
Write-Host 'No database rows, tables, world activity state, or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
