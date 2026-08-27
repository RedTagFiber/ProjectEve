using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Golden NPC 2B-5: first canonical causal-life-history pass for Eve Sinclair.
///
/// Design:
/// - WorldEvents remains TRUE HISTORY.
/// - EventParticipants links objective participants.
/// - EventFacts stores locked objective details.
/// - EventCausalLinks represents meaningful causal/influence relationships.
/// - No subjective memory/knowledge is created here; that belongs in 2B-6.
///
/// Only established/high-confidence Eve canon is seeded.
/// </summary>
public static class ProjectEveGoldenNpcLifeHistoryPopulation
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

    public static void PopulateEveLifeHistory()
    {
        ValidateCorePeople();

        using var history = Open(ProjectEveDatabaseSetup.HistoryDatabasePath);
        using var transaction = history.BeginTransaction();

        UpsertBirthEvent(history, transaction);
        UpsertMoveToAdamEvent(history, transaction);
        UpsertManagerEvent(history, transaction);
        UpsertStayInTownEvent(history, transaction);

        PopulateParticipants(history, transaction);
        PopulateFacts(history, transaction);
        PopulateCausalLinks(history, transaction);

        transaction.Commit();

        Console.WriteLine();
        Console.WriteLine("Golden NPC 2B-5 canonical life history populated for Eve Sinclair.");
        Console.WriteLine("  Birth with twin brother Adam");
        Console.WriteLine("  Adult household move: rents room at Adam's house");
        Console.WriteLine("  Adult career: becomes manager at Sinclair Coffee under Lisa");
        Console.WriteLine("  Adult choice: remains in Bellefontaine after nearly taking an out-of-town job");
        Console.WriteLine("  Objective participants + locked facts + causal links");
        Console.WriteLine();
        Console.WriteLine("No subjective memories or knowledge were created in this phase.");
    }

    private static void ValidateCorePeople()
    {
        using var main = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        ValidateCharacter(main, EveId, "Eve Sinclair");
        ValidateCharacter(main, AdamId, "Adam Sinclair");
        ValidateCharacter(main, LisaId, "Lisa Sinclair");
    }

    private static void ValidateCharacter(
        SqliteConnection connection,
        int npcId,
        string expectedName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Name
            FROM Characters
            WHERE Id = $npcId;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);

        string actual = Convert.ToString(command.ExecuteScalar())?.Trim() ?? "";

        if (!string.Equals(
            actual,
            expectedName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected Characters.Id={npcId} to be '{expectedName}', " +
                $"but found '{actual}'. Life-history population aborted.");
        }
    }

    private static void UpsertBirthEvent(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertWorldEvent(
            connection,
            transaction,
            BirthEvent,
            eventType: "Birth",
            title: "Birth of Eve and Adam Sinclair",
            summary:
                "Eve Sinclair and her twin brother Adam Sinclair are born in Bellefontaine, Ohio.",
            details:
                "Canonical birth event for the Sinclair twins. Eve's established birth date is March 14, 2001 at 09:00.",
            locationId: "",
            placeText: "Bellefontaine, Ohio",
            status: "Closed",
            gameTime: "2001-03-14T09:00:00",
            source: "GoldenNpc2B5",
            lifeStage: "Birth",
            importance: 100,
            isMajorLifeEvent: 1,
            emotionalValence: 60,
            approximateYear: 2001,
            approximateAge: 0,
            datePrecision: "Exact",
            lifeArcId: "eve-life-foundation",
            sequenceOrder: 1);
    }

    private static void UpsertMoveToAdamEvent(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertWorldEvent(
            connection,
            transaction,
            MoveToAdamEvent,
            eventType: "ResidenceChange",
            title: "Eve rents a room at Adam's house",
            summary:
                "As an adult, Eve rents a room in Adam Sinclair's house in Bellefontaine.",
            details:
                "Established canon places Eve under Adam's roof as a renter. Exact move-in date is not currently established.",
            locationId: "",
            placeText: "Adam Sinclair's house, Bellefontaine, Ohio",
            status: "Closed",
            gameTime: "",
            source: "GoldenNpc2B5",
            lifeStage: "YoungAdult",
            importance: 75,
            isMajorLifeEvent: 1,
            emotionalValence: 25,
            approximateYear: null,
            approximateAge: null,
            datePrecision: "Unknown",
            lifeArcId: "eve-adult-independence",
            sequenceOrder: 1);
    }

    private static void UpsertManagerEvent(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertWorldEvent(
            connection,
            transaction,
            ManagerEvent,
            eventType: "CareerMilestone",
            title: "Eve becomes manager at Sinclair Coffee",
            summary:
                "Eve takes on the manager role at Sinclair Coffee, the coffee shop owned by her mother Lisa Sinclair.",
            details:
                "Established current-life canon: Eve is the full-time front-of-house manager at Sinclair Coffee under Lisa Sinclair. Exact promotion/hire date is not currently locked.",
            locationId: "WP_COFFEE_001",
            placeText: "Sinclair Coffee, Bellefontaine, Ohio",
            status: "Closed",
            gameTime: "",
            source: "GoldenNpc2B5",
            lifeStage: "YoungAdult",
            importance: 90,
            isMajorLifeEvent: 1,
            emotionalValence: 45,
            approximateYear: null,
            approximateAge: null,
            datePrecision: "Unknown",
            lifeArcId: "eve-career-sinclair-coffee",
            sequenceOrder: 1);
    }

    private static void UpsertStayInTownEvent(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertWorldEvent(
            connection,
            transaction,
            StayInTownEvent,
            eventType: "LifeDecision",
            title: "Eve chooses to remain in Bellefontaine",
            summary:
                "After nearly taking a job out of town, Eve remains in Bellefontaine.",
            details:
                "Established personal canon says Eve almost took a job out of town but stayed. Exact employer, destination, and date are intentionally not invented.",
            locationId: "",
            placeText: "Bellefontaine, Ohio",
            status: "Closed",
            gameTime: "",
            source: "GoldenNpc2B5",
            lifeStage: "YoungAdult",
            importance: 85,
            isMajorLifeEvent: 1,
            emotionalValence: 15,
            approximateYear: null,
            approximateAge: null,
            datePrecision: "Unknown",
            lifeArcId: "eve-adult-bellefontaine",
            sequenceOrder: 1);
    }

    private static void PopulateParticipants(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertParticipant(connection, transaction, BirthEvent, EveId, "Born");
        UpsertParticipant(connection, transaction, BirthEvent, AdamId, "Born");

        UpsertParticipant(connection, transaction, MoveToAdamEvent, EveId, "Renter");
        UpsertParticipant(connection, transaction, MoveToAdamEvent, AdamId, "Homeowner / sibling");

        UpsertParticipant(connection, transaction, ManagerEvent, EveId, "Manager");
        UpsertParticipant(connection, transaction, ManagerEvent, LisaId, "Owner / mother");

        UpsertParticipant(connection, transaction, StayInTownEvent, EveId, "Decision-maker");
    }

    private static void PopulateFacts(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        EnsureFact(
            connection, transaction, BirthEvent,
            "BirthDate",
            "Eve Sinclair's birth date is March 14, 2001.",
            1);

        EnsureFact(
            connection, transaction, BirthEvent,
            "Sibling",
            "Adam Sinclair is Eve Sinclair's twin brother.",
            1);

        EnsureFact(
            connection, transaction, MoveToAdamEvent,
            "Residence",
            "Eve rents a room in Adam Sinclair's house.",
            1);

        EnsureFact(
            connection, transaction, MoveToAdamEvent,
            "Rent",
            "Eve's established rent is $100 per week, with additional help toward food and supplies when she can.",
            1);

        EnsureFact(
            connection, transaction, ManagerEvent,
            "Employer",
            "Eve manages Sinclair Coffee.",
            1);

        EnsureFact(
            connection, transaction, ManagerEvent,
            "Ownership",
            "Lisa Sinclair owns Sinclair Coffee.",
            1);

        EnsureFact(
            connection, transaction, ManagerEvent,
            "Workplace",
            "Sinclair Coffee uses canonical workplace id WP_COFFEE_001.",
            1);

        EnsureFact(
            connection, transaction, StayInTownEvent,
            "Decision",
            "Eve nearly took a job out of town but stayed in Bellefontaine.",
            1);
    }

    private static void PopulateCausalLinks(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertCausalLink(
            connection,
            transaction,
            StayInTownEvent,
            ManagerEvent,
            "Supported",
            70,
            "Choosing to remain in Bellefontaine supports the continuation of Eve's local career path at Sinclair Coffee; exact chronology between the original management appointment and the later decision is not asserted.");

        UpsertCausalLink(
            connection,
            transaction,
            StayInTownEvent,
            MoveToAdamEvent,
            "Reinforced",
            55,
            "Remaining in Bellefontaine reinforced Eve's existing local household arrangement with Adam; this does not assert that the decision originally caused the move-in.");
    }

    private static void UpsertWorldEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        string eventType,
        string title,
        string summary,
        string details,
        string locationId,
        string placeText,
        string status,
        string gameTime,
        string source,
        string lifeStage,
        int importance,
        int isMajorLifeEvent,
        int emotionalValence,
        int? approximateYear,
        int? approximateAge,
        string datePrecision,
        string lifeArcId,
        int? sequenceOrder)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO WorldEvents
            (
                EventId,
                WorldId,
                EventType,
                Title,
                Summary,
                Details,
                LocationId,
                PlaceText,
                Channel,
                Status,
                GameTime,
                GameTimeEnd,
                Source,
                Confidence,
                LifeStage,
                Importance,
                IsMajorLifeEvent,
                EmotionalValence,
                ApproximateYear,
                ApproximateAge,
                DatePrecision,
                LifeArcId,
                SequenceOrder,
                CreatedRealAt,
                UpdatedRealAt
            )
            VALUES
            (
                $eventId,
                'smalltown',
                $eventType,
                $title,
                $summary,
                $details,
                $locationId,
                $placeText,
                'LifeHistory',
                $status,
                $gameTime,
                '',
                $source,
                100,
                $lifeStage,
                $importance,
                $isMajorLifeEvent,
                $emotionalValence,
                $approximateYear,
                $approximateAge,
                $datePrecision,
                $lifeArcId,
                $sequenceOrder,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(EventId) DO UPDATE SET
                EventType = excluded.EventType,
                Title = excluded.Title,
                Summary = excluded.Summary,
                Details = excluded.Details,
                LocationId = excluded.LocationId,
                PlaceText = excluded.PlaceText,
                Channel = excluded.Channel,
                Status = excluded.Status,
                GameTime = excluded.GameTime,
                Source = excluded.Source,
                Confidence = excluded.Confidence,
                LifeStage = excluded.LifeStage,
                Importance = excluded.Importance,
                IsMajorLifeEvent = excluded.IsMajorLifeEvent,
                EmotionalValence = excluded.EmotionalValence,
                ApproximateYear = excluded.ApproximateYear,
                ApproximateAge = excluded.ApproximateAge,
                DatePrecision = excluded.DatePrecision,
                LifeArcId = excluded.LifeArcId,
                SequenceOrder = excluded.SequenceOrder,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$details", details);
        command.Parameters.AddWithValue("$locationId", locationId);
        command.Parameters.AddWithValue("$placeText", placeText);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$gameTime", gameTime);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$lifeStage", lifeStage);
        command.Parameters.AddWithValue("$importance", importance);
        command.Parameters.AddWithValue("$isMajorLifeEvent", isMajorLifeEvent);
        command.Parameters.AddWithValue("$emotionalValence", emotionalValence);
        command.Parameters.AddWithValue(
            "$approximateYear",
            approximateYear.HasValue ? approximateYear.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "$approximateAge",
            approximateAge.HasValue ? approximateAge.Value : DBNull.Value);
        command.Parameters.AddWithValue("$datePrecision", datePrecision);
        command.Parameters.AddWithValue("$lifeArcId", lifeArcId);
        command.Parameters.AddWithValue(
            "$sequenceOrder",
            sequenceOrder.HasValue ? sequenceOrder.Value : DBNull.Value);

        command.ExecuteNonQuery();
    }

    private static void UpsertParticipant(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        int characterId,
        string role)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO EventParticipants
            (
                EventId,
                CharacterId,
                Role
            )
            VALUES
            (
                $eventId,
                $characterId,
                $role
            )
            ON CONFLICT(EventId, CharacterId) DO UPDATE SET
                Role = excluded.Role;
            """;

        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$role", role);
        command.ExecuteNonQuery();
    }

    private static void EnsureFact(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        string factType,
        string factText,
        int isLocked)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;

        check.CommandText = """
            SELECT COUNT(*)
            FROM EventFacts
            WHERE EventId = $eventId
              AND FactType = $factType
              AND FactText = $factText;
            """;

        check.Parameters.AddWithValue("$eventId", eventId);
        check.Parameters.AddWithValue("$factType", factType);
        check.Parameters.AddWithValue("$factText", factText);

        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            return;

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;

        insert.CommandText = """
            INSERT INTO EventFacts
            (
                EventId,
                FactType,
                FactText,
                IsLocked
            )
            VALUES
            (
                $eventId,
                $factType,
                $factText,
                $isLocked
            );
            """;

        insert.Parameters.AddWithValue("$eventId", eventId);
        insert.Parameters.AddWithValue("$factType", factType);
        insert.Parameters.AddWithValue("$factText", factText);
        insert.Parameters.AddWithValue("$isLocked", isLocked);

        insert.ExecuteNonQuery();
    }

    private static void UpsertCausalLink(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceEventId,
        string targetEventId,
        string relationType,
        int strength,
        string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO EventCausalLinks
            (
                SourceEventId,
                TargetEventId,
                RelationType,
                Strength,
                Notes,
                CreatedRealAt
            )
            VALUES
            (
                $sourceEventId,
                $targetEventId,
                $relationType,
                $strength,
                $notes,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(SourceEventId, TargetEventId, RelationType) DO UPDATE SET
                Strength = excluded.Strength,
                Notes = excluded.Notes;
            """;

        command.Parameters.AddWithValue("$sourceEventId", sourceEventId);
        command.Parameters.AddWithValue("$targetEventId", targetEventId);
        command.Parameters.AddWithValue("$relationType", relationType);
        command.Parameters.AddWithValue("$strength", strength);
        command.Parameters.AddWithValue("$notes", notes);

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
