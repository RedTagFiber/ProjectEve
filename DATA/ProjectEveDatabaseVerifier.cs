using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

public sealed record DatabaseVerificationResult(
    string Name,
    string Path,
    bool Exists,
    bool Ready,
    IReadOnlyList<string> MissingTables);

public static class ProjectEveDatabaseVerifier
{
    public static IReadOnlyList<DatabaseVerificationResult> VerifyAll()
    {
        ProjectEveDatabaseSetup.EnsureAll();

        return new[]
        {
            Verify("MAIN", ProjectEveDatabaseSetup.MainDatabasePath,
                "Characters", "NpcPhysicalProfiles", "NpcCognitionProfiles", "NpcArchetypes",
                "NpcPersonas", "NpcTraitValues", "NpcEmotionTriggers", "NpcMediaAssets",
                "NpcVoiceProfiles", "NpcVoicePresets", "NpcBuildStatus"),

            Verify("HISTORY", ProjectEveDatabaseSetup.HistoryDatabasePath,
                "WorldEvents", "EventParticipants", "EventFacts", "ConversationTurns",
                "Communications", "SceneActions"),

            Verify("RELATIONSHIPS", ProjectEveDatabaseSetup.RelationshipDatabasePath,
                "RelationshipStates", "RelationshipReasons", "PersonalMemories", "KnowledgeItems"),

            Verify("LOCATIONS", ProjectEveDatabaseSetup.LocationDatabasePath,
                "LocationTemplates", "Locations", "LocationAreas", "LocationVisualAssets",
                "LocationAudioAssets", "LocationMotionRegions", "LocationSceneStates",
                "LocationNpcLinks", "LocationVisits")
        };
    }

    public static void PrintToConsole()
    {
        foreach (var result in VerifyAll())
        {
            Console.WriteLine($"{result.Name,-14} {(result.Ready ? "READY" : "NOT READY")}");
            Console.WriteLine($"  {result.Path}");
            if (!result.Ready)
            {
                foreach (var missing in result.MissingTables)
                    Console.WriteLine($"  MISSING: {missing}");
            }
        }
    }

    private static DatabaseVerificationResult Verify(string name, string path, params string[] requiredTables)
    {
        var missing = new List<string>();
        var exists = File.Exists(path);
        if (!exists)
            return new DatabaseVerificationResult(name, path, false, false, requiredTables);

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        foreach (var table in requiredTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
            cmd.Parameters.AddWithValue("$name", table);
            var found = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
            if (!found)
                missing.Add(table);
        }

        return new DatabaseVerificationResult(name, path, true, missing.Count == 0, missing);
    }
}
