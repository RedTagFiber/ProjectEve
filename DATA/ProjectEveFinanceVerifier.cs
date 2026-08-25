using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

public static class ProjectEveFinanceVerifier
{
    public static void PrintToConsole()
    {
        bool accounts = HasTable(
            ProjectEveDatabaseSetup.MainDatabasePath,
            "FinancialAccounts");

        bool obligations = HasTable(
            ProjectEveDatabaseSetup.MainDatabasePath,
            "FinancialObligations");

        bool transactions = HasTable(
            ProjectEveDatabaseSetup.HistoryDatabasePath,
            "FinancialTransactions");

        bool socialBehavior = HasTable(
            ProjectEveDatabaseSetup.MainDatabasePath,
            "NpcSocialBehavior");

        bool traitReasons = HasTable(
            ProjectEveDatabaseSetup.RelationshipDatabasePath,
            "NpcTraitReasons");

        bool traitChangeHistory = HasTable(
            ProjectEveDatabaseSetup.RelationshipDatabasePath,
            "NpcTraitChangeHistory");

        Console.WriteLine("FINANCE");
        Console.WriteLine(
            $"  FinancialAccounts      {(accounts ? "READY" : "MISSING")}");
        Console.WriteLine(
            $"  FinancialObligations   {(obligations ? "READY" : "MISSING")}");
        Console.WriteLine(
            $"  FinancialTransactions  {(transactions ? "READY" : "MISSING")}");

        Console.WriteLine("SOCIAL / FAST20 CAUSAL");
        Console.WriteLine(
            $"  NpcSocialBehavior      {(socialBehavior ? "READY" : "MISSING")}");
        Console.WriteLine(
            $"  NpcTraitReasons        {(traitReasons ? "READY" : "MISSING")}");
        Console.WriteLine(
            $"  NpcTraitChangeHistory  {(traitChangeHistory ? "READY" : "MISSING")}");
    }

    private static bool HasTable(string path, string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=$name;
            """;

        cmd.Parameters.AddWithValue("$name", tableName);

        return Convert.ToInt32(
            cmd.ExecuteScalar() ?? 0) > 0;
    }
}
