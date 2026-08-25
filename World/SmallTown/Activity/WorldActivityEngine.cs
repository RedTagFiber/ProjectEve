using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Cheap 5-minute world loop.
    /// It answers WHERE NPCs are and WHAT broad activity they are doing.
    /// It does not run deep AI reasoning.
    /// </summary>
    public static class WorldActivityEngine
    {
        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();
        }

        public static WorldTickResult Tick(DateTime gameTime, IEnumerable<SimCharacter> npcs)
        {
            Initialize();
            var result = new WorldTickResult { GameTime = gameTime };

            foreach (var npc in npcs)
            {
                if (npc == null || npc.Tier >= 5) continue;

                string activity = ResolveActivity(npc, gameTime);
                string location = ResolveLocation(npc, activity);
                bool busy = IsBusy(activity);

                Save(npc.Id, location, activity, gameTime, busy);

                npc.Location = location;
                result.UpdatedNpcIds.Add(npc.Id);
            }

            return result;
        }

        public static List<int> GetNpcIdsAtLocation(string locationId)
        {
            Initialize();
            var list = new List<int>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT NpcId
                FROM NpcWorldActivity
                WHERE LocationId=$loc;
                """;
            cmd.Parameters.AddWithValue("$loc", locationId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt32(0));
            return list;
        }

        public static ActivityState? GetState(int npcId)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT LocationId, Activity, ActivityStartGameTime, LastWorldTickGameTime, IsBusy
                FROM NpcWorldActivity
                WHERE NpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ActivityState
            {
                NpcId = npcId,
                LocationId = r.IsDBNull(0) ? "" : r.GetString(0),
                Activity = r.IsDBNull(1) ? "" : r.GetString(1),
                ActivityStartGameTime = DateTime.TryParse(r.IsDBNull(2) ? "" : r.GetString(2), out var a) ? a : DateTime.MinValue,
                LastWorldTickGameTime = DateTime.TryParse(r.IsDBNull(3) ? "" : r.GetString(3), out var b) ? b : DateTime.MinValue,
                IsBusy = !r.IsDBNull(4) && r.GetInt32(4) != 0
            };
        }

        private static string ResolveActivity(SimCharacter npc, DateTime gameTime)
        {
            try
            {
                if (npc.Job != null && npc.Job.IsWorkingAt(gameTime))
                    return "work";
            }
            catch { }

            int hour = gameTime.Hour;

            if (hour < 6) return "sleep";
            if (hour < 8) return "home";
            if (hour >= 22) return "home";

            // Existing generated schedule can override the generic fallback.
            if (npc.Schedule != null && npc.Schedule.Count > 0)
            {
                string joined = string.Join(" ", npc.Schedule).ToLowerInvariant();
                if (joined.Contains("church") && gameTime.DayOfWeek == DayOfWeek.Sunday)
                    return "church";
                if (joined.Contains("school") && hour >= 8 && hour < 15)
                    return "school";
            }

            return "idle";
        }

        private static string ResolveLocation(SimCharacter npc, string activity)
        {
            if (activity == "work")
            {
                if (!string.IsNullOrWhiteSpace(npc.Job?.Employer))
                    return npc.Job.Employer;
            }

            if (activity == "home" || activity == "sleep")
            {
                if (!string.IsNullOrWhiteSpace(npc.HomeAddress))
                    return npc.HomeAddress;
            }

            return !string.IsNullOrWhiteSpace(npc.Location)
                ? npc.Location
                : "town";
        }

        private static bool IsBusy(string activity)
            => activity is "work" or "commute" or "sleep" or "medical_visit";

        private static void Save(int npcId, string location, string activity, DateTime gameTime, bool busy)
        {
            UpsertActivityState(
                npcId,
                location,
                activity,
                gameTime,
                gameTime,
                busy,
                preserveExistingStart: true);
        }

        /// <summary>
        /// Canonical runtime write gateway for NpcWorldActivity.
        /// ActivityPlanner and world-tick code both route physical activity state here.
        /// </summary>
        public static void UpsertActivityState(
            int npcId,
            string locationId,
            string activity,
            DateTime activityStartGameTime,
            DateTime lastWorldTickGameTime,
            bool isBusy,
            bool preserveExistingStart = false)
        {
            if (npcId <= 0)
                return;

            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();

            cmd.CommandText = preserveExistingStart
                ? """
                    INSERT INTO NpcWorldActivity
                    (NpcId, LocationId, Activity, ActivityStartGameTime, LastWorldTickGameTime, IsBusy)
                    VALUES ($npc,$loc,$act,$start,$tick,$busy)
                    ON CONFLICT(NpcId) DO UPDATE SET
                        LocationId=$loc,
                        Activity=$act,
                        LastWorldTickGameTime=$tick,
                        IsBusy=$busy;
                    """
                : """
                    INSERT INTO NpcWorldActivity
                    (NpcId, LocationId, Activity, ActivityStartGameTime, LastWorldTickGameTime, IsBusy)
                    VALUES ($npc,$loc,$act,$start,$tick,$busy)
                    ON CONFLICT(NpcId) DO UPDATE SET
                        LocationId=$loc,
                        Activity=$act,
                        ActivityStartGameTime=$start,
                        LastWorldTickGameTime=$tick,
                        IsBusy=$busy;
                    """;

            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$loc", locationId ?? "");
            cmd.Parameters.AddWithValue("$act", activity ?? "idle");
            cmd.Parameters.AddWithValue("$start", activityStartGameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$tick", lastWorldTickGameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$busy", isBusy ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public sealed class ActivityState
        {
            public int NpcId { get; set; }
            public string LocationId { get; set; } = "";
            public string Activity { get; set; } = "";
            public DateTime ActivityStartGameTime { get; set; }
            public DateTime LastWorldTickGameTime { get; set; }
            public bool IsBusy { get; set; }
        }

        public sealed class WorldTickResult
        {
            public DateTime GameTime { get; set; }
            public List<int> UpdatedNpcIds { get; } = new();
        }
    }
}

