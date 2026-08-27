using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Foundation schema for canonical person-to-person life structure.
///
/// Ownership rules:
/// - FamilyLinks is objective current/structural truth and therefore lives in MAIN.
/// - RelationshipStates remains subjective directed relationship truth in RELATIONSHIPS.
/// - Objective changes that caused or ended a family/relationship state belong in HISTORY
///   and are referenced here by EventId.
/// </summary>
public static class ProjectEvePersonFoundationSchema
{
    public static void Ensure()
    {
        EnsureFamilyLinks();
        EnsureRelationshipStateExtensions();
    }

    private static void EnsureFamilyLinks()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS FamilyLinks
            (
                FamilyLinkId TEXT PRIMARY KEY,
                WorldId TEXT NOT NULL DEFAULT 'smalltown',

                CharacterAId INTEGER NOT NULL,
                CharacterBId INTEGER NOT NULL,

                LinkKind TEXT NOT NULL DEFAULT '',
                RoleA TEXT NOT NULL DEFAULT '',
                RoleB TEXT NOT NULL DEFAULT '',

                Status TEXT NOT NULL DEFAULT 'Active',

                StartedGameTime TEXT NOT NULL DEFAULT '',
                EndedGameTime TEXT NOT NULL DEFAULT '',

                StartedEventId TEXT NOT NULL DEFAULT '',
                EndedEventId TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                CHECK (CharacterAId <> CharacterBId),

                FOREIGN KEY (CharacterAId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (CharacterBId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_FamilyLinks_CharacterA
                ON FamilyLinks(CharacterAId, Status);

            CREATE INDEX IF NOT EXISTS IX_FamilyLinks_CharacterB
                ON FamilyLinks(CharacterBId, Status);

            CREATE INDEX IF NOT EXISTS IX_FamilyLinks_Pair
                ON FamilyLinks(CharacterAId, CharacterBId, LinkKind);
            """);
    }

    private static void EnsureRelationshipStateExtensions()
    {
        using var connection = Open(ProjectEveDatabaseSetup.RelationshipDatabasePath);

        AddColumnIfMissing(
            connection,
            "RelationshipStates",
            "PersonalImportanceTier",
            "INTEGER NOT NULL DEFAULT 3 CHECK (PersonalImportanceTier BETWEEN 1 AND 5)");

        AddColumnIfMissing(
            connection,
            "RelationshipStates",
            "RelationshipStatus",
            "TEXT NOT NULL DEFAULT 'Active'");

        AddColumnIfMissing(
            connection,
            "RelationshipStates",
            "LastingInfluence",
            "INTEGER NOT NULL DEFAULT 50 CHECK (LastingInfluence BETWEEN 0 AND 100)");

        AddColumnIfMissing(
            connection,
            "RelationshipStates",
            "FirstKnownEventId",
            "TEXT NOT NULL DEFAULT ''");

        AddColumnIfMissing(
            connection,
            "RelationshipStates",
            "LastMajorChangeEventId",
            "TEXT NOT NULL DEFAULT ''");

        Execute(connection, """
            CREATE INDEX IF NOT EXISTS IX_RelationshipStates_SourcePersonalTier
                ON RelationshipStates(SourceCharacterId, PersonalImportanceTier);

            CREATE INDEX IF NOT EXISTS IX_RelationshipStates_SourceStatus
                ON RelationshipStates(SourceCharacterId, RelationshipStatus);
            """);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");

        return connection;
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string sqlDefinition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName});";

            using var reader = check.ExecuteReader();

            while (reader.Read())
            {
                string existing = reader.IsDBNull(1)
                    ? ""
                    : reader.GetString(1);

                if (string.Equals(
                    existing,
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlDefinition};";
        alter.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
