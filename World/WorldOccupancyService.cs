using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Core.Scene;
using ProjectEve.Core.World;
using ProjectEve.Money;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.World;

/// <summary>
/// Phase 12 world occupancy + schedule resolver.
///
/// It resolves each NPC's authoritative current location from:
/// 1) explicit schedule override
/// 2) explicit assigned shift
/// 3) JobProfile work days/hours/commute
/// 4) home fallback
///
/// It does not simulate every minute. On a game-clock change it evaluates the
/// state that should be true at that game time and reconciles the world.
/// </summary>
public sealed class WorldOccupancyService : IWorldOccupancyService
{
    private readonly ISharedScenePresenceCoordinator _sharedScenes;
    private readonly IGroupSceneConversationOrchestrator _groupScenes;
    private readonly IScenePerceptionService _perception;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    public WorldOccupancyService(
        ISharedScenePresenceCoordinator sharedScenes,
        IGroupSceneConversationOrchestrator groupScenes,
        IScenePerceptionService perception)
    {
        _sharedScenes = sharedScenes;
        _groupScenes = groupScenes;
        _perception = perception;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<WorldOccupancySyncResult> SynchronizeAsync(
        DateTimeOffset gameTime,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            int bindingsCreated = EnsureBindingsForKnownCharacters();
            var bindings = LoadBindings();

            var result = new WorldOccupancySyncResult
            {
                GameTime = gameTime,
                BindingsCreated = bindingsCreated
            };

            foreach (var binding in bindings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var npc = CharacterRepository.LoadCharacter(binding.NpcId);
                if (npc == null)
                    continue;

                result.NpcsEvaluated++;

                var next = ResolveState(npc, binding, gameTime);
                var current = LoadState(binding.NpcId);

                if (SameState(current, next))
                {
                    // Initial DBs can already contain the correct state but no
                    // active ScenePresence row. Reconcile presence cheaply.
                    if (!string.IsNullOrWhiteSpace(next.CurrentLocationId))
                        await EnsureNpcPresenceAsync(npc, next, cancellationToken);
                    continue;
                }

                bool hadCurrent = current != null;
                string oldLocation = current?.CurrentLocationId ?? "";

                if (hadCurrent && !string.IsNullOrWhiteSpace(oldLocation) &&
                    !oldLocation.Equals(next.CurrentLocationId, StringComparison.OrdinalIgnoreCase))
                {
                    await DepartLocationAsync(
                        npc.Id,
                        npc.Name,
                        oldLocation,
                        next,
                        gameTime,
                        cancellationToken);

                    result.SceneDepartures++;
                }

                SaveState(next);
                InsertMovementEvent(current, next, gameTime);
                result.StateChanges++;

                if (!string.IsNullOrWhiteSpace(next.CurrentLocationId))
                {
                    await EnsureNpcPresenceAsync(npc, next, cancellationToken);

                    if (hadCurrent &&
                        !next.CurrentLocationId.Equals(oldLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        await AnnounceArrivalIfObservedAsync(
                            npc.Id,
                            npc.Name,
                            next.CurrentLocationId,
                            cancellationToken);
                        result.SceneArrivals++;
                    }
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NpcWorldLocationState?> GetNpcStateAsync(
        int npcId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return LoadState(npcId); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<NpcWorldOccupant>> GetLocationOccupantsAsync(
        string locationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(locationId))
            return Array.Empty<NpcWorldOccupant>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT NpcId,NpcName,CurrentLocationId,Status,Activity
FROM NpcWorldLocationState
WHERE CurrentLocationId=$location
ORDER BY NpcName,NpcId;";
            cmd.Parameters.AddWithValue("$location", locationId.Trim());

            var list = new List<NpcWorldOccupant>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new NpcWorldOccupant
                {
                    NpcId = reader.GetInt32(0),
                    NpcName = reader.GetString(1),
                    LocationId = reader.GetString(2),
                    Status = reader.GetString(3),
                    Activity = reader.GetString(4)
                });
            }
            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertScheduleBindingAsync(
        NpcScheduleBinding binding,
        CancellationToken cancellationToken = default)
    {
        if (binding.NpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(binding.NpcId));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO NpcScheduleBinding
(NpcId,HomeLocationId,HomeDisplayName,WorkLocationId,WorkDisplayName,
 ScheduleMode,UpdatedRealUtc)
VALUES($npc,$home,$homeName,$work,$workName,$mode,$real)
ON CONFLICT(NpcId) DO UPDATE SET
 HomeLocationId=excluded.HomeLocationId,
 HomeDisplayName=excluded.HomeDisplayName,
 WorkLocationId=excluded.WorkLocationId,
 WorkDisplayName=excluded.WorkDisplayName,
 ScheduleMode=excluded.ScheduleMode,
 UpdatedRealUtc=excluded.UpdatedRealUtc;";
            cmd.Parameters.AddWithValue("$npc", binding.NpcId);
            cmd.Parameters.AddWithValue("$home", Clean(binding.HomeLocationId, $"home:npc:{binding.NpcId}"));
            cmd.Parameters.AddWithValue("$homeName", Clean(binding.HomeDisplayName, "Home"));
            cmd.Parameters.AddWithValue("$work", Clean(binding.WorkLocationId, ""));
            cmd.Parameters.AddWithValue("$workName", Clean(binding.WorkDisplayName, "Work"));
            cmd.Parameters.AddWithValue("$mode", NormalizeMode(binding.ScheduleMode));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> AssignShiftAsync(
        NpcShiftAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.NpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.NpcId));
        if (request.EndGameTime <= request.StartGameTime)
            throw new ArgumentException("Shift end must be later than shift start.");
        if (string.IsNullOrWhiteSpace(request.LocationId))
            throw new ArgumentException("Shift LocationId is required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO NpcShiftAssignment
(NpcId,StartGameTime,EndGameTime,LocationId,Status,Note,Source,CreatedRealUtc)
VALUES($npc,$start,$end,$location,'scheduled',$note,$source,$real);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$npc", request.NpcId);
            cmd.Parameters.AddWithValue("$start", request.StartGameTime.ToString("O"));
            cmd.Parameters.AddWithValue("$end", request.EndGameTime.ToString("O"));
            cmd.Parameters.AddWithValue("$location", request.LocationId.Trim());
            cmd.Parameters.AddWithValue("$note", Clean(request.Note, ""));
            cmd.Parameters.AddWithValue("$source", Clean(request.Source, "manual_assignment"));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> SetScheduleOverrideAsync(
        NpcScheduleOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.NpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.NpcId));
        if (request.EndGameTime <= request.StartGameTime)
            throw new ArgumentException("Override end must be later than override start.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO NpcScheduleOverride
(NpcId,Kind,StartGameTime,EndGameTime,LocationId,Activity,Note,Status,CreatedRealUtc)
VALUES($npc,$kind,$start,$end,$location,$activity,$note,'active',$real);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$npc", request.NpcId);
            cmd.Parameters.AddWithValue("$kind", NormalizeOverrideKind(request.Kind));
            cmd.Parameters.AddWithValue("$start", request.StartGameTime.ToString("O"));
            cmd.Parameters.AddWithValue("$end", request.EndGameTime.ToString("O"));
            cmd.Parameters.AddWithValue("$location", Clean(request.LocationId, ""));
            cmd.Parameters.AddWithValue("$activity", Clean(request.Activity, ""));
            cmd.Parameters.AddWithValue("$note", Clean(request.Note, ""));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelScheduleOverrideAsync(
        long overrideId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE NpcScheduleOverride
SET Status='cancelled'
WHERE Id=$id;";
            cmd.Parameters.AddWithValue("$id", overrideId);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DateTimeOffset?> GetNextBoundaryAsync(
        int npcId,
        DateTimeOffset after,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var binding = LoadBinding(npcId);
            var npc = CharacterRepository.LoadCharacter(npcId);
            if (binding == null || npc == null)
                return null;

            var candidates = GetBoundaryTimes(npc, binding, after, after.AddDays(8));
            return candidates.Count == 0 ? null : candidates.Min();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorldScheduleBoundary?> GetNextWorldBoundaryAsync(
        DateTimeOffset after,
        DateTimeOffset through,
        CancellationToken cancellationToken = default)
    {
        if (through <= after)
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            WorldScheduleBoundary? best = null;

            foreach (var binding in LoadBindings())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var npc = CharacterRepository.LoadCharacter(binding.NpcId);
                if (npc == null)
                    continue;

                var times = GetBoundaryTimes(npc, binding, after, through);
                foreach (var time in times)
                {
                    if (time <= after || time > through)
                        continue;

                    // Resolve immediately before and after the exact boundary.
                    // This tells us whether the boundary is a real location
                    // departure/arrival or only a work/activity status change.
                    var before = ResolveState(npc, binding, time.AddSeconds(-1));
                    var afterState = ResolveState(npc, binding, time.AddSeconds(1));

                    if (SameState(before, afterState))
                        continue;

                    var candidate = new WorldScheduleBoundary
                    {
                        NpcId = npc.Id,
                        NpcName = npc.Name,
                        GameTime = time,
                        Kind = BoundaryKind(before, afterState),
                        FromStatus = before.Status,
                        ToStatus = afterState.Status,
                        FromLocationId = before.CurrentLocationId,
                        ToLocationId = afterState.CurrentLocationId,
                        Activity = afterState.Activity,
                        Source = afterState.Source
                    };

                    if (best == null ||
                        candidate.GameTime < best.GameTime ||
                        (candidate.GameTime == best.GameTime && candidate.NpcId < best.NpcId))
                    {
                        best = candidate;
                    }
                }
            }

            return best;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<DateTimeOffset> GetBoundaryTimes(
        SimCharacter npc,
        NpcScheduleBinding binding,
        DateTimeOffset after,
        DateTimeOffset through)
    {
        var candidates = new List<DateTimeOffset>();

        foreach (var x in LoadOverrides(npc.Id).Where(x => x.End > after && x.Start <= through))
        {
            if (x.Start > after && x.Start <= through) candidates.Add(x.Start);
            if (x.End > after && x.End <= through) candidates.Add(x.End);
        }

        foreach (var shift in LoadAssignedShifts(npc.Id, after.AddDays(-1), through.AddDays(1)))
        {
            double commute = EffectiveCommuteMinutes(npc.Job);
            var depart = shift.Start.AddMinutes(-commute);
            var home = shift.End.AddMinutes(commute);

            if (depart > after && depart <= through) candidates.Add(depart);
            if (shift.Start > after && shift.Start <= through) candidates.Add(shift.Start);
            if (shift.End > after && shift.End <= through) candidates.Add(shift.End);
            if (home > after && home <= through) candidates.Add(home);
        }

        if (NormalizeMode(binding.ScheduleMode) == "job_profile")
        {
            // Include a little padding so overnight shifts and commute windows
            // that begin just outside the visible date range are still found.
            var firstDate = after.Date.AddDays(-1);
            var lastDate = through.Date.AddDays(1);

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                var interval = BuildJobShift(npc.Job, date, binding, after.Offset);
                if (interval == null)
                    continue;

                foreach (var t in interval.Boundaries)
                    if (t > after && t <= through)
                        candidates.Add(t);
            }
        }

        return candidates
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    private static string BoundaryKind(
        NpcWorldLocationState before,
        NpcWorldLocationState after)
    {
        bool hadLocation = !string.IsNullOrWhiteSpace(before.CurrentLocationId);
        bool hasLocation = !string.IsNullOrWhiteSpace(after.CurrentLocationId);

        if (hadLocation && !hasLocation)
            return "depart";

        if (!hadLocation && hasLocation)
            return "arrive";

        if (hadLocation && hasLocation &&
            !before.CurrentLocationId.Equals(after.CurrentLocationId, StringComparison.OrdinalIgnoreCase))
            return "location_change";

        if (!before.Status.Equals(after.Status, StringComparison.OrdinalIgnoreCase))
            return "status_change";

        return "override_boundary";
    }

    private NpcWorldLocationState ResolveState(
        SimCharacter npc,
        NpcScheduleBinding binding,
        DateTimeOffset now)
    {
        var overrideRow = LoadOverrides(npc.Id)
            .Where(x => x.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Start <= now && now < x.End)
            .OrderByDescending(x => x.Id)
            .FirstOrDefault();

        if (overrideRow != null)
        {
            string overrideLocation = overrideRow.LocationId;

            if (string.IsNullOrWhiteSpace(overrideLocation) &&
                IsHomeOverride(overrideRow.Kind))
            {
                overrideLocation = binding.HomeLocationId;
            }

            return new NpcWorldLocationState
            {
                NpcId = npc.Id,
                NpcName = npc.Name,
                Status = "override",
                CurrentLocationId = Clean(overrideLocation, binding.HomeLocationId),
                Activity = Clean(
                    overrideRow.Activity,
                    IsHomeOverride(overrideRow.Kind) ? overrideRow.Kind : "override"),
                Source = "override:" + overrideRow.Kind,
                UpdatedGameTime = now
            };
        }

        var assigned = LoadAssignedShifts(npc.Id, now.AddDays(-1), now.AddDays(1))
            .Where(x => x.Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Start)
            .ToList();

        foreach (var shift in assigned)
        {
            var resolved = ResolveAroundShift(
                npc,
                binding,
                shift.Start,
                shift.End,
                Clean(shift.LocationId, binding.WorkLocationId),
                now,
                "assigned_shift:" + shift.Id);

            if (resolved != null)
                return resolved;
        }

        string mode = NormalizeMode(binding.ScheduleMode);
        if (mode == "home_only" || npc.Job == null || npc.Job.IsUnemployed || npc.Job.IsRetired)
            return AtHome(npc, binding, now, "home_only");

        if (mode == "assigned_shift_only")
            return AtHome(npc, binding, now, "awaiting_assigned_shift");

        // Check yesterday + today + tomorrow so overnight shifts and commute
        // windows resolve correctly without minute-by-minute simulation.
        for (int i = -1; i <= 1; i++)
        {
            var shift = BuildJobShift(npc.Job, now.Date.AddDays(i), binding, now.Offset);
            if (shift == null) continue;

            var resolved = ResolveAroundShift(
                npc,
                binding,
                shift.Start,
                shift.End,
                shift.LocationId,
                now,
                "job_profile");

            if (resolved != null)
                return resolved;
        }

        return AtHome(npc, binding, now, "off_shift");
    }

    private static NpcWorldLocationState? ResolveAroundShift(
        SimCharacter npc,
        NpcScheduleBinding binding,
        DateTimeOffset shiftStart,
        DateTimeOffset shiftEnd,
        string workLocationId,
        DateTimeOffset now,
        string source)
    {
        double commute = EffectiveCommuteMinutes(npc.Job);
        var departForWork = shiftStart.AddMinutes(-commute);
        var arriveHome = shiftEnd.AddMinutes(commute);

        if (now < departForWork || now >= arriveHome)
            return null;

        if (now < shiftStart)
        {
            return new NpcWorldLocationState
            {
                NpcId = npc.Id,
                NpcName = npc.Name,
                Status = "traveling",
                CurrentLocationId = "",
                OriginLocationId = binding.HomeLocationId,
                DestinationLocationId = workLocationId,
                DepartGameTime = departForWork,
                ExpectedArrivalGameTime = shiftStart,
                Activity = "commuting_to_work",
                Source = source,
                UpdatedGameTime = now
            };
        }

        if (now < shiftEnd)
        {
            return new NpcWorldLocationState
            {
                NpcId = npc.Id,
                NpcName = npc.Name,
                Status = "work",
                CurrentLocationId = workLocationId,
                Activity = "working",
                Source = source,
                UpdatedGameTime = now
            };
        }

        return new NpcWorldLocationState
        {
            NpcId = npc.Id,
            NpcName = npc.Name,
            Status = "traveling",
            CurrentLocationId = "",
            OriginLocationId = workLocationId,
            DestinationLocationId = binding.HomeLocationId,
            DepartGameTime = shiftEnd,
            ExpectedArrivalGameTime = arriveHome,
            Activity = "commuting_home",
            Source = source,
            UpdatedGameTime = now
        };
    }

    private static NpcWorldLocationState AtHome(
        SimCharacter npc,
        NpcScheduleBinding binding,
        DateTimeOffset now,
        string source)
        => new()
        {
            NpcId = npc.Id,
            NpcName = npc.Name,
            Status = "home",
            CurrentLocationId = binding.HomeLocationId,
            Activity = "home",
            Source = source,
            UpdatedGameTime = now
        };

    private async Task DepartLocationAsync(
        int npcId,
        string npcName,
        string oldLocationId,
        NpcWorldLocationState next,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string oldSceneId = SceneIdForLocation(oldLocationId);

        // Global scene narration must never reveal a hidden NPC. Because Phase
        // 10 world entries are shared by everyone in the scene, only emit a
        // common departure line when every active player could perceive this NPC.
        bool narrateToSharedScene = await IsNpcPerceivedByAllActivePlayersAsync(
            oldSceneId,
            npcId,
            cancellationToken);

        // When a person physically leaves a scene, old contact state must not
        // make them appear to still be hugging/kissing/grappling on a later visit.
        BreakSceneContacts(oldSceneId, "npc:" + npcId.ToString(CultureInfo.InvariantCulture), now);

        await _sharedScenes.RemoveNpcAsync(oldSceneId, npcId, cancellationToken);

        if (narrateToSharedScene)
        {
            string text = next.Status == "traveling"
                ? $"{npcName} heads out."
                : $"{npcName} leaves.";

            await _groupScenes.AppendWorldEntryAsync(
                oldSceneId,
                "scene_update",
                text,
                cancellationToken);
        }
    }

    private async Task EnsureNpcPresenceAsync(
        SimCharacter npc,
        NpcWorldLocationState state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.CurrentLocationId))
            return;

        var (x, y, facing) = PlacementFor(npc.Id);

        await _sharedScenes.UpsertNpcAsync(
            new SharedSceneNpcPlacement
            {
                SceneId = SceneIdForLocation(state.CurrentLocationId),
                NpcId = npc.Id,
                DisplayName = npc.Name,
                XFeet = x,
                YFeet = y,
                FacingDegrees = facing,
                Attention = state.Status == "work" ? 0.66 : 0.74,
                Activity = Clean(state.Activity, state.Status),
                ExclusiveLocation = false
            },
            cancellationToken);
    }

    private async Task AnnounceArrivalIfObservedAsync(
        int npcId,
        string npcName,
        string locationId,
        CancellationToken cancellationToken)
    {
        string sceneId = SceneIdForLocation(locationId);

        // Shared scene narration is only safe when every active player can
        // perceive the arrival. Otherwise the individual PRESENT/perception
        // views update without leaking the hidden NPC through narration.
        if (!await IsNpcPerceivedByAllActivePlayersAsync(
                sceneId,
                npcId,
                cancellationToken))
            return;

        await _groupScenes.AppendWorldEntryAsync(
            sceneId,
            "scene_update",
            $"{npcName} arrives.",
            cancellationToken);
    }

    private async Task<bool> IsNpcPerceivedByAllActivePlayersAsync(
        string sceneId,
        int npcId,
        CancellationToken cancellationToken)
    {
        var players = LoadActivePlayers(sceneId);
        if (players.Count == 0)
            return false;

        foreach (var playerId in players)
        {
            var perceived = await _perception.GetPerceivedPresenceAsync(
                sceneId,
                "player:" + playerId,
                cancellationToken);

            if (!perceived.Any(x => x.NpcId == npcId))
                return false;
        }

        return true;
    }

    private List<string> LoadActivePlayers(string sceneId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT PlayerId
FROM SharedScenePlayerMembership
WHERE SceneId=$scene
ORDER BY SlotIndex,PlayerId;";
        cmd.Parameters.AddWithValue("$scene", sceneId);

        var list = new List<string>();
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString(0));
        }
        catch { }

        return list;
    }

    private int EnsureBindingsForKnownCharacters()
    {
        var ids = LoadCharacterIds();
        int created = 0;

        foreach (int id in ids)
        {
            if (LoadBinding(id) != null)
                continue;

            var npc = CharacterRepository.LoadCharacter(id);
            if (npc == null)
                continue;

            var binding = DeriveBinding(npc);
            SaveBinding(binding);
            created++;
        }

        return created;
    }

    private static NpcScheduleBinding DeriveBinding(SimCharacter npc)
    {
        // The four authored Sinclair anchors get shared household/workplace IDs.
        // These are location identifiers only; their job hours still come from
        // each NPC's existing JobProfile.
        if (npc.Id == 1)
        {
            return new NpcScheduleBinding
            {
                NpcId = 1,
                HomeLocationId = "adam-house",
                HomeDisplayName = "Adam's House",
                WorkLocationId = "coffee-shop",
                WorkDisplayName = "Sinclair Coffee",
                ScheduleMode = ResolveMode(npc.Job)
            };
        }

        if (npc.Id == 2)
        {
            return new NpcScheduleBinding
            {
                NpcId = 2,
                HomeLocationId = "adam-house",
                HomeDisplayName = "Adam's House",
                WorkLocationId = "fire-station",
                WorkDisplayName = "Local Fire Department",
                ScheduleMode = ResolveMode(npc.Job)
            };
        }

        if (npc.Id == 3)
        {
            return new NpcScheduleBinding
            {
                NpcId = 3,
                HomeLocationId = "sinclair-family-home",
                HomeDisplayName = "Sinclair Family Home",
                WorkLocationId = "coffee-shop",
                WorkDisplayName = "Sinclair Coffee",
                ScheduleMode = ResolveMode(npc.Job)
            };
        }

        if (npc.Id == 4)
        {
            return new NpcScheduleBinding
            {
                NpcId = 4,
                HomeLocationId = "sinclair-family-home",
                HomeDisplayName = "Sinclair Family Home",
                WorkLocationId = "fire-station",
                WorkDisplayName = "Local Fire Department",
                ScheduleMode = ResolveMode(npc.Job)
            };
        }

        string homeId = $"home:npc:{npc.Id}";
        string homeName = string.IsNullOrWhiteSpace(npc.HomeAddress)
            ? $"{npc.Name}'s Home"
            : npc.HomeAddress.Trim();

        string workId = "";
        string workName = "";

        if (npc.Job != null && !npc.Job.IsUnemployed && !npc.Job.IsRetired)
        {
            if (string.Equals(
                    npc.Job.WorkLocationMode,
                    "remote",
                    StringComparison.OrdinalIgnoreCase))
            {
                workId = homeId;
                workName = homeName;
            }
            else
            {
                workName = !string.IsNullOrWhiteSpace(npc.Job.Employer)
                    ? npc.Job.Employer.Trim()
                    : Clean(npc.Job.JobName, "Work");

                workId = "work:" + Slug(workName);
            }
        }

        return new NpcScheduleBinding
        {
            NpcId = npc.Id,
            HomeLocationId = homeId,
            HomeDisplayName = homeName,
            WorkLocationId = workId,
            WorkDisplayName = workName,
            ScheduleMode = ResolveMode(npc.Job)
        };
    }

    private static string ResolveMode(JobProfile? job)
    {
        if (job == null || job.IsUnemployed || job.IsRetired)
            return "home_only";

        var recognized = RecognizedWorkDays(job.WorkDays);
        return recognized.Count > 0 ? "job_profile" : "assigned_shift_only";
    }

    private static JobShift? BuildJobShift(
        JobProfile job,
        DateTime localDate,
        NpcScheduleBinding binding,
        TimeSpan gameOffset)
    {
        if (job == null || job.IsUnemployed || job.IsRetired)
            return null;

        var days = RecognizedWorkDays(job.WorkDays);
        if (days.Count == 0)
            return null;

        string day = localDate.ToString("ddd", CultureInfo.InvariantCulture);
        if (!days.Contains(day))
            return null;

        var offset = gameOffset;
        var start = new DateTimeOffset(
            localDate.Year,
            localDate.Month,
            localDate.Day,
            Math.Clamp(job.StartHour, 0, 23),
            0,
            0,
            offset);

        DateTimeOffset end;
        if (job.StartHour == job.EndHour)
        {
            end = start.AddDays(1);
        }
        else if (job.StartHour < job.EndHour)
        {
            end = new DateTimeOffset(
                localDate.Year,
                localDate.Month,
                localDate.Day,
                Math.Clamp(job.EndHour, 0, 23),
                0,
                0,
                offset);
        }
        else
        {
            var next = localDate.AddDays(1);
            end = new DateTimeOffset(
                next.Year,
                next.Month,
                next.Day,
                Math.Clamp(job.EndHour, 0, 23),
                0,
                0,
                offset);
        }

        string workLocation = string.Equals(
                job.WorkLocationMode,
                "remote",
                StringComparison.OrdinalIgnoreCase)
            ? binding.HomeLocationId
            : Clean(binding.WorkLocationId, binding.HomeLocationId);

        double commute = EffectiveCommuteMinutes(job);

        return new JobShift
        {
            Start = start,
            End = end,
            LocationId = workLocation,
            Boundaries = new[]
            {
                start.AddMinutes(-commute),
                start,
                end,
                end.AddMinutes(commute)
            }
        };
    }

    private static double EffectiveCommuteMinutes(JobProfile? job)
    {
        if (job == null)
            return 0;

        if (string.Equals(job.WorkLocationMode, "remote", StringComparison.OrdinalIgnoreCase))
            return 0;

        return Math.Clamp(job.CommuteMinutesOneWay, 0, 240);
    }

    private static HashSet<string> RecognizedWorkDays(string[]? workDays)
    {
        var valid = new HashSet<string>(
            new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
            StringComparer.OrdinalIgnoreCase);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in workDays ?? Array.Empty<string>())
        {
            var x = Clean(raw, "");
            if (valid.Contains(x))
                result.Add(x);
        }
        return result;
    }

    private List<OverrideRow> LoadOverrides(int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id,NpcId,Kind,StartGameTime,EndGameTime,LocationId,Activity,Note,Status
FROM NpcScheduleOverride
WHERE NpcId=$npc AND Status='active'
ORDER BY StartGameTime,Id;";
        cmd.Parameters.AddWithValue("$npc", npcId);

        var list = new List<OverrideRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new OverrideRow
            {
                Id = r.GetInt64(0),
                NpcId = r.GetInt32(1),
                Kind = r.GetString(2),
                Start = ParseTime(r.GetString(3)),
                End = ParseTime(r.GetString(4)),
                LocationId = r.GetString(5),
                Activity = r.GetString(6),
                Note = r.GetString(7),
                Status = r.GetString(8)
            });
        }
        return list;
    }

    private List<ShiftRow> LoadAssignedShifts(
        int npcId,
        DateTimeOffset from,
        DateTimeOffset through)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id,NpcId,StartGameTime,EndGameTime,LocationId,Status,Note,Source
FROM NpcShiftAssignment
WHERE NpcId=$npc
  AND Status='scheduled'
  AND EndGameTime > $from
  AND StartGameTime < $through
ORDER BY StartGameTime,Id;";
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue("$from", from.ToString("O"));
        cmd.Parameters.AddWithValue("$through", through.ToString("O"));

        var list = new List<ShiftRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ShiftRow
            {
                Id = r.GetInt64(0),
                NpcId = r.GetInt32(1),
                Start = ParseTime(r.GetString(2)),
                End = ParseTime(r.GetString(3)),
                LocationId = r.GetString(4),
                Status = r.GetString(5),
                Note = r.GetString(6),
                Source = r.GetString(7)
            });
        }
        return list;
    }

    private List<NpcScheduleBinding> LoadBindings()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT NpcId,HomeLocationId,HomeDisplayName,WorkLocationId,WorkDisplayName,ScheduleMode
FROM NpcScheduleBinding
ORDER BY NpcId;";

        var list = new List<NpcScheduleBinding>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadBinding(r));
        return list;
    }

    private NpcScheduleBinding? LoadBinding(int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT NpcId,HomeLocationId,HomeDisplayName,WorkLocationId,WorkDisplayName,ScheduleMode
FROM NpcScheduleBinding
WHERE NpcId=$npc
LIMIT 1;";
        cmd.Parameters.AddWithValue("$npc", npcId);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadBinding(r) : null;
    }

    private static NpcScheduleBinding ReadBinding(SqliteDataReader r)
        => new()
        {
            NpcId = r.GetInt32(0),
            HomeLocationId = r.GetString(1),
            HomeDisplayName = r.GetString(2),
            WorkLocationId = r.GetString(3),
            WorkDisplayName = r.GetString(4),
            ScheduleMode = r.GetString(5)
        };

    private void SaveBinding(NpcScheduleBinding binding)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO NpcScheduleBinding
(NpcId,HomeLocationId,HomeDisplayName,WorkLocationId,WorkDisplayName,ScheduleMode,UpdatedRealUtc)
VALUES($npc,$home,$homeName,$work,$workName,$mode,$real)
ON CONFLICT(NpcId) DO NOTHING;";
        cmd.Parameters.AddWithValue("$npc", binding.NpcId);
        cmd.Parameters.AddWithValue("$home", binding.HomeLocationId);
        cmd.Parameters.AddWithValue("$homeName", binding.HomeDisplayName);
        cmd.Parameters.AddWithValue("$work", binding.WorkLocationId);
        cmd.Parameters.AddWithValue("$workName", binding.WorkDisplayName);
        cmd.Parameters.AddWithValue("$mode", NormalizeMode(binding.ScheduleMode));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private List<int> LoadCharacterIds()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Characters WHERE Id > 0 ORDER BY Id;";

        var ids = new List<int>();
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
                ids.Add(r.GetInt32(0));
        }
        catch
        {
            // Fresh databases can briefly exist before Characters is created.
        }
        return ids;
    }

    private NpcWorldLocationState? LoadState(int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT NpcId,NpcName,Status,CurrentLocationId,OriginLocationId,DestinationLocationId,
       DepartGameTime,ExpectedArrivalGameTime,Activity,Source,UpdatedGameTime
FROM NpcWorldLocationState
WHERE NpcId=$npc
LIMIT 1;";
        cmd.Parameters.AddWithValue("$npc", npcId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new NpcWorldLocationState
        {
            NpcId = r.GetInt32(0),
            NpcName = r.GetString(1),
            Status = r.GetString(2),
            CurrentLocationId = r.GetString(3),
            OriginLocationId = r.GetString(4),
            DestinationLocationId = r.GetString(5),
            DepartGameTime = r.IsDBNull(6) ? null : ParseTime(r.GetString(6)),
            ExpectedArrivalGameTime = r.IsDBNull(7) ? null : ParseTime(r.GetString(7)),
            Activity = r.GetString(8),
            Source = r.GetString(9),
            UpdatedGameTime = ParseTime(r.GetString(10))
        };
    }

    private void SaveState(NpcWorldLocationState state)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO NpcWorldLocationState
(NpcId,NpcName,Status,CurrentLocationId,OriginLocationId,DestinationLocationId,
 DepartGameTime,ExpectedArrivalGameTime,Activity,Source,UpdatedGameTime,UpdatedRealUtc)
VALUES($npc,$name,$status,$current,$origin,$destination,
       $depart,$arrival,$activity,$source,$game,$real)
ON CONFLICT(NpcId) DO UPDATE SET
 NpcName=excluded.NpcName,
 Status=excluded.Status,
 CurrentLocationId=excluded.CurrentLocationId,
 OriginLocationId=excluded.OriginLocationId,
 DestinationLocationId=excluded.DestinationLocationId,
 DepartGameTime=excluded.DepartGameTime,
 ExpectedArrivalGameTime=excluded.ExpectedArrivalGameTime,
 Activity=excluded.Activity,
 Source=excluded.Source,
 UpdatedGameTime=excluded.UpdatedGameTime,
 UpdatedRealUtc=excluded.UpdatedRealUtc;";
        cmd.Parameters.AddWithValue("$npc", state.NpcId);
        cmd.Parameters.AddWithValue("$name", state.NpcName);
        cmd.Parameters.AddWithValue("$status", state.Status);
        cmd.Parameters.AddWithValue("$current", Clean(state.CurrentLocationId, ""));
        cmd.Parameters.AddWithValue("$origin", Clean(state.OriginLocationId, ""));
        cmd.Parameters.AddWithValue("$destination", Clean(state.DestinationLocationId, ""));
        cmd.Parameters.AddWithValue("$depart", state.DepartGameTime?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$arrival", state.ExpectedArrivalGameTime?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$activity", Clean(state.Activity, ""));
        cmd.Parameters.AddWithValue("$source", Clean(state.Source, ""));
        cmd.Parameters.AddWithValue("$game", state.UpdatedGameTime.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void InsertMovementEvent(
        NpcWorldLocationState? previous,
        NpcWorldLocationState next,
        DateTimeOffset gameTime)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO NpcWorldMovementEvent
(NpcId,NpcName,FromStatus,ToStatus,FromLocationId,ToLocationId,
 OriginLocationId,DestinationLocationId,GameTime,Source,CreatedRealUtc)
VALUES($npc,$name,$fromStatus,$toStatus,$fromLocation,$toLocation,
       $origin,$destination,$game,$source,$real);";
        cmd.Parameters.AddWithValue("$npc", next.NpcId);
        cmd.Parameters.AddWithValue("$name", next.NpcName);
        cmd.Parameters.AddWithValue("$fromStatus", previous?.Status ?? "unknown");
        cmd.Parameters.AddWithValue("$toStatus", next.Status);
        cmd.Parameters.AddWithValue("$fromLocation", previous?.CurrentLocationId ?? "");
        cmd.Parameters.AddWithValue("$toLocation", next.CurrentLocationId);
        cmd.Parameters.AddWithValue("$origin", next.OriginLocationId);
        cmd.Parameters.AddWithValue("$destination", next.DestinationLocationId);
        cmd.Parameters.AddWithValue("$game", gameTime.ToString("O"));
        cmd.Parameters.AddWithValue("$source", next.Source);
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void BreakSceneContacts(
        string sceneId,
        string characterKey,
        DateTimeOffset gameTime)
    {
        using var conn = Open();
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
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$character", characterKey);

        try { cmd.ExecuteNonQuery(); }
        catch
        {
            // Phase 12 may be copied before Phase 11 on a development DB.
        }
    }

    private int ActivePlayerCount(string sceneId)
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
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static bool SameState(
        NpcWorldLocationState? a,
        NpcWorldLocationState b)
    {
        if (a == null) return false;

        return a.Status.Equals(b.Status, StringComparison.OrdinalIgnoreCase)
            && a.CurrentLocationId.Equals(b.CurrentLocationId, StringComparison.OrdinalIgnoreCase)
            && a.OriginLocationId.Equals(b.OriginLocationId, StringComparison.OrdinalIgnoreCase)
            && a.DestinationLocationId.Equals(b.DestinationLocationId, StringComparison.OrdinalIgnoreCase)
            && NullableTimeEqual(a.ExpectedArrivalGameTime, b.ExpectedArrivalGameTime)
            && a.Activity.Equals(b.Activity, StringComparison.OrdinalIgnoreCase)
            && a.Source.Equals(b.Source, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NullableTimeEqual(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (a.HasValue != b.HasValue) return false;
        return Math.Abs((a!.Value - b!.Value).TotalSeconds) < 1;
    }

    private static (double X, double Y, double Facing) PlacementFor(int npcId)
    {
        // Stable technical scene placement, not hidden personality information.
        // The spatial engine may move them naturally after arrival.
        double angle = ((npcId * 137.5) % 360.0) * Math.PI / 180.0;
        double radius = 5.0 + (npcId % 4) * 2.0;
        double x = Math.Round(Math.Cos(angle) * radius, 2);
        double y = Math.Round(Math.Sin(angle) * radius, 2);
        double facing = (Math.Atan2(-y, -x) * 180.0 / Math.PI + 360.0) % 360.0;
        return (x, y, facing);
    }

    private static string SceneIdForLocation(string locationId)
        => "inperson:" + locationId.Trim();

    private static string NormalizeMode(string? mode)
        => mode?.Trim().ToLowerInvariant() switch
        {
            "assigned_shift_only" => "assigned_shift_only",
            "home_only" => "home_only",
            _ => "job_profile"
        };

    private static string NormalizeOverrideKind(string? kind)
        => kind?.Trim().ToLowerInvariant() switch
        {
            "call_off" => "call_off",
            "sick" => "sick",
            "vacation" => "vacation",
            "appointment" => "appointment",
            "manual_location" => "manual_location",
            "stay_home" => "stay_home",
            "emergency" => "emergency",
            _ => "other"
        };

    private static bool IsHomeOverride(string kind)
        => kind.Equals("call_off", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("sick", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("vacation", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("stay_home", StringComparison.OrdinalIgnoreCase);

    private static string Slug(string value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        bool dash = false;

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }

        return sb.ToString().Trim('-');
    }

    private static DateTimeOffset ParseTime(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.Now;

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS NpcScheduleBinding
(
    NpcId INTEGER PRIMARY KEY,
    HomeLocationId TEXT NOT NULL,
    HomeDisplayName TEXT NOT NULL,
    WorkLocationId TEXT NOT NULL DEFAULT '',
    WorkDisplayName TEXT NOT NULL DEFAULT '',
    ScheduleMode TEXT NOT NULL DEFAULT 'job_profile',
    UpdatedRealUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS NpcShiftAssignment
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NpcId INTEGER NOT NULL,
    StartGameTime TEXT NOT NULL,
    EndGameTime TEXT NOT NULL,
    LocationId TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'scheduled',
    Note TEXT NOT NULL DEFAULT '',
    Source TEXT NOT NULL DEFAULT 'manual_assignment',
    CreatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_NpcShiftAssignment_NpcTime
ON NpcShiftAssignment(NpcId,Status,StartGameTime,EndGameTime);

CREATE TABLE IF NOT EXISTS NpcScheduleOverride
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NpcId INTEGER NOT NULL,
    Kind TEXT NOT NULL,
    StartGameTime TEXT NOT NULL,
    EndGameTime TEXT NOT NULL,
    LocationId TEXT NOT NULL DEFAULT '',
    Activity TEXT NOT NULL DEFAULT '',
    Note TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT 'active',
    CreatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_NpcScheduleOverride_NpcTime
ON NpcScheduleOverride(NpcId,Status,StartGameTime,EndGameTime);

CREATE TABLE IF NOT EXISTS NpcWorldLocationState
(
    NpcId INTEGER PRIMARY KEY,
    NpcName TEXT NOT NULL,
    Status TEXT NOT NULL,
    CurrentLocationId TEXT NOT NULL DEFAULT '',
    OriginLocationId TEXT NOT NULL DEFAULT '',
    DestinationLocationId TEXT NOT NULL DEFAULT '',
    DepartGameTime TEXT NULL,
    ExpectedArrivalGameTime TEXT NULL,
    Activity TEXT NOT NULL DEFAULT '',
    Source TEXT NOT NULL DEFAULT '',
    UpdatedGameTime TEXT NOT NULL,
    UpdatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_NpcWorldLocationState_Location
ON NpcWorldLocationState(CurrentLocationId,Status,NpcId);

CREATE TABLE IF NOT EXISTS NpcWorldMovementEvent
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NpcId INTEGER NOT NULL,
    NpcName TEXT NOT NULL,
    FromStatus TEXT NOT NULL,
    ToStatus TEXT NOT NULL,
    FromLocationId TEXT NOT NULL DEFAULT '',
    ToLocationId TEXT NOT NULL DEFAULT '',
    OriginLocationId TEXT NOT NULL DEFAULT '',
    DestinationLocationId TEXT NOT NULL DEFAULT '',
    GameTime TEXT NOT NULL,
    Source TEXT NOT NULL DEFAULT '',
    CreatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_NpcWorldMovementEvent_NpcTime
ON NpcWorldMovementEvent(NpcId,GameTime);
";
        cmd.ExecuteNonQuery();
    }

    private sealed class JobShift
    {
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public string LocationId { get; set; } = "";
        public IReadOnlyList<DateTimeOffset> Boundaries { get; set; } = Array.Empty<DateTimeOffset>();
    }

    private sealed class ShiftRow
    {
        public long Id { get; set; }
        public int NpcId { get; set; }
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public string LocationId { get; set; } = "";
        public string Status { get; set; } = "";
        public string Note { get; set; } = "";
        public string Source { get; set; } = "";
    }

    private sealed class OverrideRow
    {
        public long Id { get; set; }
        public int NpcId { get; set; }
        public string Kind { get; set; } = "";
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public string LocationId { get; set; } = "";
        public string Activity { get; set; } = "";
        public string Note { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
