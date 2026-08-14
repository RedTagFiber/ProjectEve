using Microsoft.Data.Sqlite;
using ProjectEve.Core.Scene;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Scene;

/// <summary>
/// Phase 10 shared scene membership.
///
/// One location uses one shared scene id, so two players can occupy the same
/// physical scene. Leaving removes only that player's presence. NPC presence is
/// never removed merely because one player closed their page.
/// </summary>
public sealed class SharedScenePresenceCoordinator : ISharedScenePresenceCoordinator
{
    private readonly IScenePerceptionService _perception;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    private static readonly TimeSpan StalePlayerAfter = TimeSpan.FromMinutes(3);

    public SharedScenePresenceCoordinator(IScenePerceptionService perception)
    {
        _perception = perception;
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<SharedSceneJoinResult> JoinAsync(
        SharedSceneJoinRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SceneId))
            throw new ArgumentException("SceneId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PlayerId))
            throw new ArgumentException("PlayerId is required.", nameof(request));

        string sceneId = request.SceneId.Trim();
        string playerId = request.PlayerId.Trim();
        string playerName = string.IsNullOrWhiteSpace(request.PlayerName)
            ? "Player"
            : request.PlayerName.Trim();
        string playerKey = "player:" + playerId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CleanupStalePlayersLockedAsync(cancellationToken);

            await _perception.UpsertSceneAsync(new SceneDefinition
            {
                SceneId = sceneId,
                LocationId = Clean(request.LocationId, "unknown"),
                DisplayName = Clean(request.DisplayName, request.LocationId),
                AmbientNoise = Math.Clamp(request.AmbientNoise, 0, 1),
                VisualClutter = Math.Clamp(request.VisualClutter, 0, 1),
                DefaultRoomZone = "main",
                DefaultAcousticZone = "main"
            }, cancellationToken);

            int slot = GetOrAssignSlotLocked(sceneId, playerId, playerName);
            var (x, y, facing) = PlayerPosition(slot);

            await _perception.UpsertPresenceAsync(new ScenePresenceUpdate
            {
                SceneId = sceneId,
                CharacterKey = playerKey,
                PlayerId = playerId,
                DisplayName = playerName,
                IsPlayer = true,
                XFeet = x,
                YFeet = y,
                FacingDegrees = facing,
                RoomZone = "main",
                AcousticZone = "main",
                Attention = 0.90,
                Activity = "conversation",
                Concealment = 0,
                IsActive = true
            }, cancellationToken);

            var placed = new List<int>();
            foreach (var seed in request.BootstrapNpcs ?? Array.Empty<SharedSceneNpcPlacement>())
            {
                if (seed.NpcId <= 0)
                    continue;

                var normalized = CloneNpc(seed);
                normalized.SceneId = sceneId;

                if (await TryPlaceNpcLockedAsync(normalized, cancellationToken))
                    placed.Add(seed.NpcId);
            }

            return new SharedSceneJoinResult
            {
                SceneId = sceneId,
                PlayerCharacterKey = playerKey,
                PlayerSlot = slot,
                ActivePlayerCount = CountActivePlayersLocked(sceneId),
                BootstrapNpcIdsPlaced = placed
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HeartbeatAsync(
        string sceneId,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(playerId))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var read = conn.CreateCommand();
            read.CommandText = @"
SELECT PlayerName, SlotIndex
FROM SharedScenePlayerMembership
WHERE SceneId=$scene AND PlayerId=$player;";
            read.Parameters.AddWithValue("$scene", sceneId.Trim());
            read.Parameters.AddWithValue("$player", playerId.Trim());

            string? name = null;
            int slot = -1;
            using (var reader = read.ExecuteReader())
            {
                if (reader.Read())
                {
                    name = reader.GetString(0);
                    slot = reader.GetInt32(1);
                }
            }

            if (slot < 0)
                return;

            using var update = conn.CreateCommand();
            update.CommandText = @"
UPDATE SharedScenePlayerMembership
SET LastHeartbeatRealUtc=$real
WHERE SceneId=$scene AND PlayerId=$player;";
            update.Parameters.AddWithValue("$real", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$scene", sceneId.Trim());
            update.Parameters.AddWithValue("$player", playerId.Trim());
            update.ExecuteNonQuery();

            var (x, y, facing) = PlayerPosition(slot);
            await _perception.UpsertPresenceAsync(new ScenePresenceUpdate
            {
                SceneId = sceneId.Trim(),
                CharacterKey = "player:" + playerId.Trim(),
                PlayerId = playerId.Trim(),
                DisplayName = Clean(name, "Player"),
                IsPlayer = true,
                XFeet = x,
                YFeet = y,
                FacingDegrees = facing,
                Attention = 0.90,
                Activity = "conversation",
                IsActive = true
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SharedSceneLeaveResult> LeaveAsync(
        string sceneId,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(playerId))
            return new SharedSceneLeaveResult { SceneId = sceneId ?? "", RemainingPlayers = 0 };

        sceneId = sceneId.Trim();
        playerId = playerId.Trim();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
DELETE FROM SharedScenePlayerMembership
WHERE SceneId=$scene AND PlayerId=$player;";
                cmd.Parameters.AddWithValue("$scene", sceneId);
                cmd.Parameters.AddWithValue("$player", playerId);
                cmd.ExecuteNonQuery();
            }

            await _perception.RemovePresenceAsync(
                sceneId,
                "player:" + playerId,
                cancellationToken);

            return new SharedSceneLeaveResult
            {
                SceneId = sceneId,
                RemainingPlayers = CountActivePlayersLocked(sceneId)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<ScenePerceivedPresence>> GetPlayerPerceivedPresenceAsync(
        string sceneId,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(playerId))
            return Task.FromResult<IReadOnlyList<ScenePerceivedPresence>>(
                Array.Empty<ScenePerceivedPresence>());

        return _perception.GetPerceivedPresenceAsync(
            sceneId.Trim(),
            "player:" + playerId.Trim(),
            cancellationToken);
    }

    public async Task UpsertNpcAsync(
        SharedSceneNpcPlacement npc,
        CancellationToken cancellationToken = default)
    {
        if (npc.NpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npc.NpcId));
        if (string.IsNullOrWhiteSpace(npc.SceneId))
            throw new ArgumentException("SceneId is required.", nameof(npc));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await TryPlaceNpcLockedAsync(CloneNpc(npc), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveNpcAsync(
        string sceneId,
        int npcId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || npcId <= 0)
            return;

        await _perception.RemovePresenceAsync(
            sceneId.Trim(),
            "npc:" + npcId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
    }

    private async Task<bool> TryPlaceNpcLockedAsync(
        SharedSceneNpcPlacement npc,
        CancellationToken cancellationToken)
    {
        if (npc.ExclusiveLocation)
        {
            var oldScenes = FindNpcScenesLocked(npc.NpcId, npc.SceneId);
            foreach (var oldScene in oldScenes)
            {
                // Never yank an NPC away from another active player's scene.
                if (CountActivePlayersLocked(oldScene) > 0)
                    return false;
            }

            foreach (var oldScene in oldScenes)
            {
                await _perception.RemovePresenceAsync(
                    oldScene,
                    "npc:" + npc.NpcId.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
        }

        await _perception.UpsertPresenceAsync(new ScenePresenceUpdate
        {
            SceneId = npc.SceneId.Trim(),
            CharacterKey = "npc:" + npc.NpcId.ToString(CultureInfo.InvariantCulture),
            NpcId = npc.NpcId,
            DisplayName = Clean(npc.DisplayName, "NPC " + npc.NpcId),
            IsPlayer = false,
            XFeet = npc.XFeet,
            YFeet = npc.YFeet,
            FacingDegrees = npc.FacingDegrees,
            RoomZone = Clean(npc.RoomZone, "main"),
            AcousticZone = Clean(npc.AcousticZone, "main"),
            Attention = Math.Clamp(npc.Attention, 0, 1),
            Activity = Clean(npc.Activity, "conversation"),
            Concealment = Math.Clamp(npc.Concealment, 0, 1),
            IsActive = true
        }, cancellationToken);

        return true;
    }

    private int GetOrAssignSlotLocked(string sceneId, string playerId, string playerName)
    {
        using var conn = Open();

        using (var existing = conn.CreateCommand())
        {
            existing.CommandText = @"
SELECT SlotIndex
FROM SharedScenePlayerMembership
WHERE SceneId=$scene AND PlayerId=$player;";
            existing.Parameters.AddWithValue("$scene", sceneId);
            existing.Parameters.AddWithValue("$player", playerId);
            var value = existing.ExecuteScalar();
            if (value != null && value != DBNull.Value)
            {
                int slot = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                TouchMembershipLocked(conn, sceneId, playerId, playerName, slot);
                return slot;
            }
        }

        var used = new HashSet<int>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT SlotIndex
FROM SharedScenePlayerMembership
WHERE SceneId=$scene;";
            cmd.Parameters.AddWithValue("$scene", sceneId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                used.Add(reader.GetInt32(0));
        }

        int slotIndex = !used.Contains(0) ? 0 : !used.Contains(1) ? 1 : -1;
        if (slotIndex < 0)
            throw new InvalidOperationException(
                "This scene already has the Phase 10 maximum of two active players.");

        TouchMembershipLocked(conn, sceneId, playerId, playerName, slotIndex);
        return slotIndex;
    }

    private static void TouchMembershipLocked(
        SqliteConnection conn,
        string sceneId,
        string playerId,
        string playerName,
        int slot)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO SharedScenePlayerMembership
(SceneId,PlayerId,PlayerName,SlotIndex,LastHeartbeatRealUtc)
VALUES($scene,$player,$name,$slot,$real)
ON CONFLICT(SceneId,PlayerId) DO UPDATE SET
    PlayerName=excluded.PlayerName,
    SlotIndex=excluded.SlotIndex,
    LastHeartbeatRealUtc=excluded.LastHeartbeatRealUtc;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$player", playerId);
        cmd.Parameters.AddWithValue("$name", playerName);
        cmd.Parameters.AddWithValue("$slot", slot);
        cmd.Parameters.AddWithValue("$real", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
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
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private List<string> FindNpcScenesLocked(int npcId, string exceptSceneId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT DISTINCT SceneId
FROM ScenePresence
WHERE NpcId=$npc
  AND IsActive=1
  AND SceneId<>$scene;";
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue("$scene", exceptSceneId);

        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    private async Task CleanupStalePlayersLockedAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - StalePlayerAfter;
        var stale = new List<(string SceneId, string PlayerId)>();

        using (var conn = Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT SceneId, PlayerId
FROM SharedScenePlayerMembership
WHERE LastHeartbeatRealUtc < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                stale.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var row in stale)
        {
            using (var conn = Open())
            using (var delete = conn.CreateCommand())
            {
                delete.CommandText = @"
DELETE FROM SharedScenePlayerMembership
WHERE SceneId=$scene AND PlayerId=$player;";
                delete.Parameters.AddWithValue("$scene", row.SceneId);
                delete.Parameters.AddWithValue("$player", row.PlayerId);
                delete.ExecuteNonQuery();
            }

            try
            {
                await _perception.RemovePresenceAsync(
                    row.SceneId,
                    "player:" + row.PlayerId,
                    cancellationToken);
            }
            catch { }
        }
    }

    private static (double X, double Y, double Facing) PlayerPosition(int slot)
        => slot == 0 ? (0.0, 0.0, 0.0) : (0.0, 6.0, 330.0);

    private static SharedSceneNpcPlacement CloneNpc(SharedSceneNpcPlacement x)
        => new()
        {
            SceneId = x.SceneId,
            NpcId = x.NpcId,
            DisplayName = x.DisplayName,
            XFeet = x.XFeet,
            YFeet = x.YFeet,
            FacingDegrees = x.FacingDegrees,
            RoomZone = x.RoomZone,
            AcousticZone = x.AcousticZone,
            Attention = x.Attention,
            Activity = x.Activity,
            Concealment = x.Concealment,
            ExclusiveLocation = x.ExclusiveLocation
        };

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SharedScenePlayerMembership
(
    SceneId TEXT NOT NULL,
    PlayerId TEXT NOT NULL,
    PlayerName TEXT NOT NULL,
    SlotIndex INTEGER NOT NULL,
    LastHeartbeatRealUtc TEXT NOT NULL,
    PRIMARY KEY(SceneId, PlayerId)
);

CREATE INDEX IF NOT EXISTS IX_SharedScenePlayerMembership_Scene
ON SharedScenePlayerMembership(SceneId, SlotIndex);
";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
