using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Data;
using System;
using System.Collections.Generic;

namespace ProjectEve.Memory;

/// <summary>
/// NPC subjective memory store.
///
/// Canonical ownership:
///   project_eve_relationships.db
///
/// Objective events belong in project_eve_history.db.
/// This class stores what an NPC remembers/believes, not God's-eye truth.
/// </summary>
public class MemoryDatabase
{
    private readonly string _dbPath;

    public MemoryDatabase(string? dbPath = null)
    {
        _dbPath = dbPath
            ?? Environment.GetEnvironmentVariable("EVE_RELATIONSHIP_DB_PATH")
            ?? ProjectEveDatabaseSetup.RelationshipDatabasePath;

        ProjectEveDatabaseSetup.EnsureAll();
        EnsureSchema();
    }

    private string ConnStr => $"Data Source={_dbPath}";

    private void EnsureSchema()
    {
        // Canonical schema ownership belongs only to ProjectEveDatabaseSetup.
        // MemoryDatabase remains the runtime read/write gateway for personal
        // memories, but it no longer creates or alters the table itself.
        ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();
    }

    public void AddMemory(MemoryRecord memory)
    {
        if (memory == null || string.IsNullOrWhiteSpace(memory.Summary))
            return;

        if (memory.Strength <= 0)
            memory.Strength = Math.Clamp(40f + memory.Importance * 0.5f, 20f, 100f);

        var id = $"memory:{memory.NpcId}:{Guid.NewGuid():N}";
        var time = memory.Timestamp == default ? DateTime.UtcNow : memory.Timestamp;

        string category = memory.Category ?? "General";
        string eventId = memory.EventId ?? "";

        using var conn = new SqliteConnection(ConnStr);
        conn.Open();

        // Exact-memory idempotency:
        // rerunning a seed/build step must not create five copies of the same
        // subjective memory. A different EventId is still allowed to represent
        // a genuinely separate occurrence.
        using (var find = conn.CreateCommand())
        {
            find.CommandText = """
                SELECT Id
                FROM PersonalMemories
                WHERE KnowerCharacterId = $npc
                  AND SubjectCharacterId IS NULL
                  AND lower(trim(MemoryType)) = lower(trim($type))
                  AND trim(MemoryText) = trim($text)
                  AND COALESCE(EventId, '') = $eventId
                ORDER BY CreatedRealAt DESC, rowid DESC
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$npc", memory.NpcId);
            find.Parameters.AddWithValue("$type", category);
            find.Parameters.AddWithValue("$text", memory.Summary);
            find.Parameters.AddWithValue("$eventId", eventId);

            string? existingId = find.ExecuteScalar()?.ToString();

            if (!string.IsNullOrWhiteSpace(existingId))
            {
                using var update = conn.CreateCommand();
                update.CommandText = """
                    UPDATE PersonalMemories
                    SET Importance = MAX(Importance, $importance),
                        Strength = MAX(Strength, $strength),
                        IsLockedPeak = MAX(IsLockedPeak, $locked),
                        LastUpdatedGameTime = $gameTime
                    WHERE Id = $id;
                    """;
                update.Parameters.AddWithValue("$importance", Math.Clamp(memory.Importance, 1, 100));
                update.Parameters.AddWithValue("$strength", Math.Clamp((int)Math.Round(memory.Strength), 0, 100));
                update.Parameters.AddWithValue("$locked", memory.IsLockedPeak ? 1 : 0);
                update.Parameters.AddWithValue("$gameTime", time.ToString("o"));
                update.Parameters.AddWithValue("$id", existingId);
                update.ExecuteNonQuery();
                return;
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PersonalMemories
            (Id, KnowerCharacterId, SubjectCharacterId, EventId, MemoryType, MemoryText,
             Interpretation, EmotionalMeaning, Importance, Strength, Confidence,
             IsLockedPeak, LearnedGameTime, LastUpdatedGameTime)
            VALUES
            ($id, $npc, NULL, $eventId, $type, $text,
             '', '', $importance, $strength, 70,
             $locked, $gameTime, $gameTime);
            """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$npc", memory.NpcId);
        cmd.Parameters.AddWithValue("$eventId", eventId);
        cmd.Parameters.AddWithValue("$type", category);
        cmd.Parameters.AddWithValue("$text", memory.Summary);
        cmd.Parameters.AddWithValue("$importance", Math.Clamp(memory.Importance, 1, 100));
        cmd.Parameters.AddWithValue("$strength", Math.Clamp((int)Math.Round(memory.Strength), 0, 100));
        cmd.Parameters.AddWithValue("$locked", memory.IsLockedPeak ? 1 : 0);
        cmd.Parameters.AddWithValue("$gameTime", time.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<MemoryRecord> GetMemories(string characterName)
    {
        int npcId = ResolveNpcId(characterName);
        return npcId <= 0 ? new List<MemoryRecord>() : GetMemories(npcId, 80);
    }

    public List<MemoryRecord> GetMemories(int npcId, int limit = 40)
    {
        var list = new List<MemoryRecord>();

        using var conn = new SqliteConnection(ConnStr);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, KnowerCharacterId, MemoryText, MemoryType, Importance,
                   Strength, IsLockedPeak, EventId, LearnedGameTime
            FROM PersonalMemories
            WHERE KnowerCharacterId = $id
            ORDER BY Importance DESC, CreatedRealAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var m = new MemoryRecord
            {
                NpcId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                CharacterName = ResolveNpcName(npcId),
                Summary = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Category = reader.IsDBNull(3) ? "General" : reader.GetString(3),
                Importance = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                Strength = reader.IsDBNull(5) ? 50f : Convert.ToSingle(reader.GetValue(5)),
                IsLockedPeak = !reader.IsDBNull(6) && reader.GetInt32(6) != 0,
                EventId = reader.IsDBNull(7) ? null : reader.GetString(7)
            };

            if (!reader.IsDBNull(8) && DateTime.TryParse(reader.GetString(8), out var ts))
                m.Timestamp = ts;

            list.Add(m);
        }

        return list;
    }

    public static int AutoImportance(SimCharacter npc, string summary, string category)
    {
        int importance = category.ToLowerInvariant() switch
        {
            "trauma" => 85,
            "peak" => 90,
            "romance" or "emotional" => 45,
            "negative" => 50,
            "positive" => 30,
            "social" => 20,
            "work" => 15,
            _ => 10
        };

        string s = (summary ?? "").ToLowerInvariant();
        if (s.Contains("love") || s.Contains("kiss")) importance += 20;
        if (s.Contains("fight") || s.Contains("hurt")) importance += 25;

        if (npc?.Traits != null)
        {
            if (npc.Traits.Get("trait.affection") >= 70) importance += 10;
            if (npc.Traits.Get("trait.hurt") >= 60) importance += 10;
        }

        return Math.Clamp(importance, 1, 100);
    }

    private static int ResolveNpcId(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return 0;

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id
            FROM Characters
            WHERE Name = $name OR Nickname = $name
            ORDER BY CASE WHEN Name = $name THEN 0 ELSE 1 END
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", characterName);

        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static string ResolveNpcName(int npcId)
    {
        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }
}
