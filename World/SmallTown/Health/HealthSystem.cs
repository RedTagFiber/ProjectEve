using Microsoft.Data.Sqlite;
using System;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Minimal persistent health/injury state.
    ///
    /// This does NOT invent diagnoses.
    /// Event/consequence context supplies the injury or medical fact.
    /// </summary>
    public static class HealthSystem
    {
        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcHealthState (
                    NpcId INTEGER PRIMARY KEY,
                    GeneralHealth INTEGER NOT NULL DEFAULT 75,
                    CurrentPain INTEGER NOT NULL DEFAULT 0,
                    IsHospitalized INTEGER NOT NULL DEFAULT 0,
                    LastHealthUpdateGameTime TEXT
                );

                CREATE TABLE IF NOT EXISTS HealthIncident (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    EventId TEXT NOT NULL,
                    Severity INTEGER NOT NULL,
                    Fact TEXT,
                    GameTime TEXT NOT NULL,
                    Resolved INTEGER NOT NULL DEFAULT 0
                );
                """;
            cmd.ExecuteNonQuery();
        }

        public static void EnsureNpc(int npcId, DateTime gameTime)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO NpcHealthState
                (NpcId,GeneralHealth,CurrentPain,IsHospitalized,LastHealthUpdateGameTime)
                VALUES ($npc,75,0,0,$t);
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$t", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public static long RecordIncident(
            int npcId,
            string eventId,
            int severity,
            string fact,
            DateTime gameTime,
            bool hospitalized = false)
        {
            EnsureNpc(npcId, gameTime);
            severity = Math.Clamp(severity, 1, 10);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            long id;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO HealthIncident
                    (NpcId,EventId,Severity,Fact,GameTime,Resolved)
                    VALUES ($npc,$e,$s,$f,$g,0);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("$npc", npcId);
                cmd.Parameters.AddWithValue("$e", eventId);
                cmd.Parameters.AddWithValue("$s", severity);
                cmd.Parameters.AddWithValue("$f", fact ?? "");
                cmd.Parameters.AddWithValue("$g", gameTime.ToString("o"));
                id = Convert.ToInt64(cmd.ExecuteScalar());
            }

            using (var update = conn.CreateCommand())
            {
                update.CommandText = """
                    UPDATE NpcHealthState
                    SET CurrentPain=MIN(100, CurrentPain + $pain),
                        GeneralHealth=MAX(0, GeneralHealth - $damage),
                        IsHospitalized=CASE WHEN $h=1 THEN 1 ELSE IsHospitalized END,
                        LastHealthUpdateGameTime=$g
                    WHERE NpcId=$npc;
                    """;
                update.Parameters.AddWithValue("$pain", severity * 7);
                update.Parameters.AddWithValue("$damage", severity * 3);
                update.Parameters.AddWithValue("$h", hospitalized ? 1 : 0);
                update.Parameters.AddWithValue("$g", gameTime.ToString("o"));
                update.Parameters.AddWithValue("$npc", npcId);
                update.ExecuteNonQuery();
            }

            return id;
        }

        public static void ResolveIncident(long incidentId, int npcId, DateTime gameTime)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE HealthIncident
                SET Resolved=1
                WHERE Id=$id;

                UPDATE NpcHealthState
                SET CurrentPain=MAX(0, CurrentPain - 20),
                    LastHealthUpdateGameTime=$g
                WHERE NpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$id", incidentId);
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$g", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }
}
