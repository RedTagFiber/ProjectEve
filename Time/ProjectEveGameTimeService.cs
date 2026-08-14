using Microsoft.Data.Sqlite;
using ProjectEve.Core.Time;

namespace ProjectEve.Time;

/// <summary>
/// SQLite-backed authoritative world clock and lightweight player-event queue.
/// The stored game time only changes when Project Eve explicitly advances it.
/// </summary>
public sealed class ProjectEveGameTimeService : IGameTimeService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;
    private DateTimeOffset _now;

    public ProjectEveGameTimeService()
    {
        var configured = Environment.GetEnvironmentVariable("EVE_DB_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _dbPath = configured;
        }
        else
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dir);
            _dbPath = Path.Combine(dir, "project_eve.db");
        }

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
        _now = LoadOrCreateClock();
    }

    public DateTimeOffset Now => _now;

    public event Action<GameTimeSnapshot>? Changed;

    public GameTimeSnapshot GetSnapshot()
        => new()
        {
            GameTime = _now,
            RealUtcObserved = DateTime.UtcNow,
            WorldPausedWhenNoPlayers = true
        };

    public async Task<GameTimeAdvanceResult> AdvanceByAsync(
        string playerId,
        TimeSpan amount,
        string reason = "player_wait",
        CancellationToken cancellationToken = default)
    {
        if (amount <= TimeSpan.Zero)
        {
            return new GameTimeAdvanceResult
            {
                FromGameTime = _now,
                ToGameTime = _now,
                Message = "Time was not advanced."
            };
        }

        return await AdvanceUntilAsync(
            playerId,
            _now.Add(amount),
            reason,
            cancellationToken);
    }

    public async Task<GameTimeAdvanceResult> AdvanceUntilAsync(
        string playerId,
        DateTimeOffset targetGameTime,
        string reason = "player_wait_until",
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var from = _now;
            if (targetGameTime <= from)
            {
                return new GameTimeAdvanceResult
                {
                    FromGameTime = from,
                    ToGameTime = from,
                    Message = "Target time is not later than the current game time."
                };
            }

            var interrupt = FindNextInterruptingEvent(playerId, from, targetGameTime);
            var final = interrupt?.GameTime ?? targetGameTime;

            PersistClock(final, reason);
            _now = final;

            if (interrupt is not null)
                MarkTriggered(interrupt.Id);

            PublishChanged();

            return new GameTimeAdvanceResult
            {
                FromGameTime = from,
                ToGameTime = final,
                InterruptedByEvent = interrupt is not null,
                Event = interrupt,
                Message = interrupt is null
                    ? $"Advanced to {final:ddd h:mm tt}."
                    : $"Stopped for: {interrupt.Title}"
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GameTimeAdvanceResult> AdvanceToNextPlayerEventAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var from = _now;
            var next = FindNextPlayerEvent(playerId, includeCurrent: true);

            if (next is null)
            {
                return new GameTimeAdvanceResult
                {
                    FromGameTime = from,
                    ToGameTime = from,
                    Message = "No player-relevant event is queued yet."
                };
            }

            var final = next.GameTime > from ? next.GameTime : from;
            PersistClock(final, "next_event");
            _now = final;
            MarkTriggered(next.Id);
            PublishChanged();

            return new GameTimeAdvanceResult
            {
                FromGameTime = from,
                ToGameTime = final,
                InterruptedByEvent = true,
                Event = next,
                Message = next.Title
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GameEventRecord?> PeekNextPlayerEventAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return FindNextPlayerEvent(playerId, includeCurrent: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> SchedulePlayerEventAsync(
        GameEventScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.GameTime == default)
            request.GameTime = _now;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            if (!string.IsNullOrWhiteSpace(request.SourceKey))
            {
                using var existing = conn.CreateCommand();
                existing.CommandText = "SELECT Id FROM GameEvent WHERE SourceKey=$key LIMIT 1;";
                existing.Parameters.AddWithValue("$key", request.SourceKey.Trim());
                var found = existing.ExecuteScalar();
                if (found is not null && found != DBNull.Value)
                    return Convert.ToInt64(found);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO GameEvent
                    (PlayerId,EventType,Title,GameTime,InterruptFastForward,
                     Status,SourceKey,DataJson,CreatedRealUtc)
                VALUES
                    ($player,$type,$title,$game,$interrupt,
                     'scheduled',$source,$data,$created);
                SELECT last_insert_rowid();
                """;

            cmd.Parameters.AddWithValue("$player", Clean(request.PlayerId, "*"));
            cmd.Parameters.AddWithValue("$type", Clean(request.EventType, "world_event"));
            cmd.Parameters.AddWithValue("$title", Clean(request.Title, "Something happens"));
            cmd.Parameters.AddWithValue("$game", request.GameTime.ToString("O"));
            cmd.Parameters.AddWithValue("$interrupt", request.InterruptFastForward ? 1 : 0);
            cmd.Parameters.AddWithValue("$source", string.IsNullOrWhiteSpace(request.SourceKey) ? DBNull.Value : request.SourceKey.Trim());
            cmd.Parameters.AddWithValue("$data", string.IsNullOrWhiteSpace(request.DataJson) ? "{}" : request.DataJson);
            cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));

            var id = Convert.ToInt64(cmd.ExecuteScalar());
            PublishChanged();
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkEventHandledAsync(
        long eventId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE GameEvent SET Status='handled' WHERE Id=$id;";
            cmd.Parameters.AddWithValue("$id", eventId);
            cmd.ExecuteNonQuery();
            PublishChanged();
        }
        finally
        {
            _gate.Release();
        }
    }

    private GameEventRecord? FindNextInterruptingEvent(
        string playerId,
        DateTimeOffset after,
        DateTimeOffset through)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id,PlayerId,EventType,Title,GameTime,InterruptFastForward,
                   Status,SourceKey,DataJson
            FROM GameEvent
            WHERE Status='scheduled'
              AND InterruptFastForward=1
              AND (PlayerId=$player OR PlayerId='*')
              AND GameTime > $after
              AND GameTime <= $through
            ORDER BY GameTime,Id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "*"));
        cmd.Parameters.AddWithValue("$after", after.ToString("O"));
        cmd.Parameters.AddWithValue("$through", through.ToString("O"));

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEvent(r) : null;
    }

    private GameEventRecord? FindNextPlayerEvent(string playerId, bool includeCurrent)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        var comparison = includeCurrent ? ">=" : ">";
        cmd.CommandText = $"""
            SELECT Id,PlayerId,EventType,Title,GameTime,InterruptFastForward,
                   Status,SourceKey,DataJson
            FROM GameEvent
            WHERE Status='scheduled'
              AND InterruptFastForward=1
              AND (PlayerId=$player OR PlayerId='*')
              AND GameTime {comparison} $now
            ORDER BY GameTime,Id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "*"));
        cmd.Parameters.AddWithValue("$now", _now.ToString("O"));

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEvent(r) : null;
    }

    private static GameEventRecord ReadEvent(SqliteDataReader r)
        => new()
        {
            Id = r.GetInt64(0),
            PlayerId = r.GetString(1),
            EventType = r.GetString(2),
            Title = r.GetString(3),
            GameTime = ParseGameTime(r.GetString(4)),
            InterruptFastForward = r.GetInt32(5) != 0,
            Status = r.GetString(6),
            SourceKey = r.IsDBNull(7) ? null : r.GetString(7),
            DataJson = r.IsDBNull(8) ? "{}" : r.GetString(8)
        };

    private void PersistClock(DateTimeOffset value, string reason)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO GameClockState(Id,CurrentGameTime,UpdatedRealUtc,LastReason)
            VALUES(1,$game,$real,$reason)
            ON CONFLICT(Id) DO UPDATE SET
                CurrentGameTime=excluded.CurrentGameTime,
                UpdatedRealUtc=excluded.UpdatedRealUtc,
                LastReason=excluded.LastReason;
            """;
        cmd.Parameters.AddWithValue("$game", value.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$reason", Clean(reason, "advance"));
        cmd.ExecuteNonQuery();
    }

    private DateTimeOffset LoadOrCreateClock()
    {
        using var conn = Open();
        using var find = conn.CreateCommand();
        find.CommandText = "SELECT CurrentGameTime FROM GameClockState WHERE Id=1 LIMIT 1;";
        var value = find.ExecuteScalar()?.ToString();
        if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, out var parsed))
            return parsed;

        var env = Environment.GetEnvironmentVariable("EVE_GAME_START_TIME");
        DateTimeOffset start;
        if (!string.IsNullOrWhiteSpace(env) && DateTimeOffset.TryParse(env, out var configured))
            start = configured;
        else
            start = DateTimeOffset.Now;

        start = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, start.Offset);
        PersistClock(start, "first_boot");
        return start;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS GameClockState(
                Id INTEGER PRIMARY KEY CHECK(Id=1),
                CurrentGameTime TEXT NOT NULL,
                UpdatedRealUtc TEXT NOT NULL,
                LastReason TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS GameEvent(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlayerId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Title TEXT NOT NULL,
                GameTime TEXT NOT NULL,
                InterruptFastForward INTEGER NOT NULL DEFAULT 1,
                Status TEXT NOT NULL DEFAULT 'scheduled',
                SourceKey TEXT NULL,
                DataJson TEXT NOT NULL DEFAULT '{}',
                CreatedRealUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_GameEvent_PlayerTime
                ON GameEvent(PlayerId,Status,GameTime);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_GameEvent_SourceKey
                ON GameEvent(SourceKey)
                WHERE SourceKey IS NOT NULL;
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        return conn;
    }

    private void MarkTriggered(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE GameEvent SET Status='triggered' WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private void PublishChanged()
    {
        var snapshot = GetSnapshot();
        _ = Task.Run(() =>
        {
            try { Changed?.Invoke(snapshot); }
            catch { }
        });
    }

    private static DateTimeOffset ParseGameTime(string value)
        => DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.Now;

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
