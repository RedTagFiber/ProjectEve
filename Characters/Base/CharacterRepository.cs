using Microsoft.Data.Sqlite;
using ProjectEve.AI.Brain;
using ProjectEve.Money;
using ProjectEve.Relationships;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.IO;
using ProjectEve.Characters.NPCs;
using ProjectEve.Characters.NPCs.Body;
using System.Text.Json;
using ProjectEve.Characters.Cognition;

namespace ProjectEve.Characters.Base
{
    public static class CharacterRepository
    {
        private static string DbPath
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("EVE_DB_PATH");
                if (!string.IsNullOrWhiteSpace(env))
                    return env;
                return Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");
            }
        }

        private static string ConnStr => $"Data Source={DbPath}";

        public static SimCharacter? LoadCharacter(int id)
        {
            if (!File.Exists(DbPath))
            {
                if (id == 1) return CreateFallbackEve();
                return null;
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureJobProfileColumns(conn);
            EnsureBodyProfileTable(conn);
            EnsureCognitionTable(conn);

            SimCharacter? npc = TryLoadCharacterWide(conn, id)
                             ?? TryLoadCharacterNarrow(conn, id);

            if (npc == null)
            {
                if (id == 1) return CreateFallbackEve();
                return null;
            }

            LoadTraits(conn, npc);
            LoadBrainState(conn, npc);
            LoadMoney(conn, npc);
            LoadJob(conn, npc);
            LoadCognition(conn, npc);
            LoadRelationships(conn, npc);
            LoadOrCreateBodyProfile(conn, npc);

            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            return npc;
        }

        private static SimCharacter? TryLoadCharacterWide(SqliteConnection conn, int id)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, Name, Age, Gender, Occupation, Location,
                           Goal, Need, Fear, Want, PersonalityContext,
                           BirthYear, BirthMonth, BirthDay, BirthHour,
                           Zodiac, HeightCm, WeightKg, BodyShape,
                           HairColor, HairStyle, EyeColor, EyeStyle,
                           SkinTone, Glasses, ScarNotes, Hometown, Address, Tier
                    FROM Characters
                    WHERE Id = $id
                    """;
                cmd.Parameters.AddWithValue("$id", id);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;

                var npc = ReadIdentityCore(reader);

                int? by = GetNullableInt(reader, 11);
                int? bm = GetNullableInt(reader, 12);
                int? bd = GetNullableInt(reader, 13);
                int? bh = GetNullableInt(reader, 14);

                if (by is > 0 && bm is >= 1 and <= 12 && bd is >= 1 and <= 31)
                {
                    try
                    {
                        var birth = new DateTime(
                            by.Value, bm.Value, bd.Value,
                            bh is >= 0 and <= 23 ? bh.Value : 12, 0, 0);
                        SetBirthDate(npc, birth);
                        if (npc.Age <= 0)
                            npc.Age = CalculateAge(birth);
                    }
                    catch { }
                }

                npc.Zodiac = GetString(reader, 15);
                TrySet(npc, "HeightCm", GetNullableInt(reader, 16));
                TrySet(npc, "WeightKg", GetNullableInt(reader, 17));
                npc.BodyShape = GetString(reader, 18);
                npc.HairColor = GetString(reader, 19);
                npc.HairStyle = GetString(reader, 20);
                npc.EyeColor = GetString(reader, 21);
                TrySet(npc, "EyeStyle", GetString(reader, 22));
                npc.SkinTone = GetString(reader, 23);
                TrySet(npc, "Glasses", GetString(reader, 24));
                TrySet(npc, "ScarNotes", GetString(reader, 25));
                npc.Hometown = GetString(reader, 26);
                npc.HomeAddress = GetString(reader, 27);
                if (!reader.IsDBNull(28))
                    npc.Tier = reader.GetInt32(28);

                return npc;
            }
            catch { return null; }
        }

        private static SimCharacter? TryLoadCharacterNarrow(SqliteConnection conn, int id)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, Name, Age, Gender, Occupation, Location,
                           Goal, Need, Fear, Want, PersonalityContext
                    FROM Characters
                    WHERE Id = $id
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return ReadIdentityCore(reader);
            }
            catch { return null; }
        }

        private static SimCharacter ReadIdentityCore(SqliteDataReader reader)
        {
            return new SimCharacter(reader.GetString(1), reader.IsDBNull(2) ? 25 : reader.GetInt32(2))
            {
                Id = reader.GetInt32(0),
                Gender = GetString(reader, 3),
                Occupation = GetString(reader, 4),
                Location = GetString(reader, 5),
                Goal = GetString(reader, 6),
                Need = GetString(reader, 7),
                Fear = GetString(reader, 8),
                Want = GetString(reader, 9),
                PersonalityContext = GetString(reader, 10)
            };
        }

        // ============================================================
        // TRAITS
        // ============================================================
        private static void LoadTraits(SqliteConnection conn, SimCharacter npc)
        {
            npc.Traits ??= new NpcTraits();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT TraitId, Value FROM Traits WHERE NpcId = $id";
                cmd.Parameters.AddWithValue("$id", npc.Id);

                var loaded = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        loaded[reader.GetString(0)] = (float)reader.GetDouble(1);
                }

                if (loaded.Count == 0) return;

                var fast = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in loaded)
                {
                    if (kv.Key.StartsWith("mid.", StringComparison.OrdinalIgnoreCase))
                        mid[kv.Key] = kv.Value;
                    else if (kv.Key.StartsWith("slow.", StringComparison.OrdinalIgnoreCase))
                        slow[kv.Key] = kv.Value;
                    else
                        fast[kv.Key] = kv.Value;
                }

                if (fast.Count == 0)
                    fast = TraitJsonLoader.BuildFastDefaults(45f);

                npc.Traits.InitializeFromLayers(fast, mid, slow);
            }
            catch { }
        }

        public static void SaveTraits(int npcId, NpcTraits traits)
        {
            if (traits == null) return;
            EnsureDataDir();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using (var create = conn.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS Traits (
                        NpcId INTEGER NOT NULL,
                        TraitId TEXT NOT NULL,
                        Value REAL NOT NULL DEFAULT 50,
                        PRIMARY KEY (NpcId, TraitId)
                    );
                    """;
                create.ExecuteNonQuery();
            }

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
                ins.CommandText = "INSERT INTO Traits (NpcId, TraitId, Value) VALUES ($id, $tid, $val)";
                ins.Parameters.AddWithValue("$id", npcId);
                ins.Parameters.AddWithValue("$tid", kv.Key);
                ins.Parameters.AddWithValue("$val", kv.Value);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // ============================================================
        // BRAIN
        // ============================================================
        private static void LoadBrainState(SqliteConnection conn, SimCharacter npc)
        {
            npc.Brain ??= new Brain();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Mood, Stress, Energy, Affection, Attraction, Trust, Tension
                    FROM BrainState WHERE NpcId = $id
                    """;
                cmd.Parameters.AddWithValue("$id", npc.Id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return;
                npc.Brain.Mood = (float)reader.GetDouble(0);
                npc.Brain.Stress = (float)reader.GetDouble(1);
                npc.Brain.Energy = (float)reader.GetDouble(2);
                npc.Brain.Affection = (float)reader.GetDouble(3);
                npc.Brain.Attraction = (float)reader.GetDouble(4);
                npc.Brain.Trust = (float)reader.GetDouble(5);
                npc.Brain.Tension = (float)reader.GetDouble(6);
            }
            catch { }
        }

        public static void SaveBrainState(SimCharacter npc)
        {
            if (npc?.Brain == null) return;
            EnsureDataDir();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using (var create = conn.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS BrainState (
                        NpcId INTEGER PRIMARY KEY,
                        Mood REAL, Stress REAL, Energy REAL,
                        Affection REAL, Attraction REAL, Trust REAL, Tension REAL,
                        LastThought TEXT
                    );
                    """;
                create.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BrainState
                (NpcId, Mood, Stress, Energy, Affection, Attraction, Trust, Tension, LastThought)
                VALUES ($id, $mood, $stress, $energy, $aff, $attr, $trust, $ten, $thought)
                ON CONFLICT(NpcId) DO UPDATE SET
                    Mood=$mood, Stress=$stress, Energy=$energy,
                    Affection=$aff, Attraction=$attr, Trust=$trust,
                    Tension=$ten, LastThought=$thought
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
        // MONEY
        // ============================================================
        private static void LoadMoney(SqliteConnection conn, SimCharacter npc)
        {
            npc.Money ??= new MoneyProfile();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Cash, Bank, Debt FROM MoneyProfile WHERE NpcId = $id";
                cmd.Parameters.AddWithValue("$id", npc.Id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return;
                npc.Money.Cash = reader.IsDBNull(0) ? 0m : (decimal)reader.GetDouble(0);
                npc.Money.Bank = reader.IsDBNull(1) ? 0m : (decimal)reader.GetDouble(1);
                npc.Money.Debt = reader.IsDBNull(2) ? 0m : (decimal)reader.GetDouble(2);
            }
            catch { }
        }

        public static void SaveMoney(SimCharacter npc)
        {
            if (npc?.Money == null) return;
            EnsureDataDir();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using (var create = conn.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS MoneyProfile (
                        NpcId INTEGER PRIMARY KEY,
                        Cash REAL, Bank REAL, Debt REAL
                    );
                    """;
                create.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO MoneyProfile (NpcId, Cash, Bank, Debt)
                VALUES ($id, $cash, $bank, $debt)
                ON CONFLICT(NpcId) DO UPDATE SET Cash=$cash, Bank=$bank, Debt=$debt
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$cash", (double)npc.Money.Cash);
            cmd.Parameters.AddWithValue("$bank", (double)npc.Money.Bank);
            cmd.Parameters.AddWithValue("$debt", (double)npc.Money.Debt);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // JOB — full schema + migrate legacy Title/Shift/PayRate
        // ============================================================
        private static void EnsureJobProfileColumns(SqliteConnection conn)
        {
            // Base table (may already exist as legacy)
            using (var create = conn.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS JobProfile (
                        NpcId INTEGER PRIMARY KEY,
                        JobName TEXT,
                        Employer TEXT,
                        JobType TEXT,
                        IndustryPath TEXT,
                        Department TEXT,
                        TitleLevel TEXT,
                        StartHour INTEGER,
                        EndHour INTEGER,
                        ShiftType TEXT,
                        WorkLocationMode TEXT,
                        HourlyRate REAL,
                        WeeklyHours REAL,
                        IsSalaried INTEGER,
                        AnnualSalary REAL,
                        StressLoad INTEGER,
                        SocialDemand INTEGER,
                        PhysicalDemand INTEGER,
                        CognitiveDemand INTEGER,
                        BurnoutAccum INTEGER,
                        HasInsurance INTEGER,
                        BossName TEXT,
                        BossRelationship TEXT,
                        TeamClimate TEXT
                    );
                    """;
                create.ExecuteNonQuery();
            }

            string[] alters =
            {
                "ALTER TABLE JobProfile ADD COLUMN JobName TEXT",
                "ALTER TABLE JobProfile ADD COLUMN Employer TEXT",
                "ALTER TABLE JobProfile ADD COLUMN JobType TEXT",
                "ALTER TABLE JobProfile ADD COLUMN IndustryPath TEXT",
                "ALTER TABLE JobProfile ADD COLUMN Department TEXT",
                "ALTER TABLE JobProfile ADD COLUMN TitleLevel TEXT",
                "ALTER TABLE JobProfile ADD COLUMN StartHour INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN EndHour INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN ShiftType TEXT",
                "ALTER TABLE JobProfile ADD COLUMN WorkLocationMode TEXT",
                "ALTER TABLE JobProfile ADD COLUMN HourlyRate REAL",
                "ALTER TABLE JobProfile ADD COLUMN WeeklyHours REAL",
                "ALTER TABLE JobProfile ADD COLUMN IsSalaried INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN AnnualSalary REAL",
                "ALTER TABLE JobProfile ADD COLUMN StressLoad INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN SocialDemand INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN PhysicalDemand INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN CognitiveDemand INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN BurnoutAccum INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN HasInsurance INTEGER",
                "ALTER TABLE JobProfile ADD COLUMN BossName TEXT",
                "ALTER TABLE JobProfile ADD COLUMN BossRelationship TEXT",
                "ALTER TABLE JobProfile ADD COLUMN TeamClimate TEXT",
                // keep legacy columns readable if present
                "ALTER TABLE JobProfile ADD COLUMN Title TEXT",
                "ALTER TABLE JobProfile ADD COLUMN Shift TEXT",
                "ALTER TABLE JobProfile ADD COLUMN PayRate REAL"
            };

            foreach (var sql in alters)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch { /* already exists */ }
            }

            // One-time bridge: legacy Title/Shift/PayRate → modern fields
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE JobProfile SET JobName = Title
                    WHERE (JobName IS NULL OR JobName = '') AND Title IS NOT NULL AND Title != '';
                    UPDATE JobProfile SET ShiftType = Shift
                    WHERE (ShiftType IS NULL OR ShiftType = '') AND Shift IS NOT NULL AND Shift != '';
                    UPDATE JobProfile SET HourlyRate = PayRate
                    WHERE (HourlyRate IS NULL OR HourlyRate = 0) AND PayRate IS NOT NULL AND PayRate != 0;
                    """;
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private static void LoadJob(SqliteConnection conn, SimCharacter npc)
        {
            npc.Job ??= new JobProfile();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT JobName, Employer, JobType, IndustryPath,
                           StartHour, EndHour, ShiftType, WorkLocationMode,
                           HourlyRate, WeeklyHours, IsSalaried, AnnualSalary,
                           StressLoad, SocialDemand, PhysicalDemand, CognitiveDemand, BurnoutAccum,
                           HasInsurance, BossName, BossRelationship, TeamClimate
                    FROM JobProfile WHERE NpcId = $id
                    """;
                cmd.Parameters.AddWithValue("$id", npc.Id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    LoadJobLegacy(conn, npc);
                    return;
                }

                npc.Job.JobName = GetString(reader, 0);
                npc.Job.Employer = GetString(reader, 1);
                npc.Job.JobType = GetString(reader, 2);
                npc.Job.IndustryPath = GetString(reader, 3);
                if (!reader.IsDBNull(4)) npc.Job.StartHour = reader.GetInt32(4);
                if (!reader.IsDBNull(5)) npc.Job.EndHour = reader.GetInt32(5);
                var shift = GetString(reader, 6);
                npc.Job.ShiftType = string.IsNullOrWhiteSpace(shift) ? "days" : shift;
                npc.Job.WorkLocationMode = GetString(reader, 7);
                if (!reader.IsDBNull(8)) npc.Job.HourlyRate = (decimal)reader.GetDouble(8);
                if (!reader.IsDBNull(9)) npc.Job.WeeklyHours = (decimal)reader.GetDouble(9);
                if (!reader.IsDBNull(10)) npc.Job.IsSalaried = reader.GetInt32(10) != 0;
                if (!reader.IsDBNull(11)) npc.Job.AnnualSalary = (decimal)reader.GetDouble(11);
                if (!reader.IsDBNull(12)) npc.Job.StressLoad = reader.GetInt32(12);
                if (!reader.IsDBNull(13)) npc.Job.SocialDemand = reader.GetInt32(13);
                if (!reader.IsDBNull(14)) npc.Job.PhysicalDemand = reader.GetInt32(14);
                if (!reader.IsDBNull(15)) npc.Job.CognitiveDemand = reader.GetInt32(15);
                if (!reader.IsDBNull(16)) npc.Job.BurnoutAccum = reader.GetInt32(16);
                if (!reader.IsDBNull(17)) npc.Job.HasInsurance = reader.GetInt32(17) != 0;
                npc.Job.BossName = GetString(reader, 18);
                npc.Job.BossRelationship = GetString(reader, 19);
                npc.Job.TeamClimate = GetString(reader, 20);

                if (string.IsNullOrWhiteSpace(npc.Job.JobName))
                    LoadJobLegacy(conn, npc);
            }
            catch { LoadJobLegacy(conn, npc); }
        }

        private static void LoadJobLegacy(SqliteConnection conn, SimCharacter npc)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Title, Employer, Shift, PayRate FROM JobProfile WHERE NpcId = $id";
                cmd.Parameters.AddWithValue("$id", npc.Id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return;
                var title = GetString(reader, 0);
                if (!string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(npc.Job.JobName))
                    npc.Job.JobName = title;
                if (string.IsNullOrWhiteSpace(npc.Job.Employer))
                    npc.Job.Employer = GetString(reader, 1);
                var shift = GetString(reader, 2);
                if (!string.IsNullOrWhiteSpace(shift) && string.IsNullOrWhiteSpace(npc.Job.ShiftType))
                    npc.Job.ShiftType = shift;
                if (!reader.IsDBNull(3) && npc.Job.HourlyRate <= 0)
                    npc.Job.HourlyRate = (decimal)reader.GetDouble(3);
            }
            catch { }
        }

        public static void SaveJob(SimCharacter npc)
        {
            if (npc?.Job == null) return;
            EnsureDataDir();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureJobProfileColumns(conn);

            var j = npc.Job;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO JobProfile (
                    NpcId, JobName, Employer, JobType, IndustryPath,
                    StartHour, EndHour, ShiftType, WorkLocationMode,
                    HourlyRate, WeeklyHours, IsSalaried, AnnualSalary,
                    StressLoad, SocialDemand, PhysicalDemand, CognitiveDemand, BurnoutAccum,
                    HasInsurance, BossName, BossRelationship, TeamClimate,
                    Title, Shift, PayRate
                ) VALUES (
                    $id, $name, $emp, $type, $ind,
                    $start, $end, $shift, $loc,
                    $rate, $hours, $sal, $annual,
                    $stress, $social, $phys, $cog, $burn,
                    $ins, $boss, $bossRel, $team,
                    $name, $shift, $rate
                )
                ON CONFLICT(NpcId) DO UPDATE SET
                    JobName=$name, Employer=$emp, JobType=$type, IndustryPath=$ind,
                    StartHour=$start, EndHour=$end, ShiftType=$shift, WorkLocationMode=$loc,
                    HourlyRate=$rate, WeeklyHours=$hours, IsSalaried=$sal, AnnualSalary=$annual,
                    StressLoad=$stress, SocialDemand=$social, PhysicalDemand=$phys,
                    CognitiveDemand=$cog, BurnoutAccum=$burn,
                    HasInsurance=$ins, BossName=$boss, BossRelationship=$bossRel, TeamClimate=$team,
                    Title=$name, Shift=$shift, PayRate=$rate
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$name", j.JobName ?? "");
            cmd.Parameters.AddWithValue("$emp", j.Employer ?? "");
            cmd.Parameters.AddWithValue("$type", j.JobType ?? "");
            cmd.Parameters.AddWithValue("$ind", j.IndustryPath ?? "");
            cmd.Parameters.AddWithValue("$start", j.StartHour);
            cmd.Parameters.AddWithValue("$end", j.EndHour);
            cmd.Parameters.AddWithValue("$shift", j.ShiftType ?? "days");
            cmd.Parameters.AddWithValue("$loc", j.WorkLocationMode ?? "office");
            cmd.Parameters.AddWithValue("$rate", (double)j.HourlyRate);
            cmd.Parameters.AddWithValue("$hours", (double)j.WeeklyHours);
            cmd.Parameters.AddWithValue("$sal", j.IsSalaried ? 1 : 0);
            cmd.Parameters.AddWithValue("$annual", (double)j.AnnualSalary);
            cmd.Parameters.AddWithValue("$stress", j.StressLoad);
            cmd.Parameters.AddWithValue("$social", j.SocialDemand);
            cmd.Parameters.AddWithValue("$phys", j.PhysicalDemand);
            cmd.Parameters.AddWithValue("$cog", j.CognitiveDemand);
            cmd.Parameters.AddWithValue("$burn", j.BurnoutAccum);
            cmd.Parameters.AddWithValue("$ins", j.HasInsurance ? 1 : 0);
            cmd.Parameters.AddWithValue("$boss", j.BossName ?? "");
            cmd.Parameters.AddWithValue("$bossRel", j.BossRelationship ?? "neutral");
            cmd.Parameters.AddWithValue("$team", j.TeamClimate ?? "cordial");
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // COGNITION — stable profile persisted as JSON by NpcId
        // ============================================================
        private static void EnsureCognitionTable(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcCognitionProfile (
                    NpcId INTEGER PRIMARY KEY,
                    ProfileJson TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        private static void LoadCognition(SqliteConnection conn, SimCharacter npc)
        {
            if (npc == null) return;
            EnsureCognitionTable(conn);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT ProfileJson
                    FROM NpcCognitionProfile
                    WHERE NpcId = $id
                    """;
                cmd.Parameters.AddWithValue("$id", npc.Id);

                object? value = cmd.ExecuteScalar();
                if (value is not string json || string.IsNullOrWhiteSpace(json))
                    return;

                var loaded = JsonSerializer.Deserialize<CognitiveProfile>(json);
                if (loaded != null)
                    npc.Cognition = loaded;
            }
            catch
            {
                // Leave empty/default profile; CharacterFactory.EnsureCognition
                // will generate it once and SaveCognition will persist it.
            }
        }

        public static void SaveCognition(SimCharacter npc)
        {
            if (npc?.Cognition == null || npc.Id <= 0) return;

            EnsureDataDir();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureCognitionTable(conn);

            string json = JsonSerializer.Serialize(npc.Cognition);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NpcCognitionProfile
                    (NpcId, ProfileJson, UpdatedUtc)
                VALUES
                    ($id, $json, $utc)
                ON CONFLICT(NpcId) DO UPDATE SET
                    ProfileJson=$json,
                    UpdatedUtc=$utc
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // BODY / APPEARANCE — persistent JSON snapshot by NpcId
        // ============================================================
        private static readonly JsonSerializerOptions BodyJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private static void EnsureBodyProfileTable(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcBodyProfile (
                    NpcId INTEGER PRIMARY KEY,
                    SchemaVersion TEXT NOT NULL DEFAULT '1.0',
                    AppearanceJson TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        private static void LoadOrCreateBodyProfile(SqliteConnection conn, SimCharacter npc)
        {
            if (npc == null) return;
            EnsureBodyProfileTable(conn);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT AppearanceJson
                    FROM NpcBodyProfile
                    WHERE NpcId = $id
                    """;
                cmd.Parameters.AddWithValue("$id", npc.Id);

                var jsonObj = cmd.ExecuteScalar();
                if (jsonObj is string json && !string.IsNullOrWhiteSpace(json))
                {
                    var loaded = JsonSerializer.Deserialize<NPCAppearance>(json, BodyJsonOptions);
                    if (loaded != null)
                    {
                        npc.Appearance = loaded;
                        npc.Appearance.Body ??= new HumanBodyProfile();

                        npc.Appearance.Age = npc.Age;
                        if (string.IsNullOrWhiteSpace(npc.Appearance.Gender) ||
                            npc.Appearance.Gender.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                            npc.Appearance.Gender = npc.Gender;

                        SyncAppearanceFacadeToCharacter(npc);
                        npc.Appearance.SyncLegacyToBody();
                        return;
                    }
                }
            }
            catch
            {
                // Fall through to one-time migration.
            }

            npc.Appearance ??= new NPCAppearance();
            var a = npc.Appearance;

            a.Age = npc.Age;
            a.Gender = string.IsNullOrWhiteSpace(npc.Gender) ? "Unknown" : npc.Gender;

            if (string.IsNullOrWhiteSpace(a.HairColor) || a.HairColor == "Unknown")
                a.HairColor = EmptyToUnknown(npc.HairColor);
            if (string.IsNullOrWhiteSpace(a.HairStyle) || a.HairStyle == "Unknown")
                a.HairStyle = EmptyToUnknown(npc.HairStyle);
            if (string.IsNullOrWhiteSpace(a.EyeColor) || a.EyeColor == "Unknown")
                a.EyeColor = EmptyToUnknown(npc.EyeColor);
            if (string.IsNullOrWhiteSpace(a.EyeStyle) || a.EyeStyle == "Unknown")
                a.EyeStyle = EmptyToUnknown(npc.EyeStyle);
            if (string.IsNullOrWhiteSpace(a.SkinTone) || a.SkinTone == "Unknown")
                a.SkinTone = EmptyToUnknown(npc.SkinTone);
            if (string.IsNullOrWhiteSpace(a.BodyType) || a.BodyType == "Unknown")
                a.BodyType = EmptyToUnknown(npc.BodyShape);

            if (a.HeightCm <= 0 && npc.HeightCm.HasValue)
                a.HeightCm = npc.HeightCm.Value;
            if (a.WeightKg <= 0 && npc.WeightKg.HasValue)
                a.WeightKg = npc.WeightKg.Value;

            if (string.IsNullOrWhiteSpace(a.Glasses) || a.Glasses == "none")
            {
                if (!string.IsNullOrWhiteSpace(npc.Glasses))
                    a.Glasses = npc.Glasses;
            }

            if (string.IsNullOrWhiteSpace(a.ScarNotes) && !string.IsNullOrWhiteSpace(npc.ScarNotes))
                a.ScarNotes = npc.ScarNotes;

            a.Body ??= new HumanBodyProfile();
            a.SyncLegacyToBody();

            // Generate deeper body values ONCE for legacy NPCs.
            BodyGenerator.FillMissing(a.Body, npc.Gender, npc.Age, a.Race);

            SaveBodyProfile(conn, npc);
            SyncAppearanceFacadeToCharacter(npc);
        }

        public static void SaveBodyProfile(SimCharacter npc)
        {
            if (npc == null) return;
            EnsureDataDir();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureBodyProfileTable(conn);
            SaveBodyProfile(conn, npc);
        }

        private static void SaveBodyProfile(SqliteConnection conn, SimCharacter npc)
        {
            if (npc == null) return;

            npc.Appearance ??= new NPCAppearance
            {
                Age = npc.Age,
                Gender = npc.Gender
            };

            npc.Appearance.Body ??= new HumanBodyProfile();
            npc.Appearance.Age = npc.Age;

            if (string.IsNullOrWhiteSpace(npc.Appearance.Gender) ||
                npc.Appearance.Gender.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                npc.Appearance.Gender = npc.Gender;

            npc.Appearance.SyncLegacyToBody();

            string json = JsonSerializer.Serialize(npc.Appearance, BodyJsonOptions);
            string schema = string.IsNullOrWhiteSpace(npc.Appearance.Body.SchemaVersion)
                ? "1.0"
                : npc.Appearance.Body.SchemaVersion;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NpcBodyProfile
                    (NpcId, SchemaVersion, AppearanceJson, UpdatedUtc)
                VALUES
                    ($id, $schema, $json, $utc)
                ON CONFLICT(NpcId) DO UPDATE SET
                    SchemaVersion=$schema,
                    AppearanceJson=$json,
                    UpdatedUtc=$utc
                """;
            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$schema", schema);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            SyncAppearanceFacadeToCharacter(npc);
        }

        private static void SyncAppearanceFacadeToCharacter(SimCharacter npc)
        {
            if (npc?.Appearance == null) return;
            var a = npc.Appearance;

            if (a.HeightCm > 0) npc.HeightCm = a.HeightCm;
            if (a.WeightKg > 0) npc.WeightKg = a.WeightKg;

            if (!string.IsNullOrWhiteSpace(a.BodyType) && a.BodyType != "Unknown")
                npc.BodyShape = a.BodyType;
            if (!string.IsNullOrWhiteSpace(a.HairColor) && a.HairColor != "Unknown")
                npc.HairColor = a.HairColor;
            if (!string.IsNullOrWhiteSpace(a.HairStyle) && a.HairStyle != "Unknown")
                npc.HairStyle = a.HairStyle;
            if (!string.IsNullOrWhiteSpace(a.EyeColor) && a.EyeColor != "Unknown")
                npc.EyeColor = a.EyeColor;
            if (!string.IsNullOrWhiteSpace(a.EyeStyle) && a.EyeStyle != "Unknown")
                npc.EyeStyle = a.EyeStyle;
            if (!string.IsNullOrWhiteSpace(a.SkinTone) && a.SkinTone != "Unknown")
                npc.SkinTone = a.SkinTone;
            if (!string.IsNullOrWhiteSpace(a.Glasses))
                npc.Glasses = a.Glasses;
            if (!string.IsNullOrWhiteSpace(a.ScarNotes))
                npc.ScarNotes = a.ScarNotes;
        }

        private static string EmptyToUnknown(string? value)
            => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

        // ============================================================
        // RELATIONSHIPS
        // ============================================================
        private static void LoadRelationships(SqliteConnection conn, SimCharacter npc)
        {
            npc.Relationships ??= new List<Relationship>();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT TargetName, Trust, Respect, Affection, Attraction, Tension, RelationshipType
                    FROM Relationships WHERE NpcId = $id
                    """;
                cmd.Parameters.AddWithValue("$id", npc.Id);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var rel = new Relationship
                    {
                        TargetName = reader.GetString(0),
                        Trust = (int)reader.GetDouble(1),
                        Respect = (int)reader.GetDouble(2),
                        Affection = (int)reader.GetDouble(3),
                        Attraction = (int)reader.GetDouble(4)
                    };
                    if (reader.FieldCount > 5 && !reader.IsDBNull(5))
                        rel.Tension = (int)reader.GetDouble(5);
                    npc.Relationships.Add(rel);
                }
            }
            catch { }
        }

        public static void SaveCharacterState(SimCharacter npc)
        {
            if (npc == null) return;
            if (npc.Traits != null) SaveTraits(npc.Id, npc.Traits);
            SaveBrainState(npc);
            SaveMoney(npc);
            SaveJob(npc);
            SaveCognition(npc);
            SaveBodyProfile(npc);
        }

        private static SimCharacter CreateFallbackEve()
        {
            var eve = new ProjectEve.Characters.NPCs.Eve();
            if (eve.Id == 0) eve.Id = 1;
            eve.Brain ??= new Brain();
            eve.Brain.Owner = eve;
            eve.Traits ??= new NpcTraits();
            return eve;
        }

        public static void PrintCharacterSheet(SimCharacter eve)
            => CharacterSheetPrinter.Print(eve);

        private static void EnsureDataDir()
        {
            var dir = Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        private static void SetBirthDate(SimCharacter npc, DateTime birth)
        {
            try
            {
                var p = typeof(SimCharacter).GetProperty("BirthDate");
                if (p == null) return;
                if (p.PropertyType == typeof(DateTime?))
                    p.SetValue(npc, (DateTime?)birth);
                else
                    p.SetValue(npc, birth);
            }
            catch { }
        }

        private static int CalculateAge(DateTime birth)
        {
            var today = DateTime.Today;
            int age = today.Year - birth.Year;
            if (birth.Date > today.AddYears(-age)) age--;
            return Math.Max(0, age);
        }

        private static string GetString(SqliteDataReader r, int i)
            => r.IsDBNull(i) ? "" : r.GetString(i);

        private static int? GetNullableInt(SqliteDataReader r, int i)
            => r.IsDBNull(i) ? null : r.GetInt32(i);

        private static void TrySet(object target, string prop, object? value)
        {
            if (target == null || value == null) return;
            try
            {
                var p = target.GetType().GetProperty(prop);
                if (p == null || !p.CanWrite) return;

                if (value.GetType() == typeof(int?))
                {
                    var ni = (int?)value;
                    if (!ni.HasValue) return;
                    value = ni.Value;
                }

                var dest = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

                if (dest == typeof(decimal) && value is double d)
                    p.SetValue(target, (decimal)d);
                else if (dest == typeof(decimal) && value is float f)
                    p.SetValue(target, (decimal)f);
                else if (dest == typeof(int) && value is int i)
                    p.SetValue(target, i);
                else
                    p.SetValue(target, Convert.ChangeType(value, dest));
            }
            catch { }
        }
    }
}