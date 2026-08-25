$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'World\WorldOccupancyService.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $path)) {
    throw 'World\WorldOccupancyService.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass8B' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\CanonicalPass8B' $stamp

$backupFile = Join-Path $backupRoot 'World\WorldOccupancyService.cs'
$archiveFile = Join-Path $archiveRoot 'World\WorldOccupancyService.pre-location-routing.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null
Copy-Item $path $backupFile -Force
Copy-Item $path $archiveFile -Force

$text = Get-Content $path -Raw
$nl = [Environment]::NewLine

# Add canonical data namespace.
if ($text -notmatch '(?m)^using ProjectEve\.Data;') {
    $anchor = 'using ProjectEve.Core.Time;'
    $idx = $text.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) { throw 'Could not find using insertion point.' }
    $text = $text.Insert($idx, 'using ProjectEve.Data;' + $nl)
}

$oldCtor = @'
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
'@

$newCtor = @'
        ProjectEveDatabaseSetup.EnsureAll();
        _dbPath = ProjectEveDatabaseSetup.LocationDatabasePath;

        EnsureSchema();
        MigrateLegacyMainOccupancyDataIfNeeded();
'@

if ($text.Contains($oldCtor)) {
    $text = $text.Replace($oldCtor, $newCtor)
}
elseif ($text -notmatch 'MigrateLegacyMainOccupancyDataIfNeeded\(\);') {
    throw 'Could not replace WorldOccupancyService database routing.'
}

# Keep reads of canonical NPC identity in the main DB.
$oldLoadIds = @'
    private List<int> LoadCharacterIds()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Characters WHERE Id > 0 ORDER BY Id;";
'@

$newLoadIds = @'
    private List<int> LoadCharacterIds()
    {
        using var conn = OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Characters WHERE Id > 0 ORDER BY Id;";
'@

if ($text.Contains($oldLoadIds)) {
    $text = $text.Replace($oldLoadIds, $newLoadIds)
}
elseif ($text -notmatch 'private List<int> LoadCharacterIds\(\)\s*\{\s*using var conn = OpenMain\(\);') {
    throw 'Could not reroute LoadCharacterIds to the main DB.'
}

# Scene membership/contact tables remain on their current main-DB owner in 8B.
$sceneMethods = @(
    'private List<string> LoadActivePlayers(string sceneId)',
    'private void BreakSceneContacts(',
    'private int ActivePlayerCount(string sceneId)'
)

foreach ($sig in $sceneMethods) {
    $start = $text.IndexOf($sig, [System.StringComparison]::Ordinal)
    if ($start -lt 0) { throw ('Could not find method: ' + $sig) }

    $openIdx = $text.IndexOf('using var conn = Open();', $start, [System.StringComparison]::Ordinal)
    if ($openIdx -lt 0) { throw ('Could not find Open() inside method: ' + $sig) }

    # Make sure we did not accidentally jump into a later method.
    $nextMethod = $text.IndexOf('private ', $start + $sig.Length, [System.StringComparison]::Ordinal)
    if ($nextMethod -ge 0 -and $openIdx -gt $nextMethod) {
        throw ('Open() was not found inside expected method: ' + $sig)
    }

    $text = $text.Remove($openIdx, 'using var conn = Open();'.Length)
    $text = $text.Insert($openIdx, 'using var conn = OpenMain();')
}

# Add a main DB opener beside the canonical location opener.
if ($text -notmatch 'private SqliteConnection OpenMain\(\)') {
$openMain = @'

    private static SqliteConnection OpenMain()
    {
        var conn = new SqliteConnection(
            "Data Source=" + ProjectEveDatabaseSetup.MainDatabasePath);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

'@

    $anchor = '    private void EnsureSchema()'
    $idx = $text.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) { throw 'Could not find EnsureSchema insertion point.' }
    $text = $text.Insert($idx, $openMain)
}

# Add one-time migration helper before EnsureSchema.
if ($text -notmatch 'private void MigrateLegacyMainOccupancyDataIfNeeded') {
$helper = @'

    /// <summary>
    /// One-time compatibility migration:
    /// when canonical occupancy tables are empty, copy existing legacy rows
    /// from project_eve.db into project_eve_locations.db.
    /// Legacy rows are never deleted.
    /// </summary>
    private void MigrateLegacyMainOccupancyDataIfNeeded()
    {
        using var conn = Open();

        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM NpcScheduleBinding;";
            long existing = Convert.ToInt64(count.ExecuteScalar() ?? 0);
            if (existing > 0)
                return;
        }

        string legacyMainPath = ProjectEveDatabaseSetup.MainDatabasePath;

        if (!File.Exists(legacyMainPath) ||
            string.Equals(
                legacyMainPath,
                _dbPath,
                StringComparison.OrdinalIgnoreCase))
            return;

        string escaped = legacyMainPath.Replace("'", "''");

        try
        {
            using (var attach = conn.CreateCommand())
            {
                attach.CommandText = $"ATTACH DATABASE '{escaped}' AS legacy_main;";
                attach.ExecuteNonQuery();
            }

            CopyLegacyOccupancyTable(
                conn,
                "NpcScheduleBinding",
                "NpcId,HomeLocationId,HomeDisplayName,WorkLocationId," +
                "WorkDisplayName,ScheduleMode,UpdatedRealUtc");

            CopyLegacyOccupancyTable(
                conn,
                "NpcShiftAssignment",
                "Id,NpcId,StartGameTime,EndGameTime,LocationId,Status," +
                "Note,Source,CreatedRealUtc");

            CopyLegacyOccupancyTable(
                conn,
                "NpcScheduleOverride",
                "Id,NpcId,Kind,StartGameTime,EndGameTime,LocationId," +
                "Activity,Note,Status,CreatedRealUtc");

            CopyLegacyOccupancyTable(
                conn,
                "NpcWorldLocationState",
                "NpcId,NpcName,Status,CurrentLocationId,OriginLocationId," +
                "DestinationLocationId,DepartGameTime,ExpectedArrivalGameTime," +
                "Activity,Source,UpdatedGameTime,UpdatedRealUtc");

            CopyLegacyOccupancyTable(
                conn,
                "NpcWorldMovementEvent",
                "Id,NpcId,NpcName,FromStatus,ToStatus,FromLocationId," +
                "ToLocationId,OriginLocationId,DestinationLocationId," +
                "GameTime,Source,CreatedRealUtc");

            using (var detach = conn.CreateCommand())
            {
                detach.CommandText = "DETACH DATABASE legacy_main;";
                detach.ExecuteNonQuery();
            }
        }
        catch
        {
            // Compatibility migration only.
            // Never block startup and never delete legacy data.
        }
    }

    private static void CopyLegacyOccupancyTable(
        SqliteConnection conn,
        string tableName,
        string columns)
    {
        using (var exists = conn.CreateCommand())
        {
            exists.CommandText = """
                SELECT COUNT(*)
                FROM legacy_main.sqlite_master
                WHERE type='table' AND name=$name;
                """;
            exists.Parameters.AddWithValue("$name", tableName);

            if (Convert.ToInt32(exists.ExecuteScalar() ?? 0) == 0)
                return;
        }

        using var copy = conn.CreateCommand();
        copy.CommandText =
            $"INSERT OR IGNORE INTO {tableName} ({columns}) " +
            $"SELECT {columns} FROM legacy_main.{tableName};";
        copy.ExecuteNonQuery();
    }

'@

    $anchor = '    private void EnsureSchema()'
    $idx = $text.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) { throw 'Could not find EnsureSchema insertion point for migration helper.' }
    $text = $text.Insert($idx, $helper)
}

Set-Content $path $text -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

@'
PASS 8B - NPC OCCUPANCY / MOVEMENT ROUTING
==========================================

Canonical location DB:
D:\ProjectEveData\Database\project_eve_locations.db

Moved for active occupancy writes:
- NpcScheduleBinding
- NpcShiftAssignment
- NpcScheduleOverride
- NpcWorldLocationState
- NpcWorldMovementEvent

Still read from main DB where appropriate:
- Characters identity list
- CharacterRepository NPC/job/profile truth
- SharedScenePlayerMembership (temporary)
- ScenePhysicalContact (temporary)

Why:
WorldOccupancyService mixes two kinds of data:
1. location/occupancy state
2. canonical NPC identity/profile and scene compatibility tables

Pass 8B moves only location/occupancy truth.
Scene membership/contact ownership will be handled separately after their
own services are audited.

Compatibility:
- Existing legacy occupancy rows are copied into locations.db only when the
  canonical occupancy tables are empty.
- Legacy main-DB rows are never deleted.
'@ | Set-Content (Join-Path $reportRoot 'PASS8B_OCCUPANCY_LOCATION_ROUTING.txt') -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'NPC Occupancy / Movement Routing Pass 8B applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'Moved to project_eve_locations.db:'
Write-Host '  NpcScheduleBinding'
Write-Host '  NpcShiftAssignment'
Write-Host '  NpcScheduleOverride'
Write-Host '  NpcWorldLocationState'
Write-Host '  NpcWorldMovementEvent'
Write-Host ''
Write-Host 'Characters and temporary scene compatibility reads remain on main DB.'
Write-Host 'No legacy rows or tables were deleted.'
Write-Host 'No NPCs or databases were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
