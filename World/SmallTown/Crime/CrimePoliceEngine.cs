using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// CRIME / POLICE STATE ENGINE
    ///
    /// HumanEventEngine decides whether someone commits a crime.
    /// This system records the incident, witnesses, reports and police case state.
    ///
    /// IMPORTANT:
    /// An arrest is never automatic just because a crime event exists.
    /// Police action requires an actual reported/observed case and lawful basis.
    /// </summary>
    public static class CrimePoliceEngine
    {
        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS CrimeIncident (
                    CrimeId INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventId TEXT NOT NULL,
                    SuspectNpcId INTEGER,
                    VictimNpcId INTEGER,
                    LocationId TEXT,
                    GameTime TEXT NOT NULL,
                    Severity INTEGER NOT NULL,
                    Description TEXT,
                    Status TEXT NOT NULL DEFAULT 'unreported'
                );

                CREATE TABLE IF NOT EXISTS CrimeWitness (
                    CrimeId INTEGER NOT NULL,
                    WitnessNpcId INTEGER NOT NULL,
                    SawEnoughToIdentify INTEGER NOT NULL DEFAULT 0,
                    ReportedToPolice INTEGER NOT NULL DEFAULT 0,
                    Confidence REAL NOT NULL DEFAULT 1.0,
                    PRIMARY KEY(CrimeId, WitnessNpcId)
                );

                CREATE TABLE IF NOT EXISTS PoliceCase (
                    CaseId INTEGER PRIMARY KEY AUTOINCREMENT,
                    CrimeId INTEGER NOT NULL,
                    OpenedGameTime TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'open',
                    AssignedOfficerNpcId INTEGER,
                    ProbableCause INTEGER NOT NULL DEFAULT 0,
                    ArrestedNpcId INTEGER
                );
                """;
            cmd.ExecuteNonQuery();
        }

        public static long RecordCrime(
            string eventId,
            int? suspectNpcId,
            int? victimNpcId,
            string? locationId,
            DateTime gameTime,
            int severity,
            string? description = null)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CrimeIncident
                (EventId,SuspectNpcId,VictimNpcId,LocationId,GameTime,Severity,Description,Status)
                VALUES ($e,$s,$v,$l,$g,$sev,$d,'unreported');
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$e", eventId);
            cmd.Parameters.AddWithValue("$s", (object?)suspectNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$v", (object?)victimNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$l", locationId ?? "");
            cmd.Parameters.AddWithValue("$g", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$sev", Math.Clamp(severity, 1, 10));
            cmd.Parameters.AddWithValue("$d", description ?? "");
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        public static void AddWitness(
            long crimeId,
            int witnessNpcId,
            bool canIdentify,
            double confidence = 1.0)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CrimeWitness
                (CrimeId,WitnessNpcId,SawEnoughToIdentify,ReportedToPolice,Confidence)
                VALUES ($c,$w,$i,0,$conf)
                ON CONFLICT(CrimeId,WitnessNpcId) DO UPDATE SET
                    SawEnoughToIdentify=MAX(CrimeWitness.SawEnoughToIdentify,$i),
                    Confidence=MAX(CrimeWitness.Confidence,$conf);
                """;
            cmd.Parameters.AddWithValue("$c", crimeId);
            cmd.Parameters.AddWithValue("$w", witnessNpcId);
            cmd.Parameters.AddWithValue("$i", canIdentify ? 1 : 0);
            cmd.Parameters.AddWithValue("$conf", Math.Clamp(confidence, 0.0, 1.0));
            cmd.ExecuteNonQuery();
        }

        public static long ReportCrime(long crimeId, int reporterNpcId, DateTime gameTime)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using (var update = conn.CreateCommand())
            {
                update.CommandText = """
                    UPDATE CrimeWitness
                    SET ReportedToPolice=1
                    WHERE CrimeId=$c AND WitnessNpcId=$w;

                    UPDATE CrimeIncident
                    SET Status='reported'
                    WHERE CrimeId=$c;
                    """;
                update.Parameters.AddWithValue("$c", crimeId);
                update.Parameters.AddWithValue("$w", reporterNpcId);
                update.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PoliceCase
                (CrimeId,OpenedGameTime,Status,ProbableCause)
                VALUES ($c,$g,'open',0);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$c", crimeId);
            cmd.Parameters.AddWithValue("$g", gameTime.ToString("o"));
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        public static void SetProbableCause(long caseId, bool value)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE PoliceCase
                SET ProbableCause=$v
                WHERE CaseId=$id;
                """;
            cmd.Parameters.AddWithValue("$v", value ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", caseId);
            cmd.ExecuteNonQuery();
        }

        public static bool TryRecordArrest(long caseId, int suspectNpcId)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var check = conn.CreateCommand();
            check.CommandText = """
                SELECT ProbableCause
                FROM PoliceCase
                WHERE CaseId=$id AND Status='open';
                """;
            check.Parameters.AddWithValue("$id", caseId);

            object? value = check.ExecuteScalar();
            if (value == null || Convert.ToInt32(value) == 0)
                return false;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE PoliceCase
                SET ArrestedNpcId=$npc, Status='arrest_made'
                WHERE CaseId=$id;
                """;
            cmd.Parameters.AddWithValue("$npc", suspectNpcId);
            cmd.Parameters.AddWithValue("$id", caseId);
            cmd.ExecuteNonQuery();
            return true;
        }
    }
}
