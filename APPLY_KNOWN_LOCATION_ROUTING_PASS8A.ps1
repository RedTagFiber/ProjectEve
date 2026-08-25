$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'World\KnownLocationService.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $path)) {
    throw 'World\KnownLocationService.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass8A' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\CanonicalPass8A' $stamp

$backupFile = Join-Path $backupRoot 'World\KnownLocationService.cs'
$archiveFile = Join-Path $archiveRoot 'World\KnownLocationService.pre-location-routing.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null
Copy-Item $path $backupFile -Force
Copy-Item $path $archiveFile -Force

$text = Get-Content $path -Raw
$nl = [Environment]::NewLine

# Add canonical data namespace.
if ($text -notmatch '(?m)^using ProjectEve\.Data;') {
    $insertAt = $text.IndexOf('using ProjectEve.Core.Time;')
    if ($insertAt -lt 0) {
        $insertAt = $text.IndexOf('namespace ProjectEve.World;')
    }
    if ($insertAt -lt 0) {
        throw 'Could not find using insertion point.'
    }

    $text = $text.Insert($insertAt, 'using ProjectEve.Data;' + $nl)
}

$old = @'
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
'@

$new = @'
        ProjectEveDatabaseSetup.EnsureAll();
        _dbPath = ProjectEveDatabaseSetup.LocationDatabasePath;

        EnsureSchema();
        MigrateLegacyMainLocationDirectoryIfNeeded();
'@

if ($text.Contains($old)) {
    $text = $text.Replace($old, $new)
}
elseif ($text -notmatch 'ProjectEveDatabaseSetup\.LocationDatabasePath') {
    throw 'Could not replace KnownLocationService database routing.'
}

# Add one-time copy helper before EnsureSchema.
if ($text -notmatch 'private void MigrateLegacyMainLocationDirectoryIfNeeded') {
$helper = @'

    /// <summary>
    /// One-time compatibility migration for the travel directory.
    /// If the canonical location DB has no directory rows yet, copy any
    /// existing legacy rows from project_eve.db. Legacy rows are preserved.
    /// </summary>
    private void MigrateLegacyMainLocationDirectoryIfNeeded()
    {
        using var conn = Open();

        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM TravelLocationIndex;";
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

            if (LegacyTableExists(conn, "TravelLocationIndex"))
            {
                using var copy = conn.CreateCommand();
                copy.CommandText = """
                    INSERT OR IGNORE INTO TravelLocationIndex
                    (LocationId,Name,Aliases,LocationType,AddressText,UpdatedRealUtc)
                    SELECT LocationId,Name,Aliases,LocationType,AddressText,UpdatedRealUtc
                    FROM legacy_main.TravelLocationIndex;
                    """;
                copy.ExecuteNonQuery();
            }

            if (LegacyTableExists(conn, "PlayerKnownLocation"))
            {
                using var copy = conn.CreateCommand();
                copy.CommandText = """
                    INSERT OR IGNORE INTO PlayerKnownLocation
                    (PlayerId,LocationId,LearnedFrom,FirstKnownGameTime,
                     CanTravelDirectly,UpdatedRealUtc)
                    SELECT PlayerId,LocationId,LearnedFrom,FirstKnownGameTime,
                           CanTravelDirectly,UpdatedRealUtc
                    FROM legacy_main.PlayerKnownLocation;
                    """;
                copy.ExecuteNonQuery();
            }

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

    private static bool LegacyTableExists(
        SqliteConnection conn,
        string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM legacy_main.sqlite_master
            WHERE type='table' AND name=$name;
            """;
        cmd.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

'@

    $anchor = '    private void EnsureSchema()'
    $idx = $text.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) {
        throw 'Could not find EnsureSchema insertion point.'
    }
    $text = $text.Insert($idx, $helper)
}

Set-Content $path $text -Encoding UTF8

$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
@'
PASS 8A - KNOWN LOCATION DIRECTORY ROUTING
==========================================

Canonical DB:
D:\ProjectEveData\Database\project_eve_locations.db

Moved for future writes:
- TravelLocationIndex
- PlayerKnownLocation

Service:
World\KnownLocationService.cs

Compatibility:
- Existing legacy main-DB rows are copied once only when canonical location
  directory is empty.
- Legacy rows are never deleted.
- No cross-database JOIN is introduced.

NOT YET MOVED IN 8A:
- NpcWorldLocationState
- NpcScheduleBinding
- occupancy / movement state
- player presence / travel state
- scene perception tables

Those need a separate routing pass because some of those services also read
canonical NPC/job/finance data from project_eve.db.
'@ | Set-Content (Join-Path $reportRoot 'PASS8A_KNOWN_LOCATION_ROUTING.txt') -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Known Location Routing Pass 8A applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'TravelLocationIndex -> project_eve_locations.db'
Write-Host 'PlayerKnownLocation -> project_eve_locations.db'
Write-Host 'Legacy rows remain preserved in project_eve.db.'
Write-Host ''
Write-Host 'No NPCs or databases were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
