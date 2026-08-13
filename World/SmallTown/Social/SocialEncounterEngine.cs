using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Relationships;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Finds plausible social encounters between people who are actually together.
    /// Creates opportunities; HumanEventEngine still decides what they do.
    /// </summary>
    public static class SocialEncounterEngine
    {
        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS SocialEncounterHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcAId INTEGER NOT NULL,
                    NpcBId INTEGER NOT NULL,
                    LocationId TEXT,
                    GameTime TEXT NOT NULL,
                    OpportunityType TEXT
                );
                """;
            cmd.ExecuteNonQuery();
        }

        public static EncounterOpportunity? Evaluate(
            SimCharacter a,
            SimCharacter b,
            DateTime gameTime)
        {
            Initialize();
            if (a == null || b == null || a.Id == b.Id) return null;

            var sa = WorldActivityEngine.GetState(a.Id);
            var sb = WorldActivityEngine.GetState(b.Id);

            if (sa == null || sb == null) return null;
            if (!string.Equals(sa.LocationId, sb.LocationId, StringComparison.OrdinalIgnoreCase))
                return null;
            if (sa.IsBusy || sb.IsBusy) return null;

            var rel = a.Relationships?.FirstOrDefault(r => r.TargetId == b.Id);
            string type = "conversation_opportunity";

            if (rel != null)
            {
                if (rel.Tension >= 65) type = "active_conflict";
                else if (rel.Attraction >= 65 && rel.Affection >= 50) type = "romantic_opportunity";
            }

            Save(a.Id, b.Id, sa.LocationId, gameTime, type);

            return new EncounterOpportunity
            {
                Actor = a,
                Target = b,
                LocationId = sa.LocationId,
                OpportunityType = type
            };
        }

        private static void Save(int a, int b, string loc, DateTime time, string type)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SocialEncounterHistory
                (NpcAId,NpcBId,LocationId,GameTime,OpportunityType)
                VALUES ($a,$b,$l,$t,$o);
                """;
            cmd.Parameters.AddWithValue("$a", a);
            cmd.Parameters.AddWithValue("$b", b);
            cmd.Parameters.AddWithValue("$l", loc);
            cmd.Parameters.AddWithValue("$t", time.ToString("o"));
            cmd.Parameters.AddWithValue("$o", type);
            cmd.ExecuteNonQuery();
        }

        public sealed class EncounterOpportunity
        {
            public SimCharacter Actor { get; set; } = null!;
            public SimCharacter Target { get; set; } = null!;
            public string LocationId { get; set; } = "";
            public string OpportunityType { get; set; } = "conversation_opportunity";

            public void ApplyTo(HumanEventEngine.HumanEventContext ctx)
            {
                ctx.Target = Target;
                ctx.LocationId = LocationId;
                ctx.Tags.Add("conversation_opportunity");
                ctx.Tags.Add(OpportunityType);
            }
        }
    }
}
