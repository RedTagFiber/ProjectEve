using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Foundation schema for causal life-history metadata.
///
/// WorldEvents remains the canonical objective event table in HISTORY.
/// This extension adds life-stage/importance metadata and a many-to-many
/// causal graph without creating a second history system.
/// </summary>
public static class ProjectEveLifeHistoryFoundationSchema
{
    public static void Ensure()
    {
        EnsureWorldEventLifeMetadata();
        EnsureEventCausalLinks();
    }

    private static void EnsureWorldEventLifeMetadata()
    {
        using var connection = Open(ProjectEveDatabaseSetup.HistoryDatabasePath);

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "LifeStage",
            "TEXT NOT NULL DEFAULT ''");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "Importance",
            "INTEGER NOT NULL DEFAULT 50 CHECK (Importance BETWEEN 0 AND 100)");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "IsMajorLifeEvent",
            "INTEGER NOT NULL DEFAULT 0 CHECK (IsMajorLifeEvent IN (0,1))");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "EmotionalValence",
            "INTEGER NOT NULL DEFAULT 0 CHECK (EmotionalValence BETWEEN -100 AND 100)");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "ApproximateYear",
            "INTEGER NULL");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "ApproximateAge",
            "INTEGER NULL");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "DatePrecision",
            "TEXT NOT NULL DEFAULT 'Exact'");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "LifeArcId",
            "TEXT NOT NULL DEFAULT ''");

        AddColumnIfMissing(
            connection,
            "WorldEvents",
            "SequenceOrder",
            "INTEGER NULL");

        Execute(connection, """
            CREATE INDEX IF NOT EXISTS IX_WorldEvents_LifeStage
                ON WorldEvents(LifeStage, GameTime);

            CREATE INDEX IF NOT EXISTS IX_WorldEvents_MajorLifeEvent
                ON WorldEvents(IsMajorLifeEvent, Importance, GameTime);

            CREATE INDEX IF NOT EXISTS IX_WorldEvents_LifeArc
                ON WorldEvents(LifeArcId, SequenceOrder, GameTime);
            """);
    }

    private static void EnsureEventCausalLinks()
    {
        using var connection = Open(ProjectEveDatabaseSetup.HistoryDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS EventCausalLinks
            (
                SourceEventId TEXT NOT NULL,
                TargetEventId TEXT NOT NULL,

                RelationType TEXT NOT NULL DEFAULT 'Caused',

                Strength INTEGER NOT NULL DEFAULT 50
                    CHECK (Strength BETWEEN 0 AND 100),

                Notes TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                PRIMARY KEY
                (
                    SourceEventId,
                    TargetEventId,
                    RelationType
                ),

                CHECK (SourceEventId <> TargetEventId),

                FOREIGN KEY (SourceEventId)
                    REFERENCES WorldEvents(EventId)
                    ON DELETE CASCADE,

                FOREIGN KEY (TargetEventId)
                    REFERENCES WorldEvents(EventId)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_EventCausalLinks_Source
                ON EventCausalLinks(SourceEventId, RelationType);

            CREATE INDEX IF NOT EXISTS IX_EventCausalLinks_Target
                ON EventCausalLinks(TargetEventId, RelationType);
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
