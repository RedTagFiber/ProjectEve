using Microsoft.Data.Sqlite;
using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Characters;
using ProjectEve.Characters.Traits.Core;
using ProjectEve.Data;
using ProjectEve.Relationships;
using ProjectEve.Traits;
using ProjectEve.Traits.Matrix;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class Program
{
    static string DataDir => ProjectEveDatabaseSetup.DatabaseRoot;
    static string DbPath => ProjectEveDatabaseSetup.MainDatabasePath;
    static string HistoryDbPath => ProjectEveDatabaseSetup.HistoryDatabasePath;
    static string NpcRoot => ProjectEveDatabaseSetup.NpcRoot;

    static readonly string TraitJsonRoot =
        @"D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean\Characters\Traits\TraitJson";

    static readonly Random Rng = new();

    static int _nextNpcIdCache = 0;
    static readonly HashSet<string> NamesReservedThisRun = new(StringComparer.OrdinalIgnoreCase);

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "ProjectEve NPC Seeder";

        if (args.Length > 0 && args[0].Equals("reset-db", StringComparison.OrdinalIgnoreCase))
        {
            ResetBothDatabasesWithConfirmation();
            return;
        }

        InitializeWorldSystems();      // Sets up folders, schema, traits, jobs, and core NPC rows.

        if (args.Length == 0)
        {
            RunInteractiveMenu();
            return;
        }

        var cmd = args[0].Trim().ToLowerInvariant();

        switch (cmd)
        {
            case "seed":
            case "seed-world":
                {
                    int townCount = ParseIntArg(args, 1, 200);
                    int historyCount = ParseIntArg(args, 2, 500);
                    SeedWorld(townCount, historyCount);
                    break;
                }

            case "seed-town":
                {
                    int townCount = ParseIntArg(args, 1, 200);
                    SeedTownNpcs(townCount);
                    break;
                }

            case "seed-history":
                {
                    int historyCount = ParseIntArg(args, 1, 500);
                    SeedHistoryNpcs(historyCount);
                    break;
                }

            case "ensure-core":
            case "ensure-eve":
                {
                    EnsureCoreNpcRows();
                    Console.WriteLine("Core NPC rows ensured: Eve=1, Adam=2, Lisa=3, Edward=4.");
                    break;
                }

            case "repair-relationships":
            case "repair-relations":
                {
                    RepairRelationshipsFromExistingCharacters();
                    break;
                }

            case "verify":
            case "verify-db":
                {
                    VerifyTownDatabase();
                    break;
                }

            default:
                PrintUsage();
                break;
        }
    }

    static void RunInteractiveMenu()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("ProjectEve NPC Seeder");
            Console.WriteLine("---------------------");
            Console.WriteLine("1. Seed full world (200 town + 500 history)");
            Console.WriteLine("2. Seed town NPCs only");
            Console.WriteLine("3. Seed history NPCs only");
            Console.WriteLine("4. Ensure core NPC rows only");
            Console.WriteLine("5. Repair / create relationships");
            Console.WriteLine("6. Verify town database");
            Console.WriteLine("7. DELETE both databases");
            Console.WriteLine("8. Exit");
            Console.WriteLine();

            Console.Write("Choose: ");
            var input = (Console.ReadLine() ?? "").Trim();

            if (input == "1")
            {
                Console.Write("Town NPC count [200]: ");
                int townCount = ParseConsoleInt(200);

                Console.Write("History NPC count [500]: ");
                int historyCount = ParseConsoleInt(500);

                SeedWorld(townCount, historyCount);
            }
            else if (input == "2")
            {
                Console.Write("Town NPC count [200]: ");
                int townCount = ParseConsoleInt(200);
                SeedTownNpcs(townCount);
            }
            else if (input == "3")
            {
                Console.Write("History NPC count [500]: ");
                int historyCount = ParseConsoleInt(500);
                SeedHistoryNpcs(historyCount);
            }
            else if (input == "4")
            {
                EnsureCoreNpcRows();
                Console.WriteLine("Core NPC rows ensured: Eve=1, Adam=2, Lisa=3, Edward=4.");
            }
            else if (input == "5")
            {
                // Repairs the relationship table without deleting or reseeding characters.
                RepairRelationshipsFromExistingCharacters();
            }
            else if (input == "6")
            {
                // Prints useful database counts directly in the console.
                VerifyTownDatabase();
            }
            else if (input == "7")
            {
                ResetBothDatabasesWithConfirmation();

                Console.WriteLine();
                Console.Write("Rebuild empty databases and core NPC rows now? y/N: ");
                var rebuild = (Console.ReadLine() ?? "").Trim();

                if (rebuild.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                    rebuild.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    InitializeWorldSystems();
                    Console.WriteLine("Databases rebuilt and core NPC rows ensured.");
                }
            }
            else if (input == "8" || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
    }
    static void PrintSingleCount(SqliteConnection conn, string title, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var result = cmd.ExecuteScalar();
        Console.WriteLine($"{title}: {result}");
    }

    static void PrintGroupedCounts(SqliteConnection conn, string title, string sql)
    {
        Console.WriteLine(title + ":");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            string label = reader["Label"]?.ToString() ?? "";
            string count = reader["Count"]?.ToString() ?? "0";

            if (string.IsNullOrWhiteSpace(label))
                label = "(blank)";

            Console.WriteLine($"  {label,-35} {count}");
        }
    }
    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- seed-world [townCount] [historyCount]");
        Console.WriteLine("  dotnet run -- seed-town [townCount]");
        Console.WriteLine("  dotnet run -- seed-history [historyCount]");
        Console.WriteLine("  dotnet run -- ensure-core");
        Console.WriteLine("  dotnet run -- repair-relationships");
        Console.WriteLine("  dotnet run -- verify");
        Console.WriteLine("  dotnet run -- reset-db");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- seed-world 200 500");
        Console.WriteLine("  dotnet run -- seed-town 200");
        Console.WriteLine("  dotnet run -- seed-history 500");
        Console.WriteLine("  dotnet run -- repair-relationships");
        Console.WriteLine("  dotnet run -- verify");
    }

    static int ParseIntArg(string[] args, int index, int fallback)
    {
        if (args.Length <= index)
            return fallback;

        return int.TryParse(args[index], out var value) && value > 0
            ? value
            : fallback;
    }

    static int ParseConsoleInt(int fallback)
    {
        var text = (Console.ReadLine() ?? "").Trim();

        return int.TryParse(text, out var value) && value > 0
            ? value
            : fallback;
    }
    static void VerifyTownDatabase()
    {
        Console.WriteLine();
        Console.WriteLine("ProjectEve Database Verification");
        Console.WriteLine("--------------------------------");

        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        PrintSingleCount(conn, "Total characters", """
        SELECT COUNT(*)
        FROM Characters;
        """);

        Console.WriteLine();
        PrintGroupedCounts(conn, "By status", """
        SELECT IFNULL(Status, '') AS Label, COUNT(*) AS Count
        FROM Characters
        GROUP BY Status
        ORDER BY Count DESC;
        """);

        Console.WriteLine();
        PrintGroupedCounts(conn, "By tier", """
        SELECT CAST(Tier AS TEXT) AS Label, COUNT(*) AS Count
        FROM Characters
        GROUP BY Tier
        ORDER BY Tier;
        """);

        Console.WriteLine();
        PrintGroupedCounts(conn, "Top occupations", """
        SELECT IFNULL(Occupation, '') AS Label, COUNT(*) AS Count
        FROM Characters
        GROUP BY Occupation
        ORDER BY Count DESC
        LIMIT 25;
        """);

        Console.WriteLine();
        PrintGroupedCounts(conn, "Top relationship counts", """
        SELECT CAST(r.NpcId AS TEXT) || ' - ' || IFNULL(c.Name, 'Unknown') AS Label,
               COUNT(*) AS Count
        FROM NpcRelationships r
        LEFT JOIN Characters c ON c.Id = r.NpcId
        GROUP BY r.NpcId, c.Name
        ORDER BY Count DESC
        LIMIT 20;
        """);

        Console.WriteLine();
        Console.WriteLine("Verification complete.");
    }
    static void InitializeWorldSystems()
    {
        ProjectEveDatabaseSetup.EnsureAll();

        Console.WriteLine("DB → " + DbPath);
        Console.WriteLine("History DB → " + HistoryDbPath);
        Console.WriteLine("NPC Root → " + NpcRoot);
        Console.WriteLine();

        // Build/patch the minimum schema first.
        // DatabaseInitializer expects columns like Nickname, so this must run before DatabaseInitializer.Initialize().
        EnsureCoreTables();

        try
        {
            DatabaseInitializer.Initialize();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Database init warning: " + ex.Message);
        }

        try
        {
            TraitRegistry.LoadBaseTraits();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Trait registry warning: " + ex.Message);
        }

        try
        {
            BehaviorRegistry.Load();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Behavior registry warning: " + ex.Message);
        }

        try
        {
            TraitJsonLoader.SetRoot(TraitJsonRoot);
        }
        catch (Exception ex)
        {
            Console.WriteLine("TraitJson root warning: " + ex.Message);
        }

        try
        {
            RelationshipMatrixLoader.Load(Path.Combine(TraitJsonRoot, "Matrix"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Relationship matrix warning: " + ex.Message);
        }

        try
        {
            CharacterFactory.ReloadWorkplaceSystem();
            CharacterFactory.PrintWorkplaceSystemStatus();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Workplace system warning: " + ex.Message);
        }

        EnsureCoreNpcRows();
    }

    static void SeedWorld(int townCount, int historyCount)
    {
        ResetSeederRuntimeState();

        Console.WriteLine();
        Console.WriteLine("Seeding full world...");
        Console.WriteLine($"Town NPCs   : {townCount}");
        Console.WriteLine($"History NPCs: {historyCount}");
        Console.WriteLine();

        var historyNpcs = CreateHistoryNpcBatch(historyCount);
        SaveNpcBatch(historyNpcs, "history");

        var townNpcs = CreateTownNpcBatch(townCount);
        SaveNpcBatch(townNpcs, "town");

        // Guaranteed starting social web for the main characters.
        // This creates at least 10 current-town friends for Eve and 10 for Adam.
        EnsureCoreNpcFriendGroups(townNpcs, minimumFriendsEach: 10);

        // General town web: every generated town NPC gets 1-3 links to history/background NPCs.
        LinkTownNpcsToHistory(townNpcs, historyNpcs);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine("World seed complete.");
        Console.WriteLine($"Town NPCs saved   : {townNpcs.Count}");
        Console.WriteLine($"History NPCs saved: {historyNpcs.Count}");
        Console.ResetColor();
    }

    static void SeedTownNpcs(int townCount)
    {
        ResetSeederRuntimeState();

        Console.WriteLine();
        Console.WriteLine("Seeding town NPCs...");
        Console.WriteLine($"Town NPCs: {townCount}");
        Console.WriteLine();

        var townNpcs = CreateTownNpcBatch(townCount);
        SaveNpcBatch(townNpcs, "town");

        // Town-only seeding still gives Eve and Adam starter friends.
        EnsureCoreNpcFriendGroups(townNpcs, minimumFriendsEach: 10);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine($"Town seed complete. Saved {townNpcs.Count} NPCs.");
        Console.ResetColor();
    }

    static void SeedHistoryNpcs(int historyCount)
    {
        ResetSeederRuntimeState();

        Console.WriteLine();
        Console.WriteLine("Seeding history NPCs...");
        Console.WriteLine($"History NPCs: {historyCount}");
        Console.WriteLine();

        var historyNpcs = CreateHistoryNpcBatch(historyCount);
        SaveNpcBatch(historyNpcs, "history");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine($"History seed complete. Saved {historyNpcs.Count} NPCs.");
        Console.ResetColor();
    }

    static void ResetSeederRuntimeState()
    {
        _nextNpcIdCache = 0;
        NamesReservedThisRun.Clear();

        try
        {
            CharacterFactory.ClearReservedJobSlots();
        }
        catch
        {
        }
    }

    static List<SimCharacter> CreateTownNpcBatch(int count)
    {
        var list = new List<SimCharacter>();

        for (int i = 0; i < count; i++)
        {
            var npc = BuildTownNpc(i);
            list.Add(npc);
        }

        return list;
    }

    static List<SimCharacter> CreateHistoryNpcBatch(int count)
    {
        var list = new List<SimCharacter>();

        for (int i = 0; i < count; i++)
        {
            var npc = BuildHistoryNpc(i);
            list.Add(npc);
        }

        return list;
    }

    static SimCharacter BuildTownNpc(int index)
    {
        string name = GenerateUniqueName();
        int age = Rng.Next(18, 76);
        string gender = PickGender();
        string preferredLane = PickPreferredLane();

        SimCharacter npc;

        try
        {
            npc = CharacterFactory.CreateWithOpenJobSlot(
                name: name,
                age: age,
                gender: gender,
                location: "Bellefontaine / Sidney, Ohio area",
                preferredLaneOrCategory: preferredLane,
                preferredJobId: null,
                rng: Rng);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Job slot fallback for " + name + ": " + ex.Message);

            npc = CharacterFactory.Create(
                name,
                age,
                gender,
                "Bellefontaine / Sidney, Ohio area",
                "Local worker");
        }

        EnsureNpcCore(npc);
        EnsureNpcTraits(npc);

        npc.Id = GenerateNextNpcId();
        npc.Tier = PickTownTier();
        npc.Name = name;
        npc.Gender = gender;
        npc.Location = "Bellefontaine / Sidney, Ohio area";
        npc.Hometown = PickLocalHometown();
        npc.HomeAddress = "in town";

        if (string.IsNullOrWhiteSpace(npc.Goal))
            npc.Goal = "Build a stable life in town.";

        if (string.IsNullOrWhiteSpace(npc.Need))
            npc.Need = "A sense of belonging and consistency.";

        if (string.IsNullOrWhiteSpace(npc.Fear))
            npc.Fear = "Falling behind or becoming disconnected.";

        if (string.IsNullOrWhiteSpace(npc.Want))
            npc.Want = "A life that feels secure and meaningful.";

        npc.PersonalityContext =
            AppendLine(
                npc.PersonalityContext,
                $"Generated town NPC. Preferred lane={preferredLane}. Seed batch=town. Job uses real workplace slots.");

        return npc;
    }

    static SimCharacter BuildHistoryNpc(int index)
    {
        string name = GenerateUniqueName();
        int age = Rng.Next(18, 84);
        string gender = PickGender();
        string historyRole = PickHistoryRole();
        string location = PickOutOfTownLocation();

        SimCharacter npc;

        try
        {
            npc = CharacterFactory.Create(
                name,
                age,
                gender,
                location,
                historyRole);
        }
        catch
        {
            npc = new SimCharacter(name, age)
            {
                Gender = gender,
                Location = location,
                Occupation = historyRole
            };
        }

        EnsureNpcCore(npc);
        EnsureNpcTraits(npc);

        npc.Id = GenerateNextNpcId();
        npc.Tier = 5;
        npc.Name = name;
        npc.Gender = gender;
        npc.Location = location;
        npc.Hometown = location;
        npc.HomeAddress = "out of town";
        npc.Occupation = historyRole;

        npc.Goal = "Live their own life outside the main town story.";
        npc.Need = "Stable connections with family and friends.";
        npc.Fear = "Losing touch with people who matter.";
        npc.Want = "To stay connected even from a distance.";

        npc.PersonalityContext =
            $"Generated history-only NPC. Role={historyRole}. Seed batch=history. Out-of-town contact.";

        return npc;
    }

    static void SaveNpcBatch(List<SimCharacter> npcs, string batchLabel)
    {
        int saved = 0;

        foreach (var npc in npcs)
        {
            try
            {
                SaveNpc(npc, batchLabel);
                saved++;

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine(
                    $"[{saved}] {npc.Id:D6} | {npc.Name} | Tier {npc.Tier} | {npc.Occupation}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to save {npc.Name}: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    static void SaveNpc(SimCharacter npc, string batchLabel)
    {
        SaveNpcIdentityStub(npc, batchLabel);

        ProjectEveDatabaseSetup.EnsureNpcFolders(npc.Id, npc.Name ?? "");
        UpdateNpcFolderInfo(npc.Id, npc.Name ?? "");
        EnsureNpcStudioRows(npc);
        SaveNpcTraitsToStudioTable(npc);

        try
        {
            CharacterRepository.SaveCharacterState(npc);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  state save warning: " + ex.Message);
        }

        try
        {
            if (npc.Traits != null)
                CharacterRepository.SaveTraits(npc.Id, npc.Traits);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  trait save warning: " + ex.Message);
        }

        SaveStudioRevision(
            npc.Id,
            "Created",
            batchLabel == "history" ? "History NPC generated" : "Town NPC generated",
            $"Seeder created {npc.Name}. Batch={batchLabel}. Tier={npc.Tier}. Occupation={npc.Occupation}.");
    }

    static void LinkTownNpcsToHistory(List<SimCharacter> townNpcs, List<SimCharacter> historyNpcs)
    {
        if (townNpcs.Count == 0 || historyNpcs.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Linking town NPCs to history NPCs...");

        foreach (var townNpc in townNpcs)
        {
            int linkCount = Rng.Next(1, 4);

            var picks = historyNpcs
                .OrderBy(_ => Rng.Next())
                .Take(linkCount)
                .ToList();

            foreach (var historyNpc in picks)
            {
                var relationshipType = PickRelationshipType();

                UpsertRelationshipIfMissing(
                    npcId: townNpc.Id,
                    targetName: historyNpc.Name ?? "Unknown",
                    relationshipType: relationshipType,
                    trust: Rng.Next(25, 81),
                    respect: Rng.Next(20, 81),
                    affection: relationshipType is "family" or "friend" ? Rng.Next(20, 91) : Rng.Next(0, 41),
                    attraction: relationshipType == "romantic_history" ? Rng.Next(15, 81) : 0,
                    tension: Rng.Next(0, 51),
                    notes: $"History link to Tier-5 NPC {historyNpc.Id:D6} ({historyNpc.Location})."
                );
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("History links created.");
        Console.ResetColor();
    }

    // Creates or repairs relationships using the characters that are already in the database.
    // Use this after you already have 704 characters but the relationship count is blank/zero.
    static void RepairRelationshipsFromExistingCharacters()
    {
        Console.WriteLine();
        Console.WriteLine("Repairing relationships from existing characters...");

        var townNpcs = LoadExistingTownNpcs();
        var historyNpcs = LoadExistingHistoryNpcs();

        Console.WriteLine($"Town NPCs found   : {townNpcs.Count}");
        Console.WriteLine($"History NPCs found: {historyNpcs.Count}");

        if (townNpcs.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No town NPCs found. Seed town NPCs first.");
            Console.ResetColor();
            return;
        }

        // Guarantees at least 10 friends for Eve and Adam.
        EnsureCoreNpcFriendGroups(townNpcs, minimumFriendsEach: 10);

        // Links generated town NPCs to history/background NPCs if history NPCs exist.
        LinkTownNpcsToHistory(townNpcs, historyNpcs);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Relationship repair complete.");
        Console.ResetColor();
    }

    // Gives Eve and Adam a guaranteed friend group from existing/generated town NPCs.
    // Relationships are saved in both directions:
    //   Eve/Adam -> friend
    //   friend -> Eve/Adam
    static void EnsureCoreNpcFriendGroups(List<SimCharacter> townNpcs, int minimumFriendsEach)
    {
        if (townNpcs.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Linking core NPC friends...");

        EnsureFriendsForCoreNpc(
            coreNpcId: 1,
            coreNpcName: "Eve Sinclair",
            townNpcs: townNpcs,
            minimumFriends: minimumFriendsEach);

        EnsureFriendsForCoreNpc(
            coreNpcId: 2,
            coreNpcName: "Adam Sinclair",
            townNpcs: townNpcs,
            minimumFriends: minimumFriendsEach);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Core friendship links created.");
        Console.ResetColor();
    }

    static void EnsureFriendsForCoreNpc(
        int coreNpcId,
        string coreNpcName,
        List<SimCharacter> townNpcs,
        int minimumFriends)
    {
        var candidates = townNpcs
            .Where(n => n.Id > 4 && !string.IsNullOrWhiteSpace(n.Name))
            .OrderBy(_ => Rng.Next())
            .Take(minimumFriends)
            .ToList();

        foreach (var friend in candidates)
        {
            string friendName = friend.Name ?? "Unknown";

            int trust = Rng.Next(55, 86);
            int respect = Rng.Next(45, 81);
            int affection = Rng.Next(45, 86);
            int tension = Rng.Next(0, 26);

            // Core NPC -> town friend.
            UpsertRelationshipIfMissing(
                npcId: coreNpcId,
                targetName: friendName,
                relationshipType: "friend",
                trust: trust,
                respect: respect,
                affection: affection,
                attraction: 0,
                tension: tension,
                notes: $"Starter friendship. {coreNpcName} knows {friendName} before game start.");

            // Town friend -> core NPC.
            UpsertRelationshipIfMissing(
                npcId: friend.Id,
                targetName: coreNpcName,
                relationshipType: "friend",
                trust: ClampRelationshipScore(trust + Rng.Next(-8, 9)),
                respect: ClampRelationshipScore(respect + Rng.Next(-8, 9)),
                affection: ClampRelationshipScore(affection + Rng.Next(-8, 9)),
                attraction: 0,
                tension: ClampRelationshipScore(tension + Rng.Next(-5, 6)),
                notes: $"Starter friendship. {friendName} knows {coreNpcName} before game start.");
        }
    }

    // Loads already-saved town NPCs from the database.
    // This lets option 5 repair relationships without reseeding or deleting the town.
    static List<SimCharacter> LoadExistingTownNpcs()
    {
        var list = new List<SimCharacter>();

        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT Id, Name, Age, Gender, Occupation, Location, Tier
            FROM Characters
            WHERE Id > 4
              AND IFNULL(Status, '') = 'Draft'
              AND Tier IN (2, 3, 4)
            ORDER BY Id;
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var npc = new SimCharacter(
                reader["Name"]?.ToString() ?? "",
                Convert.ToInt32(reader["Age"]))
            {
                Id = Convert.ToInt32(reader["Id"]),
                Gender = reader["Gender"]?.ToString() ?? "",
                Occupation = reader["Occupation"]?.ToString() ?? "",
                Location = reader["Location"]?.ToString() ?? "",
                Tier = Convert.ToInt32(reader["Tier"])
            };

            list.Add(npc);
        }

        return list;
    }

    // Loads Tier 5 / HistoryOnly NPCs from the database.
    // These are background people used for family, old friends, exes, classmates, and other history links.
    static List<SimCharacter> LoadExistingHistoryNpcs()
    {
        var list = new List<SimCharacter>();

        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT Id, Name, Age, Gender, Occupation, Location, Tier
            FROM Characters
            WHERE IFNULL(Status, '') = 'HistoryOnly'
               OR Tier = 5
            ORDER BY Id;
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var npc = new SimCharacter(
                reader["Name"]?.ToString() ?? "",
                Convert.ToInt32(reader["Age"]))
            {
                Id = Convert.ToInt32(reader["Id"]),
                Gender = reader["Gender"]?.ToString() ?? "",
                Occupation = reader["Occupation"]?.ToString() ?? "",
                Location = reader["Location"]?.ToString() ?? "",
                Tier = Convert.ToInt32(reader["Tier"])
            };

            list.Add(npc);
        }

        return list;
    }

    static int ClampRelationshipScore(int value)
    {
        if (value < 0)
            return 0;

        if (value > 100)
            return 100;

        return value;
    }

    static void EnsureCoreTables()     ///// Must run BEFORE DatabaseInitializer.Initialize() /////////
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        Execute(conn, """
                    CREATE TABLE IF NOT EXISTS Characters
                    (
                        Id INTEGER PRIMARY KEY,

                        NpcKey TEXT,
                        FolderName TEXT,
                        FolderPath TEXT,

                        Name TEXT NOT NULL DEFAULT '',
                        Nickname TEXT,
                        DirtyName TEXT,
                        DarkName TEXT,
                        DisplayName TEXT,
                        FirstName TEXT,
                        LastName TEXT,

                        Age INTEGER NOT NULL DEFAULT 0,
                        Gender TEXT,
                        Occupation TEXT,
                        Location TEXT,
                        Status TEXT,

                        Goal TEXT,
                        Need TEXT,
                        Fear TEXT,
                        Want TEXT,
                        PersonalityContext TEXT,
                        Hometown TEXT,
                        Address TEXT,

                        Tier INTEGER NOT NULL DEFAULT 5,
                        UpdatedRealAt TEXT
                    );
                    """);
        EnsureColumn(conn, "Characters", "NpcKey", "TEXT");
        EnsureColumn(conn, "Characters", "Name", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "Nickname", "TEXT");
        EnsureColumn(conn, "Characters", "DirtyName", "TEXT");
        EnsureColumn(conn, "Characters", "DarkName", "TEXT");
        EnsureColumn(conn, "Characters", "DisplayName", "TEXT");
        EnsureColumn(conn, "Characters", "FirstName", "TEXT");
        EnsureColumn(conn, "Characters", "LastName", "TEXT");
        EnsureColumn(conn, "Characters", "FolderName", "TEXT");
        EnsureColumn(conn, "Characters", "FolderPath", "TEXT");
        EnsureColumn(conn, "Characters", "Age", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Characters", "Gender", "TEXT");
        EnsureColumn(conn, "Characters", "Occupation", "TEXT");
        EnsureColumn(conn, "Characters", "Location", "TEXT");
        EnsureColumn(conn, "Characters", "Status", "TEXT");
        EnsureColumn(conn, "Characters", "Goal", "TEXT");
        EnsureColumn(conn, "Characters", "Need", "TEXT");
        EnsureColumn(conn, "Characters", "Fear", "TEXT");
        EnsureColumn(conn, "Characters", "Want", "TEXT");
        EnsureColumn(conn, "Characters", "PersonalityContext", "TEXT");
        EnsureColumn(conn, "Characters", "Hometown", "TEXT");
        EnsureColumn(conn, "Characters", "Address", "TEXT");
        EnsureColumn(conn, "Characters", "Tier", "INTEGER NOT NULL DEFAULT 5");
        EnsureColumn(conn, "Characters", "UpdatedRealAt", "TEXT");
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcRelationships
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            TargetName TEXT NOT NULL,
            RelationshipType TEXT,
            Trust INTEGER NOT NULL DEFAULT 0,
            Respect INTEGER NOT NULL DEFAULT 0,
            Affection INTEGER NOT NULL DEFAULT 0,
            Attraction INTEGER NOT NULL DEFAULT 0,
            Tension INTEGER NOT NULL DEFAULT 0,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcAppearanceProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcVoiceProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            VoiceStatus TEXT,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcTraitValues
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            MainGroup TEXT,
            SubGroup TEXT,
            SubSubGroup TEXT,
            TraitId TEXT,
            TraitName TEXT,
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            StartingValue INTEGER NOT NULL DEFAULT 50,
            CurrentValue INTEGER NOT NULL DEFAULT 50,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcBuildRevisions
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            RevisionType TEXT,
            Title TEXT,
            Details TEXT,
            OldValue TEXT,
            NewValue TEXT,
            CreatedRealAt TEXT
        );
        """);
    }

    static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static void EnsureColumn(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName});";

            using var reader = check.ExecuteReader();

            while (reader.Read())
            {
                var existing = reader["name"]?.ToString() ?? "";

                if (existing.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    static void SaveNpcIdentityStub(SimCharacter npc, string batchLabel)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO Characters
            (
                Id,
                NpcKey,
                FolderName,
                FolderPath,
                Name,
                Age,
                Gender,
                Occupation,
                Location,
                Status,
                Goal,
                Need,
                Fear,
                Want,
                PersonalityContext,
                Hometown,
                Address,
                Tier,
                UpdatedRealAt
            )
            VALUES
            (
                $id,
                $npcKey,
                $folderName,
                $folderPath,
                $name,
                $age,
                $gender,
                $occ,
                $loc,
                $status,
                $goal,
                $need,
                $fear,
                $want,
                $ctx,
                $home,
                $addr,
                $tier,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                NpcKey = $npcKey,
                FolderName = $folderName,
                FolderPath = $folderPath,
                Name = $name,
                Age = $age,
                Gender = $gender,
                Occupation = $occ,
                Location = $loc,
                Status = $status,
                Goal = $goal,
                Need = $need,
                Fear = $fear,
                Want = $want,
                PersonalityContext = $ctx,
                Hometown = $home,
                Address = $addr,
                Tier = $tier,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        var folderName = ProjectEveDatabaseSetup.GetNpcFolderName(npc.Id, npc.Name ?? "");
        var folderPath = ProjectEveDatabaseSetup.GetNpcFolderPath(npc.Id, npc.Name ?? "");
        var npcKey = $"npc_{npc.Id:D6}";

        string status = npc.Tier >= 5
            ? "HistoryOnly"
            : "Draft";

        cmd.Parameters.AddWithValue("$id", npc.Id);
        cmd.Parameters.AddWithValue("$npcKey", npcKey);
        cmd.Parameters.AddWithValue("$folderName", folderName);
        cmd.Parameters.AddWithValue("$folderPath", folderPath);
        cmd.Parameters.AddWithValue("$name", npc.Name ?? "");
        cmd.Parameters.AddWithValue("$age", npc.Age);
        cmd.Parameters.AddWithValue("$gender", npc.Gender ?? "");
        cmd.Parameters.AddWithValue("$occ", npc.Occupation ?? "");
        cmd.Parameters.AddWithValue("$loc", npc.Location ?? "");
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$goal", npc.Goal ?? "");
        cmd.Parameters.AddWithValue("$need", npc.Need ?? "");
        cmd.Parameters.AddWithValue("$fear", npc.Fear ?? "");
        cmd.Parameters.AddWithValue("$want", npc.Want ?? "");
        cmd.Parameters.AddWithValue("$ctx", npc.PersonalityContext ?? "");
        cmd.Parameters.AddWithValue("$home", npc.Hometown ?? "");
        cmd.Parameters.AddWithValue("$addr", npc.HomeAddress ?? "");
        cmd.Parameters.AddWithValue("$tier", npc.Tier);

        cmd.ExecuteNonQuery();
    }

    static void UpdateNpcFolderInfo(int npcId, string npcName)
    {
        var folderName = ProjectEveDatabaseSetup.GetNpcFolderName(npcId, npcName);
        var folderPath = ProjectEveDatabaseSetup.GetNpcFolderPath(npcId, npcName);
        var npcKey = $"npc_{npcId:D6}";

        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            UPDATE Characters
            SET
                NpcKey = $npcKey,
                FolderName = $folderName,
                FolderPath = $folderPath,
                UpdatedRealAt = CURRENT_TIMESTAMP
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$npcKey", npcKey);
        cmd.Parameters.AddWithValue("$folderName", folderName);
        cmd.Parameters.AddWithValue("$folderPath", folderPath);

        cmd.ExecuteNonQuery();
    }

    static void EnsureNpcStudioRows(SimCharacter npc)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO NpcAppearanceProfiles
                (
                    NpcId,
                    Notes
                )
                VALUES
                (
                    $id,
                    $notes
                );
                """;

            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.Parameters.AddWithValue("$notes", "Appearance profile awaiting NPC Studio review.");
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO NpcVoiceProfiles
                (
                    NpcId,
                    VoiceStatus,
                    Notes
                )
                VALUES
                (
                    $id,
                    'Draft',
                    'Voice profile awaiting NPC Studio review.'
                );
                """;

            cmd.Parameters.AddWithValue("$id", npc.Id);
            cmd.ExecuteNonQuery();
        }
    }

    static void SaveNpcTraitsToStudioTable(SimCharacter npc)
    {
        if (npc.Traits == null)
            return;

        var allTraits = npc.Traits.GetAll();

        if (allTraits == null || allTraits.Count == 0)
            return;

        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        foreach (var pair in allTraits)
        {
            var traitId = pair.Key ?? "";

            if (string.IsNullOrWhiteSpace(traitId))
                continue;

            var value = ClampTraitValue(pair.Value);

            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                INSERT OR IGNORE INTO NpcTraitValues
                (
                    Id,
                    NpcId,
                    MainGroup,
                    SubGroup,
                    SubSubGroup,
                    TraitId,
                    TraitName,
                    IsEnabled,
                    StartingValue,
                    CurrentValue,
                    Notes
                )
                VALUES
                (
                    $rowId,
                    $npcId,
                    $mainGroup,
                    $subGroup,
                    $subSubGroup,
                    $traitId,
                    $traitName,
                    1,
                    $startingValue,
                    $currentValue,
                    ''
                );
                """;

            cmd.Parameters.AddWithValue("$rowId", $"{npc.Id}_{traitId}");
            cmd.Parameters.AddWithValue("$npcId", npc.Id);
            cmd.Parameters.AddWithValue("$mainGroup", GuessTraitMainGroup(traitId));
            cmd.Parameters.AddWithValue("$subGroup", GuessTraitSubGroup(traitId));
            cmd.Parameters.AddWithValue("$subSubGroup", "");
            cmd.Parameters.AddWithValue("$traitId", traitId);
            cmd.Parameters.AddWithValue("$traitName", PrettyTraitName(traitId));
            cmd.Parameters.AddWithValue("$startingValue", value);
            cmd.Parameters.AddWithValue("$currentValue", value);

            cmd.ExecuteNonQuery();
        }
    }

    static int ClampTraitValue(float value)
    {
        if (value < 1)
            return 1;

        if (value > 100)
            return 100;

        return (int)Math.Round(value);
    }

    static string GuessTraitMainGroup(string traitId)
    {
        var clean = traitId.ToLowerInvariant();

        if (clean.Contains("anger") || clean.Contains("anxiety") || clean.Contains("hurt") || clean.Contains("fear"))
            return "Emotional";

        if (clean.Contains("trust") || clean.Contains("affection") || clean.Contains("attraction") || clean.Contains("tension"))
            return "Relationship";

        if (clean.Contains("openness") || clean.Contains("guard") || clean.Contains("hope") || clean.Contains("desire"))
            return "Personality";

        return "General";
    }

    static string GuessTraitSubGroup(string traitId)
    {
        var clean = traitId.ToLowerInvariant();

        if (clean.Contains("anger"))
            return "Anger";

        if (clean.Contains("anxiety"))
            return "Anxiety";

        if (clean.Contains("hurt"))
            return "Hurt";

        if (clean.Contains("trust"))
            return "Trust";

        if (clean.Contains("affection"))
            return "Affection";

        if (clean.Contains("guard"))
            return "Guard";

        return "";
    }

    static string PrettyTraitName(string traitId)
    {
        var value = traitId;

        if (value.StartsWith("trait.", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("trait.".Length);

        value = value.Replace("_", " ").Replace(".", " ").Trim();

        if (string.IsNullOrWhiteSpace(value))
            return traitId;

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    static void SaveStudioRevision(int npcId, string revisionType, string title, string details)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcBuildRevisions
            (
                Id,
                NpcId,
                RevisionType,
                Title,
                Details,
                OldValue,
                NewValue,
                CreatedRealAt
            )
            VALUES
            (
                $id,
                $npcId,
                $revisionType,
                $title,
                $details,
                '',
                '',
                CURRENT_TIMESTAMP
            );
            """;

        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$revisionType", revisionType);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$details", details);

        cmd.ExecuteNonQuery();
    }

    static void UpsertRelationship(
        int npcId,
        string targetName,
        string relationshipType,
        int trust,
        int respect,
        int affection,
        int attraction,
        int tension,
        string notes)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcRelationships
            (
                Id,
                NpcId,
                TargetName,
                RelationshipType,
                Trust,
                Respect,
                Affection,
                Attraction,
                Tension,
                Notes
            )
            VALUES
            (
                $id,
                $npcId,
                $targetName,
                $relationshipType,
                $trust,
                $respect,
                $affection,
                $attraction,
                $tension,
                $notes
            );
            """;

        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$targetName", targetName);
        cmd.Parameters.AddWithValue("$relationshipType", relationshipType);
        cmd.Parameters.AddWithValue("$trust", trust);
        cmd.Parameters.AddWithValue("$respect", respect);
        cmd.Parameters.AddWithValue("$affection", affection);
        cmd.Parameters.AddWithValue("$attraction", attraction);
        cmd.Parameters.AddWithValue("$tension", tension);
        cmd.Parameters.AddWithValue("$notes", notes);

        cmd.ExecuteNonQuery();
    }

    // Inserts a relationship only if the same NPC already does not have the same
    // target name and relationship type. This keeps repair/reseed runs from duplicating rows.
    static void UpsertRelationshipIfMissing(
        int npcId,
        string targetName,
        string relationshipType,
        int trust,
        int respect,
        int affection,
        int attraction,
        int tension,
        string notes)
    {
        if (RelationshipExists(npcId, targetName, relationshipType))
            return;

        UpsertRelationship(
            npcId,
            targetName,
            relationshipType,
            trust,
            respect,
            affection,
            attraction,
            tension,
            notes);
    }

    static bool RelationshipExists(int npcId, string targetName, string relationshipType)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT COUNT(1)
            FROM NpcRelationships
            WHERE NpcId = $npcId
              AND TargetName = $targetName
              AND RelationshipType = $relationshipType;
            """;

        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$targetName", targetName);
        cmd.Parameters.AddWithValue("$relationshipType", relationshipType);

        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }

    static void EnsureCoreNpcRows()
    {
        EnsureCoreNpcRow(
            id: 1,
            name: "Eve Sinclair",
            age: 25,
            gender: "Female",
            occupation: "Coffee shop manager",
            location: "Bellefontaine, Ohio",
            goal: "Keep Sinclair Coffee and her life from falling apart.",
            need: "To feel known without feeling trapped.",
            fear: "Being abandoned or becoming invisible.",
            want: "A life that feels real and chosen.",
            context: "Core NPC. Eve Sinclair is the central small-town coffee shop manager.",
            hometown: "Bellefontaine, OH",
            address: "in town",
            tier: 1);

        EnsureCoreNpcRow(
            id: 2,
            name: "Adam Sinclair",
            age: 27,
            gender: "Male",
            occupation: "Unassigned",
            location: "Bellefontaine, Ohio",
            goal: "Hold his life together while carrying old family pressure.",
            need: "Respect without being controlled.",
            fear: "Failing the people who still expect something from him.",
            want: "A future that feels like his own.",
            context: "Core NPC. Adam Sinclair is part of the main Sinclair family web.",
            hometown: "Bellefontaine, OH",
            address: "in town",
            tier: 1);

        EnsureCoreNpcRow(
            id: 3,
            name: "Lisa Sinclair",
            age: 49,
            gender: "Female",
            occupation: "Unassigned",
            location: "Bellefontaine, Ohio",
            goal: "Keep the family from breaking further.",
            need: "Emotional security and control over uncertainty.",
            fear: "Losing her children or losing her place in their lives.",
            want: "A family that still feels whole.",
            context: "Core NPC. Lisa Sinclair is part of the main Sinclair family web.",
            hometown: "Bellefontaine, OH",
            address: "in town",
            tier: 1);

        EnsureCoreNpcRow(
            id: 4,
            name: "Edward Sinclair",
            age: 52,
            gender: "Male",
            occupation: "Unassigned",
            location: "Bellefontaine, Ohio",
            goal: "Protect his name, family, and place in town.",
            need: "Control, respect, and a reason to believe he still matters.",
            fear: "Being exposed as weak or unnecessary.",
            want: "To be seen as the man holding everything together.",
            context: "Core NPC. Edward Sinclair is part of the main Sinclair family web.",
            hometown: "Bellefontaine, OH",
            address: "in town",
            tier: 1);
    }

    static void EnsureCoreNpcRow(
        int id,
        string name,
        int age,
        string gender,
        string occupation,
        string location,
        string goal,
        string need,
        string fear,
        string want,
        string context,
        string hometown,
        string address,
        int tier)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        var folderName = ProjectEveDatabaseSetup.GetNpcFolderName(id, name);
        var folderPath = ProjectEveDatabaseSetup.GetNpcFolderPath(id, name);
        var npcKey = $"npc_{id:D6}";

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO Characters
            (
                Id,
                NpcKey,
                FolderName,
                FolderPath,
                Name,
                Age,
                Gender,
                Occupation,
                Location,
                Status,
                Goal,
                Need,
                Fear,
                Want,
                PersonalityContext,
                Hometown,
                Address,
                Tier,
                UpdatedRealAt
            )
            VALUES
            (
                $id,
                $npcKey,
                $folderName,
                $folderPath,
                $name,
                $age,
                $gender,
                $occupation,
                $location,
                'Core',
                $goal,
                $need,
                $fear,
                $want,
                $context,
                $hometown,
                $address,
                $tier,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                NpcKey = $npcKey,
                FolderName = $folderName,
                FolderPath = $folderPath,
                Name = $name,
                Age = $age,
                Gender = $gender,
                Occupation = $occupation,
                Location = $location,
                Status = 'Core',
                Goal = $goal,
                Need = $need,
                Fear = $fear,
                Want = $want,
                PersonalityContext = $context,
                Hometown = $hometown,
                Address = $address,
                Tier = $tier,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$npcKey", npcKey);
        cmd.Parameters.AddWithValue("$folderName", folderName);
        cmd.Parameters.AddWithValue("$folderPath", folderPath);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$age", age);
        cmd.Parameters.AddWithValue("$gender", gender);
        cmd.Parameters.AddWithValue("$occupation", occupation);
        cmd.Parameters.AddWithValue("$location", location);
        cmd.Parameters.AddWithValue("$goal", goal);
        cmd.Parameters.AddWithValue("$need", need);
        cmd.Parameters.AddWithValue("$fear", fear);
        cmd.Parameters.AddWithValue("$want", want);
        cmd.Parameters.AddWithValue("$context", context);
        cmd.Parameters.AddWithValue("$hometown", hometown);
        cmd.Parameters.AddWithValue("$address", address);
        cmd.Parameters.AddWithValue("$tier", tier);

        cmd.ExecuteNonQuery();

        ProjectEveDatabaseSetup.EnsureNpcFolders(id, name);
    }

    static void EnsureNpcCore(SimCharacter npc)
    {
        try
        {
            CharacterFactory.EnsureCore(npc);
        }
        catch
        {
            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            npc.Money ??= new ProjectEve.Money.MoneyProfile();
            npc.Job ??= new ProjectEve.Money.JobProfile();
            npc.Traits ??= new NpcTraits();
            npc.Relationships ??= new List<Relationship>();
        }
    }

    static void EnsureNpcTraits(SimCharacter npc)
    {
        try
        {
            CharacterFactory.EnsureTraits(npc);
        }
        catch
        {
            npc.Traits ??= new NpcTraits();

            if (npc.Traits.GetAll().Count == 0)
                npc.Traits.InitializeFastDefaults();
        }
    }

    static int GenerateNextNpcId()
    {
        if (_nextNpcIdCache <= 0)
        {
            using var conn = new SqliteConnection("Data Source=" + DbPath);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM Characters;";
            var result = cmd.ExecuteScalar();

            int maxId = result is null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);

            _nextNpcIdCache = Math.Max(4, maxId);
        }

        _nextNpcIdCache++;

        return Math.Max(5, _nextNpcIdCache);
    }

    static string GenerateUniqueName()
    {
        string[] firstNames =
        {
            "Alex", "Jordan", "Sam", "Casey", "Riley", "Taylor", "Morgan", "Quinn",
            "Avery", "Drew", "Jamie", "Cameron", "Parker", "Reese", "Skyler", "Tessa",
            "Maya", "Nadine", "Bree", "Chris", "Olivia", "Hannah", "Derek", "Lena",
            "Elena", "Noah", "Mason", "Leah", "Audrey", "Mila", "Grant", "Tyler",
            "Alicia", "Jonah", "Luke", "Natalie", "Brooke", "Madison", "Isaac", "Wyatt",
            "Clara", "Ethan", "Grace", "Logan", "Naomi", "Caleb", "Julia", "Marcus",
            "Paige", "Trevor", "Sophie", "Eli", "Molly", "Blake", "Caroline", "Owen"
        };

        string[] lastNames =
        {
            "Miller", "Brooks", "Cole", "Hale", "Lang", "Quinn", "Park", "Shaw",
            "Grubb", "Rivera", "Molnar", "Nash", "Bell", "Crowe", "Diaz", "Walsh",
            "Turner", "Ross", "Kent", "Ward", "Bennett", "Avery", "Mitchell", "Reed",
            "Harper", "Foster", "Hughes", "Bryant", "Hayes", "Fletcher", "Owens", "Morgan"
        };

        for (int attempt = 0; attempt < 5000; attempt++)
        {
            string name = firstNames[Rng.Next(firstNames.Length)] + " " +
                          lastNames[Rng.Next(lastNames.Length)];

            if (NamesReservedThisRun.Contains(name))
                continue;

            if (CharacterNameExists(name))
                continue;

            NamesReservedThisRun.Add(name);
            return name;
        }

        string fallback = "Generated NPC " + Guid.NewGuid().ToString("N")[..8];
        NamesReservedThisRun.Add(fallback);
        return fallback;
    }

    static bool CharacterNameExists(string name)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Characters WHERE Name = $name;";
        cmd.Parameters.AddWithValue("$name", name);

        var result = Convert.ToInt32(cmd.ExecuteScalar());
        return result > 0;
    }

    static string PickGender()
    {
        string[] values = { "Female", "Male" };
        return values[Rng.Next(values.Length)];
    }

    static int PickTownTier()
    {
        int roll = Rng.Next(1, 101);

        if (roll <= 15)
            return 2;

        if (roll <= 60)
            return 3;

        return 4;
    }

    static string PickPreferredLane()
    {
        var rolls = new[]
        {
            "shop", "shop", "shop",
            "crew", "crew",
            "school",
            "casual", "casual", "casual",
            "art"
        };

        return rolls[Rng.Next(rolls.Length)];
    }

    static string PickLocalHometown()
    {
        string[] homes =
        {
            "Bellefontaine, OH",
            "Sidney, OH",
            "West Liberty, OH",
            "Urbana, OH",
            "De Graff, OH"
        };

        return homes[Rng.Next(homes.Length)];
    }

    static string PickOutOfTownLocation()
    {
        string[] locations =
        {
            "Columbus, Ohio",
            "Dayton, Ohio",
            "Lima, Ohio",
            "Toledo, Ohio",
            "Cincinnati, Ohio",
            "Cleveland, Ohio",
            "Indianapolis, Indiana",
            "Louisville, Kentucky",
            "Pittsburgh, Pennsylvania",
            "Nashville, Tennessee"
        };

        return locations[Rng.Next(locations.Length)];
    }

    static string PickHistoryRole()
    {
        string[] roles =
        {
            "Sibling",
            "Parent",
            "Cousin",
            "Old friend",
            "Ex-coworker",
            "Former classmate",
            "Online friend",
            "Distant relative",
            "Ex-partner",
            "Family friend"
        };

        return roles[Rng.Next(roles.Length)];
    }

    static string PickRelationshipType()
    {
        string[] types =
        {
            "family",
            "friend",
            "old_friend",
            "coworker_history",
            "romantic_history"
        };

        return types[Rng.Next(types.Length)];
    }

    static string AppendLine(string existing, string line)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return line;

        return existing.TrimEnd() + Environment.NewLine + line;
    }

    static void ResetBothDatabasesWithConfirmation()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine();
        Console.WriteLine("WARNING: This will delete BOTH Project Eve databases:");
        Console.WriteLine("  Main DB    : " + DbPath);
        Console.WriteLine("  History DB : " + HistoryDbPath);
        Console.WriteLine();
        Console.WriteLine("This does NOT delete NPC folders, pictures, voices, or other media.");
        Console.WriteLine("Type DELETE to continue.");
        Console.ResetColor();

        Console.Write("> ");
        string confirm = (Console.ReadLine() ?? "").Trim();

        if (!confirm.Equals("DELETE", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        bool ok = DeleteDatabaseFiles(DbPath);
        bool historyOk = DeleteDatabaseFiles(HistoryDbPath);

        if (ok && historyOk)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Both databases deleted.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Delete finished with warnings. Close any running ProjectEve apps and try again if files remain.");
            Console.ResetColor();
        }

        _nextNpcIdCache = 0;
        NamesReservedThisRun.Clear();

        try
        {
            CharacterFactory.ClearReservedJobSlots();
        }
        catch
        {
        }
    }

    static bool DeleteDatabaseFiles(string dbPath)
    {
        bool ok = true;

        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch
        {
        }

        string[] paths =
        {
            dbPath,
            dbPath + "-wal",
            dbPath + "-shm"
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            bool deleted = false;
            Exception? lastError = null;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    File.Delete(path);
                    deleted = !File.Exists(path);

                    if (deleted)
                    {
                        Console.WriteLine("Deleted " + path);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                try
                {
                    SqliteConnection.ClearAllPools();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    System.Threading.Thread.Sleep(150);
                }
                catch
                {
                }
            }

            if (!deleted)
            {
                ok = false;
                Console.WriteLine("FAILED to delete " + path + (lastError != null ? " — " + lastError.Message : ""));
            }
        }

        return ok;
    }
}