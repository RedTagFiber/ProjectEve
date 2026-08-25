using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Small runtime checks for the canonical ownership migration.
/// </summary>
public static class ProjectEveOwnershipVerifier
{
    public static void PrintToConsole()
    {
        bool traits = HasTable(ProjectEveDatabaseSetup.MainDatabasePath, "NpcTraitValues");
        bool traitControl = HasTable(ProjectEveDatabaseSetup.MainDatabasePath, "NpcTraitControl");
        bool relationships = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "RelationshipStates");
        bool memories = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "PersonalMemories");
        bool familyWeb = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "FamilyFriendWeb");
        bool householdMembers = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "HouseholdMembers");

        Console.WriteLine("CANONICAL OWNERSHIP");
        Console.WriteLine($"  NPC traits            {(traits ? "READY" : "MISSING")}");
        Console.WriteLine($"  Trait control         {(traitControl ? "READY" : "MISSING")}");
        Console.WriteLine($"  Relationship states   {(relationships ? "READY" : "MISSING")}");
        Console.WriteLine($"  Personal memories     {(memories ? "READY" : "MISSING")}");
        Console.WriteLine($"  Family / friend web   {(familyWeb ? "READY" : "MISSING")}");
        Console.WriteLine($"  Household members     {(householdMembers ? "READY" : "MISSING")}");
    }

    private static bool HasTable(string path, string table)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=$name;
            """;
        cmd.Parameters.AddWithValue("$name", table);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }
}

