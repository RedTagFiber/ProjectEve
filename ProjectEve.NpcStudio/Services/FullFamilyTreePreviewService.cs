using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Stage 3A full-family-tree designer.
///
/// Preview only. No NPCs or relationships are created.
///
/// Stage 3A-3 rules:
/// - exact aunt/uncle spouse + child branches
/// - current and birth surnames are separate
/// - every outside spouse receives a unique birth-family surname by default
/// - marriage surname policy is explicit per branch
/// - child surname policy is explicit per branch
/// </summary>
public sealed class FullFamilyTreePreviewService
{
    private readonly NpcStudioOptions _options;
    private readonly CanonicalKinshipTitleService _kinship;

    private static readonly string[] SurnamePool =
    [
        "Bennett","Carter","Hayes","Mercer","Collins","Parker",
        "Foster","Sullivan","Miller","Reed","Walsh","Turner",
        "Hughes","Morgan","Dalton","Harris","Brooks","Griffin",
        "Mason","Walker","Porter","Dawson","Snyder","Keller",
        "Fletcher","Monroe","Bradley","Murray","Warren","Holland",
        "Pierce","Franklin","Lawson","Chambers","Barrett","Vaughn",
        "Stephens","Ramsey","Cross","Hawkins","Bishop","Fleming"
    ];

    public FullFamilyTreePreviewService(
        NpcStudioOptions options,
        CanonicalKinshipTitleService kinship)
    {
        _options = options;
        _kinship = kinship;
    }

    public FullFamilyTreePreview Build(
        int rootNpcId,
        FullFamilyTreeDesign design)
    {
        var rootName = GetName(rootNpcId);

        var result = new FullFamilyTreePreview
        {
            RootNpcId = rootNpcId,
            RootName = rootName
        };

        if (rootNpcId <= 0 || string.IsNullOrWhiteSpace(rootName))
        {
            result.Warnings.Add("Root NPC could not be loaded.");
            return result;
        }

        var existing = _kinship.ResolveFamily(rootNpcId);
        var surnames = BuildSurnameContext(rootNpcId, existing);

        result.RootCurrentSurname = surnames.RootCurrent;
        result.RootBirthSurname = surnames.RootBirth;
        result.MaternalFamilySurname = surnames.Maternal;
        result.PaternalFamilySurname = surnames.Paternal;

        var usedNpcIds = new HashSet<int>();

        var usedSurnames = surnames.AllKnown()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AddSingle(
            result, existing, usedNpcIds,
            "mother", "Mother", "Direct parent",
            design.IncludeMother,
            x => x.Kind == "Parent" &&
                 (
                     x.Title.Equals("Mother", StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Equals("Stepmother", StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Equals("Adoptive Mother", StringComparison.OrdinalIgnoreCase)
                 ),
            surnames.RootCurrent,
            surnames.Maternal);

        AddSingle(
            result, existing, usedNpcIds,
            "father", "Father", "Direct parent",
            design.IncludeFather,
            x => x.Kind == "Parent" &&
                 (
                     x.Title.Equals("Father", StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Equals("Stepfather", StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Equals("Adoptive Father", StringComparison.OrdinalIgnoreCase)
                 ),
            surnames.Paternal,
            surnames.Paternal);

        AddMany(
            result, existing, usedNpcIds,
            "brother", "Brother", "Sibling",
            Math.Clamp(design.BrotherCount, 0, 8),
            x => (x.Kind is "Sibling" or "HalfSibling" or "StepSibling") &&
                 (
                     x.Title.Contains("brother", StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Equals("Sibling", StringComparison.OrdinalIgnoreCase)
                 ),
            surnames.RootCurrent,
            surnames.RootBirth);

        AddMany(
            result, existing, usedNpcIds,
            "sister", "Sister", "Sibling",
            Math.Clamp(design.SisterCount, 0, 8),
            x => (x.Kind is "Sibling" or "HalfSibling" or "StepSibling") &&
                 (
                     x.Title.Contains("sister", StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Equals("Sibling", StringComparison.OrdinalIgnoreCase)
                 ),
            surnames.RootCurrent,
            surnames.RootBirth);

        AddSingle(
            result, existing, usedNpcIds,
            "maternal-grandmother", "Maternal Grandmother", "Grandparent",
            design.IncludeMaternalGrandmother,
            x => x.Kind == "Grandparent" &&
                 x.FamilySide.Equals("Maternal", StringComparison.OrdinalIgnoreCase) &&
                 x.Title.Equals("Grandmother", StringComparison.OrdinalIgnoreCase),
            surnames.Maternal,
            surnames.MaternalGrandmotherBirth);

        AddSingle(
            result, existing, usedNpcIds,
            "maternal-grandfather", "Maternal Grandfather", "Grandparent",
            design.IncludeMaternalGrandfather,
            x => x.Kind == "Grandparent" &&
                 x.FamilySide.Equals("Maternal", StringComparison.OrdinalIgnoreCase) &&
                 x.Title.Equals("Grandfather", StringComparison.OrdinalIgnoreCase),
            surnames.Maternal,
            surnames.Maternal);

        AddSingle(
            result, existing, usedNpcIds,
            "paternal-grandmother", "Paternal Grandmother", "Grandparent",
            design.IncludePaternalGrandmother,
            x => x.Kind == "Grandparent" &&
                 x.FamilySide.Equals("Paternal", StringComparison.OrdinalIgnoreCase) &&
                 x.Title.Equals("Grandmother", StringComparison.OrdinalIgnoreCase),
            surnames.Paternal,
            surnames.PaternalGrandmotherBirth);

        AddSingle(
            result, existing, usedNpcIds,
            "paternal-grandfather", "Paternal Grandfather", "Grandparent",
            design.IncludePaternalGrandfather,
            x => x.Kind == "Grandparent" &&
                 x.FamilySide.Equals("Paternal", StringComparison.OrdinalIgnoreCase) &&
                 x.Title.Equals("Grandfather", StringComparison.OrdinalIgnoreCase),
            surnames.Paternal,
            surnames.Paternal);

        foreach (var branch in design.Branches
                     .OrderBy(x => SideOrder(x.Side))
                     .ThenBy(x => x.BloodRole)
                     .ThenBy(x => x.Ordinal))
        {
            AddExtendedBranch(
                result,
                existing,
                usedNpcIds,
                usedSurnames,
                rootNpcId,
                branch,
                surnames);
        }

        if (design.IncludeGreatGrandparents)
        {
            AddGreatGrandparents(
                result,
                usedNpcIds,
                usedSurnames,
                rootNpcId,
                surnames);
        }

        AddSingle(
            result, existing, usedNpcIds,
            "root-spouse", "Spouse", "Root household",
            design.IncludeRootSpouse,
            x => x.Kind == "Spouse",
            surnames.RootCurrent,
            "");

        AddMany(
            result, existing, usedNpcIds,
            "root-child", "Child", "Root household",
            Math.Clamp(design.RootChildCount, 0, 8),
            x => x.Kind == "Child",
            surnames.RootCurrent,
            surnames.RootCurrent);

        foreach (var extra in existing.Where(x => !usedNpcIds.Contains(x.TargetNpcId)))
        {
            result.ExistingOutsideDesign.Add(new FullFamilyTreePreviewRow
            {
                MemberKey = $"existing-{extra.TargetNpcId}",
                RequestedRole = DisplayRole(extra),
                Branch = string.IsNullOrWhiteSpace(extra.FamilySide)
                    ? "Existing canonical family"
                    : extra.FamilySide,
                Action = "KEEP EXISTING",
                ExistingNpcId = extra.TargetNpcId,
                ExistingName = extra.TargetName,
                Notes =
                    "This canonical relative already exists but is outside the requested design. " +
                    "Stage 3A never removes family."
            });
        }

        result.Rows = result.Rows
            .OrderBy(x => ActionOrder(x.Action))
            .ThenBy(x => BranchOrder(x.Branch))
            .ThenBy(x => x.MemberKey)
            .ToList();

        return result;
    }

    private void AddExtendedBranch(
        FullFamilyTreePreview result,
        IReadOnlyList<KinshipResolution> rootFamily,
        HashSet<int> usedNpcIds,
        HashSet<string> usedSurnames,
        int rootNpcId,
        FullFamilyBranchDesign branch,
        SurnameContext surnames)
    {
        var side = branch.Side.Equals(
            "Maternal",
            StringComparison.OrdinalIgnoreCase)
            ? "Maternal"
            : "Paternal";

        var isUncle = branch.BloodRole.Equals(
            "Uncle",
            StringComparison.OrdinalIgnoreCase);

        var bloodTitle = isUncle ? "Uncle" : "Aunt";
        var requestedRole = $"{side} {bloodTitle}";

        var bloodFamilySurname = side == "Maternal"
            ? surnames.Maternal
            : surnames.Paternal;

        var blood = rootFamily
            .Where(x =>
                x.Kind == "AuntUncle" &&
                x.FamilySide.Equals(side, StringComparison.OrdinalIgnoreCase) &&
                x.Title.Equals(bloodTitle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.TargetNpcId)
            .Skip(Math.Max(0, branch.Ordinal - 1))
            .FirstOrDefault();

        IReadOnlyList<KinshipResolution> bloodFamily =
            Array.Empty<KinshipResolution>();

        KinshipResolution? spouse = null;

        if (blood is not null)
        {
            bloodFamily = _kinship.ResolveFamily(blood.TargetNpcId);
            spouse = bloodFamily.FirstOrDefault(x => x.Kind == "Spouse");
        }

        string spouseBirthSurname;

        if (spouse is not null)
        {
            spouseBirthSurname = FirstNonBlank(
                GetBirthSurname(spouse.TargetNpcId),
                GetCurrentSurname(spouse.TargetNpcId));

            if (!string.IsNullOrWhiteSpace(spouseBirthSurname))
                usedSurnames.Add(spouseBirthSurname);
        }
        else
        {
            spouseBirthSurname = NextUniqueOutsideSurname(
                rootNpcId,
                $"{side}-{bloodTitle}-{branch.Ordinal}-spouse",
                usedSurnames);
        }

        var marriage = ResolveMarriageSurnames(
            bloodFamilySurname,
            spouseBirthSurname,
            branch.MarriageSurnameRule,
            isUncle);

        var childSurname = ResolveChildSurname(
            bloodFamilySurname,
            spouseBirthSurname,
            marriage.HouseholdSurname,
            branch.ChildSurnameRule);

        var branchKey =
            $"{side.ToLowerInvariant()}-{bloodTitle.ToLowerInvariant()}-{branch.Ordinal}";

        AddResolvedOrCreate(
            result,
            usedNpcIds,
            branchKey,
            requestedRole,
            $"{side} parent-sibling branch",
            blood,
            marriage.BloodCurrentSurname,
            bloodFamilySurname,
            "Blood relative. Birth surname follows the root's " +
            side.ToLowerInvariant() +
            " family line.");

        if (branch.IncludeSpouse)
        {
            AddResolvedOrCreate(
                result,
                usedNpcIds,
                $"{branchKey}-spouse",
                $"{requestedRole} Spouse",
                $"{side} extended household",
                spouse,
                marriage.SpouseCurrentSurname,
                spouseBirthSurname,
                $"Outside spouse birth family is {spouseBirthSurname}. " +
                $"Marriage surname rule: {MarriageRuleLabel(branch.MarriageSurnameRule)}.");
        }

        var existingChildren = bloodFamily
            .Where(x => x.Kind == "Child")
            .OrderBy(x => x.TargetNpcId)
            .ToList();

        for (var i = 1;
             i <= Math.Clamp(branch.ChildCount, 0, 8);
             i++)
        {
            var child = i <= existingChildren.Count
                ? existingChildren[i - 1]
                : null;

            AddResolvedOrCreate(
                result,
                usedNpcIds,
                $"{branchKey}-child-{i}",
                $"{side} Cousin",
                $"{requestedRole} household",
                child,
                childSurname,
                childSurname,
                $"Child {i} is structurally assigned to {requestedRole} #{branch.Ordinal}. " +
                $"Child surname rule: {ChildRuleLabel(branch.ChildSurnameRule)}.");
        }

        result.BranchSummaries.Add(new FullFamilyBranchPreview
        {
            BranchKey = branchKey,
            Side = side,
            BloodRole = bloodTitle,
            Ordinal = branch.Ordinal,
            BloodBirthSurname = bloodFamilySurname,
            BloodCurrentSurname = marriage.BloodCurrentSurname,
            OutsideSpouseBirthSurname = spouseBirthSurname,
            SpouseCurrentSurname = marriage.SpouseCurrentSurname,
            HouseholdSurname = marriage.HouseholdSurname,
            ChildSurname = childSurname,
            MarriageSurnameRule = branch.MarriageSurnameRule,
            ChildSurnameRule = branch.ChildSurnameRule,
            IncludeSpouse = branch.IncludeSpouse,
            ChildCount = Math.Clamp(branch.ChildCount, 0, 8)
        });
    }

    private void AddGreatGrandparents(
        FullFamilyTreePreview result,
        HashSet<int> usedNpcIds,
        HashSet<string> usedSurnames,
        int rootNpcId,
        SurnameContext s)
    {
        var specs = new[]
        {
            new GreatSpec(
                "maternal-grandmother",
                "Maternal Grandmother",
                s.MaternalGrandmotherBirth),
            new GreatSpec(
                "maternal-grandfather",
                "Maternal Grandfather",
                s.Maternal),
            new GreatSpec(
                "paternal-grandmother",
                "Paternal Grandmother",
                s.PaternalGrandmotherBirth),
            new GreatSpec(
                "paternal-grandfather",
                "Paternal Grandfather",
                s.Paternal)
        };

        foreach (var spec in specs)
        {
            var motherBirth = NextUniqueOutsideSurname(
                rootNpcId,
                $"{spec.Key}-great-grandmother-birth",
                usedSurnames);

            AddResolvedOrCreate(
                result,
                usedNpcIds,
                $"{spec.Key}-father",
                $"{spec.Label}'s Father",
                "Great-grandparent branch",
                null,
                spec.ParentFamilySurname,
                spec.ParentFamilySurname,
                $"Exact structural father of {spec.Label}.");

            AddResolvedOrCreate(
                result,
                usedNpcIds,
                $"{spec.Key}-mother",
                $"{spec.Label}'s Mother",
                "Great-grandparent branch",
                null,
                spec.ParentFamilySurname,
                motherBirth,
                $"Exact structural mother of {spec.Label}. Birth family is {motherBirth}.");
        }
    }

    private static MarriageSurnamePlan ResolveMarriageSurnames(
        string bloodSurname,
        string spouseBirthSurname,
        string rule,
        bool bloodRelativeIsMale)
    {
        var normalized = string.IsNullOrWhiteSpace(rule)
            ? "Traditional"
            : rule.Trim();

        if (normalized.Equals("BloodFamily", StringComparison.OrdinalIgnoreCase))
        {
            return new MarriageSurnamePlan(
                bloodSurname,
                bloodSurname,
                bloodSurname);
        }

        if (normalized.Equals("SpouseFamily", StringComparison.OrdinalIgnoreCase))
        {
            return new MarriageSurnamePlan(
                spouseBirthSurname,
                spouseBirthSurname,
                spouseBirthSurname);
        }

        if (normalized.Equals("KeepOwn", StringComparison.OrdinalIgnoreCase))
        {
            return new MarriageSurnamePlan(
                bloodSurname,
                spouseBirthSurname,
                "Separate surnames");
        }

        if (normalized.Equals("Hyphenate", StringComparison.OrdinalIgnoreCase))
        {
            var combined = CombineSurnames(
                bloodSurname,
                spouseBirthSurname);

            return new MarriageSurnamePlan(
                combined,
                combined,
                combined);
        }

        // Traditional default:
        // - uncle keeps blood-family surname and spouse joins it
        // - aunt takes spouse-family surname
        if (bloodRelativeIsMale)
        {
            return new MarriageSurnamePlan(
                bloodSurname,
                bloodSurname,
                bloodSurname);
        }

        return new MarriageSurnamePlan(
            spouseBirthSurname,
            spouseBirthSurname,
            spouseBirthSurname);
    }

    private static string ResolveChildSurname(
        string bloodSurname,
        string spouseBirthSurname,
        string householdSurname,
        string rule)
    {
        var normalized = string.IsNullOrWhiteSpace(rule)
            ? "Household"
            : rule.Trim();

        if (normalized.Equals("BloodFamily", StringComparison.OrdinalIgnoreCase))
            return bloodSurname;

        if (normalized.Equals("SpouseFamily", StringComparison.OrdinalIgnoreCase))
            return spouseBirthSurname;

        if (normalized.Equals("Hyphenate", StringComparison.OrdinalIgnoreCase))
            return CombineSurnames(bloodSurname, spouseBirthSurname);

        if (!householdSurname.Equals(
                "Separate surnames",
                StringComparison.OrdinalIgnoreCase))
        {
            return householdSurname;
        }

        return bloodSurname;
    }

    private static string CombineSurnames(
        string a,
        string b)
    {
        if (string.IsNullOrWhiteSpace(a))
            return b;

        if (string.IsNullOrWhiteSpace(b))
            return a;

        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return a;

        return $"{a}-{b}";
    }

    private static string MarriageRuleLabel(string rule)
        => rule switch
        {
            "BloodFamily" => "Both use blood-relative family surname",
            "SpouseFamily" => "Both use outside spouse family surname",
            "KeepOwn" => "Each keeps own surname",
            "Hyphenate" => "Both use hyphenated surname",
            _ => "Traditional default"
        };

    private static string ChildRuleLabel(string rule)
        => rule switch
        {
            "BloodFamily" => "Blood-relative family surname",
            "SpouseFamily" => "Outside spouse family surname",
            "Hyphenate" => "Hyphenated surname",
            _ => "Household surname"
        };

    private string NextUniqueOutsideSurname(
        int rootNpcId,
        string branchKey,
        HashSet<string> usedSurnames)
    {
        var seed = StableHash($"{rootNpcId}:{branchKey}");
        var start = Math.Abs(seed % SurnamePool.Length);

        for (var i = 0; i < SurnamePool.Length; i++)
        {
            var candidate =
                SurnamePool[(start + i) % SurnamePool.Length];

            if (usedSurnames.Add(candidate))
                return candidate;
        }

        var suffix = 2;
        while (true)
        {
            var candidate =
                $"Family{Math.Abs(seed % 999)}{suffix}";

            if (usedSurnames.Add(candidate))
                return candidate;

            suffix++;
        }
    }

    private void AddSingle(
        FullFamilyTreePreview result,
        IReadOnlyList<KinshipResolution> existing,
        HashSet<int> used,
        string key,
        string requestedRole,
        string branch,
        bool include,
        Func<KinshipResolution, bool> match,
        string proposedCurrentSurname,
        string proposedBirthSurname)
    {
        if (!include)
            return;

        var found = existing
            .Where(match)
            .FirstOrDefault(x => !used.Contains(x.TargetNpcId));

        AddResolvedOrCreate(
            result,
            used,
            key,
            requestedRole,
            branch,
            found,
            proposedCurrentSurname,
            proposedBirthSurname,
            found is null
                ? "Requested family slot is structurally missing."
                : "Already resolved from canonical structural kinship.");
    }

    private void AddMany(
        FullFamilyTreePreview result,
        IReadOnlyList<KinshipResolution> existing,
        HashSet<int> used,
        string keyPrefix,
        string requestedRole,
        string branch,
        int count,
        Func<KinshipResolution, bool> match,
        string proposedCurrentSurname,
        string proposedBirthSurname)
    {
        if (count <= 0)
            return;

        var matches = existing
            .Where(match)
            .Where(x => !used.Contains(x.TargetNpcId))
            .OrderBy(x => x.TargetNpcId)
            .ToList();

        for (var i = 1; i <= count; i++)
        {
            var found = i <= matches.Count
                ? matches[i - 1]
                : null;

            AddResolvedOrCreate(
                result,
                used,
                $"{keyPrefix}-{i}",
                requestedRole,
                branch,
                found,
                proposedCurrentSurname,
                proposedBirthSurname,
                found is null
                    ? "Requested family slot is structurally missing."
                    : "Already resolved from canonical structural kinship.");
        }
    }

    private void AddResolvedOrCreate(
        FullFamilyTreePreview result,
        HashSet<int> used,
        string key,
        string requestedRole,
        string branch,
        KinshipResolution? found,
        string proposedCurrentSurname,
        string proposedBirthSurname,
        string notes)
    {
        if (found is not null)
        {
            used.Add(found.TargetNpcId);

            var profile = LoadSurnameProfile(found.TargetNpcId);

            result.Rows.Add(new FullFamilyTreePreviewRow
            {
                MemberKey = key,
                RequestedRole = requestedRole,
                Branch = branch,
                Action = "REUSE",
                ExistingNpcId = found.TargetNpcId,
                ExistingName = found.TargetName,
                CanonicalTitle = found.Title,
                CurrentSurname = FirstNonBlank(
                    profile.Current,
                    proposedCurrentSurname),
                BirthSurname = FirstNonBlank(
                    profile.Birth,
                    proposedBirthSurname),
                Notes = notes
            });
            return;
        }

        result.Rows.Add(new FullFamilyTreePreviewRow
        {
            MemberKey = key,
            RequestedRole = requestedRole,
            Branch = branch,
            Action = "CREATE IN STAGE 3B",
            CurrentSurname = proposedCurrentSurname,
            BirthSurname = proposedBirthSurname,
            Notes = notes
        });
    }

    private SurnameContext BuildSurnameContext(
        int rootNpcId,
        IReadOnlyList<KinshipResolution> existing)
    {
        var root = LoadSurnameProfile(rootNpcId);
        var rootFallback = LastTokenForSurname(GetName(rootNpcId));

        var rootCurrent = FirstNonBlank(
            root.Current,
            rootFallback,
            "Family");

        var rootBirth = FirstNonBlank(
            root.Birth,
            rootCurrent);

        var fatherId = existing
            .FirstOrDefault(x =>
                x.Kind == "Parent" &&
                x.Title.Contains(
                    "Father",
                    StringComparison.OrdinalIgnoreCase))
            ?.TargetNpcId ?? 0;

        var motherId = existing
            .FirstOrDefault(x =>
                x.Kind == "Parent" &&
                x.Title.Contains(
                    "Mother",
                    StringComparison.OrdinalIgnoreCase))
            ?.TargetNpcId ?? 0;

        var father = LoadSurnameProfile(fatherId);
        var mother = LoadSurnameProfile(motherId);

        var paternal = FirstNonBlank(
            father.Birth,
            father.Current,
            rootBirth,
            rootCurrent);

        var initialUsed = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        initialUsed.Add(rootCurrent);
        initialUsed.Add(rootBirth);
        initialUsed.Add(paternal);

        var maternalKnown = FirstNonBlank(
            mother.Birth,
            mother.Current);

        string maternal;

        if (!string.IsNullOrWhiteSpace(maternalKnown) &&
            !maternalKnown.Equals(
                paternal,
                StringComparison.OrdinalIgnoreCase))
        {
            maternal = maternalKnown;
            initialUsed.Add(maternal);
        }
        else
        {
            maternal = NextUniqueOutsideSurname(
                rootNpcId,
                "maternal-family",
                initialUsed);
        }

        var maternalGrandmotherBirth =
            NextUniqueOutsideSurname(
                rootNpcId,
                "maternal-grandmother-birth",
                initialUsed);

        var paternalGrandmotherBirth =
            NextUniqueOutsideSurname(
                rootNpcId,
                "paternal-grandmother-birth",
                initialUsed);

        return new SurnameContext(
            rootCurrent,
            rootBirth,
            maternal,
            paternal,
            maternalGrandmotherBirth,
            paternalGrandmotherBirth);
    }

    private (string Current, string Birth) LoadSurnameProfile(int npcId)
    {
        if (npcId <= 0 || !File.Exists(_options.MainDbPath))
            return ("", "");

        using var conn = new SqliteConnection(
            "Data Source=" + _options.MainDbPath);
        conn.Open();

        if (!TableExists(conn, "NpcNameProfiles"))
            return ("", "");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(CurrentLastName,''),
                COALESCE(BirthLastName,'')
            FROM NpcNameProfiles
            WHERE NpcId=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();

        return r.Read()
            ? (r.GetString(0), r.GetString(1))
            : ("", "");
    }

    private string GetBirthSurname(int npcId)
    {
        var profile = LoadSurnameProfile(npcId);
        return FirstNonBlank(profile.Birth, profile.Current);
    }

    private string GetCurrentSurname(int npcId)
    {
        var profile = LoadSurnameProfile(npcId);
        return FirstNonBlank(profile.Current, profile.Birth);
    }

    private string GetName(int npcId)
    {
        if (!File.Exists(_options.MainDbPath))
            return "";

        using var conn = new SqliteConnection(
            "Data Source=" + _options.MainDbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(
                NULLIF(DisplayName,''),
                NULLIF(Name,''),
                'NPC ' || CAST(Id AS TEXT))
            FROM Characters
            WHERE Id=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;

            foreach (var c in value)
                hash = hash * 31 + c;

            return hash;
        }
    }

    private static string LastTokenForSurname(string name)
    {
        var parts = (name ?? "")
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        return parts.Length == 0
            ? ""
            : parts[^1];
    }

    private static string FirstNonBlank(
        params string[] values)
        => values.FirstOrDefault(
               x => !string.IsNullOrWhiteSpace(x))
           ?.Trim()
           ?? "";

    private static bool TableExists(
        SqliteConnection conn,
        string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table'
              AND name=$name;
            """;
        cmd.Parameters.AddWithValue("$name", name);

        return Convert.ToInt32(
            cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static string DisplayRole(KinshipResolution row)
    {
        if (string.IsNullOrWhiteSpace(row.FamilySide))
            return row.Title;

        if (row.Kind is "Grandparent" or "GreatGrandparent" or "AuntUncle" or "Cousin")
            return $"{row.FamilySide} {row.Title}";

        return row.Title;
    }

    private static int ActionOrder(string action)
        => action switch
        {
            "REUSE" => 10,
            "CREATE IN STAGE 3B" => 20,
            _ => 90
        };

    private static int BranchOrder(string branch)
    {
        if (branch.Contains("Direct parent", StringComparison.OrdinalIgnoreCase))
            return 10;
        if (branch.Contains("Sibling", StringComparison.OrdinalIgnoreCase))
            return 20;
        if (branch.Equals("Grandparent", StringComparison.OrdinalIgnoreCase))
            return 30;
        if (branch.Contains("parent-sibling", StringComparison.OrdinalIgnoreCase))
            return 40;
        if (branch.Contains("household", StringComparison.OrdinalIgnoreCase))
            return 50;
        if (branch.Contains("Great-grandparent", StringComparison.OrdinalIgnoreCase))
            return 60;
        if (branch.Contains("Root household", StringComparison.OrdinalIgnoreCase))
            return 70;
        return 90;
    }

    private static int SideOrder(string side)
        => side.Equals("Maternal", StringComparison.OrdinalIgnoreCase)
            ? 10
            : 20;

    private sealed record GreatSpec(
        string Key,
        string Label,
        string ParentFamilySurname);

    private sealed record MarriageSurnamePlan(
        string BloodCurrentSurname,
        string SpouseCurrentSurname,
        string HouseholdSurname);

    private sealed record SurnameContext(
        string RootCurrent,
        string RootBirth,
        string Maternal,
        string Paternal,
        string MaternalGrandmotherBirth,
        string PaternalGrandmotherBirth)
    {
        public string[] AllKnown()
            =>
            [
                RootCurrent,
                RootBirth,
                Maternal,
                Paternal,
                MaternalGrandmotherBirth,
                PaternalGrandmotherBirth
            ];
    }
}

public sealed class FullFamilyTreeDesign
{
    public bool IncludeMother { get; set; } = true;
    public bool IncludeFather { get; set; } = true;

    public int BrotherCount { get; set; } = 1;
    public int SisterCount { get; set; } = 1;

    public bool IncludeMaternalGrandmother { get; set; } = true;
    public bool IncludeMaternalGrandfather { get; set; } = true;
    public bool IncludePaternalGrandmother { get; set; } = true;
    public bool IncludePaternalGrandfather { get; set; } = true;

    public int MaternalUncleCount { get; set; } = 1;
    public int MaternalAuntCount { get; set; } = 1;
    public int PaternalUncleCount { get; set; } = 1;
    public int PaternalAuntCount { get; set; } = 1;

    public bool IncludeGreatGrandparents { get; set; }

    public bool IncludeRootSpouse { get; set; }
    public int RootChildCount { get; set; }

    public List<FullFamilyBranchDesign> Branches { get; set; } = new();
}

public sealed class FullFamilyBranchDesign
{
    public string Side { get; set; } = "";
    public string BloodRole { get; set; } = "";
    public int Ordinal { get; set; }

    public bool IncludeSpouse { get; set; } = true;
    public int ChildCount { get; set; } = 1;

    // Traditional | BloodFamily | SpouseFamily | KeepOwn | Hyphenate
    public string MarriageSurnameRule { get; set; } = "Traditional";

    // Household | BloodFamily | SpouseFamily | Hyphenate
    public string ChildSurnameRule { get; set; } = "Household";

    public string Key =>
        $"{Side.ToLowerInvariant()}-{BloodRole.ToLowerInvariant()}-{Ordinal}";
}

public sealed class FullFamilyTreePreview
{
    public int RootNpcId { get; set; }
    public string RootName { get; set; } = "";

    public string RootCurrentSurname { get; set; } = "";
    public string RootBirthSurname { get; set; } = "";
    public string MaternalFamilySurname { get; set; } = "";
    public string PaternalFamilySurname { get; set; } = "";

    public List<FullFamilyTreePreviewRow> Rows { get; set; } = new();
    public List<FullFamilyTreePreviewRow> ExistingOutsideDesign { get; set; } = new();
    public List<FullFamilyBranchPreview> BranchSummaries { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public int ReuseCount =>
        Rows.Count(x => x.Action == "REUSE");

    public int CreateCount =>
        Rows.Count(x => x.Action == "CREATE IN STAGE 3B");
}

public sealed class FullFamilyBranchPreview
{
    public string BranchKey { get; set; } = "";
    public string Side { get; set; } = "";
    public string BloodRole { get; set; } = "";
    public int Ordinal { get; set; }

    public string BloodBirthSurname { get; set; } = "";
    public string BloodCurrentSurname { get; set; } = "";

    public string OutsideSpouseBirthSurname { get; set; } = "";
    public string SpouseCurrentSurname { get; set; } = "";

    public string HouseholdSurname { get; set; } = "";
    public string ChildSurname { get; set; } = "";

    public string MarriageSurnameRule { get; set; } = "";
    public string ChildSurnameRule { get; set; } = "";

    public bool IncludeSpouse { get; set; }
    public int ChildCount { get; set; }
}

public sealed class FullFamilyTreePreviewRow
{
    public string MemberKey { get; set; } = "";
    public string RequestedRole { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Action { get; set; } = "";
    public int? ExistingNpcId { get; set; }
    public string ExistingName { get; set; } = "";
    public string CanonicalTitle { get; set; } = "";
    public string CurrentSurname { get; set; } = "";
    public string BirthSurname { get; set; } = "";
    public string Notes { get; set; } = "";
}
