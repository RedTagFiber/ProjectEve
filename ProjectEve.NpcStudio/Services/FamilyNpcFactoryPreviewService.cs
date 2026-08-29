using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Preview-only Family NPC Factory.
///
/// It combines the saved family plan with the resolved canonical family graph.
/// Existing relatives are reused. Only truly missing requested relatives are
/// proposed as NEW NPCs.
///
/// This service DOES NOT write Characters, traits, physical profiles, folders,
/// relationships, history, memories, or any other persistent data.
/// </summary>
public sealed class FamilyNpcFactoryPreviewService
{
    private readonly NpcStudioOptions _options;
    private readonly FamilyGraphResolverService _familyGraph;

    private static readonly string[] FemaleFirstNames =
    [
        "Anna","Claire","Emily","Grace","Hannah","Julia","Laura","Megan",
        "Natalie","Rachel","Rebecca","Sarah","Victoria","Wendy","Caroline",
        "Diana","Erin","Heather","Katherine","Melissa","Nicole","Olivia"
    ];

    private static readonly string[] MaleFirstNames =
    [
        "Aaron","Benjamin","Christopher","Daniel","David","Eric","Ethan",
        "Gabriel","Henry","Jacob","James","Jason","Jonathan","Matthew",
        "Michael","Nathan","Nicholas","Robert","Ryan","Samuel","Thomas","William"
    ];

    private static readonly string[] GeneralOccupations =
    [
        "Teacher",
        "Nurse",
        "Office Manager",
        "Retail Manager",
        "Police Officer",
        "Mechanic",
        "Accountant",
        "Administrative Assistant",
        "Warehouse Supervisor",
        "Sales Representative",
        "Dental Assistant",
        "Bank Teller",
        "Electrician",
        "Restaurant Manager",
        "Library Assistant"
    ];

    private static readonly string[] Interests =
    [
        "cooking","local sports","gardening","reading","music","walking",
        "home projects","community events","movies","photography","fitness",
        "crafts","outdoor activities","family gatherings","travel"
    ];

    public FamilyNpcFactoryPreviewService(
        NpcStudioOptions options,
        FamilyGraphResolverService familyGraph)
    {
        _options = options;
        _familyGraph = familyGraph;
    }

    public FamilyNpcFactoryManifest BuildPreview(int rootNpcId)
    {
        using var conn = new SqliteConnection("Data Source=" + _options.MainDbPath);
        conn.Open();

        var root = LoadRoot(conn, rootNpcId);
        if (root is null)
            return new FamilyNpcFactoryManifest
            {
                RootNpcId = rootNpcId,
                Warnings = { "Root NPC could not be loaded." }
            };

        var plan = LoadPlan(conn, rootNpcId);
        if (plan is null)
            return new FamilyNpcFactoryManifest
            {
                RootNpcId = rootNpcId,
                RootName = root.Name,
                Warnings = { "No saved Family Setup plan exists for this NPC." }
            };

        var graph = _familyGraph.Resolve(rootNpcId);

        var manifest = new FamilyNpcFactoryManifest
        {
            RootNpcId = rootNpcId,
            RootName = root.Name
        };

        var rootTraits = LoadTraits(conn, rootNpcId);
        var usedNames = LoadAllNames(conn);

        if (plan.CreateMother)
            AddRequested(manifest, graph, root, rootTraits, usedNames, "mother", "Mother", "Female", 2);

        if (plan.CreateFather)
            AddRequested(manifest, graph, root, rootTraits, usedNames, "father", "Father", "Male", 2);

        for (var i = 1; i <= Math.Max(0, plan.BrotherCount); i++)
            AddRequested(manifest, graph, root, rootTraits, usedNames, $"brother-{i}", "Brother", "Male", 2, i);

        for (var i = 1; i <= Math.Max(0, plan.SisterCount); i++)
            AddRequested(manifest, graph, root, rootTraits, usedNames, $"sister-{i}", "Sister", "Female", 2, i);

        if (plan.CreateMaternalGrandmother)
            AddRequested(manifest, graph, root, rootTraits, usedNames, "maternal-grandmother", "Maternal Grandmother", "Female", 3);

        if (plan.CreateMaternalGrandfather)
            AddRequested(manifest, graph, root, rootTraits, usedNames, "maternal-grandfather", "Maternal Grandfather", "Male", 3);

        if (plan.CreatePaternalGrandmother)
            AddRequested(manifest, graph, root, rootTraits, usedNames, "paternal-grandmother", "Paternal Grandmother", "Female", 3);

        if (plan.CreatePaternalGrandfather)
            AddRequested(manifest, graph, root, rootTraits, usedNames, "paternal-grandfather", "Paternal Grandfather", "Male", 3);

        if (manifest.Rows.Count == 0)
            manifest.Warnings.Add("The saved Family Setup plan does not currently request any Pass 1 relatives.");

        return manifest;
    }

    private void AddRequested(
        FamilyNpcFactoryManifest manifest,
        FamilyGraphPreview graph,
        RootNpc root,
        Dictionary<string,int> rootTraits,
        HashSet<string> usedNames,
        string memberKey,
        string role,
        string gender,
        int tier,
        int ordinal = 1)
    {
        var existing = FindExistingForRole(graph, role, ordinal);
        if (existing is not null)
        {
            manifest.Rows.Add(new FamilyNpcFactoryRow
            {
                Action = "REUSE NPC",
                MemberKey = memberKey,
                FamilyRole = role,
                ExistingNpcId = existing.NpcId,
                ProposedName = existing.Name,
                ProposedTier = tier,
                Notes = $"Canonical family graph already resolves this role. Locked to reuse NPC {existing.NpcId}."
            });

            manifest.ProposedRelationshipWrites.Add(
                $"REUSE existing canonical link/path for {root.Name} → {existing.Name} ({role}).");
            return;
        }

        var seed = StableSeed(root.Id, memberKey);
        var rng = new Random(seed);

        var lastName = GuessFamilyLastName(root.Name, role);
        var firstName = UniqueFirstName(gender, lastName, usedNames, rng);
        var fullName = (firstName + " " + lastName).Trim();
        usedNames.Add(fullName);

        var age = EstimateAge(root.Age, role, ordinal, rng);
        var occupation = PickOccupation(age, role, rng);
        var employer = occupation == "Retired" ? "" : "To be matched from existing workplace registry";
        var location = string.IsNullOrWhiteSpace(root.Location) ? root.Hometown : root.Location;
        var hometown = string.IsNullOrWhiteSpace(root.Hometown) ? location : root.Hometown;

        var row = new FamilyNpcFactoryRow
        {
            Action = "NEW NPC",
            MemberKey = memberKey,
            FamilyRole = role,
            ProposedName = fullName,
            ProposedAge = age,
            ProposedGender = gender,
            ProposedTier = tier,
            Location = location ?? "",
            Hometown = hometown ?? "",
            Occupation = occupation,
            Employer = employer,
            PersonalitySummary = BuildPersonality(role, root, rng),
            Goal = BuildGoal(role, age, rng),
            Need = BuildNeed(role, rng),
            Fear = BuildFear(role, rng),
            Want = BuildWant(role, age, rng),
            Interests = PickDistinctInterests(rng, 3),
            Traits = BuildTraits(rootTraits, role, rng),
            PhysicalDirection = BuildPhysicalDirection(role, root, rng),
            EducationCareerDirection = BuildEducationCareer(age, occupation),
            Notes =
                "Preview-only populated family NPC proposal. Shares some family context and soft-trait resemblance " +
                "with the root NPC without cloning them. TRUE HISTORY and memories are intentionally not generated yet."
        };

        manifest.Rows.Add(row);

        manifest.ProposedRelationshipWrites.Add(
            $"{root.Name} ↔ {fullName}: create canonical two-way {role} family relationship on confirm.");

        if (role.Contains("Grand", StringComparison.OrdinalIgnoreCase))
            manifest.ProposedRelationshipWrites.Add(
                $"{fullName}: attach to the correct parent branch so siblings inherit the same grandparent.");

        if (role is "Brother" or "Sister")
            manifest.ProposedRelationshipWrites.Add(
                $"{fullName}: attach to the same resolved parents as {root.Name} when those parents exist.");
    }

    private static FamilyGraphPerson? FindExistingForRole(
        FamilyGraphPreview graph,
        string requestedRole,
        int ordinal)
    {
        bool Matches(string actual)
        {
            if (requestedRole.Equals(actual, StringComparison.OrdinalIgnoreCase))
                return true;

            if (requestedRole == "Mother")
                return actual.Equals("Stepmother", StringComparison.OrdinalIgnoreCase);

            if (requestedRole == "Father")
                return actual.Equals("Stepfather", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        var matches = graph.ExistingPeople
            .Where(x => Matches(x.Role))
            .OrderBy(x => x.NpcId)
            .ToList();

        return ordinal > 0 && ordinal <= matches.Count ? matches[ordinal - 1] : null;
    }

    private static RootNpc? LoadRoot(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            Id,
            IFNULL(Name,''),
            IFNULL(Age,0),
            IFNULL(Gender,''),
            IFNULL(Location,''),
            IFNULL(Hometown,''),
            IFNULL(Occupation,''),
            IFNULL(PersonalityContext,''),
            IFNULL(PersonalitySummary,''),
            IFNULL(Goal,''),
            IFNULL(Need,''),
            IFNULL(Fear,''),
            IFNULL(Want,'')
        FROM Characters
        WHERE Id=$id;
        """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new RootNpc(
            r.GetInt32(0),
            r.GetString(1),
            Convert.ToInt32(r.GetValue(2)),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.GetString(6),
            r.GetString(7),
            r.GetString(8),
            r.GetString(9),
            r.GetString(10),
            r.GetString(11),
            r.GetString(12));
    }

    private static FamilyPlan? LoadPlan(SqliteConnection conn, int rootId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            CreateMother, CreateFather, BrotherCount, SisterCount,
            CreateMaternalGrandmother, CreateMaternalGrandfather,
            CreatePaternalGrandmother, CreatePaternalGrandfather
        FROM NpcFamilyBuildPlans
        WHERE RootNpcId=$id;
        """;
        cmd.Parameters.AddWithValue("$id", rootId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new FamilyPlan(
            Bool(r,0), Bool(r,1), Int(r,2), Int(r,3),
            Bool(r,4), Bool(r,5), Bool(r,6), Bool(r,7));
    }

    private static Dictionary<string,int> LoadTraits(SqliteConnection conn, int npcId)
    {
        var result = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            SELECT IFNULL(TraitName,''), IFNULL(CurrentValue,0)
            FROM NpcTraitValues
            WHERE NpcId=$id
              AND IFNULL(IsEnabled,1) <> 0
              AND TRIM(IFNULL(TraitName,'')) <> '';
            """;
            cmd.Parameters.AddWithValue("$id", npcId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0).Trim();
                if (!result.ContainsKey(name))
                    result[name] = Convert.ToInt32(r.GetValue(1));
            }
        }
        catch (SqliteException)
        {
            // Preview remains usable even if a legacy DB lacks this table.
        }

        return result;
    }

    private static HashSet<string> LoadAllNames(SqliteConnection conn)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(Name,'') FROM Characters WHERE TRIM(IFNULL(Name,'')) <> '';";
        using var r = cmd.ExecuteReader();

        while (r.Read())
            result.Add(r.GetString(0).Trim());

        return result;
    }

    private static string UniqueFirstName(
        string gender,
        string lastName,
        HashSet<string> usedNames,
        Random rng)
    {
        var pool = gender.Equals("Female", StringComparison.OrdinalIgnoreCase)
            ? FemaleFirstNames
            : MaleFirstNames;

        var start = rng.Next(pool.Length);
        for (var i = 0; i < pool.Length; i++)
        {
            var first = pool[(start + i) % pool.Length];
            if (!usedNames.Contains($"{first} {lastName}".Trim()))
                return first;
        }

        return pool[start] + " " + rng.Next(2, 99);
    }

    private static int EstimateAge(int rootAge, string role, int ordinal, Random rng)
    {
        if (rootAge <= 0) rootAge = 25;

        if (role == "Mother" || role == "Father")
            return Math.Max(rootAge + 18, rootAge + rng.Next(22, 36));

        if (role == "Brother" || role == "Sister")
        {
            var offset = rng.Next(-6, 7);
            if (offset == 0) offset = ordinal % 2 == 0 ? -2 : 2;
            return Math.Max(0, rootAge + offset);
        }

        if (role.Contains("Grand", StringComparison.OrdinalIgnoreCase))
            return Math.Max(rootAge + 40, rootAge + rng.Next(45, 65));

        return rootAge;
    }

    private static string PickOccupation(int age, string role, Random rng)
    {
        if (age >= 67 && rng.NextDouble() < 0.65)
            return "Retired";

        return GeneralOccupations[rng.Next(GeneralOccupations.Length)];
    }

    private static string BuildPersonality(string role, RootNpc root, Random rng)
    {
        var styles = new[]
        {
            "steady, practical, warm, and observant",
            "social, dependable, patient, and quietly stubborn",
            "thoughtful, loyal, independent, and family-minded",
            "friendly, grounded, organized, and emotionally perceptive",
            "reserved at first, reliable, caring, and strong-willed",
            "good-humored, responsible, curious, and protective"
        };

        var rootEcho =
            !string.IsNullOrWhiteSpace(root.PersonalitySummary)
                ? " Shares a few family-shaped tendencies with the root NPC but remains a distinct person."
                : " Designed as a distinct relative rather than a personality clone.";

        return $"{role}: {styles[rng.Next(styles.Length)]}.{rootEcho}";
    }

    private static string BuildGoal(string role, int age, Random rng)
    {
        if (age >= 65)
            return rng.NextDouble() < 0.5
                ? "Stay connected to family while protecting independence."
                : "Enjoy a stable later life and remain useful to the people they care about.";

        return rng.Next(4) switch
        {
            0 => "Build a stable and satisfying life without losing close family connections.",
            1 => "Become more secure in work and home life.",
            2 => "Make progress on a personal ambition while maintaining important relationships.",
            _ => "Create a life that feels dependable, meaningful, and self-directed."
        };
    }

    private static string BuildNeed(string role, Random rng)
        => rng.Next(4) switch
        {
            0 => "Respect and reliability from the people closest to them.",
            1 => "A sense of belonging without feeling controlled.",
            2 => "Emotional safety and practical stability.",
            _ => "To feel valued for who they are, not only for their family role."
        };

    private static string BuildFear(string role, Random rng)
        => rng.Next(4) switch
        {
            0 => "Family conflict becoming permanent.",
            1 => "Losing independence or becoming trapped by obligations.",
            2 => "Failing the people who depend on them.",
            _ => "Being misunderstood by the people whose opinion matters most."
        };

    private static string BuildWant(string role, int age, Random rng)
        => rng.Next(4) switch
        {
            0 => "More time for personal interests outside routine obligations.",
            1 => "A calmer and more predictable home life.",
            2 => "A stronger sense of financial and personal security.",
            _ => "To improve one important relationship without forcing it."
        };

    private static List<string> PickDistinctInterests(Random rng, int count)
        => Interests
            .OrderBy(_ => rng.Next())
            .Take(Math.Clamp(count, 1, Interests.Length))
            .ToList();

    private static List<FamilyNpcFactoryTrait> BuildTraits(
        Dictionary<string,int> rootTraits,
        string role,
        Random rng)
    {
        if (rootTraits.Count > 0)
        {
            return rootTraits
                .OrderBy(x => x.Key)
                .Take(10)
                .Select(x => new FamilyNpcFactoryTrait
                {
                    Name = x.Key,
                    Value = Math.Clamp(x.Value + rng.Next(-18, 19), 0, 100)
                })
                .ToList();
        }

        var defaults = new[]
        {
            "Loyalty","Patience","Sociability","Empathy","Conscientiousness",
            "Assertiveness","Curiosity","RiskTolerance"
        };

        return defaults.Select(name => new FamilyNpcFactoryTrait
        {
            Name = name,
            Value = rng.Next(35, 76)
        }).ToList();
    }

    private static string BuildPhysicalDirection(string role, RootNpc root, Random rng)
    {
        var resemblance = role.Contains("Grand", StringComparison.OrdinalIgnoreCase)
            ? "some plausible multigenerational family resemblance"
            : "some visible family resemblance without duplicating the root NPC";

        return $"Natural realistic adult/age-appropriate appearance with {resemblance}. " +
               "Exact height, build, hair, eyes, and other canonical physical fields should be generated on confirm.";
    }

    private static string BuildEducationCareer(int age, string occupation)
    {
        if (occupation == "Retired")
            return "Generate a plausible completed education and prior career appropriate to age, location, and family timeline.";

        return $"Generate education/training consistent with age and proposed occupation ({occupation}); employer must be matched to the canonical workplace system before final write.";
    }

    private static string GuessFamilyLastName(string rootName, string role)
    {
        var parts = rootName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rootLast = parts.Length == 0 ? "Family" : parts[^1];

        // Preview-only: keep the visible family surname unless the eventual
        // confirmed relationship/history supplies a different maiden/married name.
        return rootLast;
    }

    private static int StableSeed(int rootId, string memberKey)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + rootId;
            foreach (var ch in memberKey)
                hash = hash * 31 + ch;
            return hash;
        }
    }

    private static bool Bool(SqliteDataReader r, int i)
        => !r.IsDBNull(i) && Convert.ToInt32(r.GetValue(i)) != 0;

    private static int Int(SqliteDataReader r, int i)
        => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

    private sealed record RootNpc(
        int Id,
        string Name,
        int Age,
        string Gender,
        string Location,
        string Hometown,
        string Occupation,
        string PersonalityContext,
        string PersonalitySummary,
        string Goal,
        string Need,
        string Fear,
        string Want);

    private sealed record FamilyPlan(
        bool CreateMother,
        bool CreateFather,
        int BrotherCount,
        int SisterCount,
        bool CreateMaternalGrandmother,
        bool CreateMaternalGrandfather,
        bool CreatePaternalGrandmother,
        bool CreatePaternalGrandfather);
}
