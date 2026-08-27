using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Golden NPC 2B-6: Eve Sinclair subjective memory + knowledge layer.
///
/// Rules:
/// - TRUE HISTORY remains canonical in HISTORY.
/// - Subjective records live in RELATIONSHIPS.
/// - Every seeded record points back to a canonical EventId.
/// - Eve only receives facts/interpretations she can reasonably know.
/// - Birth gets knowledge but no impossible first-person birth memory.
/// - No other participant receives copied knowledge automatically.
/// </summary>
public static class ProjectEveGoldenNpcSubjectivePopulation
{
    private const int EveId = 1;
    private const int AdamId = 2;
    private const int LisaId = 3;

    private const string BirthEvent =
        "eve-life-2001-birth-with-adam";

    private const string MoveToAdamEvent =
        "eve-life-adult-rents-room-at-adams";

    private const string ManagerEvent =
        "eve-life-adult-manager-sinclair-coffee";

    private const string StayInTownEvent =
        "eve-life-adult-chooses-to-stay-bellefontaine";

    public static void PopulateEveSubjectiveLayer()
    {
        ValidateCanonicalEvents();

        using var relationship = Open(
            ProjectEveDatabaseSetup.RelationshipDatabasePath);

        using var transaction = relationship.BeginTransaction();

        PopulateBirthKnowledge(relationship, transaction);
        PopulateMoveSubjective(relationship, transaction);
        PopulateManagerSubjective(relationship, transaction);
        PopulateStaySubjective(relationship, transaction);

        transaction.Commit();

        Console.WriteLine();
        Console.WriteLine("Golden NPC 2B-6 subjective layer populated for Eve Sinclair.");
        Console.WriteLine("  Birth event -> knowledge only (no impossible birth memory)");
        Console.WriteLine("  Adam-house event -> Eve memory + Eve knowledge");
        Console.WriteLine("  Sinclair Coffee manager event -> Eve memory + Eve knowledge");
        Console.WriteLine("  Stay-in-Bellefontaine event -> Eve memory + Eve knowledge");
        Console.WriteLine();
        Console.WriteLine("All records point to canonical TRUE HISTORY EventIds.");
        Console.WriteLine("No Adam/Lisa knowledge was auto-copied.");
    }

    private static void ValidateCanonicalEvents()
    {
        using var history = Open(
            ProjectEveDatabaseSetup.HistoryDatabasePath);

        ValidateEvent(history, BirthEvent);
        ValidateEvent(history, MoveToAdamEvent);
        ValidateEvent(history, ManagerEvent);
        ValidateEvent(history, StayInTownEvent);
    }

    private static void ValidateEvent(
        SqliteConnection connection,
        string eventId)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM WorldEvents
            WHERE EventId = $eventId;
            """;

        command.Parameters.AddWithValue("$eventId", eventId);

        if (Convert.ToInt64(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException(
                $"Canonical EventId '{eventId}' is missing. " +
                "Run Golden NPC 2B-5 first.");
        }
    }

    private static void PopulateBirthKnowledge(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertKnowledge(
            connection,
            transaction,
            id: "eve-knowledge-own-birth",
            subjectCharacterId: EveId,
            eventId: BirthEvent,
            knowledgeType: "PersonalHistory",
            whatTheyKnow:
                "Eve knows she was born on March 14, 2001 and that Adam Sinclair is her twin brother.",
            howTheyLearnedIt:
                "Family records, family history, and being raised with Adam as her twin.",
            sourceCharacterId: null,
            confidence: 100,
            isRumor: 0,
            isSecret: 0,
            isFalseBelief: 0);
    }

    private static void PopulateMoveSubjective(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertMemory(
            connection,
            transaction,
            id: "eve-memory-moving-into-adams",
            subjectCharacterId: AdamId,
            eventId: MoveToAdamEvent,
            memoryType: "LifeEvent",
            memoryText:
                "Eve remembers moving into Adam's house as a practical choice that also made their sibling bond part of everyday adult life.",
            interpretation:
                "Living under Adam's roof gives her support and familiarity, but also means independence and family boundaries are constantly close together.",
            emotionalMeaning:
                "Home feels secure and familiar, but never completely separate from family expectations.",
            importance: 78,
            strength: 82,
            confidence: 100,
            isLockedPeak: 0);

        UpsertKnowledge(
            connection,
            transaction,
            id: "eve-knowledge-adam-house-arrangement",
            subjectCharacterId: AdamId,
            eventId: MoveToAdamEvent,
            knowledgeType: "Household",
            whatTheyKnow:
                "Eve knows she rents a room in Adam's house for $100 per week and helps with food and supplies when she can.",
            howTheyLearnedIt:
                "Direct participation in the household arrangement.",
            sourceCharacterId: AdamId,
            confidence: 100,
            isRumor: 0,
            isSecret: 0,
            isFalseBelief: 0);
    }

    private static void PopulateManagerSubjective(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertMemory(
            connection,
            transaction,
            id: "eve-memory-becoming-manager-sinclair-coffee",
            subjectCharacterId: LisaId,
            eventId: ManagerEvent,
            memoryType: "CareerMilestone",
            memoryText:
                "Eve remembers taking on the manager role at Sinclair Coffee as both an achievement and an extension of her complicated mother-daughter relationship with Lisa.",
            interpretation:
                "Being good at the job matters to Eve, but working for her mother means work and family rarely stay completely separate.",
            emotionalMeaning:
                "Pride, competence, responsibility, and pressure are tied together.",
            importance: 90,
            strength: 88,
            confidence: 100,
            isLockedPeak: 1);

        UpsertKnowledge(
            connection,
            transaction,
            id: "eve-knowledge-sinclair-coffee-manager",
            subjectCharacterId: LisaId,
            eventId: ManagerEvent,
            knowledgeType: "Career",
            whatTheyKnow:
                "Eve knows she is the full-time front-of-house manager at Sinclair Coffee, which is owned by Lisa Sinclair.",
            howTheyLearnedIt:
                "Direct employment and daily management responsibility.",
            sourceCharacterId: LisaId,
            confidence: 100,
            isRumor: 0,
            isSecret: 0,
            isFalseBelief: 0);
    }

    private static void PopulateStaySubjective(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertMemory(
            connection,
            transaction,
            id: "eve-memory-choosing-to-stay-bellefontaine",
            subjectCharacterId: null,
            eventId: StayInTownEvent,
            memoryType: "LifeDecision",
            memoryText:
                "Eve remembers being close to taking a job out of town and ultimately staying in Bellefontaine.",
            interpretation:
                "The choice represents the tension between wanting a life that feels chosen and remaining tied to the people, work, and place that know her best.",
            emotionalMeaning:
                "Relief, uncertainty, attachment, and the lingering question of what another life might have looked like.",
            importance: 88,
            strength: 86,
            confidence: 100,
            isLockedPeak: 1);

        UpsertKnowledge(
            connection,
            transaction,
            id: "eve-knowledge-stayed-bellefontaine",
            subjectCharacterId: EveId,
            eventId: StayInTownEvent,
            knowledgeType: "PersonalDecision",
            whatTheyKnow:
                "Eve knows she nearly took a job out of town but chose to remain in Bellefontaine.",
            howTheyLearnedIt:
                "It was her own decision.",
            sourceCharacterId: EveId,
            confidence: 100,
            isRumor: 0,
            isSecret: 1,
            isFalseBelief: 0);
    }

    private static void UpsertMemory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        int? subjectCharacterId,
        string eventId,
        string memoryType,
        string memoryText,
        string interpretation,
        string emotionalMeaning,
        int importance,
        int strength,
        int confidence,
        int isLockedPeak)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO PersonalMemories
            (
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
            )
            VALUES
            (
                $id,
                $knowerCharacterId,
                $subjectCharacterId,
                $eventId,
                $memoryType,
                $memoryText,
                $interpretation,
                $emotionalMeaning,
                $importance,
                $strength,
                $confidence,
                $isLockedPeak,
                '',
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                KnowerCharacterId = excluded.KnowerCharacterId,
                SubjectCharacterId = excluded.SubjectCharacterId,
                EventId = excluded.EventId,
                MemoryType = excluded.MemoryType,
                MemoryText = excluded.MemoryText,
                Interpretation = excluded.Interpretation,
                EmotionalMeaning = excluded.EmotionalMeaning,
                Importance = excluded.Importance,
                Strength = excluded.Strength,
                Confidence = excluded.Confidence,
                IsLockedPeak = excluded.IsLockedPeak;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$knowerCharacterId", EveId);
        command.Parameters.AddWithValue(
            "$subjectCharacterId",
            subjectCharacterId.HasValue
                ? subjectCharacterId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$memoryType", memoryType);
        command.Parameters.AddWithValue("$memoryText", memoryText);
        command.Parameters.AddWithValue("$interpretation", interpretation);
        command.Parameters.AddWithValue("$emotionalMeaning", emotionalMeaning);
        command.Parameters.AddWithValue("$importance", importance);
        command.Parameters.AddWithValue("$strength", strength);
        command.Parameters.AddWithValue("$confidence", confidence);
        command.Parameters.AddWithValue("$isLockedPeak", isLockedPeak);

        command.ExecuteNonQuery();
    }

    private static void UpsertKnowledge(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        int? subjectCharacterId,
        string eventId,
        string knowledgeType,
        string whatTheyKnow,
        string howTheyLearnedIt,
        int? sourceCharacterId,
        int confidence,
        int isRumor,
        int isSecret,
        int isFalseBelief)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO KnowledgeItems
            (
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
            )
            VALUES
            (
                $id,
                $knowerCharacterId,
                $subjectCharacterId,
                $eventId,
                $knowledgeType,
                $whatTheyKnow,
                $howTheyLearnedIt,
                $sourceCharacterId,
                $confidence,
                $isRumor,
                $isSecret,
                $isFalseBelief,
                '',
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                KnowerCharacterId = excluded.KnowerCharacterId,
                SubjectCharacterId = excluded.SubjectCharacterId,
                EventId = excluded.EventId,
                KnowledgeType = excluded.KnowledgeType,
                WhatTheyKnow = excluded.WhatTheyKnow,
                HowTheyLearnedIt = excluded.HowTheyLearnedIt,
                SourceCharacterId = excluded.SourceCharacterId,
                Confidence = excluded.Confidence,
                IsRumor = excluded.IsRumor,
                IsSecret = excluded.IsSecret,
                IsFalseBelief = excluded.IsFalseBelief;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$knowerCharacterId", EveId);
        command.Parameters.AddWithValue(
            "$subjectCharacterId",
            subjectCharacterId.HasValue
                ? subjectCharacterId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$knowledgeType", knowledgeType);
        command.Parameters.AddWithValue("$whatTheyKnow", whatTheyKnow);
        command.Parameters.AddWithValue("$howTheyLearnedIt", howTheyLearnedIt);
        command.Parameters.AddWithValue(
            "$sourceCharacterId",
            sourceCharacterId.HasValue
                ? sourceCharacterId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue("$confidence", confidence);
        command.Parameters.AddWithValue("$isRumor", isRumor);
        command.Parameters.AddWithValue("$isSecret", isSecret);
        command.Parameters.AddWithValue("$isFalseBelief", isFalseBelief);

        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();

        return connection;
    }
}
