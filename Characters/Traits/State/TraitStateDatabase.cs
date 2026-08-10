using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Traits.State;
using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectEve.Characters.Traits
{
    /// <summary>
    /// Optional row-level trait state API.
    /// Default DB = same folder as CharacterRepository (project_eve.db).
    /// Prefer CharacterRepository.SaveTraits for full bag save.
    /// </summary>
    public class TraitStateDatabase
    {
        private readonly string _dbPath;

        public TraitStateDatabase(string? dbPath = null)
        {
            _dbPath = dbPath
                ?? Path.Combine(AppContext.BaseDirectory, "project_eve.db");

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

            // Live bag (matches CharacterRepository)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS Traits (
                        NpcId INTEGER NOT NULL,
                        TraitId TEXT NOT NULL,
                        Value REAL NOT NULL,
                        PRIMARY KEY (NpcId, TraitId)
                    );
                    """;
                cmd.ExecuteNonQuery();
            }

            // Optional control side-table (expression regulation)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS TraitControl (
                        NpcId INTEGER NOT NULL,
                        TraitId TEXT NOT NULL,
                        Control INTEGER NOT NULL DEFAULT 50,
                        LastUpdated TEXT,
                        PRIMARY KEY (NpcId, TraitId)
                    );
                    """;
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Upsert intensity into Traits + optional Control.</summary>
        public void SaveTraitState(TraitState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.TraitId))
                return;

            if (state.NpcId <= 0 && !string.IsNullOrWhiteSpace(state.CharacterName))
            {
                // name-only legacy path not resolved here — set NpcId at call site
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Traits (NpcId, TraitId, Value)
                    VALUES ($id, $tid, $val)
                    ON CONFLICT(NpcId, TraitId) DO UPDATE SET Value = $val
                    """;
                cmd.Parameters.AddWithValue("$id", state.NpcId);
                cmd.Parameters.AddWithValue("$tid", state.TraitId);
                cmd.Parameters.AddWithValue("$val", (double)state.Intensity);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO TraitControl (NpcId, TraitId, Control, LastUpdated)
                    VALUES ($id, $tid, $ctl, $ts)
                    ON CONFLICT(NpcId, TraitId) DO UPDATE SET
                        Control = $ctl,
                        LastUpdated = $ts
                    """;
                cmd.Parameters.AddWithValue("$id", state.NpcId);
                cmd.Parameters.AddWithValue("$tid", state.TraitId);
                cmd.Parameters.AddWithValue("$ctl", state.Control);
                cmd.Parameters.AddWithValue("$ts", state.LastUpdated.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public TraitState? Load(int npcId, string traitId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT t.Value, COALESCE(c.Control, 50), c.LastUpdated
                FROM Traits t
                LEFT JOIN TraitControl c ON c.NpcId = t.NpcId AND c.TraitId = t.TraitId
                WHERE t.NpcId = $id AND t.TraitId = $tid
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            cmd.Parameters.AddWithValue("$tid", traitId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            var state = new TraitState
            {
                NpcId = npcId,
                TraitId = traitId,
                Intensity = (int)Math.Round(reader.GetDouble(0)),
                Control = reader.IsDBNull(1) ? 50 : reader.GetInt32(1),
                LastUpdated = DateTime.UtcNow
            };

            if (!reader.IsDBNull(2) &&
                DateTime.TryParse(reader.GetString(2), out var ts))
                state.LastUpdated = ts;

            return state;
        }

        public List<TraitState> LoadAllForNpc(int npcId)
        {
            var list = new List<TraitState>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT t.TraitId, t.Value, COALESCE(c.Control, 50)
                FROM Traits t
                LEFT JOIN TraitControl c ON c.NpcId = t.NpcId AND c.TraitId = t.TraitId
                WHERE t.NpcId = $id
                """;
            cmd.Parameters.AddWithValue("$id", npcId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TraitState
                {
                    NpcId = npcId,
                    TraitId = reader.GetString(0),
                    Intensity = (int)Math.Round(reader.GetDouble(1)),
                    Control = reader.IsDBNull(2) ? 50 : Convert.ToInt32(reader.GetValue(2)),
                    LastUpdated = DateTime.UtcNow
                });
            }
            return list;
        }
    }
}