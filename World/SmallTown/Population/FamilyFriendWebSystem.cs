using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Characters;
using ProjectEve.Relationships;
using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Project Eve Family and Friend Web.
    ///
    /// IMPORTANT: WebTier belongs to the EDGE, not globally to the person.
    /// Sarah may hold Tom at Tier 1 while John holds Tom at Tier 2 and Eve at Tier 4.
    /// The same TargetNpcId is reused everywhere.
    /// </summary>
    public static class FamilyFriendWebSystem
    {
        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS FamilyFriendWeb (
                    OwnerNpcId INTEGER NOT NULL,
                    TargetNpcId INTEGER NOT NULL,
                    WebTier INTEGER NOT NULL,
                    RelationshipType TEXT NOT NULL,
                    IsHistoryOnly INTEGER NOT NULL DEFAULT 0,
                    Source TEXT,
                    Notes TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (OwnerNpcId, TargetNpcId),
                    FOREIGN KEY (OwnerNpcId) REFERENCES Characters(Id),
                    FOREIGN KEY (TargetNpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_family_web_owner_tier
                    ON FamilyFriendWeb(OwnerNpcId, WebTier);

                CREATE INDEX IF NOT EXISTS ix_family_web_target
                    ON FamilyFriendWeb(TargetNpcId);

                CREATE TABLE IF NOT EXISTS HouseholdMembers (
                    HouseholdId TEXT NOT NULL,
                    NpcId INTEGER NOT NULL,
                    HouseholdRole TEXT,
                    JoinedAt TEXT,
                    LeftAt TEXT,
                    PRIMARY KEY (HouseholdId, NpcId),
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        public static void Link(
            int ownerNpcId,
            int targetNpcId,
            int webTier,
            string relationshipType,
            bool historyOnly = false,
            string source = "generated_web",
            string notes = "",
            bool mirrorIntoRelationships = true)
        {
            if (ownerNpcId == targetNpcId) return;
            webTier = Math.Clamp(webTier, 1, 5);
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO FamilyFriendWeb
                    (OwnerNpcId, TargetNpcId, WebTier, RelationshipType, IsHistoryOnly, Source, Notes, CreatedAt, UpdatedAt)
                    VALUES ($owner, $target, $tier, $type, $history, $source, $notes, $now, $now)
                    ON CONFLICT(OwnerNpcId, TargetNpcId) DO UPDATE SET
                        WebTier=$tier,
                        RelationshipType=$type,
                        IsHistoryOnly=$history,
                        Source=$source,
                        Notes=$notes,
                        UpdatedAt=$now;
                    """;
                cmd.Parameters.AddWithValue("$owner", ownerNpcId);
                cmd.Parameters.AddWithValue("$target", targetNpcId);
                cmd.Parameters.AddWithValue("$tier", webTier);
                cmd.Parameters.AddWithValue("$type", relationshipType);
                cmd.Parameters.AddWithValue("$history", historyOnly ? 1 : 0);
                cmd.Parameters.AddWithValue("$source", source);
                cmd.Parameters.AddWithValue("$notes", notes);
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // Keep Project Eve's existing Relationship system aware of Tier 1-4 bonds.
            // Tier 5 can stay lore-only until it becomes relevant.
            if (mirrorIntoRelationships && webTier <= 4)
                MirrorToExistingRelationshipTable(conn, ownerNpcId, targetNpcId, relationshipType, webTier, notes);

            UpdateCharacterMaterializationTier(conn, targetNpcId, webTier);
        }

        public static void LinkTwoWay(
            int aNpcId,
            int bNpcId,
            int tierFromAToB,
            int tierFromBToA,
            string typeFromAToB,
            string typeFromBToA,
            bool historyOnly = false,
            string notes = "")
        {
            Link(aNpcId, bNpcId, tierFromAToB, typeFromAToB, historyOnly, notes: notes);
            Link(bNpcId, aNpcId, tierFromBToA, typeFromBToA, historyOnly, notes: notes);
        }

        /// <summary>
        /// Called when the player/NPC directly interacts with a Tier 5 lore person.
        /// The SAME character row is promoted. No reroll, no replacement identity.
        /// </summary>
        public static SimCharacter? PromoteHistoryPersonToTier4(int ownerNpcId, int targetNpcId)
        {
            Initialize();

            using (var conn = new SqliteConnection(ConnStr))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE FamilyFriendWeb
                    SET WebTier=4, IsHistoryOnly=0, UpdatedAt=$now
                    WHERE OwnerNpcId=$owner AND TargetNpcId=$target AND WebTier=5;
                    """;
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$owner", ownerNpcId);
                cmd.Parameters.AddWithValue("$target", targetNpcId);
                cmd.ExecuteNonQuery();

                UpdateCharacterMaterializationTier(conn, targetNpcId, 4);
            }

            var npc = CharacterFactory.LoadCharacter(targetNpcId);
            if (npc == null) return null;

            // Now it is worth spending resources on a real interactive character.
            CharacterFactory.EnsureCore(npc);
            CharacterFactory.EnsureTraits(npc);
            CharacterRepository.SaveTraits(npc.Id, npc.Traits);
            CharacterRepository.SaveBrainState(npc);
            CharacterRepository.SaveMoney(npc);

            return npc;
        }

        public static List<WebEdge> GetWeb(int ownerNpcId, int? tier = null)
        {
            Initialize();
            var result = new List<WebEdge>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = tier.HasValue
                ? """
                  SELECT f.OwnerNpcId, f.TargetNpcId, c.Name, f.WebTier, f.RelationshipType,
                         f.IsHistoryOnly, f.Source, f.Notes
                  FROM FamilyFriendWeb f
                  JOIN Characters c ON c.Id=f.TargetNpcId
                  WHERE f.OwnerNpcId=$owner AND f.WebTier=$tier
                  ORDER BY c.Name;
                  """
                : """
                  SELECT f.OwnerNpcId, f.TargetNpcId, c.Name, f.WebTier, f.RelationshipType,
                         f.IsHistoryOnly, f.Source, f.Notes
                  FROM FamilyFriendWeb f
                  JOIN Characters c ON c.Id=f.TargetNpcId
                  WHERE f.OwnerNpcId=$owner
                  ORDER BY f.WebTier, c.Name;
                  """;
            cmd.Parameters.AddWithValue("$owner", ownerNpcId);
            if (tier.HasValue) cmd.Parameters.AddWithValue("$tier", tier.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new WebEdge
                {
                    OwnerNpcId = reader.GetInt32(0),
                    TargetNpcId = reader.GetInt32(1),
                    TargetName = reader.GetString(2),
                    WebTier = reader.GetInt32(3),
                    RelationshipType = reader.GetString(4),
                    IsHistoryOnly = reader.GetInt32(5) != 0,
                    Source = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Notes = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }

            return result;
        }

        public static int CountTier(int ownerNpcId, int webTier)
        {
            Initialize();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM FamilyFriendWeb WHERE OwnerNpcId=$owner AND WebTier=$tier";
            cmd.Parameters.AddWithValue("$owner", ownerNpcId);
            cmd.Parameters.AddWithValue("$tier", webTier);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        private static void MirrorToExistingRelationshipTable(
            SqliteConnection conn,
            int ownerNpcId,
            int targetNpcId,
            string relationshipType,
            int webTier,
            string notes)
        {
            string targetName = "Unknown";
            using (var name = conn.CreateCommand())
            {
                name.CommandText = "SELECT Name FROM Characters WHERE Id=$id";
                name.Parameters.AddWithValue("$id", targetNpcId);
                targetName = Convert.ToString(name.ExecuteScalar()) ?? "Unknown";
            }

            // Different tiers get different default bond starting points.
            int trust = webTier switch { 1 => 78, 2 => 66, 3 => 55, 4 => 45, _ => 40 };
            int affection = webTier switch { 1 => 80, 2 => 64, 3 => 50, 4 => 38, _ => 35 };

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM Relationships
                WHERE NpcId=$owner AND TargetId=$target;

                INSERT INTO Relationships
                (NpcId, TargetName, TargetId, Trust, Respect, Affection, Attraction, Tension, RelationshipType, Notes)
                VALUES
                ($owner, $name, $target, $trust, $respect, $affection, 0, 10, $type, $notes);
                """;
            cmd.Parameters.AddWithValue("$owner", ownerNpcId);
            cmd.Parameters.AddWithValue("$name", targetName);
            cmd.Parameters.AddWithValue("$target", targetNpcId);
            cmd.Parameters.AddWithValue("$trust", trust);
            cmd.Parameters.AddWithValue("$respect", Math.Max(35, trust - 3));
            cmd.Parameters.AddWithValue("$affection", affection);
            cmd.Parameters.AddWithValue("$type", relationshipType);
            cmd.Parameters.AddWithValue("$notes", $"Family/Friend Web T{webTier}. {notes}".Trim());
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Characters.Tier is used as the cheapest global materialization hint:
        /// keep the closest tier the person has anywhere in the web.
        /// The directional truth remains FamilyFriendWeb.WebTier.
        /// </summary>
        private static void UpdateCharacterMaterializationTier(SqliteConnection conn, int npcId, int webTier)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Characters
                SET Tier = CASE
                    WHEN Tier IS NULL THEN $tier
                    WHEN $tier < Tier THEN $tier
                    ELSE Tier
                END
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$tier", webTier);
            cmd.Parameters.AddWithValue("$id", npcId);
            cmd.ExecuteNonQuery();
        }

        public sealed class WebEdge
        {
            public int OwnerNpcId { get; set; }
            public int TargetNpcId { get; set; }
            public string TargetName { get; set; } = "";
            public int WebTier { get; set; }
            public string RelationshipType { get; set; } = "";
            public bool IsHistoryOnly { get; set; }
            public string Source { get; set; } = "";
            public string Notes { get; set; } = "";
        }
    }
}
