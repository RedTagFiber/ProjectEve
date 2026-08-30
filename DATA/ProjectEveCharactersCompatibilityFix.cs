using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Repairs older live Characters tables so they match the canonical
/// Characters shape used by the current ProjectEve database setup.
/// Safe to run repeatedly.
/// </summary>
public static class ProjectEveCharactersCompatibilityFix
{
    public static void Ensure()
    {
        Directory.CreateDirectory(ProjectEveDatabaseSetup.DatabaseRoot);

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();

        // Existing live databases can predate many of the canonical columns.
        // CREATE TABLE IF NOT EXISTS never adds missing columns to an existing table.
        Add(conn, "WorldId", "TEXT NOT NULL DEFAULT 'smalltown'");
        Add(conn, "NpcKey", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "FolderName", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "FolderPath", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Nickname", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "BirthYear", "INTEGER NULL");
        Add(conn, "BirthMonth", "INTEGER NULL");
        Add(conn, "BirthDay", "INTEGER NULL");
        Add(conn, "BirthHour", "INTEGER NULL");
        Add(conn, "Zodiac", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Employer", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "CurrentLocationId", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "HomeLocationId", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "WorkLocationId", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Location", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Hometown", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Address", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Status", "TEXT NOT NULL DEFAULT 'Draft'");
        Add(conn, "Tier", "INTEGER NOT NULL DEFAULT 4");
        Add(conn, "Goal", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Need", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Fear", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "Want", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "PersonalityContext", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "BackstoryShort", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "BackstoryLong", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "PersonalitySummary", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "SpeakingStyle", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "CreatedRealAt", "TEXT NOT NULL DEFAULT ''");
        Add(conn, "UpdatedRealAt", "TEXT NOT NULL DEFAULT ''");

        using var normalize = conn.CreateCommand();
        normalize.CommandText = """
        UPDATE Characters
        SET WorldId = 'smalltown'
        WHERE TRIM(COALESCE(WorldId, '')) = '';

        UPDATE Characters
        SET CreatedRealAt = CURRENT_TIMESTAMP
        WHERE TRIM(COALESCE(CreatedRealAt, '')) = '';

        UPDATE Characters
        SET UpdatedRealAt = CURRENT_TIMESTAMP
        WHERE TRIM(COALESCE(UpdatedRealAt, '')) = '';
        """;
        normalize.ExecuteNonQuery();

        Console.WriteLine("Characters compatibility check complete.");
        PrintColumns(conn);
    }

    private static void Add(
        SqliteConnection conn,
        string column,
        string definition)
    {
        if (HasColumn(conn, column))
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE Characters ADD COLUMN [{column}] {definition};";
        cmd.ExecuteNonQuery();

        Console.WriteLine($"  added Characters.{column}");
    }

    private static bool HasColumn(SqliteConnection conn, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Characters);";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var existing = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (string.Equals(existing, column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void PrintColumns(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Characters);";

        using var reader = cmd.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Console.WriteLine($"Characters columns now: {names.Count}");
        Console.WriteLine(string.Join(", ", names));
    }
}
