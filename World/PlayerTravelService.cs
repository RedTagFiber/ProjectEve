using Microsoft.Data.Sqlite;
using ProjectEve.Core.Time;
using ProjectEve.Core.World;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.World;

/// <summary>
/// Phase 15 route planner + travel execution.
///
/// Route durations are explicit world data in WorldTravelRoute. If no route is
/// known, travel is unavailable rather than inventing a duration.
/// </summary>
public sealed class PlayerTravelService : IPlayerTravelService
{
    private readonly IPlayerWorldPresenceService _presence;
    private readonly IKnownLocationService _knownLocations;
    private readonly IWorldAdvanceCoordinator _worldTime;
    private readonly IGameTimeService _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    public PlayerTravelService(
        IPlayerWorldPresenceService presence,
        IKnownLocationService knownLocations,
        IWorldAdvanceCoordinator worldTime,
        IGameTimeService clock)
    {
        _presence = presence;
        _knownLocations = knownLocations;
        _worldTime = worldTime;
        _clock = clock;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<long> RegisterRouteAsync(
        WorldTravelRouteRegistration route,
        CancellationToken cancellationToken = default)
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));

        string from = Clean(route.FromLocationId, "");
        string to = Clean(route.ToLocationId, "");
        string method = NormalizeMethod(route.Method);

        if (from.Length == 0 || to.Length == 0)
            throw new ArgumentException("Both route endpoint IDs are required.");
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A route must connect two different locations.");
        if (route.Minutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(route.Minutes), "Route minutes must be greater than zero.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            long first = UpsertRoute(
                from,
                to,
                method,
                route.Minutes,
                Clean(route.Source, "authored_world"),
                Clean(route.Note, ""));

            if (route.Bidirectional)
            {
                UpsertRoute(
                    to,
                    from,
                    method,
                    route.Minutes,
                    Clean(route.Source, "authored_world"),
                    Clean(route.Note, ""));
            }

            return first;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerTravelPlan> PlanAsync(
        PlayerTravelPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string playerId = Clean(request.PlayerId, "");
        string destinationId = Clean(request.DestinationLocationId, "");
        string method = NormalizeMethod(request.Method);

        if (playerId.Length == 0 || destinationId.Length == 0)
        {
            return Unavailable(
                playerId,
                destinationId,
                method,
                "Player and destination are required.");
        }

        var state = await _presence.GetAsync(playerId, cancellationToken);
        if (state == null)
            return Unavailable(playerId, destinationId, method, "Player has no world-presence state.");

        if (state.Status.Equals("traveling", StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(
                playerId,
                destinationId,
                method,
                "You are already traveling. Continue the current trip first.");
        }

        if (!state.HasLocation)
            return Unavailable(playerId, destinationId, method, "Player is not currently at a travel origin.");

        if (state.LocationId.Equals(destinationId, StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(
                playerId,
                destinationId,
                method,
                "You are already at that location.");
        }

        // Knowledge gate. SearchKnownAsync is the current authoritative player
        // travel-knowledge projection; require an exact destination match.
        var known = await _knownLocations.SearchKnownAsync(
            playerId,
            destinationId,
            25,
            cancellationToken);

        var destination = known.FirstOrDefault(x =>
            x.LocationId.Equals(destinationId, StringComparison.OrdinalIgnoreCase));

        if (destination == null)
        {
            return Unavailable(
                playerId,
                destinationId,
                method,
                "You do not know that destination well enough to travel there.");
        }

        if (!destination.CanTravelDirectly)
        {
            return Unavailable(
                playerId,
                destinationId,
                method,
                "You know about that place, but do not know how to travel there directly yet.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var legs = FindShortestRoute(
                state.LocationId,
                destinationId,
                method);

            if (legs.Count == 0)
            {
                return new PlayerTravelPlan
                {
                    Available = false,
                    PlayerId = playerId,
                    OriginLocationId = state.LocationId,
                    OriginDisplayName = state.LocationDisplayName,
                    DestinationLocationId = destinationId,
                    DestinationDisplayName = destination.Name,
                    Method = method,
                    Reason =
                        $"No {method} route/time is registered between " +
                        $"{state.LocationDisplayName} and {destination.Name}. " +
                        "Project Eve will not invent a travel time."
                };
            }

            int total = legs.Sum(x => x.Minutes);

            return new PlayerTravelPlan
            {
                Available = true,
                PlayerId = playerId,
                OriginLocationId = state.LocationId,
                OriginDisplayName = state.LocationDisplayName,
                DestinationLocationId = destinationId,
                DestinationDisplayName = destination.Name,
                Method = method,
                TotalMinutes = total,
                Legs = legs,
                Reason = $"{total} game minutes by {method}."
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerTravelStartResult> StartTravelAsync(
        PlayerTravelStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var plan = await PlanAsync(request, cancellationToken);
        if (!plan.Available)
        {
            return new PlayerTravelStartResult
            {
                Plan = plan,
                Started = false,
                Message = plan.Reason
            };
        }

        await _gate.WaitAsync(cancellationToken);
        PlayerTravelJourney journey;

        try
        {
            var existing = LoadActiveJourney(request.PlayerId);
            if (existing != null)
            {
                return new PlayerTravelStartResult
                {
                    Plan = plan,
                    Journey = existing,
                    Started = false,
                    Message = "A trip is already active."
                };
            }

            var depart = _clock.Now;
            var arrival = depart.AddMinutes(plan.TotalMinutes);

            long journeyId = InsertJourney(
                request,
                plan,
                depart,
                arrival);

            journey = LoadJourney(journeyId)
                ?? throw new InvalidOperationException("Travel journey could not be reloaded.");

            long eventId = await _clock.SchedulePlayerEventAsync(
                new GameEventScheduleRequest
                {
                    PlayerId = request.PlayerId,
                    EventType = "player_travel_arrival",
                    Title = $"Arrive at {plan.DestinationDisplayName}",
                    GameTime = arrival,
                    InterruptFastForward = true,
                    SourceKey = $"player_travel_arrival:{journeyId}",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        JourneyId = journeyId,
                        plan.DestinationLocationId,
                        plan.DestinationDisplayName,
                        plan.Method
                    })
                },
                cancellationToken);

            SetArrivalEventId(journeyId, eventId);
            journey.ArrivalEventId = eventId;
        }
        finally
        {
            _gate.Release();
        }

        await _presence.BeginTravelAsync(
            new PlayerWorldTravelStartRequest
            {
                PlayerId = request.PlayerId,
                PlayerName = request.PlayerName,
                TravelId = journey.Id,
                DestinationLocationId = journey.DestinationLocationId,
                DestinationDisplayName = journey.DestinationDisplayName,
                Method = journey.Method,
                DepartGameTime = journey.DepartGameTime,
                ExpectedArrivalGameTime = journey.ExpectedArrivalGameTime,
                Reason = "player_travel_depart"
            },
            cancellationToken);

        return await ContinueJourneyCoreAsync(
            journey,
            plan,
            cancellationToken);
    }

    public async Task<PlayerTravelStartResult> ContinueTravelAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");

        await _gate.WaitAsync(cancellationToken);
        PlayerTravelJourney? journey;
        try
        {
            journey = LoadActiveJourney(playerId);
        }
        finally
        {
            _gate.Release();
        }

        if (journey == null)
        {
            return new PlayerTravelStartResult
            {
                Started = false,
                Message = "No active trip."
            };
        }

        var plan = new PlayerTravelPlan
        {
            Available = true,
            PlayerId = journey.PlayerId,
            OriginLocationId = journey.OriginLocationId,
            OriginDisplayName = journey.OriginDisplayName,
            DestinationLocationId = journey.DestinationLocationId,
            DestinationDisplayName = journey.DestinationDisplayName,
            Method = journey.Method,
            TotalMinutes = journey.PlannedMinutes,
            Legs = journey.Legs,
            Reason = $"{journey.PlannedMinutes} game minutes by {journey.Method}."
        };

        return await ContinueJourneyCoreAsync(
            journey,
            plan,
            cancellationToken);
    }

    public async Task<PlayerTravelJourney?> GetActiveTravelAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return LoadActiveJourney(playerId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> FinalizeDueTravelsAsync(
        DateTimeOffset gameTime,
        CancellationToken cancellationToken = default)
    {
        List<PlayerTravelJourney> due;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            due = LoadDueJourneys(gameTime);
        }
        finally
        {
            _gate.Release();
        }

        int completed = 0;

        foreach (var journey in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await CompleteJourneyAsync(
                journey,
                gameTime,
                cancellationToken);

            completed++;
        }

        return completed;
    }

    private async Task<PlayerTravelStartResult> ContinueJourneyCoreAsync(
        PlayerTravelJourney journey,
        PlayerTravelPlan plan,
        CancellationToken cancellationToken)
    {
        if (_clock.Now >= journey.ExpectedArrivalGameTime)
        {
            await CompleteJourneyAsync(
                journey,
                _clock.Now,
                cancellationToken);

            return new PlayerTravelStartResult
            {
                Plan = plan,
                Journey = await GetJourneyAsync(journey.Id, cancellationToken),
                Started = true,
                Arrived = true,
                Message = $"Arrived at {journey.DestinationDisplayName}."
            };
        }

        var time = await _worldTime.AdvanceUntilAsync(
            journey.PlayerId,
            journey.ExpectedArrivalGameTime,
            "player_travel",
            cancellationToken);

        // Arrival may have been the GameEvent that interrupted the world advance.
        if (_clock.Now >= journey.ExpectedArrivalGameTime)
        {
            await CompleteJourneyAsync(
                journey,
                _clock.Now,
                cancellationToken);

            return new PlayerTravelStartResult
            {
                Plan = plan,
                Journey = await GetJourneyAsync(journey.Id, cancellationToken),
                TimeAdvance = time,
                Started = true,
                Arrived = true,
                Interrupted = false,
                Message = $"Arrived at {journey.DestinationDisplayName}."
            };
        }

        string interruptTitle =
            time.Event?.Title ??
            (time.InterruptedByEvent ? time.Message : "");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetLastInterrupt(journey.Id, interruptTitle);
        }
        finally
        {
            _gate.Release();
        }

        var remaining = journey.ExpectedArrivalGameTime - _clock.Now;
        int minutesRemaining = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));

        return new PlayerTravelStartResult
        {
            Plan = plan,
            Journey = await GetJourneyAsync(journey.Id, cancellationToken),
            TimeAdvance = time,
            Started = true,
            Arrived = false,
            Interrupted = time.InterruptedByEvent,
            Message = time.InterruptedByEvent
                ? $"Travel paused for: {time.Message} ({minutesRemaining} game min remaining)."
                : $"Traveling to {journey.DestinationDisplayName} ({minutesRemaining} game min remaining)."
        };
    }

    private async Task CompleteJourneyAsync(
        PlayerTravelJourney journey,
        DateTimeOffset gameTime,
        CancellationToken cancellationToken)
    {
        // Claim completion before moving physical presence so the UI path and
        // hosted clock listener cannot both finalize the same journey.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = LoadJourney(journey.Id);
            if (current == null ||
                !current.Status.Equals("traveling", StringComparison.OrdinalIgnoreCase))
                return;

            MarkArriving(journey.Id);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await _presence.MoveToLocationAsync(
            new PlayerWorldPresenceMoveRequest
            {
                PlayerId = journey.PlayerId,
                PlayerName = journey.PlayerName,
                LocationId = journey.DestinationLocationId,
                LocationDisplayName = journey.DestinationDisplayName,
                Activity = "arrived",
                Attention = 0.82,
                Reason = "player_travel_arrival"
                },
                cancellationToken);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                MarkArrived(journey.Id, gameTime);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                RestoreTraveling(journey.Id);
            }
            finally
            {
                _gate.Release();
            }

            throw;
        }

        if (journey.ArrivalEventId.HasValue)
        {
            try
            {
                await _clock.MarkEventHandledAsync(
                    journey.ArrivalEventId.Value,
                    cancellationToken);
            }
            catch { }
        }
    }

    private async Task<PlayerTravelJourney?> GetJourneyAsync(
        long id,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return LoadJourney(id); }
        finally { _gate.Release(); }
    }

    private List<PlayerTravelLeg> FindShortestRoute(
        string origin,
        string destination,
        string method)
    {
        var edges = LoadRoutes(method);
        if (edges.Count == 0)
            return new List<PlayerTravelLeg>();

        var nodes = new HashSet<string>(
            edges.SelectMany(x => new[] { x.FromLocationId, x.ToLocationId }),
            StringComparer.OrdinalIgnoreCase);

        if (!nodes.Contains(origin) || !nodes.Contains(destination))
            return new List<PlayerTravelLeg>();

        var distance = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, RouteRow>(StringComparer.OrdinalIgnoreCase);
        var unvisited = new HashSet<string>(nodes, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
            distance[node] = int.MaxValue;

        distance[origin] = 0;

        while (unvisited.Count > 0)
        {
            string? current = null;
            int best = int.MaxValue;

            foreach (var node in unvisited)
            {
                int d = distance[node];
                if (d < best)
                {
                    best = d;
                    current = node;
                }
            }

            if (current == null || best == int.MaxValue)
                break;

            if (current.Equals(destination, StringComparison.OrdinalIgnoreCase))
                break;

            unvisited.Remove(current);

            foreach (var edge in edges.Where(x =>
                x.FromLocationId.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                if (!unvisited.Contains(edge.ToLocationId))
                    continue;

                int alt;
                try { alt = checked(best + edge.Minutes); }
                catch { alt = int.MaxValue; }

                if (alt < distance[edge.ToLocationId])
                {
                    distance[edge.ToLocationId] = alt;
                    previous[edge.ToLocationId] = edge;
                }
            }
        }

        if (!previous.ContainsKey(destination))
            return new List<PlayerTravelLeg>();

        var reversed = new List<PlayerTravelLeg>();
        string cursor = destination;

        while (!cursor.Equals(origin, StringComparison.OrdinalIgnoreCase))
        {
            if (!previous.TryGetValue(cursor, out var edge))
                return new List<PlayerTravelLeg>();

            reversed.Add(new PlayerTravelLeg
            {
                RouteId = edge.Id,
                FromLocationId = edge.FromLocationId,
                ToLocationId = edge.ToLocationId,
                Method = edge.Method,
                Minutes = edge.Minutes,
                Source = edge.Source
            });

            cursor = edge.FromLocationId;
        }

        reversed.Reverse();
        return reversed;
    }

    private List<RouteRow> LoadRoutes(string method)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id,FromLocationId,ToLocationId,Method,Minutes,Source,Note
FROM WorldTravelRoute
WHERE IsActive=1 AND Method=$method
ORDER BY Id;";
        cmd.Parameters.AddWithValue("$method", method);

        var list = new List<RouteRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RouteRow
            {
                Id = r.GetInt64(0),
                FromLocationId = r.GetString(1),
                ToLocationId = r.GetString(2),
                Method = r.GetString(3),
                Minutes = r.GetInt32(4),
                Source = r.GetString(5),
                Note = r.GetString(6)
            });
        }

        return list;
    }

    private long UpsertRoute(
        string from,
        string to,
        string method,
        int minutes,
        string source,
        string note)
    {
        using var conn = Open();

        using (var upsert = conn.CreateCommand())
        {
            upsert.CommandText = @"
INSERT INTO WorldTravelRoute
(FromLocationId,ToLocationId,Method,Minutes,Source,Note,IsActive,UpdatedRealUtc)
VALUES($from,$to,$method,$minutes,$source,$note,1,$real)
ON CONFLICT(FromLocationId,ToLocationId,Method) DO UPDATE SET
 Minutes=excluded.Minutes,
 Source=excluded.Source,
 Note=excluded.Note,
 IsActive=1,
 UpdatedRealUtc=excluded.UpdatedRealUtc;";
            upsert.Parameters.AddWithValue("$from", from);
            upsert.Parameters.AddWithValue("$to", to);
            upsert.Parameters.AddWithValue("$method", method);
            upsert.Parameters.AddWithValue("$minutes", minutes);
            upsert.Parameters.AddWithValue("$source", source);
            upsert.Parameters.AddWithValue("$note", note);
            upsert.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            upsert.ExecuteNonQuery();
        }

        using var find = conn.CreateCommand();
        find.CommandText = @"
SELECT Id
FROM WorldTravelRoute
WHERE FromLocationId=$from
  AND ToLocationId=$to
  AND Method=$method
LIMIT 1;";
        find.Parameters.AddWithValue("$from", from);
        find.Parameters.AddWithValue("$to", to);
        find.Parameters.AddWithValue("$method", method);

        return Convert.ToInt64(
            find.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private long InsertJourney(
        PlayerTravelStartRequest request,
        PlayerTravelPlan plan,
        DateTimeOffset depart,
        DateTimeOffset arrival)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO PlayerTravelJourney
(PlayerId,PlayerName,OriginLocationId,OriginDisplayName,
 DestinationLocationId,DestinationDisplayName,Method,PlannedMinutes,
 DepartGameTime,ExpectedArrivalGameTime,ActualArrivalGameTime,
 Status,ArrivalEventId,LastInterruptTitle,LegsJson,CreatedRealUtc,UpdatedRealUtc)
VALUES
($player,$name,$origin,$originName,
 $destination,$destinationName,$method,$minutes,
 $depart,$arrival,NULL,
 'traveling',NULL,'',$legs,$real,$real);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$player", request.PlayerId);
        cmd.Parameters.AddWithValue("$name", Clean(request.PlayerName, "Player"));
        cmd.Parameters.AddWithValue("$origin", plan.OriginLocationId);
        cmd.Parameters.AddWithValue("$originName", plan.OriginDisplayName);
        cmd.Parameters.AddWithValue("$destination", plan.DestinationLocationId);
        cmd.Parameters.AddWithValue("$destinationName", plan.DestinationDisplayName);
        cmd.Parameters.AddWithValue("$method", plan.Method);
        cmd.Parameters.AddWithValue("$minutes", plan.TotalMinutes);
        cmd.Parameters.AddWithValue("$depart", depart.ToString("O"));
        cmd.Parameters.AddWithValue("$arrival", arrival.ToString("O"));
        cmd.Parameters.AddWithValue("$legs", JsonSerializer.Serialize(plan.Legs));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(
            cmd.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private PlayerTravelJourney? LoadActiveJourney(string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id,PlayerId,PlayerName,OriginLocationId,OriginDisplayName,
       DestinationLocationId,DestinationDisplayName,Method,PlannedMinutes,
       DepartGameTime,ExpectedArrivalGameTime,ActualArrivalGameTime,
       Status,ArrivalEventId,LastInterruptTitle,LegsJson
FROM PlayerTravelJourney
WHERE PlayerId=$player AND Status='traveling'
ORDER BY Id DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$player", playerId);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadJourney(r) : null;
    }

    private PlayerTravelJourney? LoadJourney(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id,PlayerId,PlayerName,OriginLocationId,OriginDisplayName,
       DestinationLocationId,DestinationDisplayName,Method,PlannedMinutes,
       DepartGameTime,ExpectedArrivalGameTime,ActualArrivalGameTime,
       Status,ArrivalEventId,LastInterruptTitle,LegsJson
FROM PlayerTravelJourney
WHERE Id=$id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadJourney(r) : null;
    }

    private List<PlayerTravelJourney> LoadDueJourneys(DateTimeOffset gameTime)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id,PlayerId,PlayerName,OriginLocationId,OriginDisplayName,
       DestinationLocationId,DestinationDisplayName,Method,PlannedMinutes,
       DepartGameTime,ExpectedArrivalGameTime,ActualArrivalGameTime,
       Status,ArrivalEventId,LastInterruptTitle,LegsJson
FROM PlayerTravelJourney
WHERE Status='traveling'
  AND ExpectedArrivalGameTime <= $game
ORDER BY ExpectedArrivalGameTime,Id;";
        cmd.Parameters.AddWithValue("$game", gameTime.ToString("O"));

        var list = new List<PlayerTravelJourney>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadJourney(r));

        return list;
    }

    private static PlayerTravelJourney ReadJourney(SqliteDataReader r)
    {
        IReadOnlyList<PlayerTravelLeg> legs = Array.Empty<PlayerTravelLeg>();
        try
        {
            legs = JsonSerializer.Deserialize<List<PlayerTravelLeg>>(r.GetString(15))
                ?? new List<PlayerTravelLeg>();
        }
        catch { }

        return new PlayerTravelJourney
        {
            Id = r.GetInt64(0),
            PlayerId = r.GetString(1),
            PlayerName = r.GetString(2),
            OriginLocationId = r.GetString(3),
            OriginDisplayName = r.GetString(4),
            DestinationLocationId = r.GetString(5),
            DestinationDisplayName = r.GetString(6),
            Method = r.GetString(7),
            PlannedMinutes = r.GetInt32(8),
            DepartGameTime = ParseTime(r.GetString(9)),
            ExpectedArrivalGameTime = ParseTime(r.GetString(10)),
            ActualArrivalGameTime = r.IsDBNull(11) ? null : ParseTime(r.GetString(11)),
            Status = r.GetString(12),
            ArrivalEventId = r.IsDBNull(13) ? null : r.GetInt64(13),
            LastInterruptTitle = r.GetString(14),
            Legs = legs
        };
    }

    private void SetArrivalEventId(long journeyId, long eventId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE PlayerTravelJourney
SET ArrivalEventId=$event,
    UpdatedRealUtc=$real
WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$event", eventId);
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", journeyId);
        cmd.ExecuteNonQuery();
    }

    private void SetLastInterrupt(long journeyId, string title)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE PlayerTravelJourney
SET LastInterruptTitle=$title,
    UpdatedRealUtc=$real
WHERE Id=$id AND Status='traveling';";
        cmd.Parameters.AddWithValue("$title", Clean(title, ""));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", journeyId);
        cmd.ExecuteNonQuery();
    }

    private void MarkArriving(long journeyId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE PlayerTravelJourney
SET Status='arriving',
    UpdatedRealUtc=$real
WHERE Id=$id AND Status='traveling';";
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", journeyId);
        cmd.ExecuteNonQuery();
    }

    private void RestoreTraveling(long journeyId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE PlayerTravelJourney
SET Status='traveling',
    UpdatedRealUtc=$real
WHERE Id=$id AND Status='arriving';";
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", journeyId);
        cmd.ExecuteNonQuery();
    }

    private void MarkArrived(long journeyId, DateTimeOffset gameTime)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE PlayerTravelJourney
SET Status='arrived',
    ActualArrivalGameTime=$game,
    UpdatedRealUtc=$real
WHERE Id=$id AND Status='arriving';";
        cmd.Parameters.AddWithValue("$game", gameTime.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", journeyId);
        cmd.ExecuteNonQuery();
    }

    private PlayerTravelPlan Unavailable(
        string playerId,
        string destination,
        string method,
        string reason)
        => new()
        {
            Available = false,
            PlayerId = playerId,
            DestinationLocationId = destination,
            Method = method,
            Reason = reason
        };

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS WorldTravelRoute
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FromLocationId TEXT NOT NULL,
    ToLocationId TEXT NOT NULL,
    Method TEXT NOT NULL,
    Minutes INTEGER NOT NULL,
    Source TEXT NOT NULL DEFAULT 'authored_world',
    Note TEXT NOT NULL DEFAULT '',
    IsActive INTEGER NOT NULL DEFAULT 1,
    UpdatedRealUtc TEXT NOT NULL,
    UNIQUE(FromLocationId,ToLocationId,Method)
);

CREATE INDEX IF NOT EXISTS IX_WorldTravelRoute_FromMethod
ON WorldTravelRoute(FromLocationId,Method,IsActive);

CREATE TABLE IF NOT EXISTS PlayerTravelJourney
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerId TEXT NOT NULL,
    PlayerName TEXT NOT NULL,
    OriginLocationId TEXT NOT NULL,
    OriginDisplayName TEXT NOT NULL,
    DestinationLocationId TEXT NOT NULL,
    DestinationDisplayName TEXT NOT NULL,
    Method TEXT NOT NULL,
    PlannedMinutes INTEGER NOT NULL,
    DepartGameTime TEXT NOT NULL,
    ExpectedArrivalGameTime TEXT NOT NULL,
    ActualArrivalGameTime TEXT NULL,
    Status TEXT NOT NULL DEFAULT 'traveling',
    ArrivalEventId INTEGER NULL,
    LastInterruptTitle TEXT NOT NULL DEFAULT '',
    LegsJson TEXT NOT NULL DEFAULT '[]',
    CreatedRealUtc TEXT NOT NULL,
    UpdatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_PlayerTravelJourney_PlayerStatus
ON PlayerTravelJourney(PlayerId,Status,ExpectedArrivalGameTime);
";
        cmd.ExecuteNonQuery();

        using var recover = conn.CreateCommand();
        recover.CommandText = @"
UPDATE PlayerTravelJourney
SET Status='traveling',
    UpdatedRealUtc=$real
WHERE Status='arriving';";
        recover.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        recover.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        return conn;
    }

    private static string NormalizeMethod(string? method)
        => method?.Trim().ToLowerInvariant() switch
        {
            "truck" => "truck",
            "bike" => "bike",
            "bicycle" => "bike",
            "walk" => "walk",
            "walking" => "walk",
            "bus" => "bus",
            _ => "car"
        };

    private static DateTimeOffset ParseTime(string value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
                ? parsed
                : DateTimeOffset.Now;

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed class RouteRow
    {
        public long Id { get; set; }
        public string FromLocationId { get; set; } = "";
        public string ToLocationId { get; set; } = "";
        public string Method { get; set; } = "";
        public int Minutes { get; set; }
        public string Source { get; set; } = "";
        public string Note { get; set; } = "";
    }
}
