using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Golden NPC 2B-2: objective family + household canon for Eve Sinclair.
///
/// Canon used:
///   Eve Sinclair   = 1
///   Adam Sinclair  = 2 (brother)
///   Lisa Sinclair  = 3 (mother)
///   Edward Sinclair= 4 (father)
///
/// Ownership:
/// - FamilyLinks -> MAIN (objective structural truth)
/// - HouseholdMembers / FamilyFriendWeb -> RELATIONSHIPS
/// - Existing directed RelationshipStates are intentionally not replaced
///
/// Safe to run repeatedly: all inserted rows use stable keys and UPSERT.
/// </summary>
public static class ProjectEveGoldenNpcFamilyPopulation
{
    private const int EveId = 1;
    private const int AdamId = 2;
    private const int LisaId = 3;
    private const int EdwardId = 4;

    private const string SinclairHouseholdId = "household-sinclair-core";

    public static void PopulateEveFamily()
    {
        ValidateCoreFamily();

        PopulateObjectiveFamilyLinks();
        PopulateRelationshipFamilyWeb();

        Console.WriteLine();
        Console.WriteLine("Golden NPC 2B-2 populated for Eve Sinclair.");
        Console.WriteLine("  Objective family links:");
        Console.WriteLine("    Edward Sinclair -> father");
        Console.WriteLine("    Lisa Sinclair   -> mother");
        Console.WriteLine("    Adam Sinclair   -> brother");
        Console.WriteLine("  Household:");
        Console.WriteLine("    Edward, Lisa, Adam, Eve");
        Console.WriteLine("  Eve family/friend web:");
        Console.WriteLine("    Edward, Lisa, Adam");
        Console.WriteLine();
        Console.WriteLine("Existing directed RelationshipStates were preserved.");
    }

    private static void ValidateCoreFamily()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        ValidateCharacter(connection, EveId, "Eve Sinclair");
        ValidateCharacter(connection, AdamId, "Adam Sinclair");
        ValidateCharacter(connection, LisaId, "Lisa Sinclair");
        ValidateCharacter(connection, EdwardId, "Edward Sinclair");
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

        var value = command.ExecuteScalar();

        if (value is null || value == DBNull.Value)
            throw new InvalidOperationException(
                $"Expected Characters.Id={npcId} ({expectedName}), but the row is missing.");

        string actualName = Convert.ToString(value)?.Trim() ?? "";

        if (!string.Equals(
            actualName,
            expectedName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected Characters.Id={npcId} to be '{expectedName}', " +
                $"but found '{actualName}'. Population aborted.");
        }
    }

    private static void PopulateObjectiveFamilyLinks()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);
        using var transaction = connection.BeginTransaction();

        UpsertFamilyLink(
            connection,
            transaction,
            familyLinkId: "family-eve-edward-father",
            characterAId: EveId,
            characterBId: EdwardId,
            linkKind: "ParentChild",
            roleA: "Daughter",
            roleB: "Father",
            notes: "Canonical immediate-family link: Edward Sinclair is Eve Sinclair's father.");

        UpsertFamilyLink(
            connection,
            transaction,
            familyLinkId: "family-eve-lisa-mother",
            characterAId: EveId,
            characterBId: LisaId,
            linkKind: "ParentChild",
            roleA: "Daughter",
            roleB: "Mother",
            notes: "Canonical immediate-family link: Lisa Sinclair is Eve Sinclair's mother.");

        UpsertFamilyLink(
            connection,
            transaction,
            familyLinkId: "family-eve-adam-sibling",
            characterAId: EveId,
            characterBId: AdamId,
            linkKind: "Sibling",
            roleA: "Sister",
            roleB: "Brother",
            notes: "Canonical immediate-family link: Adam Sinclair is Eve Sinclair's brother.");

        transaction.Commit();
    }

    private static void UpsertFamilyLink(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string familyLinkId,
        int characterAId,
        int characterBId,
        string linkKind,
        string roleA,
        string roleB,
        string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO FamilyLinks
            (
                FamilyLinkId,
                WorldId,
                CharacterAId,
                CharacterBId,
                LinkKind,
                RoleA,
                RoleB,
                Status,
                StartedGameTime,
                EndedGameTime,
                StartedEventId,
                EndedEventId,
                Notes,
                CreatedRealAt,
                UpdatedRealAt
            )
            VALUES
            (
                $familyLinkId,
                'smalltown',
                $characterAId,
                $characterBId,
                $linkKind,
                $roleA,
                $roleB,
                'Active',
                '',
                '',
                '',
                '',
                $notes,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(FamilyLinkId) DO UPDATE SET
                WorldId = excluded.WorldId,
                CharacterAId = excluded.CharacterAId,
                CharacterBId = excluded.CharacterBId,
                LinkKind = excluded.LinkKind,
                RoleA = excluded.RoleA,
                RoleB = excluded.RoleB,
                Status = excluded.Status,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        command.Parameters.AddWithValue("$familyLinkId", familyLinkId);
        command.Parameters.AddWithValue("$characterAId", characterAId);
        command.Parameters.AddWithValue("$characterBId", characterBId);
        command.Parameters.AddWithValue("$linkKind", linkKind);
        command.Parameters.AddWithValue("$roleA", roleA);
        command.Parameters.AddWithValue("$roleB", roleB);
        command.Parameters.AddWithValue("$notes", notes);

        command.ExecuteNonQuery();
    }

    private static void PopulateRelationshipFamilyWeb()
    {
        using var connection = Open(ProjectEveDatabaseSetup.RelationshipDatabasePath);
        using var transaction = connection.BeginTransaction();

        // Objective household membership. This represents the core Sinclair family
        // household structure used for the Golden NPC baseline.
        UpsertHouseholdMember(
            connection,
            transaction,
            SinclairHouseholdId,
            EdwardId,
            "Father");

        UpsertHouseholdMember(
            connection,
            transaction,
            SinclairHouseholdId,
            LisaId,
            "Mother");

        UpsertHouseholdMember(
            connection,
            transaction,
            SinclairHouseholdId,
            AdamId,
            "Son");

        UpsertHouseholdMember(
            connection,
            transaction,
            SinclairHouseholdId,
            EveId,
            "Daughter");

        // Eve's own immediate-family web. Tier 1 = immediate/core family.
        UpsertFamilyWeb(
            connection,
            transaction,
            ownerNpcId: EveId,
            targetNpcId: EdwardId,
            webTier: 1,
            relationshipType: "Father",
            notes: "Immediate family: Eve Sinclair's father.");

        UpsertFamilyWeb(
            connection,
            transaction,
            ownerNpcId: EveId,
            targetNpcId: LisaId,
            webTier: 1,
            relationshipType: "Mother",
            notes: "Immediate family: Eve Sinclair's mother.");

        UpsertFamilyWeb(
            connection,
            transaction,
            ownerNpcId: EveId,
            targetNpcId: AdamId,
            webTier: 1,
            relationshipType: "Brother",
            notes: "Immediate family: Eve Sinclair's brother.");

        transaction.Commit();
    }

    private static void UpsertHouseholdMember(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string householdId,
        int npcId,
        string householdRole)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO HouseholdMembers
            (
                HouseholdId,
                NpcId,
                HouseholdRole,
                JoinedAt,
                LeftAt
            )
            VALUES
            (
                $householdId,
                $npcId,
                $householdRole,
                '',
                ''
            )
            ON CONFLICT(HouseholdId, NpcId) DO UPDATE SET
                HouseholdRole = excluded.HouseholdRole,
                LeftAt = '';
            """;

        command.Parameters.AddWithValue("$householdId", householdId);
        command.Parameters.AddWithValue("$npcId", npcId);
        command.Parameters.AddWithValue("$householdRole", householdRole);

        command.ExecuteNonQuery();
    }

    private static void UpsertFamilyWeb(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int ownerNpcId,
        int targetNpcId,
        int webTier,
        string relationshipType,
        string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO FamilyFriendWeb
            (
                OwnerNpcId,
                TargetNpcId,
                WebTier,
                RelationshipType,
                IsHistoryOnly,
                Source,
                Notes,
                CreatedAt,
                UpdatedAt
            )
            VALUES
            (
                $ownerNpcId,
                $targetNpcId,
                $webTier,
                $relationshipType,
                0,
                'GoldenNpc2B2',
                $notes,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(OwnerNpcId, TargetNpcId) DO UPDATE SET
                WebTier = excluded.WebTier,
                RelationshipType = excluded.RelationshipType,
                IsHistoryOnly = excluded.IsHistoryOnly,
                Source = excluded.Source,
                Notes = excluded.Notes,
                UpdatedAt = CURRENT_TIMESTAMP;
            """;

        command.Parameters.AddWithValue("$ownerNpcId", ownerNpcId);
        command.Parameters.AddWithValue("$targetNpcId", targetNpcId);
        command.Parameters.AddWithValue("$webTier", webTier);
        command.Parameters.AddWithValue("$relationshipType", relationshipType);
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
