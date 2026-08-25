using ProjectEve.Data;

namespace ProjectEve.Relationships;

/// <summary>
/// The single persistence gateway for current directed relationship state.
///
/// Canonical database: project_eve_relationships.db
/// Canonical table: RelationshipStates
///
/// Objective events that changed a relationship belong in history.db.
/// Reasons/interpretations belong in RelationshipReasons in this database.
/// </summary>
public static class RelationshipRepository
{
    public static List<Relationship> LoadForSource(int sourceCharacterId)
    {
        var result = new List<Relationship>();

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TargetCharacterId, TargetName, RelationshipType,
                   Trust, Respect, Love, Attraction, Tension, Notes
            FROM RelationshipStates
            WHERE SourceCharacterId = $source
            ORDER BY Importance DESC, TargetName;
            """;
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Relationship
            {
                TargetId = reader.IsDBNull(0) ? null : Convert.ToInt32(reader.GetValue(0)),
                TargetName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                RelationshipType = reader.IsDBNull(2) ? "acquaintance" : reader.GetString(2),
                Trust = reader.IsDBNull(3) ? 50 : Convert.ToInt32(reader.GetValue(3)),
                Respect = reader.IsDBNull(4) ? 50 : Convert.ToInt32(reader.GetValue(4)),
                Affection = reader.IsDBNull(5) ? 50 : Convert.ToInt32(reader.GetValue(5)),
                Attraction = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6)),
                Tension = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7)),
                Notes = reader.IsDBNull(8) ? "" : reader.GetString(8)
            });
        }

        return result;
    }

    public static bool Exists(
        int sourceCharacterId,
        string targetName,
        string relationshipType)
    {
        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1)
            FROM RelationshipStates
            WHERE SourceCharacterId = $source
              AND TargetName = $target
              AND RelationshipType = $type;
            """;
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);
        cmd.Parameters.AddWithValue("$target", targetName ?? "");
        cmd.Parameters.AddWithValue("$type", relationshipType ?? "");

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    public static string Upsert(
        int sourceCharacterId,
        int? targetCharacterId,
        string targetName,
        string relationshipType,
        int trust,
        int respect,
        int affection,
        int attraction,
        int tension,
        string notes,
        string familyRole = "",
        int loyalty = 50,
        int anger = 0,
        int resentment = 0,
        int fear = 0,
        int jealousy = 0,
        int importance = 50,
        string updatedGameTime = "")
    {
        ProjectEveDatabaseSetup.EnsureAll();

        string id = BuildId(
            sourceCharacterId,
            targetCharacterId,
            targetName,
            relationshipType);

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RelationshipStates
            (
                RelationshipId,
                SourceCharacterId,
                TargetCharacterId,
                TargetName,
                RelationshipType,
                FamilyRole,
                Love,
                Trust,
                Respect,
                Loyalty,
                Anger,
                Resentment,
                Fear,
                Jealousy,
                Attraction,
                Tension,
                Importance,
                Notes,
                UpdatedGameTime,
                UpdatedRealAt
            )
            VALUES
            (
                $id, $source, $targetId, $targetName, $type, $familyRole,
                $love, $trust, $respect, $loyalty,
                $anger, $resentment, $fear, $jealousy,
                $attraction, $tension, $importance, $notes,
                $gameTime, CURRENT_TIMESTAMP
            )
            ON CONFLICT(RelationshipId) DO UPDATE SET
                TargetCharacterId = excluded.TargetCharacterId,
                TargetName = excluded.TargetName,
                RelationshipType = excluded.RelationshipType,
                FamilyRole = excluded.FamilyRole,
                Love = excluded.Love,
                Trust = excluded.Trust,
                Respect = excluded.Respect,
                Loyalty = excluded.Loyalty,
                Anger = excluded.Anger,
                Resentment = excluded.Resentment,
                Fear = excluded.Fear,
                Jealousy = excluded.Jealousy,
                Attraction = excluded.Attraction,
                Tension = excluded.Tension,
                Importance = excluded.Importance,
                Notes = excluded.Notes,
                UpdatedGameTime = excluded.UpdatedGameTime,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);
        cmd.Parameters.AddWithValue("$targetId", (object?)targetCharacterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$targetName", targetName ?? "");
        cmd.Parameters.AddWithValue("$type", relationshipType ?? "");
        cmd.Parameters.AddWithValue("$familyRole", familyRole ?? "");
        cmd.Parameters.AddWithValue("$love", Clamp(affection));
        cmd.Parameters.AddWithValue("$trust", Clamp(trust));
        cmd.Parameters.AddWithValue("$respect", Clamp(respect));
        cmd.Parameters.AddWithValue("$loyalty", Clamp(loyalty));
        cmd.Parameters.AddWithValue("$anger", Clamp(anger));
        cmd.Parameters.AddWithValue("$resentment", Clamp(resentment));
        cmd.Parameters.AddWithValue("$fear", Clamp(fear));
        cmd.Parameters.AddWithValue("$jealousy", Clamp(jealousy));
        cmd.Parameters.AddWithValue("$attraction", Clamp(attraction));
        cmd.Parameters.AddWithValue("$tension", Clamp(tension));
        cmd.Parameters.AddWithValue("$importance", Clamp(importance));
        cmd.Parameters.AddWithValue("$notes", notes ?? "");
        cmd.Parameters.AddWithValue("$gameTime", updatedGameTime ?? "");
        cmd.ExecuteNonQuery();

        return id;
    }

    public static string Upsert(
        int sourceCharacterId,
        Relationship relationship,
        string updatedGameTime = "")
    {
        ArgumentNullException.ThrowIfNull(relationship);

        return Upsert(
            sourceCharacterId,
            relationship.TargetId,
            relationship.TargetName,
            relationship.RelationshipType,
            relationship.Trust,
            relationship.Respect,
            relationship.Affection,
            relationship.Attraction,
            relationship.Tension,
            relationship.Notes,
            updatedGameTime: updatedGameTime);
    }

    private static string BuildId(
        int sourceCharacterId,
        int? targetCharacterId,
        string targetName,
        string relationshipType)
    {
        string target = targetCharacterId?.ToString()
            ?? Normalize(targetName);

        return $"rel:{sourceCharacterId}:{target}:{Normalize(relationshipType)}";
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray())
            .Trim('_');
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
