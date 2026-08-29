using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public static class NpcStudioFamilySchemaCompatibility
{
    public static void Ensure(NpcStudioOptions options)
    {
        using var conn = new SqliteConnection("Data Source=" + options.MainDbPath);
        conn.Open();

        using (var create = conn.CreateCommand())
        {
            create.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcFamilyBuildPlans
            (
                RootNpcId INTEGER PRIMARY KEY,
                CreateMother INTEGER NOT NULL DEFAULT 1,
                MotherSiblingCount INTEGER NOT NULL DEFAULT 0,
                CreateFather INTEGER NOT NULL DEFAULT 1,
                FatherSiblingCount INTEGER NOT NULL DEFAULT 0,
                BrotherCount INTEGER NOT NULL DEFAULT 0,
                SisterCount INTEGER NOT NULL DEFAULT 0,
                SiblingBirthPattern TEXT NOT NULL DEFAULT 'Auto',
                CreateMaternalGrandmother INTEGER NOT NULL DEFAULT 1,
                CreateMaternalGrandfather INTEGER NOT NULL DEFAULT 1,
                CreatePaternalGrandmother INTEGER NOT NULL DEFAULT 1,
                CreatePaternalGrandfather INTEGER NOT NULL DEFAULT 1,
                GenerateAuntsUncles INTEGER NOT NULL DEFAULT 1,
                GenerateCousins INTEGER NOT NULL DEFAULT 1,
                GenerateSpousesInLaws INTEGER NOT NULL DEFAULT 1,
                ReuseExistingTownNpcForSpouses INTEGER NOT NULL DEFAULT 1,
                ExtendedFamilyDepth TEXT NOT NULL DEFAULT 'Deep',
                GenerateSharedFamilyHistory INTEGER NOT NULL DEFAULT 1,
                GenerateIndividualMemories INTEGER NOT NULL DEFAULT 1,
                GenerateFullNpcProfiles INTEGER NOT NULL DEFAULT 1,
                Status TEXT NOT NULL DEFAULT 'Draft',
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
            create.ExecuteNonQuery();
        }

        EnsureColumn(conn, "GenerateSharedFamilyHistory", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(conn, "GenerateIndividualMemories", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(conn, "GenerateFullNpcProfiles", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(conn, "Notes", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection conn, string columnName, string definition)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(NpcFamilyBuildPlans);";
            using var reader = check.ExecuteReader();

            while (reader.Read())
            {
                var existing = reader["name"]?.ToString() ?? string.Empty;
                if (existing.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE NpcFamilyBuildPlans ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }
}
