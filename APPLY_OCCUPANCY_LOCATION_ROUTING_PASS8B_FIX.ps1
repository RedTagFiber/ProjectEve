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
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass8BFix' $stamp
$backupFile = Join-Path $backupRoot 'World\WorldOccupancyService.cs'
New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
Copy-Item $path $backupFile -Force

$text = Get-Content $path -Raw
$nl = [Environment]::NewLine

# We intentionally use fully-qualified ProjectEve.Data.ProjectEveDatabaseSetup
# so this repair does not depend on the exact using directives in the local file.

# 1) Route the service's primary DB path to locations DB.
if ($text -notmatch 'ProjectEve\.Data\.ProjectEveDatabaseSetup\.LocationDatabasePath') {

    $pattern = '(?s)_dbPath\s*=\s*Environment\.GetEnvironmentVariable\("EVE_DB_PATH"\)\s*\?\?\s*Path\.Combine\(AppContext\.BaseDirectory,\s*"Data",\s*"project_eve\.db"\);\s*(?:var parent = Path\.GetDirectoryName\(_dbPath\);\s*if \(!string\.IsNullOrWhiteSpace\(parent\)\)\s*Directory\.CreateDirectory\(parent\);\s*)?EnsureSchema\(\);'

    $replacement = @'
ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();
        _dbPath = ProjectEve.Data.ProjectEveDatabaseSetup.LocationDatabasePath;

        EnsureSchema();
        MigrateLegacyMainOccupancyDataIfNeeded();
'@

    if ([regex]::IsMatch($text, $pattern)) {
        $text = [regex]::Replace($text, $pattern, $replacement, 1)
    }
    else {
        throw 'Could not locate the WorldOccupancyService database-path block.'
    }
}

# 2) LoadCharacterIds must keep reading Characters from MAIN.
$loadIdsPattern = '(?s)(private\s+List<int>\s+LoadCharacterIds\(\)\s*\{\s*)using var conn = Open\(\);'
if ([regex]::IsMatch($text, $loadIdsPattern)) {
    $text = [regex]::Replace(
        $text,
        $loadIdsPattern,
        '$1using var conn = OpenMain();',
        1)
}
elseif ($text -notmatch '(?s)private\s+List<int>\s+LoadCharacterIds\(\)\s*\{\s*using var conn = OpenMain\(\);') {
    throw 'Could not reroute LoadCharacterIds to MAIN.'
}

# Helper: replace first Open() call inside a named method with OpenMain().
function Route-Method-ToMain {
    param(
        [string]$MethodMarker
    )

    $start = $text.IndexOf($MethodMarker, [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw ('Could not find method marker: ' + $MethodMarker)
    }

    $open = $text.IndexOf('using var conn = Open();', $start, [System.StringComparison]::Ordinal)
    if ($open -lt 0) {
        # Already routed?
        $already = $text.IndexOf('using var conn = OpenMain();', $start, [System.StringComparison]::Ordinal)
        if ($already -ge 0) { return }
        throw ('Could not find DB open call in method: ' + $MethodMarker)
    }

    # Ensure the match belongs to this method, not a later one.
    $nextPrivate = $text.IndexOf($nl + '    private ', $start + $MethodMarker.Length, [System.StringComparison]::Ordinal)
    if ($nextPrivate -ge 0 -and $open -gt $nextPrivate) {
        throw ('Could not safely route method: ' + $MethodMarker)
    }

    $text = $text.Remove($open, 'using var conn = Open();'.Length)
    $text = $text.Insert($open, 'using var conn = OpenMain();')
}

# 3) Scene compatibility tables remain on MAIN for this pass.
Route-Method-ToMain 'private List<string> LoadActivePlayers(string sceneId)'
Route-Method-ToMain 'private void BreakSceneContacts('
Route-Method-ToMain 'private int ActivePlayerCount(string sceneId)'

# 4) Add OpenMain() if missing.
if ($text -notmatch 'private\s+static\s+SqliteConnection\s+OpenMain\(\)') {
$openMain = @'

    private static SqliteConnection OpenMain()
    {
        var conn = new SqliteConnection(
            "Data Source=" + ProjectEve.Data.ProjectEveDatabaseSetup.MainDatabasePath);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

'@

    $anchor = '    private void EnsureSchema()'
    $idx = $text.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) {
        throw 'Could not find EnsureSchema insertion point.'
    }
    $text = $text.Insert($idx, $openMain)
}

# 5) Add one-time legacy occupancy migration helper if missing.
if ($text -notmatch 'private\s+void\s+MigrateLegacyMainOccupancyDataIfNeeded\(\)') {
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

        string legacyMainPath =
            ProjectEve.Data.ProjectEveDatabaseSetup.MainDatabasePath;

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
                attach.CommandText =
                    $"ATTACH DATABASE '{escaped}' AS legacy_main;";
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
    if ($idx -lt 0) {
        throw 'Could not find EnsureSchema insertion point for migration helper.'
    }
    $text = $text.Insert($idx, $helper)
}

# 6) Sanity checks before writing.
if ($text -notmatch 'ProjectEve\.Data\.ProjectEveDatabaseSetup\.LocationDatabasePath') {
    throw 'Location DB routing marker is missing.'
}
if ($text -notmatch '(?s)LoadCharacterIds\(\).*?OpenMain\(\)') {
    throw 'LoadCharacterIds MAIN routing marker is missing.'
}
if ($text -notmatch 'MigrateLegacyMainOccupancyDataIfNeeded\(\);') {
    throw 'Migration call marker is missing.'
}

Set-Content $path $text -Encoding UTF8

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 8B repair/continuation applied.' -ForegroundColor Green
Write-Host ('Backup: ' + $backupRoot)
Write-Host ''
Write-Host 'This repair avoided local using-directive assumptions.'
Write-Host 'No database rows or NPCs were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
