using ProjectEve.Data;

namespace ProjectEve.Relationships;

/// <summary>
/// Read model for the NpcStudio relationship dossier.
///
/// Canonical ownership remains unchanged:
/// - RelationshipStates / RelationshipReasons / PersonalMemories / KnowledgeItems
///   in project_eve_relationships.db.
/// - Objective event truth remains in project_eve_history.db.
///
/// This service adds no schema and performs no writes.
/// </summary>
public static class RelationshipDossierService
{
    public static RelationshipDossier Load(int sourceCharacterId)
    {
        if (sourceCharacterId <= 0)
            return new RelationshipDossier();

        ProjectEveDatabaseSetup.EnsureAll();

        var relationships = LoadRelationships(sourceCharacterId);
        var reasons = LoadReasons(sourceCharacterId);
        var memories = LoadMemories(sourceCharacterId);
        var knowledge = LoadKnowledge(sourceCharacterId);

        var reasonCounts = reasons
            .Where(x => x.IsStillActive)
            .GroupBy(x => x.RelationshipId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var memoryCounts = memories
            .Where(x => x.SubjectCharacterId.HasValue)
            .GroupBy(x => x.SubjectCharacterId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        var knowledgeCounts = knowledge
            .Where(x => x.SubjectCharacterId.HasValue)
            .GroupBy(x => x.SubjectCharacterId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        foreach (var relationship in relationships)
        {
            relationship.ActiveReasonCount =
                reasonCounts.TryGetValue(relationship.RelationshipId, out int rc) ? rc : 0;

            if (relationship.TargetCharacterId.HasValue)
            {
                int targetId = relationship.TargetCharacterId.Value;
                relationship.MemoryCount =
                    memoryCounts.TryGetValue(targetId, out int mc) ? mc : 0;
                relationship.KnowledgeCount =
                    knowledgeCounts.TryGetValue(targetId, out int kc) ? kc : 0;
            }
        }

        return new RelationshipDossier
        {
            Relationships = relationships,
            Reasons = reasons,
            Memories = memories,
            Knowledge = knowledge
        };
    }

    private static List<RelationshipDossierItem> LoadRelationships(int sourceCharacterId)
    {
        var result = new List<RelationshipDossierItem>();

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
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
            FROM RelationshipStates
            WHERE SourceCharacterId = $source
            ORDER BY Importance DESC, TargetName;
            """;
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RelationshipDossierItem
            {
                RelationshipId = GetString(reader, 0),
                SourceCharacterId = GetInt(reader, 1),
                TargetCharacterId = GetNullableInt(reader, 2),
                TargetName = GetString(reader, 3, "Unknown"),
                RelationshipType = GetString(reader, 4, "acquaintance"),
                FamilyRole = GetString(reader, 5),
                Love = GetInt(reader, 6, 50),
                Trust = GetInt(reader, 7, 50),
                Respect = GetInt(reader, 8, 50),
                Loyalty = GetInt(reader, 9, 50),
                Anger = GetInt(reader, 10),
                Resentment = GetInt(reader, 11),
                Fear = GetInt(reader, 12),
                Jealousy = GetInt(reader, 13),
                Attraction = GetInt(reader, 14),
                Tension = GetInt(reader, 15),
                Importance = GetInt(reader, 16, 50),
                Notes = GetString(reader, 17),
                UpdatedGameTime = GetString(reader, 18),
                UpdatedRealAt = GetString(reader, 19)
            });
        }

        return result;
    }

    private static List<RelationshipReasonDossierItem> LoadReasons(int sourceCharacterId)
    {
        var result = new List<RelationshipReasonDossierItem>();

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                rr.Id,
                rr.RelationshipId,
                rs.TargetCharacterId,
                rs.TargetName,
                rr.EventId,
                rr.ScoreName,
                rr.Delta,
                rr.Reason,
                rr.Interpretation,
                rr.IsStillActive,
                rr.Importance,
                rr.GameTime,
                rr.CreatedRealAt
            FROM RelationshipReasons rr
            JOIN RelationshipStates rs
              ON rs.RelationshipId = rr.RelationshipId
            WHERE rs.SourceCharacterId = $source
            ORDER BY rr.IsStillActive DESC, rr.Importance DESC, rr.CreatedRealAt DESC;
            """;
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RelationshipReasonDossierItem
            {
                Id = GetString(reader, 0),
                RelationshipId = GetString(reader, 1),
                TargetCharacterId = GetNullableInt(reader, 2),
                TargetName = GetString(reader, 3, "Unknown"),
                EventId = GetString(reader, 4),
                ScoreName = GetString(reader, 5),
                Delta = GetInt(reader, 6),
                Reason = GetString(reader, 7),
                Interpretation = GetString(reader, 8),
                IsStillActive = GetInt(reader, 9, 1) != 0,
                Importance = GetInt(reader, 10, 50),
                GameTime = GetString(reader, 11),
                CreatedRealAt = GetString(reader, 12)
            });
        }

        return result;
    }

    private static List<PersonalMemoryDossierItem> LoadMemories(int sourceCharacterId)
    {
        var result = new List<PersonalMemoryDossierItem>();

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                Id,
                KnowerCharacterId,
                SubjectCharacterId,
                EventId,
                MemoryType,
                MemoryText,
                Interpretation,
                EmotionalMeaning,
                Importance,
                Strength,
                Confidence,
                IsLockedPeak,
                LearnedGameTime,
                LastUpdatedGameTime,
                CreatedRealAt
            FROM PersonalMemories
            WHERE KnowerCharacterId = $source
            ORDER BY Importance DESC, CreatedRealAt DESC
            LIMIT 250;
            """;
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int? subjectId = GetNullableInt(reader, 2);
            result.Add(new PersonalMemoryDossierItem
            {
                Id = GetString(reader, 0),
                KnowerCharacterId = GetInt(reader, 1),
                SubjectCharacterId = subjectId,
                SubjectName = subjectId.HasValue ? ResolveCharacterName(subjectId.Value) : "",
                EventId = GetString(reader, 3),
                MemoryType = GetString(reader, 4, "General"),
                MemoryText = GetString(reader, 5),
                Interpretation = GetString(reader, 6),
                EmotionalMeaning = GetString(reader, 7),
                Importance = GetInt(reader, 8, 50),
                Strength = GetInt(reader, 9, 50),
                Confidence = GetInt(reader, 10, 70),
                IsLockedPeak = GetInt(reader, 11) != 0,
                LearnedGameTime = GetString(reader, 12),
                LastUpdatedGameTime = GetString(reader, 13),
                CreatedRealAt = GetString(reader, 14)
            });
        }

        return result;
    }

    private static List<KnowledgeDossierItem> LoadKnowledge(int sourceCharacterId)
    {
        var result = new List<KnowledgeDossierItem>();

        using var conn = ProjectEveDatabaseConnections.OpenRelationships();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                Id,
                KnowerCharacterId,
                SubjectCharacterId,
                EventId,
                KnowledgeType,
                WhatTheyKnow,
                HowTheyLearnedIt,
                SourceCharacterId,
                Confidence,
                IsRumor,
                IsSecret,
                IsFalseBelief,
                LearnedGameTime,
                LastUpdatedGameTime,
                CreatedRealAt
            FROM KnowledgeItems
            WHERE KnowerCharacterId = $source
            ORDER BY IsFalseBelief DESC, IsRumor DESC, Confidence DESC, CreatedRealAt DESC
            LIMIT 250;
            """;
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int? subjectId = GetNullableInt(reader, 2);
            int? sourceId = GetNullableInt(reader, 7);

            result.Add(new KnowledgeDossierItem
            {
                Id = GetString(reader, 0),
                KnowerCharacterId = GetInt(reader, 1),
                SubjectCharacterId = subjectId,
                SubjectName = subjectId.HasValue ? ResolveCharacterName(subjectId.Value) : "",
                EventId = GetString(reader, 3),
                KnowledgeType = GetString(reader, 4),
                WhatTheyKnow = GetString(reader, 5),
                HowTheyLearnedIt = GetString(reader, 6),
                SourceCharacterId = sourceId,
                SourceName = sourceId.HasValue ? ResolveCharacterName(sourceId.Value) : "",
                Confidence = GetInt(reader, 8, 50),
                IsRumor = GetInt(reader, 9) != 0,
                IsSecret = GetInt(reader, 10) != 0,
                IsFalseBelief = GetInt(reader, 11) != 0,
                LearnedGameTime = GetString(reader, 12),
                LastUpdatedGameTime = GetString(reader, 13),
                CreatedRealAt = GetString(reader, 14)
            });
        }

        return result;
    }

    private static string ResolveCharacterName(int characterId)
    {
        if (characterId <= 0)
            return "";

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", characterId);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private static string GetString(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int index,
        string fallback = "") =>
        reader.IsDBNull(index)
            ? fallback
            : reader.GetValue(index)?.ToString() ?? fallback;

    private static int GetInt(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int index,
        int fallback = 0)
    {
        if (reader.IsDBNull(index))
            return fallback;

        return int.TryParse(reader.GetValue(index)?.ToString(), out int value)
            ? value
            : fallback;
    }

    private static int? GetNullableInt(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int index)
    {
        if (reader.IsDBNull(index))
            return null;

        return int.TryParse(reader.GetValue(index)?.ToString(), out int value)
            ? value
            : null;
    }
}

public sealed class RelationshipDossier
{
    public IReadOnlyList<RelationshipDossierItem> Relationships { get; init; } =
        Array.Empty<RelationshipDossierItem>();

    public IReadOnlyList<RelationshipReasonDossierItem> Reasons { get; init; } =
        Array.Empty<RelationshipReasonDossierItem>();

    public IReadOnlyList<PersonalMemoryDossierItem> Memories { get; init; } =
        Array.Empty<PersonalMemoryDossierItem>();

    public IReadOnlyList<KnowledgeDossierItem> Knowledge { get; init; } =
        Array.Empty<KnowledgeDossierItem>();
}

public sealed class RelationshipDossierItem
{
    public string RelationshipId { get; init; } = "";
    public int SourceCharacterId { get; init; }
    public int? TargetCharacterId { get; init; }
    public string TargetName { get; init; } = "Unknown";
    public string RelationshipType { get; init; } = "acquaintance";
    public string FamilyRole { get; init; } = "";

    public int Love { get; init; }
    public int Trust { get; init; }
    public int Respect { get; init; }
    public int Loyalty { get; init; }
    public int Anger { get; init; }
    public int Resentment { get; init; }
    public int Fear { get; init; }
    public int Jealousy { get; init; }
    public int Attraction { get; init; }
    public int Tension { get; init; }
    public int Importance { get; init; }

    public string Notes { get; init; } = "";
    public string UpdatedGameTime { get; init; } = "";
    public string UpdatedRealAt { get; init; } = "";

    public int ActiveReasonCount { get; set; }
    public int MemoryCount { get; set; }
    public int KnowledgeCount { get; set; }

    public int PositiveAverage =>
        (Love + Trust + Respect + Loyalty) / 4;

    public int FrictionAverage =>
        (Anger + Resentment + Fear + Jealousy + Tension) / 5;
}

public sealed class RelationshipReasonDossierItem
{
    public string Id { get; init; } = "";
    public string RelationshipId { get; init; } = "";
    public int? TargetCharacterId { get; init; }
    public string TargetName { get; init; } = "Unknown";
    public string EventId { get; init; } = "";
    public string ScoreName { get; init; } = "";
    public int Delta { get; init; }
    public string Reason { get; init; } = "";
    public string Interpretation { get; init; } = "";
    public bool IsStillActive { get; init; }
    public int Importance { get; init; }
    public string GameTime { get; init; } = "";
    public string CreatedRealAt { get; init; } = "";
}

public sealed class PersonalMemoryDossierItem
{
    public string Id { get; init; } = "";
    public int KnowerCharacterId { get; init; }
    public int? SubjectCharacterId { get; init; }
    public string SubjectName { get; init; } = "";
    public string EventId { get; init; } = "";
    public string MemoryType { get; init; } = "";
    public string MemoryText { get; init; } = "";
    public string Interpretation { get; init; } = "";
    public string EmotionalMeaning { get; init; } = "";
    public int Importance { get; init; }
    public int Strength { get; init; }
    public int Confidence { get; init; }
    public bool IsLockedPeak { get; init; }
    public string LearnedGameTime { get; init; } = "";
    public string LastUpdatedGameTime { get; init; } = "";
    public string CreatedRealAt { get; init; } = "";
}

public sealed class KnowledgeDossierItem
{
    public string Id { get; init; } = "";
    public int KnowerCharacterId { get; init; }
    public int? SubjectCharacterId { get; init; }
    public string SubjectName { get; init; } = "";
    public string EventId { get; init; } = "";
    public string KnowledgeType { get; init; } = "";
    public string WhatTheyKnow { get; init; } = "";
    public string HowTheyLearnedIt { get; init; } = "";
    public int? SourceCharacterId { get; init; }
    public string SourceName { get; init; } = "";
    public int Confidence { get; init; }
    public bool IsRumor { get; init; }
    public bool IsSecret { get; init; }
    public bool IsFalseBelief { get; init; }
    public string LearnedGameTime { get; init; } = "";
    public string LastUpdatedGameTime { get; init; } = "";
    public string CreatedRealAt { get; init; } = "";
}
