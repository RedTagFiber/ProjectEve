using Microsoft.Data.Sqlite;
using ProjectEve.Core.Time;
using ProjectEve.Core.World;

namespace ProjectEve.World;

/// <summary>
/// ProjectEve-owned knowledge gate for travel search.
/// The directory may contain many locations; search returns only locations the
/// specific player has learned and can directly travel to.
/// </summary>
public sealed class KnownLocationService : IKnownLocationService
{
    private readonly IGameTimeService _clock;
    private readonly string _dbPath;

    public KnownLocationService(IGameTimeService clock)
    {
        _clock = clock;
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public Task RegisterWorldLocationAsync(
        WorldLocationRegistration location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(location.LocationId))
            throw new ArgumentException("LocationId is required.", nameof(location));

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TravelLocationIndex(LocationId,Name,Aliases,LocationType,AddressText,UpdatedRealUtc)
            VALUES($id,$name,$aliases,$type,$address,$utc)
            ON CONFLICT(LocationId) DO UPDATE SET
                Name=excluded.Name,
                Aliases=excluded.Aliases,
                LocationType=excluded.LocationType,
                AddressText=excluded.AddressText,
                UpdatedRealUtc=excluded.UpdatedRealUtc;
            """;
        cmd.Parameters.AddWithValue("$id", location.LocationId.Trim());
        cmd.Parameters.AddWithValue("$name", Clean(location.Name, location.LocationId));
        cmd.Parameters.AddWithValue("$aliases", location.Aliases?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$type", Clean(location.LocationType, "place"));
        cmd.Parameters.AddWithValue("$address", location.AddressText?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task LearnLocationAsync(
        string playerId,
        string locationId,
        string source,
        bool canTravelDirectly = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PlayerKnownLocation
                (PlayerId,LocationId,LearnedFrom,FirstKnownGameTime,
                 CanTravelDirectly,UpdatedRealUtc)
            VALUES($player,$location,$source,$game,$direct,$utc)
            ON CONFLICT(PlayerId,LocationId) DO UPDATE SET
                LearnedFrom=CASE
                    WHEN PlayerKnownLocation.LearnedFrom='' THEN excluded.LearnedFrom
                    ELSE PlayerKnownLocation.LearnedFrom
                END,
                CanTravelDirectly=MAX(PlayerKnownLocation.CanTravelDirectly,excluded.CanTravelDirectly),
                UpdatedRealUtc=excluded.UpdatedRealUtc;
            """;
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "legacy-player"));
        cmd.Parameters.AddWithValue("$location", Clean(locationId, "unknown"));
        cmd.Parameters.AddWithValue("$source", Clean(source, "discovered"));
        cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$direct", canTravelDirectly ? 1 : 0);
        cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KnownLocationResult>> SearchKnownAsync(
        string playerId,
        string query,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        limit = Math.Clamp(limit, 1, 20);
        var q = query?.Trim() ?? "";

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.LocationId,d.Name,d.LocationType,d.AddressText,
                   k.LearnedFrom,k.CanTravelDirectly,k.FirstKnownGameTime
            FROM PlayerKnownLocation k
            JOIN TravelLocationIndex d ON d.LocationId=k.LocationId
            WHERE k.PlayerId=$player
              AND k.CanTravelDirectly=1
              AND ($q='' OR d.LocationId LIKE $like COLLATE NOCASE
                         OR d.Name LIKE $like COLLATE NOCASE
                         OR d.Aliases LIKE $like COLLATE NOCASE
                         OR d.AddressText LIKE $like COLLATE NOCASE
                         OR d.LocationType LIKE $like COLLATE NOCASE)
            ORDER BY
                CASE WHEN d.Name LIKE $prefix COLLATE NOCASE THEN 0 ELSE 1 END,
                d.Name COLLATE NOCASE
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "legacy-player"));
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$like", "%" + q + "%");
        cmd.Parameters.AddWithValue("$prefix", q + "%");
        cmd.Parameters.AddWithValue("$limit", limit);

        var rows = new List<KnownLocationResult>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new KnownLocationResult
            {
                LocationId = r.GetString(0),
                Name = r.GetString(1),
                LocationType = r.GetString(2),
                AddressText = r.GetString(3),
                LearnedFrom = r.GetString(4),
                CanTravelDirectly = r.GetInt32(5) != 0,
                FirstKnownGameTime = DateTimeOffset.TryParse(r.GetString(6), out var parsed) ? parsed : _clock.Now
            });
        }

        return Task.FromResult<IReadOnlyList<KnownLocationResult>>(rows);
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS TravelLocationIndex(
                LocationId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Aliases TEXT NOT NULL DEFAULT '',
                LocationType TEXT NOT NULL DEFAULT 'place',
                AddressText TEXT NOT NULL DEFAULT '',
                UpdatedRealUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PlayerKnownLocation(
                PlayerId TEXT NOT NULL,
                LocationId TEXT NOT NULL,
                LearnedFrom TEXT NOT NULL DEFAULT '',
                FirstKnownGameTime TEXT NOT NULL,
                CanTravelDirectly INTEGER NOT NULL DEFAULT 0,
                UpdatedRealUtc TEXT NOT NULL,
                PRIMARY KEY(PlayerId,LocationId),
                FOREIGN KEY(LocationId) REFERENCES TravelLocationIndex(LocationId)
            );

            CREATE INDEX IF NOT EXISTS IX_PlayerKnownLocation_Player
                ON PlayerKnownLocation(PlayerId,CanTravelDirectly);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        return conn;
    }

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
