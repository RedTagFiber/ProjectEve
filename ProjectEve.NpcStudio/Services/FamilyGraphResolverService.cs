using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Resolves one connected canonical family graph from RelationshipStates.
///
/// IMPORTANT:
/// - Read-only. This service does not create or update NPCs.
/// - Existing relatives are returned as LOCKED REUSE candidates.
/// - Shared relatives are inferred through parents/siblings so siblings do not
///   independently generate duplicate parents, grandparents, aunts, uncles, or cousins.
/// </summary>
public sealed class FamilyGraphResolverService
{
    private readonly NpcStudioOptions _options;

    public FamilyGraphResolverService(NpcStudioOptions options)
    {
        _options = options;
    }

    public FamilyGraphPreview Resolve(int rootNpcId)
    {
        using var main = new SqliteConnection("Data Source=" + _options.MainDbPath);
        main.Open();

        using var rel = new SqliteConnection("Data Source=" + _options.RelationshipsDbPath);
        rel.Open();

        var rootName = GetName(main, rootNpcId);
        var preview = new FamilyGraphPreview
        {
            RootNpcId = rootNpcId,
            RootName = rootName
        };

        if (string.IsNullOrWhiteSpace(rootName))
            return preview;

        var seen = new HashSet<int>();

        // Direct canonical family links first.
        var direct = LoadFamilyLinks(rel, rootNpcId);

        foreach (var link in direct)
        {
            AddExisting(
                preview,
                main,
                seen,
                link.TargetId,
                NormalizeDirectRole(link.FamilyRole),
                $"{rootName} → {link.TargetName}",
                "Direct canonical family relationship");
        }

        var motherIds = direct
            .Where(x => IsRole(x.FamilyRole, "Mother", "Stepmother"))
            .Select(x => x.TargetId)
            .Distinct()
            .ToList();

        var fatherIds = direct
            .Where(x => IsRole(x.FamilyRole, "Father", "Stepfather"))
            .Select(x => x.TargetId)
            .Distinct()
            .ToList();

        var parentIds = motherIds.Concat(fatherIds).Distinct().ToList();

        // Grandparents are inherited through each parent.
        foreach (var parentId in parentIds)
        {
            var parentName = GetName(main, parentId);
            var parentLinks = LoadFamilyLinks(rel, parentId);

            var parentIsMotherSide = motherIds.Contains(parentId);
            var side = parentIsMotherSide ? "Maternal" : "Paternal";

            foreach (var grandParent in parentLinks.Where(x => IsRole(x.FamilyRole, "Mother", "Father", "Stepmother", "Stepfather")))
            {
                var grandRole = FamilyRoleGenderPart(grandParent.FamilyRole) == "Mother"
                    ? $"{side} Grandmother"
                    : $"{side} Grandfather";

                AddExisting(
                    preview,
                    main,
                    seen,
                    grandParent.TargetId,
                    grandRole,
                    $"{rootName} → {parentName} → {grandParent.TargetName}",
                    "Inherited through shared parent");
            }

            // Parent's siblings are root's aunts/uncles.
            foreach (var auntUncle in parentLinks.Where(x => IsRole(x.FamilyRole, "Brother", "Sister", "Sibling", "Stepbrother", "Stepsister")))
            {
                var role = FamilyRoleGenderPart(auntUncle.FamilyRole) switch
                {
                    "Brother" => $"{side} Uncle",
                    "Sister" => $"{side} Aunt",
                    _ => $"{side} Aunt / Uncle"
                };

                AddExisting(
                    preview,
                    main,
                    seen,
                    auntUncle.TargetId,
                    role,
                    $"{rootName} → {parentName} → {auntUncle.TargetName}",
                    "Inherited through parent's sibling");

                // Their children are root's cousins.
                foreach (var cousin in LoadFamilyLinks(rel, auntUncle.TargetId)
                             .Where(x => IsRole(x.FamilyRole, "Son", "Daughter", "Child", "Stepson", "Stepdaughter", "Stepchild")))
                {
                    AddExisting(
                        preview,
                        main,
                        seen,
                        cousin.TargetId,
                        "Cousin",
                        $"{rootName} → {parentName} → {auntUncle.TargetName} → {cousin.TargetName}",
                        "Inherited through aunt/uncle branch");
                }
            }
        }

        // Sibling spouses become in-laws; sibling children become nieces/nephews.
        foreach (var sibling in direct.Where(x => IsRole(x.FamilyRole, "Brother", "Sister", "Sibling", "Stepbrother", "Stepsister", "Half-brother", "Half-sister")))
        {
            foreach (var siblingLink in LoadFamilyLinks(rel, sibling.TargetId))
            {
                if (IsRole(siblingLink.FamilyRole, "Wife", "Husband", "Spouse"))
                {
                    AddExisting(
                        preview,
                        main,
                        seen,
                        siblingLink.TargetId,
                        "Sibling's Spouse / In-law",
                        $"{rootName} → {sibling.TargetName} → {siblingLink.TargetName}",
                        "Inherited through sibling");
                }
                else if (IsRole(siblingLink.FamilyRole, "Son", "Daughter", "Child", "Stepson", "Stepdaughter", "Stepchild"))
                {
                    var role = FamilyRoleGenderPart(siblingLink.FamilyRole) == "Son"
                        ? "Nephew"
                        : FamilyRoleGenderPart(siblingLink.FamilyRole) == "Daughter"
                            ? "Niece"
                            : "Niece / Nephew";

                    AddExisting(
                        preview,
                        main,
                        seen,
                        siblingLink.TargetId,
                        role,
                        $"{rootName} → {sibling.TargetName} → {siblingLink.TargetName}",
                        "Inherited through sibling");
                }
            }
        }

        // Spouse's children are stepchildren unless already directly linked as children.
        foreach (var spouse in direct.Where(x => IsRole(x.FamilyRole, "Wife", "Husband", "Spouse")))
        {
            foreach (var spouseChild in LoadFamilyLinks(rel, spouse.TargetId)
                         .Where(x => IsRole(x.FamilyRole, "Son", "Daughter", "Child", "Stepson", "Stepdaughter", "Stepchild")))
            {
                if (direct.Any(x => x.TargetId == spouseChild.TargetId &&
                                    IsRole(x.FamilyRole, "Son", "Daughter", "Child")))
                    continue;

                var role = FamilyRoleGenderPart(spouseChild.FamilyRole) == "Son"
                    ? "Stepson"
                    : FamilyRoleGenderPart(spouseChild.FamilyRole) == "Daughter"
                        ? "Stepdaughter"
                        : "Stepchild";

                AddExisting(
                    preview,
                    main,
                    seen,
                    spouseChild.TargetId,
                    role,
                    $"{rootName} → {spouse.TargetName} → {spouseChild.TargetName}",
                    "Inherited through spouse");
            }
        }

        AddMissingBranches(preview, direct, motherIds, fatherIds, parentIds, rel);

        preview.ExistingPeople = preview.ExistingPeople
            .OrderBy(x => RoleOrder(x.Role))
            .ThenBy(x => x.Role)
            .ThenBy(x => x.Name)
            .ToList();

        return preview;
    }

    private static void AddMissingBranches(
        FamilyGraphPreview preview,
        List<Link> direct,
        List<int> motherIds,
        List<int> fatherIds,
        List<int> parentIds,
        SqliteConnection rel)
    {
        if (motherIds.Count == 0)
            Missing(preview, "mother", "Mother", "No canonical mother/stepmother link exists.");

        if (fatherIds.Count == 0)
            Missing(preview, "father", "Father", "No canonical father/stepfather link exists.");

        var hasSibling = direct.Any(x => IsRole(x.FamilyRole,
            "Brother","Sister","Sibling","Stepbrother","Stepsister","Half-brother","Half-sister"));

        if (!hasSibling)
            Missing(preview, "siblings", "Siblings", "No canonical sibling link exists. This may be correct for an only child.");

        foreach (var parentId in parentIds)
        {
            var parentLinks = LoadFamilyLinks(rel, parentId);
            var hasParentMother = parentLinks.Any(x => IsRole(x.FamilyRole, "Mother", "Stepmother"));
            var hasParentFather = parentLinks.Any(x => IsRole(x.FamilyRole, "Father", "Stepfather"));

            if (!hasParentMother)
                Missing(preview, $"parent-{parentId}-mother", $"Parent {parentId}'s mother", "Grandparent branch is missing.");

            if (!hasParentFather)
                Missing(preview, $"parent-{parentId}-father", $"Parent {parentId}'s father", "Grandparent branch is missing.");

            var hasParentSibling = parentLinks.Any(x => IsRole(x.FamilyRole,
                "Brother","Sister","Sibling","Stepbrother","Stepsister","Half-brother","Half-sister"));

            if (!hasParentSibling)
                Missing(preview, $"parent-{parentId}-siblings", $"Parent {parentId}'s siblings / aunt-uncle branch",
                    "No aunt/uncle branch exists yet. This may be correct if the parent is an only child.");
        }

        if (!direct.Any(x => IsRole(x.FamilyRole, "Wife", "Husband", "Spouse")))
            Missing(preview, "spouse", "Current spouse", "No active spouse link exists. This can be intentionally left empty.");

        if (!direct.Any(x => IsRole(x.FamilyRole, "Son", "Daughter", "Child", "Stepson", "Stepdaughter", "Stepchild")))
            Missing(preview, "children", "Children / stepchildren", "No child link exists. This can be intentionally left empty.");
    }

    private static void Missing(FamilyGraphPreview preview, string key, string label, string why)
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

    private static void AddExisting(
        FamilyGraphPreview preview,
        SqliteConnection main,
        HashSet<int> seen,
        int npcId,
        string role,
        string path,
        string source)
    {
        if (npcId <= 0 || npcId == preview.RootNpcId)
            return;

        // Same NPC can have more than one derived role, but avoid duplicate same-role cards.
        if (preview.ExistingPeople.Any(x => x.NpcId == npcId &&
                                            x.Role.Equals(role, StringComparison.OrdinalIgnoreCase)))
            return;

        seen.Add(npcId);

        preview.ExistingPeople.Add(new FamilyGraphPerson
        {
            NpcId = npcId,
            Name = GetName(main, npcId),
            Role = role,
            RelationshipPath = path,
            Source = source,
            LockedReuse = true
        });
    }

    private static List<Link> LoadFamilyLinks(SqliteConnection rel, int sourceId)
    {
        var result = new List<Link>();

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
        SELECT
            IFNULL(TargetCharacterId,0),
            IFNULL(TargetName,''),
            IFNULL(RelationshipType,''),
            IFNULL(FamilyRole,'')
        FROM RelationshipStates
        WHERE SourceCharacterId=$source
          AND
          (
              lower(IFNULL(RelationshipType,'')) IN ('family','married','marriage','spouse')
              OR TRIM(IFNULL(FamilyRole,'')) <> ''
          );
        """;
        cmd.Parameters.AddWithValue("$source", sourceId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var targetId = Convert.ToInt32(r.GetValue(0));
            if (targetId <= 0) continue;

            result.Add(new Link(
                targetId,
                r.GetString(1),
                r.GetString(2),
                r.GetString(3)));
        }

        return result;
    }

    private static string GetName(SqliteConnection main, int npcId)
    {
        using var cmd = main.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(Name,'') FROM Characters WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private static bool IsRole(string value, params string[] roles)
        => roles.Any(r => value.Equals(r, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeDirectRole(string role)
        => string.IsNullOrWhiteSpace(role) ? "Family" : role.Trim();

    private static string FamilyRoleGenderPart(string role)
    {
        if (role.Contains("mother", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("daughter", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("sister", StringComparison.OrdinalIgnoreCase))
        {
            if (role.Contains("mother", StringComparison.OrdinalIgnoreCase)) return "Mother";
            if (role.Contains("daughter", StringComparison.OrdinalIgnoreCase)) return "Daughter";
            return "Sister";
        }

        if (role.Contains("father", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("son", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("brother", StringComparison.OrdinalIgnoreCase))
        {
            if (role.Contains("father", StringComparison.OrdinalIgnoreCase)) return "Father";
            if (role.Contains("son", StringComparison.OrdinalIgnoreCase)) return "Son";
            return "Brother";
        }

        return role;
    }

    private static int RoleOrder(string role)
    {
        var r = role.ToLowerInvariant();
        if (r.Contains("mother") || r.Contains("father")) return 10;
        if (r.Contains("grand")) return 20;
        if (r.Contains("brother") || r.Contains("sister") || r == "sibling") return 30;
        if (r.Contains("aunt") || r.Contains("uncle")) return 40;
        if (r.Contains("cousin")) return 50;
        if (r.Contains("wife") || r.Contains("husband") || r.Contains("spouse")) return 60;
        if (r.Contains("son") || r.Contains("daughter") || r.Contains("child")) return 70;
        if (r.Contains("niece") || r.Contains("nephew")) return 80;
        return 90;
    }

    private sealed record Link(
        int TargetId,
        string TargetName,
        string RelationshipType,
        string FamilyRole);
}
