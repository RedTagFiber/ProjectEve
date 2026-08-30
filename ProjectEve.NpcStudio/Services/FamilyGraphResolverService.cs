using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Compatibility preview adapter over canonical structural kinship.
///
/// IMPORTANT:
/// - FamilyParentChildLinks + FamilyUnionLinks are structural truth.
/// - CanonicalKinshipTitleService resolves viewer-relative titles.
/// - RelationshipStates.FamilyRole is NOT read here.
/// - This service remains read-only.
/// </summary>
public sealed class FamilyGraphResolverService
{
    private readonly NpcStudioOptions _options;
    private readonly CanonicalKinshipTitleService _kinship;

    public FamilyGraphResolverService(
        NpcStudioOptions options,
        CanonicalKinshipTitleService kinship)
    {
        _options = options;
        _kinship = kinship;
    }

    public FamilyGraphPreview Resolve(int rootNpcId)
    {
        var rootName = GetName(rootNpcId);

        var preview = new FamilyGraphPreview
        {
            RootNpcId = rootNpcId,
            RootName = rootName
        };

        if (rootNpcId <= 0 || string.IsNullOrWhiteSpace(rootName))
            return preview;

        var resolved = _kinship.ResolveFamily(rootNpcId);

        foreach (var row in resolved)
        {
            preview.ExistingPeople.Add(new FamilyGraphPerson
            {
                NpcId = row.TargetNpcId,
                Name = row.TargetName,
                Role = DisplayRole(row),
                RelationshipPath = BuildPath(rootName, row),
                Source = "Canonical structural kinship",
                LockedReuse = true
            });
        }

        AddMissingBranches(preview, rootNpcId, resolved);

        preview.ExistingPeople = preview.ExistingPeople
            .OrderBy(x => RoleOrder(x.Role))
            .ThenBy(x => x.Role)
            .ThenBy(x => x.Name)
            .ToList();

        return preview;
    }

    private void AddMissingBranches(
        FamilyGraphPreview preview,
        int rootNpcId,
        IReadOnlyList<KinshipResolution> rootFamily)
    {
        var mother = rootFamily.FirstOrDefault(x =>
            x.Kind == "Parent" &&
            (
                x.Title.Equals("Mother", StringComparison.OrdinalIgnoreCase) ||
                x.Title.Equals("Stepmother", StringComparison.OrdinalIgnoreCase) ||
                x.Title.Equals("Adoptive Mother", StringComparison.OrdinalIgnoreCase)
            ));

        var father = rootFamily.FirstOrDefault(x =>
            x.Kind == "Parent" &&
            (
                x.Title.Equals("Father", StringComparison.OrdinalIgnoreCase) ||
                x.Title.Equals("Stepfather", StringComparison.OrdinalIgnoreCase) ||
                x.Title.Equals("Adoptive Father", StringComparison.OrdinalIgnoreCase)
            ));

        if (mother is null)
            Missing(
                preview,
                "mother",
                "Mother",
                "No canonical structural mother link exists.");

        if (father is null)
            Missing(
                preview,
                "father",
                "Father",
                "No canonical structural father link exists.");

        if (!rootFamily.Any(x =>
                x.Kind is "Sibling" or "HalfSibling" or "StepSibling"))
        {
            Missing(
                preview,
                "siblings",
                "Siblings",
                "No canonical structural sibling link exists. This may be correct for an only child.");
        }

        foreach (var parent in rootFamily.Where(x => x.Kind == "Parent"))
        {
            var parentFamily = _kinship.ResolveFamily(parent.TargetNpcId);

            var hasMother = parentFamily.Any(x =>
                x.Kind == "Parent" &&
                (
                    x.Title.Equals("Mother", StringComparison.OrdinalIgnoreCase) ||
                    x.Title.Equals("Stepmother", StringComparison.OrdinalIgnoreCase) ||
                    x.Title.Equals("Adoptive Mother", StringComparison.OrdinalIgnoreCase)
                ));

            var hasFather = parentFamily.Any(x =>
                x.Kind == "Parent" &&
                (
                    x.Title.Equals("Father", StringComparison.OrdinalIgnoreCase) ||
                    x.Title.Equals("Stepfather", StringComparison.OrdinalIgnoreCase) ||
                    x.Title.Equals("Adoptive Father", StringComparison.OrdinalIgnoreCase)
                ));

            if (!hasMother)
            {
                Missing(
                    preview,
                    $"parent-{parent.TargetNpcId}-mother",
                    $"{parent.TargetName}'s mother",
                    "Grandparent branch is structurally missing.");
            }

            if (!hasFather)
            {
                Missing(
                    preview,
                    $"parent-{parent.TargetNpcId}-father",
                    $"{parent.TargetName}'s father",
                    "Grandparent branch is structurally missing.");
            }

            if (!parentFamily.Any(x =>
                    x.Kind is "Sibling" or "HalfSibling" or "StepSibling"))
            {
                Missing(
                    preview,
                    $"parent-{parent.TargetNpcId}-siblings",
                    $"{parent.TargetName}'s siblings / aunt-uncle branch",
                    "No structural aunt/uncle branch exists yet. This may be correct if the parent is an only child.");
            }
        }

        if (!rootFamily.Any(x => x.Kind == "Spouse"))
        {
            Missing(
                preview,
                "spouse",
                "Current spouse",
                "No active structural spouse/partner union exists. This can be intentionally left empty.");
        }

        if (!rootFamily.Any(x => x.Kind == "Child"))
        {
            Missing(
                preview,
                "children",
                "Children / stepchildren",
                "No structural child link exists. This can be intentionally left empty.");
        }
    }

    private static string DisplayRole(KinshipResolution row)
    {
        if (string.IsNullOrWhiteSpace(row.FamilySide))
            return row.Title;

        if (row.Kind is "Grandparent" or "GreatGrandparent" or "AuntUncle" or "Cousin")
            return $"{row.FamilySide} {row.Title}";

        return row.Title;
    }

    private static string BuildPath(
        string rootName,
        KinshipResolution row)
    {
        var side = string.IsNullOrWhiteSpace(row.FamilySide)
            ? ""
            : $" [{row.FamilySide}]";

        return $"{rootName} -> {row.TargetName} ({row.Title}{side})";
    }

    private static void Missing(
        FamilyGraphPreview preview,
        string key,
        string label,
        string why)
    {
        if (preview.MissingBranches.Any(x => x.BranchKey == key))
            return;

        preview.MissingBranches.Add(new FamilyGraphMissingBranch
        {
            BranchKey = key,
            Label = label,
            WhyMissing = why
        });
    }

    private string GetName(int npcId)
    {
        if (npcId <= 0 || !File.Exists(_options.MainDbPath))
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

    private static int RoleOrder(string role)
    {
        var r = role.ToLowerInvariant();

        if (r.Contains("mother") || r.Contains("father") || r == "parent")
            return 10;

        if (r.Contains("grand"))
            return 20;

        if (r.Contains("brother") || r.Contains("sister") || r.Contains("sibling"))
            return 30;

        if (r.Contains("aunt") || r.Contains("uncle"))
            return 40;

        if (r.Contains("cousin"))
            return 50;

        if (r.Contains("wife") || r.Contains("husband") || r.Contains("spouse") || r.Contains("partner"))
            return 60;

        if (r.Contains("son") || r.Contains("daughter") || r.Contains("child"))
            return 70;

        if (r.Contains("niece") || r.Contains("nephew"))
            return 80;

        return 90;
    }
}
