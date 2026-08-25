using ProjectEve.Characters.Base;
using ProjectEve.Characters.Characters;
using ProjectEve.Data;
using ProjectEve.Relationships;

namespace ProjectEve.Worlds.SmallTownSystems;

/// <summary>
/// Project Eve Family and Friend Web.
///
/// Canonical ownership:
/// - Edge / household truth -> project_eve_relationships.db
/// - NPC identity / materialization tier -> project_eve.db
/// - Relationship state -> RelationshipRepository / RelationshipStates
///
/// WebTier belongs to the EDGE, not globally to the person.
/// </summary>
public static class FamilyFriendWebSystem
{
    public static void Initialize()
    {
        ProjectEveDatabaseSetup.EnsureAll();
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
        if (ownerNpcId == targetNpcId)
            return;

        webTier = Math.Clamp(webTier, 1, 5);
        Initialize();

        using (var conn = ProjectEveDatabaseConnections.OpenRelationships())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO FamilyFriendWeb
                (
                    OwnerNpcId, TargetNpcId, WebTier, RelationshipType,
                    IsHistoryOnly, Source, Notes, CreatedAt, UpdatedAt
                )
                VALUES
                (
                    $owner, $target, $tier, $type,
                    $history, $source, $notes, $now, $now
                )
                ON CONFLICT(OwnerNpcId, TargetNpcId) DO UPDATE SET
                    WebTier = excluded.WebTier,
                    RelationshipType = excluded.RelationshipType,
                    IsHistoryOnly = excluded.IsHistoryOnly,
                    Source = excluded.Source,
                    Notes = excluded.Notes,
                    UpdatedAt = excluded.UpdatedAt;
                """;

            cmd.Parameters.AddWithValue("$owner", ownerNpcId);
            cmd.Parameters.AddWithValue("$target", targetNpcId);
            cmd.Parameters.AddWithValue("$tier", webTier);
            cmd.Parameters.AddWithValue("$type", relationshipType ?? "");
            cmd.Parameters.AddWithValue("$history", historyOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$source", source ?? "");
            cmd.Parameters.AddWithValue("$notes", notes ?? "");
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        if (mirrorIntoRelationships && webTier <= 4)
            MirrorToCanonicalRelationshipState(
                ownerNpcId,
                targetNpcId,
                relationshipType ?? "",
                webTier,
                notes ?? "");

        UpdateCharacterMaterializationTier(targetNpcId, webTier);
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
    /// Called when the player/NPC directly interacts with a Tier-5 lore person.
    /// The SAME character row is promoted. No reroll and no replacement identity.
    /// </summary>
    public static SimCharacter? PromoteHistoryPersonToTier4(int ownerNpcId, int targetNpcId)
    {
        Initialize();

        using (var conn = ProjectEveDatabaseConnections.OpenRelationships())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE FamilyFriendWeb
                SET WebTier = 4,
                    IsHistoryOnly = 0,
                    UpdatedAt = $now
                WHERE OwnerNpcId = $owner
                  AND TargetNpcId = $target
                  AND WebTier = 5;
                """;
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$owner", ownerNpcId);
            cmd.Parameters.AddWithValue("$target", targetNpcId);
            cmd.ExecuteNonQuery();
        }

        UpdateCharacterMaterializationTier(targetNpcId, 4);

        var npc = CharacterFactory.LoadCharacter(targetNpcId);
        if (npc == null)
            return null;

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

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = tier.HasValue
            ? """
              SELECT OwnerNpcId, TargetNpcId, WebTier, RelationshipType,
                     IsHistoryOnly, Source, Notes
              FROM FamilyFriendWeb
              WHERE OwnerNpcId = $owner
                AND WebTier = $tier
              ORDER BY WebTier, TargetNpcId;
              """
            : """
              SELECT OwnerNpcId, TargetNpcId, WebTier, RelationshipType,
                     IsHistoryOnly, Source, Notes
              FROM FamilyFriendWeb
              WHERE OwnerNpcId = $owner
              ORDER BY WebTier, TargetNpcId;
              """;

        cmd.Parameters.AddWithValue("$owner", ownerNpcId);
        if (tier.HasValue)
            cmd.Parameters.AddWithValue("$tier", tier.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int targetId = reader.GetInt32(1);

            result.Add(new WebEdge
            {
                OwnerNpcId = reader.GetInt32(0),
                TargetNpcId = targetId,
                TargetName = LoadCharacterName(targetId),
                WebTier = reader.GetInt32(2),
                RelationshipType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                IsHistoryOnly = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                Source = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Notes = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }

        return result;
    }

    public static int CountTier(int ownerNpcId, int webTier)
    {
        Initialize();

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM FamilyFriendWeb
            WHERE OwnerNpcId = $owner
              AND WebTier = $tier;
            """;
        cmd.Parameters.AddWithValue("$owner", ownerNpcId);
        cmd.Parameters.AddWithValue("$tier", webTier);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static void MirrorToCanonicalRelationshipState(
        int ownerNpcId,
        int targetNpcId,
        string relationshipType,
        int webTier,
        string notes)
    {
        string targetName = LoadCharacterName(targetNpcId);

        int trust = webTier switch
        {
            1 => 78,
            2 => 66,
            3 => 55,
            4 => 45,
            _ => 40
        };

        int affection = webTier switch
        {
            1 => 80,
            2 => 64,
            3 => 50,
            4 => 38,
            _ => 35
        };

        RelationshipRepository.Upsert(
            sourceCharacterId: ownerNpcId,
            targetCharacterId: targetNpcId,
            targetName: targetName,
            relationshipType: relationshipType,
            trust: trust,
            respect: Math.Max(35, trust - 3),
            affection: affection,
            attraction: 0,
            tension: 10,
            notes: $"Family/Friend Web T{webTier}. {notes}".Trim(),
            importance: webTier switch
            {
                1 => 90,
                2 => 75,
                3 => 60,
                4 => 45,
                _ => 30
            });
    }

    private static string LoadCharacterName(int npcId)
    {
        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Name
            FROM Characters
            WHERE Id = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        return Convert.ToString(cmd.ExecuteScalar()) ?? "Unknown";
    }

    /// <summary>
    /// Characters.Tier remains the cheapest global materialization hint.
    /// Directional relationship truth remains FamilyFriendWeb.WebTier.
    /// </summary>
    private static void UpdateCharacterMaterializationTier(int npcId, int webTier)
    {
        // Family/friend relationship truth lives in the relationships DB.
        // Characters.Tier is current NPC materialization truth in the MAIN DB,
        // so all runtime writes route through CharacterRepository.
        ProjectEve.Characters.Base.CharacterRepository.LowerMaterializationTier(
            npcId,
            webTier);
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


