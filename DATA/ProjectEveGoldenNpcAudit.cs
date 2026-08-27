using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Read-only Golden NPC coverage audit.
///
/// This audit is intentionally schema-tolerant:
/// - it never writes canon,
/// - it checks table/column existence before querying,
/// - missing legacy/current columns are reported instead of crashing.
/// </summary>
public static class ProjectEveGoldenNpcAudit
{
    public static void PrintToConsole(int npcId)
    {
        Console.WriteLine();
        Console.WriteLine("Golden NPC Representation Audit");
        Console.WriteLine("--------------------------------");

        using var main = Open(ProjectEveDatabaseSetup.MainDatabasePath);
        using var history = Open(ProjectEveDatabaseSetup.HistoryDatabasePath);
        using var relationships = Open(ProjectEveDatabaseSetup.RelationshipDatabasePath);
        using var locations = Open(ProjectEveDatabaseSetup.LocationDatabasePath);

        string name = ReadCharacterName(main, npcId);

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine($"NPC {npcId} was not found in MAIN.Characters.");
            return;
        }

        Console.WriteLine($"NPC: {npcId} - {name}");
        Console.WriteLine();

        PrintIdentity(main, npcId);

        Console.WriteLine();
        Console.WriteLine("MIND / BODY / PERSONALITY");
        PrintDomain("Physical profile", Count(main, "NpcPhysicalProfiles", "NpcId", npcId));
        PrintDomain("Cognition profile", Count(main, "NpcCognitionProfiles", "NpcId", npcId));
        PrintDomain("Persona", Count(main, "NpcPersonas", "NpcId", npcId));
        PrintDomain("Archetype", Count(main, "NpcArchetypes", "NpcId", npcId));
        PrintDomain("Fast / Mid / Slow traits", Count(main, "NpcTraitValues", "NpcId", npcId));
        PrintDomain("Trait control", Count(main, "NpcTraitControl", "NpcId", npcId));
        PrintDomain("Habits / interests", Count(main, "NpcHabitsAndInterests", "NpcId", npcId));
        PrintDomain("Social behavior", Count(main, "NpcSocialBehavior", "NpcId", npcId));

        Console.WriteLine();
        Console.WriteLine("FORMATION / PROFESSIONAL");
        PrintDomain("Education records", Count(main, "NpcEducationRecords", "NpcId", npcId));
        PrintDomain("Professional profile", Count(main, "NpcProfessionalProfiles", "NpcId", npcId));
        PrintDomain("Qualifications", Count(main, "NpcProfessionalQualifications", "NpcId", npcId));
        PrintDomain("Professional competencies", Count(main, "NpcProfessionalCompetencies", "NpcId", npcId));
        PrintDomain("Current job profile", CountAnyId(main, "JobProfile", npcId, "NpcId", "CharacterId", "Id"));

        Console.WriteLine();
        Console.WriteLine("FAMILY / SOCIAL STRUCTURE");
        PrintDomain(
            "Objective family links",
            CountEitherAny(
                main,
                "FamilyLinks",
                npcId,
                new[] { "CharacterAId", "NpcAId", "PersonAId" },
                new[] { "CharacterBId", "NpcBId", "PersonBId" }));

        PrintDomain("Household memberships", CountAnyId(relationships, "HouseholdMembers", npcId, "NpcId", "CharacterId"));

        PrintDomain(
            "Family / friend web",
            CountEitherAny(
                relationships,
                "FamilyFriendWeb",
                npcId,
                new[] { "OwnerNpcId", "SourceCharacterId", "SourceNpcId" },
                new[] { "TargetNpcId", "TargetCharacterId" }));

        PrintDomain(
            "Directed relationship states",
            CountEitherAny(
                relationships,
                "RelationshipStates",
                npcId,
                new[] { "SourceCharacterId", "SourceNpcId", "NpcId" },
                new[] { "TargetCharacterId", "TargetNpcId" }));

        Console.WriteLine();
        Console.WriteLine("PROPERTY / COMMUNICATION");
        PrintDomain("Phones", CountAnyId(main, "NpcPhones", npcId, "NpcId", "CharacterId"));

        PrintDomain(
            "Vehicles owned/driven",
            CountEitherAny(
                main,
                "Vehicles",
                npcId,
                new[] { "RegisteredOwnerNpcId", "OwnerNpcId", "OwnerCharacterId" },
                new[] { "PrimaryDriverNpcId", "DriverNpcId", "DriverCharacterId" }));

        PrintDomain("Financial accounts", CountOwner(main, "FinancialAccounts", npcId));
        PrintDomain("Financial obligations", CountOwner(main, "FinancialObligations", npcId));

        Console.WriteLine();
        Console.WriteLine("OBJECTIVE HISTORY");
        PrintDomain("World event participation", CountAnyId(history, "EventParticipants", npcId, "CharacterId", "NpcId"));

        PrintDomain(
            "Communications sent/received",
            CountEitherAny(
                history,
                "Communications",
                npcId,
                new[] { "FromCharacterId", "SenderCharacterId", "FromNpcId", "SenderNpcId" },
                new[] { "ToCharacterId", "RecipientCharacterId", "ToNpcId", "RecipientNpcId" }));

        PrintDomain("Scene actions", CountAnyId(history, "SceneActions", npcId, "CharacterId", "NpcId", "ActorCharacterId"));
        PrintDomain("Financial transactions", CountOwner(history, "FinancialTransactions", npcId));

        Console.WriteLine();
        Console.WriteLine("SUBJECTIVE MEMORY / KNOWLEDGE");
        PrintDomain("Personal memories as knower", CountAnyId(relationships, "PersonalMemories", npcId, "KnowerCharacterId", "KnowerNpcId"));
        PrintDomain("Personal memories as subject", CountAnyId(relationships, "PersonalMemories", npcId, "SubjectCharacterId", "SubjectNpcId"));
        PrintDomain("Knowledge items as knower", CountAnyId(relationships, "KnowledgeItems", npcId, "KnowerCharacterId", "KnowerNpcId"));
        PrintDomain("Knowledge items as subject", CountAnyId(relationships, "KnowledgeItems", npcId, "SubjectCharacterId", "SubjectNpcId"));
        PrintDomain("Relationship reasons", CountRelationshipReasons(relationships, npcId));

        Console.WriteLine();
        Console.WriteLine("PLACE");
        PrintDomain("Location links", CountAnyId(locations, "LocationNpcLinks", npcId, "CharacterId", "NpcId"));
        PrintDomain("Recorded location visits", CountAnyId(locations, "LocationVisits", npcId, "CharacterId", "NpcId"));

        Console.WriteLine();
        Console.WriteLine("Interpretation:");
        Console.WriteLine("  PRESENT = at least one canonical record exists.");
        Console.WriteLine("  EMPTY   = schema exists but this NPC has no record yet.");
        Console.WriteLine("  N/A     = table/usable owner column is not present in this database.");
        Console.WriteLine();
        Console.WriteLine("This audit is read-only. It does not create missing canon.");
    }

    private static void PrintIdentity(SqliteConnection main, int npcId)
    {
        Console.WriteLine("IDENTITY / CURRENT STATE");

        PrintField(main, npcId, "Name", "Name");
        PrintField(main, npcId, "Nickname", "Nickname");
        PrintField(main, npcId, "Age", "Age");

        string birthYear = ScalarCharacterField(main, npcId, "BirthYear");
        string birthMonth = ScalarCharacterField(main, npcId, "BirthMonth");
        string birthDay = ScalarCharacterField(main, npcId, "BirthDay");

        Console.WriteLine($"  {"Birth",-22} {FormatBirth(birthYear, birthMonth, birthDay)}");

        PrintField(main, npcId, "Gender", "Gender");
        PrintField(main, npcId, "Occupation", "Occupation");

        // Employer is compatibility-sensitive in older live DBs.
        if (ColumnExists(main, "Characters", "Employer"))
            PrintField(main, npcId, "Employer", "Employer");
        else
            Console.WriteLine($"  {"Employer",-22} (column not present)");

        PrintField(main, npcId, "Current location", "CurrentLocationId");
        PrintField(main, npcId, "Home location", "HomeLocationId");
        PrintField(main, npcId, "Work location", "WorkLocationId");
        PrintField(main, npcId, "Hometown", "Hometown");
        PrintField(main, npcId, "Address", "Address");
        PrintField(main, npcId, "Status", "Status");
        PrintField(main, npcId, "Global tier", "Tier");
    }

    private static void PrintField(
        SqliteConnection main,
        int npcId,
        string label,
        string column)
    {
        string value = ScalarCharacterField(main, npcId, column);
        Console.WriteLine($"  {label,-22} {value}");
    }

    private static string ScalarCharacterField(
        SqliteConnection main,
        int npcId,
        string column)
    {
        if (!TableExists(main, "Characters") ||
            !ColumnExists(main, "Characters", column))
            return "(column not present)";

        using var cmd = main.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);

        object? value = cmd.ExecuteScalar();

        if (value is null || value == DBNull.Value)
            return "(null)";

        string text = Convert.ToString(value) ?? "";
        return string.IsNullOrWhiteSpace(text) ? "(blank)" : text;
    }

    private static string FormatBirth(string year, string month, string day)
    {
        static string Normalize(string value)
            => value.StartsWith("(", StringComparison.Ordinal) ? "?" : value;

        return $"{Normalize(year)}-{Normalize(month)}-{Normalize(day)}";
    }

    private static void PrintDomain(string title, int? count)
    {
        string status = count.HasValue
            ? (count.Value > 0 ? "PRESENT" : "EMPTY")
            : "N/A";

        string countText = count.HasValue
            ? count.Value.ToString()
            : "-";

        Console.WriteLine($"  {title,-32} {status,-8} {countText,5}");
    }

    private static int? Count(
        SqliteConnection connection,
        string table,
        string column,
        int npcId)
    {
        if (!TableExists(connection, table) ||
            !ColumnExists(connection, table, column))
            return null;

        return SafeCount(connection, $"SELECT COUNT(*) FROM {table} WHERE {column} = $id;", npcId);
    }

    private static int? CountAnyId(
        SqliteConnection connection,
        string table,
        int npcId,
        params string[] candidateColumns)
    {
        if (!TableExists(connection, table))
            return null;

        string? column = candidateColumns.FirstOrDefault(c => ColumnExists(connection, table, c));

        if (string.IsNullOrWhiteSpace(column))
            return null;

        return SafeCount(connection, $"SELECT COUNT(*) FROM {table} WHERE {column} = $id;", npcId);
    }

    private static int? CountEitherAny(
        SqliteConnection connection,
        string table,
        int npcId,
        string[] firstCandidates,
        string[] secondCandidates)
    {
        if (!TableExists(connection, table))
            return null;

        string? first = firstCandidates.FirstOrDefault(c => ColumnExists(connection, table, c));
        string? second = secondCandidates.FirstOrDefault(c => ColumnExists(connection, table, c));

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return null;

        return SafeCount(
            connection,
            $"SELECT COUNT(*) FROM {table} WHERE {first} = $id OR {second} = $id;",
            npcId);
    }

    private static int? CountOwner(
        SqliteConnection connection,
        string table,
        int npcId)
    {
        if (!TableExists(connection, table))
            return null;

        string[] directOwnerColumns =
        {
            "OwnerNpcId",
            "OwnerCharacterId",
            "NpcId",
            "CharacterId"
        };

        string? direct = directOwnerColumns.FirstOrDefault(c => ColumnExists(connection, table, c));

        if (!string.IsNullOrWhiteSpace(direct))
            return SafeCount(connection, $"SELECT COUNT(*) FROM {table} WHERE {direct} = $id;", npcId);

        if (ColumnExists(connection, table, "OwnerId"))
        {
            if (ColumnExists(connection, table, "OwnerType"))
            {
                return SafeCount(
                    connection,
                    $"SELECT COUNT(*) FROM {table} WHERE OwnerId = $id AND upper(OwnerType) = 'NPC';",
                    npcId);
            }

            return SafeCount(connection, $"SELECT COUNT(*) FROM {table} WHERE OwnerId = $id;", npcId);
        }

        return null;
    }

    private static int? CountRelationshipReasons(
        SqliteConnection relationshipDb,
        int npcId)
    {
        if (!TableExists(relationshipDb, "RelationshipReasons") ||
            !TableExists(relationshipDb, "RelationshipStates"))
            return null;

        if (!ColumnExists(relationshipDb, "RelationshipReasons", "RelationshipId") ||
            !ColumnExists(relationshipDb, "RelationshipStates", "RelationshipId"))
            return null;

        string? source =
            new[] { "SourceCharacterId", "SourceNpcId", "NpcId" }
            .FirstOrDefault(c => ColumnExists(relationshipDb, "RelationshipStates", c));

        string? target =
            new[] { "TargetCharacterId", "TargetNpcId" }
            .FirstOrDefault(c => ColumnExists(relationshipDb, "RelationshipStates", c));

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            return null;

        return SafeCount(
            relationshipDb,
            $"""
            SELECT COUNT(*)
            FROM RelationshipReasons rr
            INNER JOIN RelationshipStates rs
                ON rs.RelationshipId = rr.RelationshipId
            WHERE rs.{source} = $id
               OR rs.{target} = $id;
            """,
            npcId);
    }

    private static int? SafeCount(
        SqliteConnection connection,
        string sql,
        int npcId)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", npcId);

            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch
        {
            return null;
        }
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND lower(name) = lower($name);
            """;
        cmd.Parameters.AddWithValue("$name", table);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        string table,
        string column)
    {
        if (!TableExists(connection, table))
            return false;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            string name = r.IsDBNull(1) ? "" : r.GetString(1);

            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ReadCharacterName(SqliteConnection main, int npcId)
    {
        if (!TableExists(main, "Characters") ||
            !ColumnExists(main, "Characters", "Name"))
            return "";

        using var cmd = main.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);

        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        return connection;
    }
}

