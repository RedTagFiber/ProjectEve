using Microsoft.Data.Sqlite;
using ProjectEve.Core.Scene;
using ProjectEve.Core.Time;
using ProjectEve.Core.World;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.World;

/// <summary>
/// Authoritative persisted physical player presence.
///
/// PhoneOS page navigation does not own physical location. This service does.
/// </summary>
public sealed class PlayerWorldPresenceService : IPlayerWorldPresenceService
{
    private readonly ISharedScenePresenceCoordinator _sharedScenes;
    private readonly IScenePerceptionService _perception;
    private readonly IGroupSceneConversationOrchestrator _groupScenes;
    private readonly IGameTimeService _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    public PlayerWorldPresenceService(
        ISharedScenePresenceCoordinator sharedScenes,
        IScenePerceptionService perception,
        IGroupSceneConversationOrchestrator groupScenes,
        IGameTimeService clock)
    {
        _sharedScenes = sharedScenes;
        _perception = perception;
        _groupScenes = groupScenes;
        _clock = clock;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<PlayerWorldPresenceState?> GetAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");
        if (playerId.Length == 0)
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return LoadState(playerId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerWorldPresenceState> AttachClientAsync(
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");
        if (playerId.Length == 0)
            throw new ArgumentException("PlayerId is required.", nameof(playerId));

        playerName = Clean(playerName, "Player");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = LoadState(playerId) ?? new PlayerWorldPresenceState
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Status = "unplaced",
                Activity = "idle",
                Attention = 0.72,
                UpdatedGameTime = _clock.Now
            };

            state.PlayerName = playerName;
            state.ClientAttached = true;
            state.LastClientHeartbeatRealUtc = DateTimeOffset.UtcNow;
            state.UpdatedGameTime = _clock.Now;

            SaveState(state);

            if (state.HasLocation)
            {
                await EnsureMembershipLockedAsync(state, cancellationToken);
                SnapshotSpatialStateLocked(state);
                SaveState(state);
            }

            return Clone(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DetachClientAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");
        if (playerId.Length == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = LoadState(playerId);
            if (state == null)
                return;

            if (state.HasLocation)
                SnapshotSpatialStateLocked(state);

            state.ClientAttached = false;
            state.UpdatedGameTime = _clock.Now;
            SaveState(state);

            // IMPORTANT:
            // Do NOT call SharedScenes.LeaveAsync here.
            // A circuit/page detach is not physical travel.
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerWorldPresenceMoveResult> MoveToLocationAsync(
        PlayerWorldPresenceMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string playerId = Clean(request.PlayerId, "");
        string playerName = Clean(request.PlayerName, "Player");
        string locationId = Clean(request.LocationId, "");

        if (playerId.Length == 0)
            throw new ArgumentException("PlayerId is required.", nameof(request));
        if (locationId.Length == 0)
            throw new ArgumentException("LocationId is required.", nameof(request));

        string newSceneId = SceneIdForLocation(locationId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = LoadState(playerId);
            bool wasTraveling =
                previous?.Status.Equals("traveling", StringComparison.OrdinalIgnoreCase) == true;

            string oldLocation = wasTraveling
                ? previous?.OriginLocationId ?? ""
                : previous?.LocationId ?? "";

            string oldScene = wasTraveling && !string.IsNullOrWhiteSpace(oldLocation)
                ? SceneIdForLocation(oldLocation)
                : previous?.SceneId ?? "";

            bool sceneChanged =
                previous == null ||
                !oldScene.Equals(newSceneId, StringComparison.OrdinalIgnoreCase);

            int remainingOld = 0;

            if (sceneChanged && previous?.HasLocation == true)
            {
                SnapshotSpatialStateLocked(previous);
                BreakPlayerContactsLocked(oldScene, playerId);

                try
                {
                    var leave = await _sharedScenes.LeaveAsync(
                        oldScene,
                        playerId,
                        cancellationToken);

                    remainingOld = leave.RemainingPlayers;

                    if (leave.RemainingPlayers == 0)
                    {
                        await _groupScenes.EndSceneAsync(
                            oldScene,
                            "player physically left location",
                            cancellationToken);
                    }
                }
                catch
                {
                    // Location truth still moves forward. The next attach/heartbeat
                    // will reconcile shared-scene membership.
                }
            }

            var state = previous ?? new PlayerWorldPresenceState
            {
                PlayerId = playerId
            };

            state.PlayerId = playerId;
            state.PlayerName = playerName;
            state.LocationId = locationId;
            state.LocationDisplayName = Clean(
                request.LocationDisplayName,
                locationId);
            state.SceneId = newSceneId;
            state.Status = "present";
            state.Activity = Clean(request.Activity, "conversation");
            state.Attention = Math.Clamp(request.Attention, 0, 1);
            state.AmbientNoise = Math.Clamp(request.AmbientNoise, 0, 1);
            state.VisualClutter = Math.Clamp(request.VisualClutter, 0, 1);
            state.OriginLocationId = "";
            state.OriginDisplayName = "";
            state.DestinationLocationId = "";
            state.DestinationDisplayName = "";
            state.TravelMethod = "";
            state.TravelDepartGameTime = null;
            state.ExpectedArrivalGameTime = null;
            state.ActiveTravelId = null;

            state.ClientAttached = true;
            state.LastClientHeartbeatRealUtc = DateTimeOffset.UtcNow;
            state.UpdatedGameTime = _clock.Now;

            if (sceneChanged)
            {
                state.HasSpatialSnapshot = false;
                state.XFeet = 0;
                state.YFeet = 0;
                state.FacingDegrees = 0;
            }

            SaveState(state);

            int activeNew = await EnsureMembershipLockedAsync(
                state,
                cancellationToken);

            SnapshotSpatialStateLocked(state);
            SaveState(state);

            if (sceneChanged)
            {
                InsertMovementEventLocked(
                    playerId,
                    playerName,
                    oldLocation,
                    locationId,
                    oldScene,
                    newSceneId,
                    Clean(request.Reason, "travel"));
            }

            return new PlayerWorldPresenceMoveResult
            {
                State = Clone(state),
                PreviousLocationId = oldLocation,
                PreviousSceneId = oldScene,
                SceneChanged = sceneChanged,
                RemainingPlayersInPreviousScene = remainingOld,
                ActivePlayersInNewScene = activeNew
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerWorldPresenceState> BeginTravelAsync(
        PlayerWorldTravelStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string playerId = Clean(request.PlayerId, "");
        if (playerId.Length == 0)
            throw new ArgumentException("PlayerId is required.", nameof(request));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = LoadState(playerId)
                ?? throw new InvalidOperationException("Player has no world-presence state.");

            if (state.Status.Equals("traveling", StringComparison.OrdinalIgnoreCase))
                return Clone(state);

            if (!state.HasLocation)
                throw new InvalidOperationException("Player must be at a location before starting travel.");

            string oldLocation = state.LocationId;
            string oldLocationName = state.LocationDisplayName;
            string oldScene = state.SceneId;

            SnapshotSpatialStateLocked(state);
            BreakPlayerContactsLocked(oldScene, playerId);

            try
            {
                var leave = await _sharedScenes.LeaveAsync(
                    oldScene,
                    playerId,
                    cancellationToken);

                if (leave.RemainingPlayers == 0)
                {
                    await _groupScenes.EndSceneAsync(
                        oldScene,
                        "player departed for travel",
                        cancellationToken);
                }
            }
            catch
            {
                // Persisted travel truth still wins. Stale membership can be
                // cleaned by the shared-scene stale-member policy.
            }

            state.PlayerName = Clean(request.PlayerName, state.PlayerName);
            state.Status = "traveling";
            state.Activity = "traveling";
            state.Attention = 0.70;

            state.OriginLocationId = oldLocation;
            state.OriginDisplayName = oldLocationName;
            state.DestinationLocationId = Clean(request.DestinationLocationId, "");
            state.DestinationDisplayName = Clean(
                request.DestinationDisplayName,
                state.DestinationLocationId);
            state.TravelMethod = NormalizeTravelMethod(request.Method);
            state.TravelDepartGameTime =
                request.DepartGameTime == default ? _clock.Now : request.DepartGameTime;
            state.ExpectedArrivalGameTime = request.ExpectedArrivalGameTime;
            state.ActiveTravelId = request.TravelId > 0 ? request.TravelId : null;

            // While physically in transit the player is not inside either
            // endpoint scene.
            state.LocationId = "";
            state.LocationDisplayName = "";
            state.SceneId = "";
            state.HasSpatialSnapshot = false;
            state.XFeet = 0;
            state.YFeet = 0;
            state.FacingDegrees = 0;

            state.ClientAttached = true;
            state.LastClientHeartbeatRealUtc = DateTimeOffset.UtcNow;
            state.UpdatedGameTime = _clock.Now;

            SaveState(state);

            InsertMovementEventLocked(
                playerId,
                state.PlayerName,
                oldLocation,
                "",
                oldScene,
                "",
                Clean(request.Reason, "travel_depart"));

            return Clone(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerWorldPresenceState?> HeartbeatAsync(
        string playerId,
        string activity,
        double attention,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");
        if (playerId.Length == 0)
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = LoadState(playerId);
            if (state == null)
                return null;

            state.ClientAttached = true;

            if (state.Status.Equals("traveling", StringComparison.OrdinalIgnoreCase))
            {
                var requested = Clean(activity, "idle");
                state.Activity = requested.Equals("using_phone", StringComparison.OrdinalIgnoreCase)
                    ? "traveling_using_phone"
                    : "traveling";
                state.Attention = Math.Clamp(
                    requested.Equals("using_phone", StringComparison.OrdinalIgnoreCase)
                        ? Math.Min(attention, 0.52)
                        : Math.Min(attention, 0.72),
                    0,
                    1);
            }
            else
            {
                state.Activity = Clean(activity, state.Activity);
                state.Attention = Math.Clamp(attention, 0, 1);
            }
            state.LastClientHeartbeatRealUtc = DateTimeOffset.UtcNow;
            state.UpdatedGameTime = _clock.Now;

            if (state.HasLocation)
            {
                await EnsureMembershipLockedAsync(state, cancellationToken);
                await UpdatePlayerSceneStateLockedAsync(state, cancellationToken);
                SnapshotSpatialStateLocked(state);
            }

            SaveState(state);
            return Clone(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ScenePerceivedPresence>> GetPerceivedPresenceAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        playerId = Clean(playerId, "");
        if (playerId.Length == 0)
            return Array.Empty<ScenePerceivedPresence>();

        string sceneId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = LoadState(playerId);
            if (state?.HasLocation != true)
                return Array.Empty<ScenePerceivedPresence>();

            sceneId = state.SceneId;
        }
        finally
        {
            _gate.Release();
        }

        return await _sharedScenes.GetPlayerPerceivedPresenceAsync(
            sceneId,
            playerId,
            cancellationToken);
    }

    private async Task<int> EnsureMembershipLockedAsync(
        PlayerWorldPresenceState state,
        CancellationToken cancellationToken)
    {
        if (!state.HasLocation)
            return 0;

        if (MembershipExistsLocked(state.SceneId, state.PlayerId))
        {
            await _sharedScenes.HeartbeatAsync(
                state.SceneId,
                state.PlayerId,
                cancellationToken);

            await UpdatePlayerSceneStateLockedAsync(state, cancellationToken);
            return CountActivePlayersLocked(state.SceneId);
        }

        var join = await _sharedScenes.JoinAsync(
            new SharedSceneJoinRequest
            {
                SceneId = state.SceneId,
                LocationId = state.LocationId,
                DisplayName = state.LocationDisplayName,
                AmbientNoise = state.AmbientNoise,
                VisualClutter = state.VisualClutter,
                PlayerId = state.PlayerId,
                PlayerName = state.PlayerName,
                BootstrapNpcs = Array.Empty<SharedSceneNpcPlacement>()
            },
            cancellationToken);

        // Restore the player's last exact in-scene coordinates after a short
        // disconnect/reconnect. A normal PhoneOS page switch never gets here
        // because the layout keeps the membership alive.
        if (state.HasSpatialSnapshot)
        {
            await _perception.UpsertPresenceAsync(
                new ScenePresenceUpdate
                {
                    SceneId = state.SceneId,
                    CharacterKey = state.CharacterKey,
                    PlayerId = state.PlayerId,
                    DisplayName = state.PlayerName,
                    IsPlayer = true,
                    XFeet = state.XFeet,
                    YFeet = state.YFeet,
                    FacingDegrees = state.FacingDegrees,
                    RoomZone = "main",
                    AcousticZone = "main",
                    Attention = state.Attention,
                    Activity = state.Activity,
                    Concealment = 0,
                    IsActive = true
                },
                cancellationToken);
        }
        else
        {
            await UpdatePlayerSceneStateLockedAsync(state, cancellationToken);
        }

        return join.ActivePlayerCount;
    }

    private async Task UpdatePlayerSceneStateLockedAsync(
        PlayerWorldPresenceState state,
        CancellationToken cancellationToken)
    {
        var row = LoadScenePresenceLocked(state.SceneId, state.CharacterKey);
        if (row == null)
            return;

        await _perception.UpsertPresenceAsync(
            new ScenePresenceUpdate
            {
                SceneId = state.SceneId,
                CharacterKey = state.CharacterKey,
                PlayerId = state.PlayerId,
                DisplayName = state.PlayerName,
                IsPlayer = true,
                XFeet = row.XFeet,
                YFeet = row.YFeet,
                FacingDegrees = row.FacingDegrees,
                RoomZone = row.RoomZone,
                AcousticZone = row.AcousticZone,
                Attention = state.Attention,
                Activity = state.Activity,
                Concealment = row.Concealment,
                IsActive = true
            },
            cancellationToken);
    }

    private void SnapshotSpatialStateLocked(PlayerWorldPresenceState state)
    {
        if (!state.HasLocation)
            return;

        var row = LoadScenePresenceLocked(state.SceneId, state.CharacterKey);
        if (row == null)
            return;

        state.XFeet = row.XFeet;
        state.YFeet = row.YFeet;
        state.FacingDegrees = row.FacingDegrees;
        state.HasSpatialSnapshot = true;
    }

    private ScenePresenceRow? LoadScenePresenceLocked(
        string sceneId,
        string characterKey)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT XFeet,YFeet,FacingDegrees,RoomZone,AcousticZone,Concealment
FROM ScenePresence
WHERE SceneId=$scene
  AND CharacterKey=$character
  AND IsActive=1
LIMIT 1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$character", characterKey);

        try
        {
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;

            return new ScenePresenceRow
            {
                XFeet = r.GetDouble(0),
                YFeet = r.GetDouble(1),
                FacingDegrees = r.GetDouble(2),
                RoomZone = r.GetString(3),
                AcousticZone = r.GetString(4),
                Concealment = r.GetDouble(5)
            };
        }
        catch
        {
            return null;
        }
    }

    private bool MembershipExistsLocked(string sceneId, string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM SharedScenePlayerMembership
WHERE SceneId=$scene AND PlayerId=$player
LIMIT 1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$player", playerId);

        try
        {
            return cmd.ExecuteScalar() != null;
        }
        catch
        {
            return false;
        }
    }

    private int CountActivePlayersLocked(string sceneId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*)
FROM SharedScenePlayerMembership
WHERE SceneId=$scene;";
        cmd.Parameters.AddWithValue("$scene", sceneId);

        try
        {
            return Convert.ToInt32(
                cmd.ExecuteScalar(),
                CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

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

    private PlayerWorldPresenceState? LoadState(string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT PlayerId,PlayerName,LocationId,LocationDisplayName,SceneId,
       Status,Activity,Attention,AmbientNoise,VisualClutter,
       XFeet,YFeet,FacingDegrees,HasSpatialSnapshot,
       OriginLocationId,OriginDisplayName,DestinationLocationId,DestinationDisplayName,
       TravelMethod,TravelDepartGameTime,ExpectedArrivalGameTime,ActiveTravelId,
       ClientAttached,LastClientHeartbeatRealUtc,UpdatedGameTime
FROM PlayerWorldPresenceState
WHERE PlayerId=$player
LIMIT 1;";
        cmd.Parameters.AddWithValue("$player", playerId);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;

        return new PlayerWorldPresenceState
        {
            PlayerId = r.GetString(0),
            PlayerName = r.GetString(1),
            LocationId = r.GetString(2),
            LocationDisplayName = r.GetString(3),
            SceneId = r.GetString(4),
            Status = r.GetString(5),
            Activity = r.GetString(6),
            Attention = r.GetDouble(7),
            AmbientNoise = r.GetDouble(8),
            VisualClutter = r.GetDouble(9),
            XFeet = r.GetDouble(10),
            YFeet = r.GetDouble(11),
            FacingDegrees = r.GetDouble(12),
            HasSpatialSnapshot = r.GetInt32(13) != 0,
            OriginLocationId = r.GetString(14),
            OriginDisplayName = r.GetString(15),
            DestinationLocationId = r.GetString(16),
            DestinationDisplayName = r.GetString(17),
            TravelMethod = r.GetString(18),
            TravelDepartGameTime = r.IsDBNull(19)
                ? null
                : ParseTime(r.GetString(19)),
            ExpectedArrivalGameTime = r.IsDBNull(20)
                ? null
                : ParseTime(r.GetString(20)),
            ActiveTravelId = r.IsDBNull(21)
                ? null
                : r.GetInt64(21),
            ClientAttached = r.GetInt32(22) != 0,
            LastClientHeartbeatRealUtc = r.IsDBNull(23)
                ? null
                : ParseTime(r.GetString(23)),
            UpdatedGameTime = ParseTime(r.GetString(24))
        };
    }

    private void SaveState(PlayerWorldPresenceState state)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO PlayerWorldPresenceState
(PlayerId,PlayerName,LocationId,LocationDisplayName,SceneId,
 Status,Activity,Attention,AmbientNoise,VisualClutter,
 XFeet,YFeet,FacingDegrees,HasSpatialSnapshot,
 OriginLocationId,OriginDisplayName,DestinationLocationId,DestinationDisplayName,
 TravelMethod,TravelDepartGameTime,ExpectedArrivalGameTime,ActiveTravelId,
 ClientAttached,LastClientHeartbeatRealUtc,UpdatedGameTime,UpdatedRealUtc)
VALUES
($player,$name,$location,$locationName,$scene,
 $status,$activity,$attention,$noise,$clutter,
 $x,$y,$facing,$hasSpatial,
 $originLocation,$originName,$destinationLocation,$destinationName,
 $travelMethod,$travelDepart,$travelArrival,$activeTravel,
 $attached,$heartbeat,$game,$real)
ON CONFLICT(PlayerId) DO UPDATE SET
 PlayerName=excluded.PlayerName,
 LocationId=excluded.LocationId,
 LocationDisplayName=excluded.LocationDisplayName,
 SceneId=excluded.SceneId,
 Status=excluded.Status,
 Activity=excluded.Activity,
 Attention=excluded.Attention,
 AmbientNoise=excluded.AmbientNoise,
 VisualClutter=excluded.VisualClutter,
 XFeet=excluded.XFeet,
 YFeet=excluded.YFeet,
 FacingDegrees=excluded.FacingDegrees,
 HasSpatialSnapshot=excluded.HasSpatialSnapshot,
 OriginLocationId=excluded.OriginLocationId,
 OriginDisplayName=excluded.OriginDisplayName,
 DestinationLocationId=excluded.DestinationLocationId,
 DestinationDisplayName=excluded.DestinationDisplayName,
 TravelMethod=excluded.TravelMethod,
 TravelDepartGameTime=excluded.TravelDepartGameTime,
 ExpectedArrivalGameTime=excluded.ExpectedArrivalGameTime,
 ActiveTravelId=excluded.ActiveTravelId,
 ClientAttached=excluded.ClientAttached,
 LastClientHeartbeatRealUtc=excluded.LastClientHeartbeatRealUtc,
 UpdatedGameTime=excluded.UpdatedGameTime,
 UpdatedRealUtc=excluded.UpdatedRealUtc;";
        cmd.Parameters.AddWithValue("$player", state.PlayerId);
        cmd.Parameters.AddWithValue("$name", Clean(state.PlayerName, "Player"));
        cmd.Parameters.AddWithValue("$location", Clean(state.LocationId, ""));
        cmd.Parameters.AddWithValue("$locationName", Clean(state.LocationDisplayName, ""));
        cmd.Parameters.AddWithValue("$scene", Clean(state.SceneId, ""));
        cmd.Parameters.AddWithValue("$status", Clean(state.Status, "unplaced"));
        cmd.Parameters.AddWithValue("$activity", Clean(state.Activity, "idle"));
        cmd.Parameters.AddWithValue("$attention", Math.Clamp(state.Attention, 0, 1));
        cmd.Parameters.AddWithValue("$noise", Math.Clamp(state.AmbientNoise, 0, 1));
        cmd.Parameters.AddWithValue("$clutter", Math.Clamp(state.VisualClutter, 0, 1));
        cmd.Parameters.AddWithValue("$x", state.XFeet);
        cmd.Parameters.AddWithValue("$y", state.YFeet);
        cmd.Parameters.AddWithValue("$facing", state.FacingDegrees);
        cmd.Parameters.AddWithValue("$hasSpatial", state.HasSpatialSnapshot ? 1 : 0);
        cmd.Parameters.AddWithValue("$originLocation", Clean(state.OriginLocationId, ""));
        cmd.Parameters.AddWithValue("$originName", Clean(state.OriginDisplayName, ""));
        cmd.Parameters.AddWithValue("$destinationLocation", Clean(state.DestinationLocationId, ""));
        cmd.Parameters.AddWithValue("$destinationName", Clean(state.DestinationDisplayName, ""));
        cmd.Parameters.AddWithValue("$travelMethod", Clean(state.TravelMethod, ""));
        cmd.Parameters.AddWithValue(
            "$travelDepart",
            state.TravelDepartGameTime?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(
            "$travelArrival",
            state.ExpectedArrivalGameTime?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(
            "$activeTravel",
            state.ActiveTravelId.HasValue ? state.ActiveTravelId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$attached", state.ClientAttached ? 1 : 0);
        cmd.Parameters.AddWithValue(
            "$heartbeat",
            state.LastClientHeartbeatRealUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$game", state.UpdatedGameTime.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void InsertMovementEventLocked(
        string playerId,
        string playerName,
        string fromLocation,
        string toLocation,
        string fromScene,
        string toScene,
        string reason)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO PlayerWorldMovementEvent
(PlayerId,PlayerName,FromLocationId,ToLocationId,FromSceneId,ToSceneId,
 GameTime,Reason,CreatedRealUtc)
VALUES($player,$name,$fromLocation,$toLocation,$fromScene,$toScene,
       $game,$reason,$real);";
        cmd.Parameters.AddWithValue("$player", playerId);
        cmd.Parameters.AddWithValue("$name", playerName);
        cmd.Parameters.AddWithValue("$fromLocation", Clean(fromLocation, ""));
        cmd.Parameters.AddWithValue("$toLocation", Clean(toLocation, ""));
        cmd.Parameters.AddWithValue("$fromScene", Clean(fromScene, ""));
        cmd.Parameters.AddWithValue("$toScene", Clean(toScene, ""));
        cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$reason", Clean(reason, "travel"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS PlayerWorldPresenceState
(
    PlayerId TEXT PRIMARY KEY,
    PlayerName TEXT NOT NULL,
    LocationId TEXT NOT NULL DEFAULT '',
    LocationDisplayName TEXT NOT NULL DEFAULT '',
    SceneId TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT 'unplaced',
    Activity TEXT NOT NULL DEFAULT 'idle',
    Attention REAL NOT NULL DEFAULT 0.72,
    AmbientNoise REAL NOT NULL DEFAULT 0.15,
    VisualClutter REAL NOT NULL DEFAULT 0.10,
    XFeet REAL NOT NULL DEFAULT 0,
    YFeet REAL NOT NULL DEFAULT 0,
    FacingDegrees REAL NOT NULL DEFAULT 0,
    HasSpatialSnapshot INTEGER NOT NULL DEFAULT 0,
    OriginLocationId TEXT NOT NULL DEFAULT '',
    OriginDisplayName TEXT NOT NULL DEFAULT '',
    DestinationLocationId TEXT NOT NULL DEFAULT '',
    DestinationDisplayName TEXT NOT NULL DEFAULT '',
    TravelMethod TEXT NOT NULL DEFAULT '',
    TravelDepartGameTime TEXT NULL,
    ExpectedArrivalGameTime TEXT NULL,
    ActiveTravelId INTEGER NULL,
    ClientAttached INTEGER NOT NULL DEFAULT 0,
    LastClientHeartbeatRealUtc TEXT NULL,
    UpdatedGameTime TEXT NOT NULL,
    UpdatedRealUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PlayerWorldMovementEvent
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerId TEXT NOT NULL,
    PlayerName TEXT NOT NULL,
    FromLocationId TEXT NOT NULL DEFAULT '',
    ToLocationId TEXT NOT NULL DEFAULT '',
    FromSceneId TEXT NOT NULL DEFAULT '',
    ToSceneId TEXT NOT NULL DEFAULT '',
    GameTime TEXT NOT NULL,
    Reason TEXT NOT NULL DEFAULT 'travel',
    CreatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_PlayerWorldMovementEvent_PlayerTime
ON PlayerWorldMovementEvent(PlayerId,GameTime);
";
        cmd.ExecuteNonQuery();

        EnsureColumn(conn, "PlayerWorldPresenceState", "OriginLocationId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "PlayerWorldPresenceState", "OriginDisplayName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "PlayerWorldPresenceState", "DestinationLocationId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "PlayerWorldPresenceState", "DestinationDisplayName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "PlayerWorldPresenceState", "TravelMethod", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "PlayerWorldPresenceState", "TravelDepartGameTime", "TEXT NULL");
        EnsureColumn(conn, "PlayerWorldPresenceState", "ExpectedArrivalGameTime", "TEXT NULL");
        EnsureColumn(conn, "PlayerWorldPresenceState", "ActiveTravelId", "INTEGER NULL");
    }

    private static void EnsureColumn(
        SqliteConnection conn,
        string table,
        string column,
        string definition)
    {
        using var inspect = conn.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";

        using (var r = inspect.ExecuteReader())
        {
            while (r.Read())
            {
                if (r.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
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

    private static PlayerWorldPresenceState Clone(PlayerWorldPresenceState x)
        => new()
        {
            PlayerId = x.PlayerId,
            PlayerName = x.PlayerName,
            LocationId = x.LocationId,
            LocationDisplayName = x.LocationDisplayName,
            SceneId = x.SceneId,
            Status = x.Status,
            Activity = x.Activity,
            Attention = x.Attention,
            AmbientNoise = x.AmbientNoise,
            VisualClutter = x.VisualClutter,
            XFeet = x.XFeet,
            YFeet = x.YFeet,
            FacingDegrees = x.FacingDegrees,
            HasSpatialSnapshot = x.HasSpatialSnapshot,
            OriginLocationId = x.OriginLocationId,
            OriginDisplayName = x.OriginDisplayName,
            DestinationLocationId = x.DestinationLocationId,
            DestinationDisplayName = x.DestinationDisplayName,
            TravelMethod = x.TravelMethod,
            TravelDepartGameTime = x.TravelDepartGameTime,
            ExpectedArrivalGameTime = x.ExpectedArrivalGameTime,
            ActiveTravelId = x.ActiveTravelId,
            ClientAttached = x.ClientAttached,
            UpdatedGameTime = x.UpdatedGameTime,
            LastClientHeartbeatRealUtc = x.LastClientHeartbeatRealUtc
        };

    private static string NormalizeTravelMethod(string? method)
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

    private static string SceneIdForLocation(string locationId)
        => "inperson:" + locationId.Trim();

    private static DateTimeOffset ParseTime(string value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
                ? parsed
                : DateTimeOffset.Now;

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

    private sealed class ScenePresenceRow
    {
        public double XFeet { get; set; }
        public double YFeet { get; set; }
        public double FacingDegrees { get; set; }
        public string RoomZone { get; set; } = "main";
        public string AcousticZone { get; set; } = "main";
        public double Concealment { get; set; }
    }
}

