using Microsoft.Data.Sqlite;
using ProjectEve.AI.Brain;
using ProjectEve.Characters.Emotion;
using ProjectEve.Money;
using ProjectEve.Relationships;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.Base
{
    /// <summary>
    /// Load / save NPCs from project_eve.db
    /// </summary>
    public static class CharacterRepository
    {
        // Prefer bin folder DB (same idea as running app). Change if your path differs.
        private static string DbPath =>
            Path.Combine(AppContext.BaseDirectory, "project_eve.db");

        private static string ConnStr => $"Data Source={DbPath}";

        // ============================================================
        // LOAD
        // ============================================================

        public static SimCharacter? LoadCharacter(int id)
        {
            if (!File.Exists(DbPath))
            {
                // No DB yet — fall back to in-code Eve seed for id 1
                if (id == 1)
                    return CreateFallbackEve();
                return null;
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, Age, Gender, Occupation, Location,
                       Goal, Need, Fear, Want, PersonalityContext
                FROM Characters
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                reader.Close();
                if (id == 1)
                    return CreateFallbackEve();
                return null;
            }

            int cid = reader.GetInt32(0);
            string name = reader.GetString(1);
            int age = reader.IsDBNull(2) ? 25 : reader.GetInt32(2);

            var npc = new SimCharacter(name, age)
            {
                Id = cid,
                Gender = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Occupation = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Location = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Goal = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Need = reader.IsDBNull(7) ? "" : reader.GetString(7),
                Fear = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Want = reader.IsDBNull(9) ? "" : reader.GetString(9),
                PersonalityContext = reader.IsDBNull(10) ? "" : reader.GetString(10)
            };

            // IMPORTANT: close reader before more queries on same connection
            reader.Close();

            LoadTraits(conn, npc);
            LoadBrainState(conn, npc);
            LoadRelationships(conn, npc);
            LoadMemories(conn, npc);

            if (npc.Brain == null)
                npc.Brain = new Brain();
            npc.Brain.Owner = npc;

            return npc;
        }

        // ============================================================
        // TRAITS
        // ============================================================

        private static void LoadTraits(SqliteConnection conn, SimCharacter npc)
        {
            if (npc.Traits == null)
                npc.Traits = new NpcTraits();

            npc.Traits.InitializeFromRegistry();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TraitId, Value FROM Traits WHERE NpcId = $id
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);

            using var reader = cmd.ExecuteReader();
            bool any = false;
            while (reader.Read())
            {
                any = true;
                string traitId = reader.GetString(0);
                float value = (float)reader.GetDouble(1);
                npc.Traits.Set(traitId, value);
            }
            reader.Close();

            // If DB had no trait rows for Eve, keep registry defaults
            // (or rely on CreateFallbackEve when whole character missing)
            _ = any;
        }

        public static void SaveTraits(int npcId, NpcTraits traits)
        {
            if (traits == null)
                return;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var tx = conn.BeginTransaction();

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM Traits WHERE NpcId = $id";
                del.Parameters.AddWithValue("$id", npcId);
                del.ExecuteNonQuery();
            }

            foreach (var kv in traits.GetAll())
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO Traits (NpcId, TraitId, Value)
                    VALUES ($id, $tid, $val)
                    """;
                ins.Parameters.AddWithValue("$id", npcId);
                ins.Parameters.AddWithValue("$tid", kv.Key);
                ins.Parameters.AddWithValue("$val", kv.Value);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // ============================================================
        // BRAIN STATE
        // ============================================================

        private static void LoadBrainState(SqliteConnection conn, SimCharacter npc)
        {
            if (npc.Brain == null)
                npc.Brain = new Brain();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Mood, Stress, Energy, Affection, Attraction, Trust, Tension, LastThought
                FROM BrainState WHERE NpcId = $id
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                reader.Close();
                return;
            }

            npc.Brain.Mood = (float)reader.GetDouble(0);
            npc.Brain.Stress = (float)reader.GetDouble(1);
            npc.Brain.Energy = (float)reader.GetDouble(2);
            npc.Brain.Affection = (float)reader.GetDouble(3);
            npc.Brain.Attraction = (float)reader.GetDouble(4);
            npc.Brain.Trust = (float)reader.GetDouble(5);
            npc.Brain.Tension = (float)reader.GetDouble(6);
            // LastThought is set by Think; skip forcing private setter if any
            reader.Close();
        }

        public static void SaveBrainState(SimCharacter npc)
        {
            if (npc?.Brain == null)
                return;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BrainState
                (NpcId, Mood, Stress, Energy, Affection, Attraction, Trust, Tension, LastThought)
                VALUES ($id, $mood, $stress, $energy, $aff, $attr, $trust, $ten, $thought)
                ON CONFLICT(NpcId) DO UPDATE SET
                    Mood = $mood,
                    Stress = $stress,
                    Energy = $energy,
                    Affection = $aff,
                    Attraction = $attr,
                    Trust = $trust,
                    Tension = $ten,
                    LastThought = $thought
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$mood", npc.Brain.Mood);
            cmd.Parameters.AddWithValue("$stress", npc.Brain.Stress);
            cmd.Parameters.AddWithValue("$energy", npc.Brain.Energy);
            cmd.Parameters.AddWithValue("$aff", npc.Brain.Affection);
            cmd.Parameters.AddWithValue("$attr", npc.Brain.Attraction);
            cmd.Parameters.AddWithValue("$trust", npc.Brain.Trust);
            cmd.Parameters.AddWithValue("$ten", npc.Brain.Tension);
            cmd.Parameters.AddWithValue("$thought", (object?)npc.Brain.LastThought ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // RELATIONSHIPS / MEMORIES (light load)
        // ============================================================

        private static void LoadRelationships(SqliteConnection conn, SimCharacter npc)
        {
            npc.Relationships ??= new List<Relationship>();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TargetName, Trust, Respect, Affection, Attraction
                FROM Relationships WHERE NpcId = $id
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                npc.Relationships.Add(new Relationship
                {
                    TargetName = reader.GetString(0),
                    Trust = (int)reader.GetDouble(1),
                    Respect = (int)reader.GetDouble(2),
                    Affection = (int)reader.GetDouble(3),
                    Attraction = (int)reader.GetDouble(4)
                });
            }
            reader.Close();
        }

        private static void LoadMemories(SqliteConnection conn, SimCharacter npc)
        {
            // If your SimCharacter.Remember API differs, adjust here.
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Summary, Category, Importance
                FROM Memories WHERE NpcId = $id
                ORDER BY Importance DESC
                LIMIT 40
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string summary = reader.GetString(0);
                string category = reader.IsDBNull(1) ? "General" : reader.GetString(1);
                int importance = reader.IsDBNull(2) ? 1 : reader.GetInt32(2);
                try
                {
                    npc.Remember(summary, category, importance);
                }
                catch
                {
                    // Remember signature may differ — ignore until matched
                }
            }
            reader.Close();
        }

        // ============================================================
        // FALLBACK EVE (no DB / missing row)
        // ============================================================

        private static SimCharacter CreateFallbackEve()
        {
            // Uses your hand-seeded Eve class
            var eve = new ProjectEve.Characters.NPCs.Eve();
            if (eve.Id == 0)
                eve.Id = 1;
            if (eve.Brain == null)
                eve.Brain = new Brain();
            eve.Brain.Owner = eve;
            return eve;
        }

        // ============================================================
        // SHEET (old printer — kept so `sheet` still works)
        // ============================================================

        public static void PrintCharacterSheet(SimCharacter eve)
        {
            void Section(string title)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("=== " + title + " ===");
                Console.ResetColor();
            }

            void Line(string label, string? value)
            {
                Console.WriteLine($"{label,-16}: {value ?? ""}");
            }

            Console.WriteLine("================================");
            Console.WriteLine(" CHARACTER SHEET");
            Console.WriteLine("================================");

            Section("IDENTITY");
            Line("Id", eve.Id.ToString());
            Line("Name", eve.Name);
            Line("Age", eve.Age.ToString());
            Line("Gender", eve.Gender);
            Line("Occupation", eve.Occupation);
            Line("Location", eve.Location);

            Section("DRIVES");
            Line("Goal", eve.Goal);
            Line("Need", eve.Need);
            Line("Fear", eve.Fear);
            Line("Want", eve.Want);

            Section("MONEY");
            if (eve.Money == null)
            {
                Console.WriteLine("(no money profile)");
            }
            else
            {
                Line("Cash", eve.Money.Cash.ToString("0.00"));
                Line("Bank", eve.Money.Bank.ToString("0.00"));
                Line("Debt", eve.Money.Debt.ToString("0.00"));
                Line("Pressure", eve.Money.PressureLabel());
            }

            Section("BRAIN");
            if (eve.Brain != null)
            {
                Line("Mood", eve.Brain.Mood.ToString("0.00"));
                Line("Stress", eve.Brain.Stress.ToString("0.00"));
                Line("Energy", eve.Brain.Energy.ToString("0.00"));
                Line("Affection", eve.Brain.Affection.ToString("0.00"));
            }

            Section("TRAITS (extremes)");
            if (eve.Traits != null)
            {
                foreach (var (id, val, _) in eve.Traits.GetMostExtreme(12))
                    Console.WriteLine($"  {id}: {val:0}");
            }

            Console.WriteLine();
        }
    }
}