using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectEve.Memory
{
    public class MemoryDatabase
    {
        private readonly string _dbPath;

        public MemoryDatabase(string? dbPath = null)
        {
            _dbPath = dbPath
                ?? Environment.GetEnvironmentVariable("EVE_DB_PATH")
                ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

            EnsureSchema();
        }

        private string ConnStr => $"Data Source={_dbPath}";

        private void EnsureSchema()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Memories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL DEFAULT 0,
                    CharacterName TEXT,
                    Summary TEXT NOT NULL,
                    Category TEXT,
                    Importance INTEGER DEFAULT 1,
                    Strength REAL DEFAULT 70,
                    IsLockedPeak INTEGER DEFAULT 0,
                    RelatedPerson TEXT,
                    EventId TEXT,
                    Timestamp TEXT
                );
                """;
            cmd.ExecuteNonQuery();
        }

        public void AddMemory(MemoryRecord memory)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.Summary))
                return;

            if (memory.Strength <= 0)
                memory.Strength = Math.Clamp(40f + memory.Importance * 0.5f, 20f, 100f);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Memories
                (NpcId, CharacterName, Summary, Category, Importance, Strength, IsLockedPeak, RelatedPerson, EventId, Timestamp)
                VALUES ($npc, $name, $summary, $cat, $imp, $str, $lock, $rel, $eid, $ts)
                """;
            cmd.Parameters.AddWithValue("$npc", memory.NpcId);
            cmd.Parameters.AddWithValue("$name", memory.CharacterName ?? "");
            cmd.Parameters.AddWithValue("$summary", memory.Summary);
            cmd.Parameters.AddWithValue("$cat", memory.Category ?? "General");
            cmd.Parameters.AddWithValue("$imp", Math.Clamp(memory.Importance, 1, 100));
            cmd.Parameters.AddWithValue("$str", memory.Strength);
            cmd.Parameters.AddWithValue("$lock", memory.IsLockedPeak ? 1 : 0);
            cmd.Parameters.AddWithValue("$rel", (object?)memory.RelatedPerson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$eid", (object?)memory.EventId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ts", memory.Timestamp.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public List<MemoryRecord> GetMemories(string characterName)
        {
            var list = new List<MemoryRecord>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, NpcId, CharacterName, Summary, Category, Importance,
                       Strength, IsLockedPeak, RelatedPerson, EventId, Timestamp
                FROM Memories WHERE CharacterName = $name
                ORDER BY Importance DESC, Id DESC LIMIT 80
                """;
            cmd.Parameters.AddWithValue("$name", characterName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ReadRow(reader));
            return list;
        }

        public List<MemoryRecord> GetMemories(int npcId, int limit = 40)
        {
            var list = new List<MemoryRecord>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, NpcId, CharacterName, Summary, Category, Importance,
                       Strength, IsLockedPeak, RelatedPerson, EventId, Timestamp
                FROM Memories WHERE NpcId = $id
                ORDER BY Importance DESC, Id DESC LIMIT $lim
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            cmd.Parameters.AddWithValue("$lim", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ReadRow(reader));
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

        private static MemoryRecord ReadRow(SqliteDataReader reader)
        {
            var m = new MemoryRecord
            {
                Id = reader.GetInt32(0),
                NpcId = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                CharacterName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Summary = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Category = reader.IsDBNull(4) ? "General" : reader.GetString(4),
                Importance = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                Strength = reader.IsDBNull(6) ? 70f : Convert.ToSingle(reader.GetValue(6)),
                IsLockedPeak = !reader.IsDBNull(7) && Convert.ToInt32(reader.GetValue(7)) != 0,
                RelatedPerson = reader.IsDBNull(8) ? null : reader.GetString(8),
                EventId = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
            if (!reader.IsDBNull(10) && DateTime.TryParse(reader.GetString(10), out var ts))
                m.Timestamp = ts;
            return m;
        }
    }
}