/// <summary>
/// Legacy bootstrap compatibility shim.
///
/// IMPORTANT:
/// DatabaseInitializer no longer owns schema, traits, relationships,
/// memories, history, locations, money, jobs, or NPC seed data.
///
/// Canonical schema ownership belongs to ProjectEve.Data.ProjectEveDatabaseSetup.
/// This class remains temporarily because older startup code still calls
/// DatabaseInitializer.Initialize().
///
/// When all callers have migrated to ProjectEveDatabaseSetup.EnsureAll(),
/// this shim can be archived entirely.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Compatibility alias for older callers.
    /// Always resolves to the canonical main database path.
    /// </summary>
    public static string DbPath
        => ProjectEve.Data.ProjectEveDatabaseSetup.MainDatabasePath;

    /// <summary>
    /// Compatibility bootstrap only.
    /// Does NOT create legacy tables and does NOT seed duplicate data.
    /// </summary>
    public static void Initialize()
    {
        ProjectEve.Data.ProjectEveDatabaseSetup.EnsureAll();
    }
}
