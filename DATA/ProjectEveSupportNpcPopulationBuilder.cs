using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Turns open support scaffold slots into actual non-family NPCs.
/// This first implementation is deterministic/local and intentionally does NOT
/// create family or TRUE HISTORY. It gives history a real populated town to query.
/// </summary>
public static class ProjectEveSupportNpcPopulationBuilder
{
    private static readonly Random Rng = new();

    private static readonly string[] MaleFirst =
    [
        "Michael","Daniel","Robert","James","William","David","Thomas","Joseph","Andrew","Matthew",
        "Christopher","Ryan","Jason","Kevin","Brian","Eric","Scott","Mark","Steven","Patrick",
        "Nathan","Justin","Adam","Jordan","Kyle","Sean","Derek","Paul","Timothy","Gregory"
    ];

    private static readonly string[] FemaleFirst =
    [
        "Sarah","Emily","Jessica","Amanda","Rachel","Megan","Hannah","Lauren","Nicole","Ashley",
        "Jennifer","Melissa","Rebecca","Stephanie","Heather","Michelle","Erin","Katie","Laura","Amy",
        "Danielle","Samantha","Brittany","Christina","Elizabeth","Molly","Allison","Katherine","Julia","Anna"
    ];

    private static readonly string[] LastNames =
    [
        "Best","Mercer","Bradley","Carter","Hayes","Reynolds","Cole","Dawson","Parker","Miller",
        "Turner","Bennett","Cooper","Morgan","Foster","Hughes","Ward","Price","Brooks","Sullivan",
        "Myers","Bailey","Griffin","Russell","Powell","Barnes","Fisher","Webb","Porter","Holland",
        "Mason","Wells","Dean","Cross","Burke","Lane","Walsh","Spencer","Murray","Harper"
    ];

    private static readonly string[] PersonalityBits =
    [
        "warm and observant",
        "quiet and dependable",
        "direct but fair",
        "funny and patient",
        "organized and practical",
        "easygoing but private",
        "community-minded and steady",
        "sharp-witted and demanding",
        "kind but reserved",
        "social and energetic",
        "calm under pressure",
        "helpful and detail-oriented"
    ];

    private static readonly string[] AppearanceBits =
    [
        "average build; practical everyday style",
        "lean build; neat casual style",
        "solid build; work-oriented clothing",
        "average build; understated professional style",
        "athletic build; casual practical style",
        "slender build; simple comfortable clothing"
    ];

    public static void Populate(int maxCount = 400)
    {
        ProjectEveHistoryGenerationFoundationSchema.Ensure();

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();

        var openSlots = LoadOpenSlots(conn, Math.Clamp(maxCount, 1, 5000));

        if (openSlots.Count == 0)
        {
            Console.WriteLine("No open support NPC scaffold slots found.");
            PrintStatus(conn);
            return;
        }

        var usedNames = LoadUsedNames(conn);
        var nextId = GetNextCharacterId(conn);
        var created = 0;

        using var tx = conn.BeginTransaction();

        foreach (var slot in openSlots)
        {
            var gender = PickGender();
            var name = GenerateUniqueName(gender, usedNames);
            usedNames.Add(name);

            var birthYear = PickBirthYear(slot);
            var age = Math.Max(18, DateTime.UtcNow.Year - birthYear);
            var tier = PickTier(slot);

            InsertCharacter(
                conn, tx,
                nextId,
                name,
                gender,
                age,
                birthYear,
                slot,
                tier);

            EnsureBasicProfile(conn, tx, nextId, slot);

            AssignSlot(conn, tx, slot.SlotId, nextId);

            ProjectEveDatabaseSetup.EnsureNpcFolders(nextId, name);

            created++;
            nextId++;
        }

        tx.Commit();

        Console.WriteLine();
        Console.WriteLine($"Created {created} support NPC Characters from scaffold slots.");
        Console.WriteLine("Family created: NO");
        Console.WriteLine("TRUE HISTORY written: NO");
        Console.WriteLine("Subjective memories written: NO");
        Console.WriteLine();
        PrintStatus(conn);
    }

    public static void PrintStatus()
    {
        ProjectEveHistoryGenerationFoundationSchema.Ensure();

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();

        PrintStatus(conn);
    }

    private sealed record Slot(
        int SlotId,
        string SlotKey,
        string Category,
        string RoleType,
        string InstitutionType,
        string InstitutionId,
        string Subject,
        int? GradeMin,
        int? GradeMax,
        int? ActiveStartYear,
        int? ActiveEndYear,
        int? PreferredAgeMin,
        int? PreferredAgeMax,
        string LocationId,
        double ReuseWeight);

    private static List<Slot> LoadOpenSlots(SqliteConnection conn, int limit)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            SlotId, SlotKey, Category, RoleType, InstitutionType,
            InstitutionId, SubjectOrSpecialty,
            GradeMin, GradeMax,
            ActiveStartYear, ActiveEndYear,
            PreferredAgeMin, PreferredAgeMax,
            LocationId, ReuseWeight
        FROM SupportNpcScaffoldSlots
        WHERE AssignedNpcId IS NULL
          AND IsFamilySlot = 0
          AND Status = 'Open'
        ORDER BY SlotId
        LIMIT $limit;
        """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<Slot>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new Slot(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.GetString(13),
                reader.GetDouble(14)));
        }

        return list;
    }

    private static HashSet<string> LoadUsedNames(SqliteConnection conn)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Characters WHERE TRIM(Name) <> '';";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(0));

        return set;
    }

    private static int GetNextCharacterId(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Characters;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void InsertCharacter(
        SqliteConnection conn,
        SqliteTransaction tx,
        int id,
        string name,
        string gender,
        int age,
        int birthYear,
        Slot slot,
        int tier)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = """
        INSERT INTO Characters
        (
            Id, WorldId, NpcKey, Name, Age, BirthYear, Gender,
            Occupation, Employer,
            CurrentLocationId, HomeLocationId, WorkLocationId,
            Location, Hometown, Status, Tier,
            PersonalityContext, PersonalitySummary,
            BackstoryShort, BackstoryLong,
            CreatedRealAt, UpdatedRealAt
        )
        VALUES
        (
            $id, 'smalltown', $npcKey, $name, $age, $birthYear, $gender,
            $occupation, $employer,
            $currentLocationId, '', $workLocationId,
            $location, 'Bellefontaine, OH', 'Scaffold', $tier,
            $personalityContext, $personalitySummary,
            $backstoryShort, '',
            CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
        );
        """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$npcKey", $"support-{id:D6}");
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$age", age);
        cmd.Parameters.AddWithValue("$birthYear", birthYear);
        cmd.Parameters.AddWithValue("$gender", gender);
        cmd.Parameters.AddWithValue("$tier", tier);
        cmd.Parameters.AddWithValue("$occupation", slot.RoleType);
        cmd.Parameters.AddWithValue("$employer",
            string.IsNullOrWhiteSpace(slot.InstitutionId)
                ? slot.InstitutionType
                : slot.InstitutionId);
        cmd.Parameters.AddWithValue("$currentLocationId", slot.LocationId ?? "");
        cmd.Parameters.AddWithValue("$workLocationId", slot.LocationId ?? "");
        cmd.Parameters.AddWithValue("$location", "Bellefontaine, OH");

        var personality = PersonalityBits[Rng.Next(PersonalityBits.Length)];
        cmd.Parameters.AddWithValue("$personalityContext",
            $"Support-town NPC. {personality}. History should reuse this person when role/year/place fit.");
        cmd.Parameters.AddWithValue("$personalitySummary", personality);

        var gradeText =
            slot.GradeMin is null && slot.GradeMax is null
                ? ""
                : $" Grades {slot.GradeMin?.ToString() ?? "?"}-{slot.GradeMax?.ToString() ?? "?"}.";

        cmd.Parameters.AddWithValue(
            "$backstoryShort",
            $"{slot.Category}: {slot.RoleType}.{gradeText} Generated as reusable non-family town scaffold.");

        cmd.ExecuteNonQuery();
    }

    private static void EnsureBasicProfile(
        SqliteConnection conn,
        SqliteTransaction tx,
        int npcId,
        Slot slot)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = """
        INSERT INTO NpcPhysicalProfiles
        (
            NpcId,
            BodyType,
            DefaultClothingStyle,
            Notes,
            UpdatedRealAt
        )
        VALUES
        (
            $id,
            $body,
            $clothing,
            $notes,
            CURRENT_TIMESTAMP
        )
        ON CONFLICT(NpcId) DO NOTHING;
        """;

        var appearance = AppearanceBits[Rng.Next(AppearanceBits.Length)];
        var parts = appearance.Split(';', 2);

        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$body", parts[0].Trim());
        cmd.Parameters.AddWithValue(
            "$clothing",
            parts.Length > 1 ? parts[1].Trim() : "practical everyday style");
        cmd.Parameters.AddWithValue(
            "$notes",
            $"Scaffold profile for {slot.RoleType}. Detailed appearance should be deepened only if history makes this NPC important.");

        cmd.ExecuteNonQuery();

        using var life = conn.CreateCommand();
        life.Transaction = tx;
        life.CommandText = """
        INSERT INTO NpcLifeBuildProfiles
        (
            NpcId, DesiredDepthTier, BuildMode,
            CharacterDirection, HistoryDepth,
            FamilyStatus, HistoryStatus, SubjectiveStatus,
            PresentLifeStatus, PhotoStatus, VoiceStatus,
            OverallPercent, LockedForCanon, Notes, UpdatedRealAt
        )
        SELECT
            $id, Tier, 'Scaffold',
            PersonalitySummary, 'InheritedThroughParticipation',
            'NotStarted', 'NotStarted', 'NotStarted',
            'ScaffoldOnly', 'NotStarted', 'NotStarted',
            15, 0,
            'Non-family support NPC. Allow history to deepen this character organically.',
            CURRENT_TIMESTAMP
        FROM Characters
        WHERE Id=$id
        ON CONFLICT(NpcId) DO NOTHING;
        """;
        life.Parameters.AddWithValue("$id", npcId);
        life.ExecuteNonQuery();
    }

    private static void AssignSlot(
        SqliteConnection conn,
        SqliteTransaction tx,
        int slotId,
        int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = """
        UPDATE SupportNpcScaffoldSlots
        SET AssignedNpcId=$npcId,
            Status='Assigned',
            UpdatedRealAt=CURRENT_TIMESTAMP
        WHERE SlotId=$slotId;
        """;

        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$slotId", slotId);
        cmd.ExecuteNonQuery();
    }

    private static string PickGender()
        => Rng.NextDouble() < 0.5 ? "Male" : "Female";

    private static string GenerateUniqueName(
        string gender,
        HashSet<string> used)
    {
        var firstPool = gender == "Male" ? MaleFirst : FemaleFirst;

        for (var tries = 0; tries < 5000; tries++)
        {
            var first = firstPool[Rng.Next(firstPool.Length)];
            var last = LastNames[Rng.Next(LastNames.Length)];
            var name = $"{first} {last}";

            if (!used.Contains(name))
                return name;
        }

        return $"{firstPool[Rng.Next(firstPool.Length)]} {LastNames[Rng.Next(LastNames.Length)]} {Guid.NewGuid().ToString("N")[..4]}";
    }

    private static int PickBirthYear(Slot slot)
    {
        var now = DateTime.UtcNow.Year;

        if (slot.PreferredAgeMin is not null || slot.PreferredAgeMax is not null)
        {
            var minAge = slot.PreferredAgeMin ?? 24;
            var maxAge = slot.PreferredAgeMax ?? 70;
            if (maxAge < minAge) (minAge, maxAge) = (maxAge, minAge);
            return now - Rng.Next(minAge, maxAge + 1);
        }

        if (slot.Category == "Social")
            return now - Rng.Next(18, 35);

        if (slot.Category == "School")
            return now - Rng.Next(25, 68);

        return now - Rng.Next(22, 72);
    }

    private static int PickTier(Slot slot)
    {
        // Most scaffold people start low, but recurring/high-exposure roles can start Tier 4.
        if (slot.ReuseWeight >= 2.0)
            return Rng.NextDouble() < 0.65 ? 4 : 5;

        return Rng.NextDouble() < 0.25 ? 4 : 5;
    }

    private static void PrintStatus(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            COUNT(*) AS TotalSlots,
            SUM(CASE WHEN AssignedNpcId IS NOT NULL THEN 1 ELSE 0 END) AS Assigned,
            SUM(CASE WHEN AssignedNpcId IS NULL THEN 1 ELSE 0 END) AS Open
        FROM SupportNpcScaffoldSlots
        WHERE IsFamilySlot=0;
        """;

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            Console.WriteLine($"Support slots total : {reader.GetInt32(0)}");
            Console.WriteLine($"Assigned NPCs       : {reader.GetInt32(1)}");
            Console.WriteLine($"Open slots          : {reader.GetInt32(2)}");
        }

        reader.Close();

        using var byCategory = conn.CreateCommand();
        byCategory.CommandText = """
        SELECT s.Category, COUNT(*) AS C
        FROM SupportNpcScaffoldSlots s
        WHERE s.AssignedNpcId IS NOT NULL
        GROUP BY s.Category
        ORDER BY s.Category;
        """;

        using var r = byCategory.ExecuteReader();
        Console.WriteLine();
        Console.WriteLine("Assigned by category:");
        while (r.Read())
            Console.WriteLine($"  {r.GetString(0),-18} {r.GetInt32(1),4}");
    }
}
