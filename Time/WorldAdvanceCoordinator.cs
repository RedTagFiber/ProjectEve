using Microsoft.Data.Sqlite;
using ProjectEve.Core.Scene;
using ProjectEve.Core.Time;
using ProjectEve.Core.World;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Time;

/// <summary>
/// Phase 13 event-driven fast-forward.
///
/// It never minute-ticks 200 NPCs. It repeatedly jumps to the next known
/// schedule boundary, reconciles world occupancy, and continues until:
/// - requested target time
/// - queued interrupting GameEvent
/// - a scene arrival/departure the requesting player actually perceives
/// </summary>
public sealed class WorldAdvanceCoordinator : IWorldAdvanceCoordinator
{
    private readonly IGameTimeService _clock;
    private readonly IWorldOccupancyService _occupancy;
    private readonly IScenePerceptionService _perception;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    private static readonly TimeSpan NextEventSearchHorizon = TimeSpan.FromDays(14);
    private const int MaxBoundarySteps = 4096;

    public WorldAdvanceCoordinator(
        IGameTimeService clock,
        IWorldOccupancyService occupancy,
        IScenePerceptionService perception)
    {
        _clock = clock;
        _occupancy = occupancy;
        _perception = perception;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");
    }

    public DateTimeOffset Now => _clock.Now;

    public Task<GameEventRecord?> PeekNextPlayerEventAsync(
        string playerId,
        CancellationToken cancellationToken = default)
        => _clock.PeekNextPlayerEventAsync(
            CleanPlayer(playerId),
            cancellationToken);

    public Task<GameTimeAdvanceResult> AdvanceByAsync(
        string playerId,
        TimeSpan amount,
        string reason = "player_wait",
        CancellationToken cancellationToken = default)
    {
        if (amount <= TimeSpan.Zero)
        {
            return Task.FromResult(new GameTimeAdvanceResult
            {
                FromGameTime = _clock.Now,
                ToGameTime = _clock.Now,
                Message = "Time was not advanced."
            });
        }

        return AdvanceUntilAsync(
            playerId,
            _clock.Now.Add(amount),
            reason,
            cancellationToken);
    }

    public async Task<GameTimeAdvanceResult> AdvanceUntilAsync(
        string playerId,
        DateTimeOffset targetGameTime,
        string reason = "player_wait_until",
        CancellationToken cancellationToken = default)
    {
        playerId = CleanPlayer(playerId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await AdvanceUntilLockedAsync(
                playerId,
                targetGameTime,
                reason,
                stopForVisibleWorldEvent: true,
                cancellationToken);
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
        playerId = CleanPlayer(playerId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var original = _clock.Now;
            await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);

            for (int step = 0; step < MaxBoundarySteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A scheduled event already due at the current game time should
                // trigger without trying to advance beyond it.
                var queued = await _clock.PeekNextPlayerEventAsync(
                    playerId,
                    cancellationToken);

                if (queued != null && queued.GameTime <= _clock.Now)
                {
                    var due = await _clock.AdvanceToNextPlayerEventAsync(
                        playerId,
                        cancellationToken);
                    await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);
                    return RebaseFrom(original, due);
                }

                DateTimeOffset horizon = _clock.Now.Add(NextEventSearchHorizon);
                if (queued != null && queued.GameTime < horizon)
                    horizon = queued.GameTime;

                var boundary = await _occupancy.GetNextWorldBoundaryAsync(
                    _clock.Now,
                    horizon,
                    cancellationToken);

                if (boundary == null)
                {
                    if (queued != null)
                    {
                        var queuedResult = await _clock.AdvanceToNextPlayerEventAsync(
                            playerId,
                            cancellationToken);
                        await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);
                        return RebaseFrom(original, queuedResult);
                    }

                    return new GameTimeAdvanceResult
                    {
                        FromGameTime = original,
                        ToGameTime = _clock.Now,
                        Message = "No player-relevant event is known in the next two weeks."
                    };
                }

                // If a queued event is earlier than or exactly at this world
                // boundary, let the authoritative GameEvent queue win.
                if (queued != null && queued.GameTime <= boundary.GameTime)
                {
                    var queuedResult = await _clock.AdvanceToNextPlayerEventAsync(
                        playerId,
                        cancellationToken);
                    await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);
                    return RebaseFrom(original, queuedResult);
                }

                bool visibleBefore = await IsNpcVisibleToPlayerAsync(
                    playerId,
                    boundary.NpcId,
                    cancellationToken);

                var stepResult = await _clock.AdvanceUntilAsync(
                    playerId,
                    boundary.GameTime,
                    "next_event_world_boundary",
                    cancellationToken);

                await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);

                if (stepResult.InterruptedByEvent)
                    return RebaseFrom(original, stepResult);

                bool visibleAfter = await IsNpcVisibleToPlayerAsync(
                    playerId,
                    boundary.NpcId,
                    cancellationToken);

                if (IsVisibleBoundaryRelevant(boundary, visibleBefore, visibleAfter))
                    return BuildWorldBoundaryResult(original, boundary);

                // Mundane boundary: silently keep going.
            }

            return new GameTimeAdvanceResult
            {
                FromGameTime = original,
                ToGameTime = _clock.Now,
                Message = "Stopped after too many world boundaries. The world state is safe, but Next Event needs a narrower horizon."
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GameTimeAdvanceResult> AdvanceUntilLockedAsync(
        string playerId,
        DateTimeOffset targetGameTime,
        string reason,
        bool stopForVisibleWorldEvent,
        CancellationToken cancellationToken)
    {
        var original = _clock.Now;

        if (targetGameTime <= original)
        {
            return new GameTimeAdvanceResult
            {
                FromGameTime = original,
                ToGameTime = original,
                Message = "Target time is not later than the current game time."
            };
        }

        await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);

        for (int step = 0; step < MaxBoundarySteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_clock.Now >= targetGameTime)
            {
                return new GameTimeAdvanceResult
                {
                    FromGameTime = original,
                    ToGameTime = _clock.Now,
                    Message = $"Advanced to {_clock.Now:ddd h:mm tt}."
                };
            }

            var dueNow = await _clock.PeekNextPlayerEventAsync(
                playerId,
                cancellationToken);

            if (dueNow != null && dueNow.GameTime <= _clock.Now)
            {
                var due = await _clock.AdvanceToNextPlayerEventAsync(
                    playerId,
                    cancellationToken);
                await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);
                return RebaseFrom(original, due);
            }

            var boundary = await _occupancy.GetNextWorldBoundaryAsync(
                _clock.Now,
                targetGameTime,
                cancellationToken);

            DateTimeOffset nextStop = boundary?.GameTime ?? targetGameTime;

            bool visibleBefore = false;
            if (boundary != null && stopForVisibleWorldEvent)
            {
                visibleBefore = await IsNpcVisibleToPlayerAsync(
                    playerId,
                    boundary.NpcId,
                    cancellationToken);
            }

            var advance = await _clock.AdvanceUntilAsync(
                playerId,
                nextStop,
                reason,
                cancellationToken);

            await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);

            if (advance.InterruptedByEvent)
                return RebaseFrom(original, advance);

            if (boundary != null &&
                Math.Abs((_clock.Now - boundary.GameTime).TotalSeconds) < 1)
            {
                if (stopForVisibleWorldEvent)
                {
                    bool visibleAfter = await IsNpcVisibleToPlayerAsync(
                        playerId,
                        boundary.NpcId,
                        cancellationToken);

                    if (IsVisibleBoundaryRelevant(boundary, visibleBefore, visibleAfter))
                        return BuildWorldBoundaryResult(original, boundary);
                }

                continue;
            }

            if (_clock.Now >= targetGameTime)
            {
                return new GameTimeAdvanceResult
                {
                    FromGameTime = original,
                    ToGameTime = _clock.Now,
                    Message = $"Advanced to {_clock.Now:ddd h:mm tt}."
                };
            }
        }

        return new GameTimeAdvanceResult
        {
            FromGameTime = original,
            ToGameTime = _clock.Now,
            Message = "Stopped after too many world boundaries."
        };
    }

    private async Task<bool> IsNpcVisibleToPlayerAsync(
        string playerId,
        int npcId,
        CancellationToken cancellationToken)
    {
        var sceneId = GetPlayerSceneId(playerId);
        if (string.IsNullOrWhiteSpace(sceneId))
            return false;

        var rows = await _perception.GetPerceivedPresenceAsync(
            sceneId,
            "player:" + playerId,
            cancellationToken);

        return rows.Any(x => x.NpcId == npcId);
    }

    private static bool IsVisibleBoundaryRelevant(
        WorldScheduleBoundary boundary,
        bool visibleBefore,
        bool visibleAfter)
    {
        return boundary.Kind switch
        {
            "depart" => visibleBefore && !visibleAfter,
            "arrive" => !visibleBefore && visibleAfter,
            "location_change" => visibleBefore || visibleAfter,
            _ => false
        };
    }

    private GameTimeAdvanceResult BuildWorldBoundaryResult(
        DateTimeOffset original,
        WorldScheduleBoundary boundary)
    {
        string title = boundary.Kind switch
        {
            "arrive" => $"{boundary.NpcName} arrives.",
            "depart" => $"{boundary.NpcName} leaves.",
            "location_change" => $"{boundary.NpcName} moves to another area.",
            _ => $"{boundary.NpcName}'s situation changes."
        };

        return new GameTimeAdvanceResult
        {
            FromGameTime = original,
            ToGameTime = _clock.Now,
            InterruptedByEvent = true,
            Event = new GameEventRecord
            {
                Id = 0,
                PlayerId = "*",
                EventType = boundary.Kind switch
                {
                    "arrive" => "npc_arrival",
                    "depart" => "npc_departure",
                    _ => "world_presence_change"
                },
                Title = title,
                GameTime = boundary.GameTime,
                InterruptFastForward = true,
                Status = "triggered",
                SourceKey = $"world_boundary:{boundary.NpcId}:{boundary.GameTime.UtcDateTime.Ticks}",
                DataJson = JsonSerializer.Serialize(boundary)
            },
            Message = title
        };
    }

    private static GameTimeAdvanceResult RebaseFrom(
        DateTimeOffset original,
        GameTimeAdvanceResult result)
    {
        result.FromGameTime = original;
        return result;
    }

    private string? GetPlayerSceneId(string playerId)
    {
        try
        {
            using var conn = new SqliteConnection("Data Source=" + _dbPath);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT SceneId
FROM SharedScenePlayerMembership
WHERE PlayerId=$player
ORDER BY LastHeartbeatRealUtc DESC
LIMIT 1;";
            cmd.Parameters.AddWithValue("$player", playerId);

            return cmd.ExecuteScalar()?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string CleanPlayer(string? playerId)
        => string.IsNullOrWhiteSpace(playerId)
            ? "legacy-player"
            : playerId.Trim();
}
