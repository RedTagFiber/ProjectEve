using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Single connection factory so new code never guesses which database owns a record.
/// </summary>
public static class ProjectEveDatabaseConnections
{
    public static SqliteConnection OpenMain()
        => Open(ProjectEveDatabaseSetup.MainDatabasePath);

    public static SqliteConnection OpenHistory()
        => Open(ProjectEveDatabaseSetup.HistoryDatabasePath);

    public static SqliteConnection OpenRelationships()
        => Open(ProjectEveDatabaseSetup.RelationshipDatabasePath);

    public static SqliteConnection OpenLocations()
        => Open(ProjectEveDatabaseSetup.LocationDatabasePath);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return connection;
    }
}
