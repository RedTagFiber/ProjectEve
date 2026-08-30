using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Resolves family titles from canonical structural kinship only.
///
/// Source of truth:
/// - FamilyParentChildLinks
/// - FamilyUnionLinks
///
/// RelationshipStates.FamilyRole is intentionally NOT used as input.
/// It is a compatibility/display cache only and may contain stale or
/// viewpoint-wrong labels from older builders.
/// </summary>
public sealed class CanonicalKinshipTitleService
{
    private readonly NpcStudioOptions _options;

    public CanonicalKinshipTitleService(NpcStudioOptions options)
    {
        _options = options;
    }

    public KinshipResolution Resolve(int viewerNpcId, int targetNpcId)
    {
        if (viewerNpcId <= 0 || targetNpcId <= 0)
            return KinshipResolution.None(viewerNpcId, targetNpcId);

        if (viewerNpcId == targetNpcId)
        {
            return new KinshipResolution
            {
                ViewerNpcId = viewerNpcId,
                TargetNpcId = targetNpcId,
                ViewerName = GetName(viewerNpcId),
                TargetName = GetName(targetNpcId),
                Title = "Self",
                Kind = "Self",
                IsResolved = true
            };
        }

        using var rel = OpenRelationships();

        var parents = LoadParents(rel);
        var unions = LoadActiveUnions(rel);

        var result = ResolveCore(
            viewerNpcId,
            targetNpcId,
            parents,
            unions);

        result.ViewerName = GetName(viewerNpcId);
        result.TargetName = GetName(targetNpcId);

        return result;
    }

    public IReadOnlyList<KinshipResolution> ResolveFamily(int viewerNpcId)
    {
        if (viewerNpcId <= 0)
            return Array.Empty<KinshipResolution>();

        using var rel = OpenRelationships();

        var parents = LoadParents(rel);
        var unions = LoadActiveUnions(rel);

        var ids = new HashSet<int>();

        foreach (var row in parents)
        {
            ids.Add(row.ParentNpcId);
            ids.Add(row.ChildNpcId);
        }

        foreach (var pair in unions)
        {
            ids.Add(pair.A);
            ids.Add(pair.B);
        }

        ids.Remove(viewerNpcId);

        var list = new List<KinshipResolution>();

        foreach (var targetId in ids.OrderBy(x => x))
        {
            var resolved = ResolveCore(
                viewerNpcId,
                targetId,
                parents,
                unions);

            if (!resolved.IsResolved)
                continue;

            resolved.ViewerName = GetName(viewerNpcId);
            resolved.TargetName = GetName(targetId);
            list.Add(resolved);
        }

        return list
            .OrderBy(x => SortOrder(x.Kind))
            .ThenBy(x => x.Title)
            .ThenBy(x => x.TargetName)
            .ToList();
    }

    private KinshipResolution ResolveCore(
        int viewer,
        int target,
        IReadOnlyList<ParentLink> parents,
        IReadOnlyList<UnionLink> unions)
    {
        var viewerParents = ParentsOf(viewer, parents);
        var targetParents = ParentsOf(target, parents);

        // Direct parent.
        var directParent = viewerParents
            .FirstOrDefault(x => x.ParentNpcId == target);

        if (directParent is not null)
        {
            return Found(
                viewer,
                target,
                ParentTitle(target, directParent.ParentKind),
                "Parent",
                directParent.ParentKind,
                FamilySideFromSlot(directParent.ParentSlot));
        }

        // Direct child.
        var directChild = targetParents
            .FirstOrDefault(x => x.ParentNpcId == viewer);

        if (directChild is not null)
        {
            return Found(
                viewer,
                target,
                ChildTitle(target, directChild.ParentKind),
                "Child",
                directChild.ParentKind);
        }

        // Spouse / partner.
        if (AreActivePartners(viewer, target, unions, out var unionType))
        {
            return Found(
                viewer,
                target,
                SpouseTitle(target, unionType),
                "Spouse",
                unionType);
        }

        // Sibling / half sibling.
        var sibling = ResolveSibling(
            viewer,
            target,
            parents);

        if (sibling is not null)
            return sibling;

        // Grandparent and great-grandparent.
        var viewerAncestorPaths = AncestorDistances(viewer, parents, 6);

        if (viewerAncestorPaths.TryGetValue(target, out var upDistance) &&
            upDistance >= 2)
        {
            var prefix = GreatPrefix(upDistance - 2);
            var title = prefix + Gendered(
                target,
                "Grandfather",
                "Grandmother",
                "Grandparent");

            return Found(
                viewer,
                target,
                title,
                upDistance == 2 ? "Grandparent" : "GreatGrandparent",
                "Ancestor",
                ResolveAncestorSide(viewer, target, parents));
        }

        // Grandchild and great-grandchild.
        var targetAncestorPaths = AncestorDistances(target, parents, 6);

        if (targetAncestorPaths.TryGetValue(viewer, out var downDistance) &&
            downDistance >= 2)
        {
            var prefix = GreatPrefix(downDistance - 2);
            var title = prefix + Gendered(
                target,
                "Grandson",
                "Granddaughter",
                "Grandchild");

            return Found(
                viewer,
                target,
                title,
                downDistance == 2 ? "Grandchild" : "GreatGrandchild",
                "Descendant");
        }

        // Aunt / uncle: target is sibling of one of viewer's parents.
        foreach (var parent in viewerParents)
        {
            var parentSibling = ResolveSibling(
                parent.ParentNpcId,
                target,
                parents);

            if (parentSibling is null)
                continue;

            return Found(
                viewer,
                target,
                Gendered(target, "Uncle", "Aunt", "Aunt/Uncle"),
                "AuntUncle",
                parentSibling.Kind,
                ParentSide(parent.ParentNpcId));
        }

        // Niece / nephew: target's parent is viewer's sibling.
        foreach (var targetParent in targetParents)
        {
            var parentIsViewerSibling = ResolveSibling(
                viewer,
                targetParent.ParentNpcId,
                parents);

            if (parentIsViewerSibling is null)
                continue;

            return Found(
                viewer,
                target,
                Gendered(target, "Nephew", "Niece", "Niece/Nephew"),
                "NieceNephew",
                parentIsViewerSibling.Kind);
        }

        // First cousin: one parent of each NPC are siblings.
        foreach (var vp in viewerParents)
        {
            foreach (var tp in targetParents)
            {
                var parentsAreSiblings = ResolveSibling(
                    vp.ParentNpcId,
                    tp.ParentNpcId,
                    parents);

                if (parentsAreSiblings is null)
                    continue;

                return Found(
                    viewer,
                    target,
                    "Cousin",
                    "Cousin",
                    parentsAreSiblings.Kind,
                    ParentSide(vp.ParentNpcId));
            }
        }

        // Step sibling: their parents are active spouses and they do not
        // share a biological/adoptive parent.
        foreach (var vp in viewerParents)
        {
            foreach (var tp in targetParents)
            {
                if (AreActivePartners(
                        vp.ParentNpcId,
                        tp.ParentNpcId,
                        unions,
                        out _))
                {
                    return Found(
                        viewer,
                        target,
                        Gendered(
                            target,
                            "Stepbrother",
                            "Stepsister",
                            "Stepsibling"),
                        "StepSibling",
                        "Step");
                }
            }
        }

        return KinshipResolution.None(viewer, target);
    }

    private KinshipResolution? ResolveSibling(
        int viewer,
        int target,
        IReadOnlyList<ParentLink> parents)
    {
        if (viewer == target)
            return null;

        var a = ParentsOf(viewer, parents)
            .Where(IsParentForSiblingMath)
            .Select(x => x.ParentNpcId)
            .ToHashSet();

        var b = ParentsOf(target, parents)
            .Where(IsParentForSiblingMath)
            .Select(x => x.ParentNpcId)
            .ToHashSet();

        var shared = a.Intersect(b).Count();

        if (shared <= 0)
            return null;

        var isHalf =
            shared == 1 &&
            (a.Count > 1 || b.Count > 1);

        return Found(
            viewer,
            target,
            isHalf
                ? Gendered(
                    target,
                    "Half-brother",
                    "Half-sister",
                    "Half-sibling")
                : Gendered(
                    target,
                    "Brother",
                    "Sister",
                    "Sibling"),
            isHalf ? "HalfSibling" : "Sibling",
            isHalf ? "Half" : "SharedParent");
    }

    private Dictionary<int, int> AncestorDistances(
        int npcId,
        IReadOnlyList<ParentLink> parents,
        int maxDepth)
    {
        var result = new Dictionary<int, int>();
        var queue = new Queue<(int Id, int Depth)>();
        queue.Enqueue((npcId, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.Depth >= maxDepth)
                continue;

            foreach (var parent in ParentsOf(current.Id, parents)
                         .Where(IsParentForAncestorMath))
            {
                var nextDepth = current.Depth + 1;

                if (result.TryGetValue(parent.ParentNpcId, out var old) &&
                    old <= nextDepth)
                    continue;

                result[parent.ParentNpcId] = nextDepth;
                queue.Enqueue((parent.ParentNpcId, nextDepth));
            }
        }

        return result;
    }

    private string ResolveAncestorSide(
        int viewer,
        int ancestor,
        IReadOnlyList<ParentLink> parents)
    {
        foreach (var parent in ParentsOf(viewer, parents))
        {
            if (parent.ParentNpcId == ancestor)
                return ParentSide(parent.ParentNpcId);

            var ancestors = AncestorDistances(
                parent.ParentNpcId,
                parents,
                5);

            if (ancestors.ContainsKey(ancestor))
                return ParentSide(parent.ParentNpcId);
        }

        return "";
    }

    private string ParentSide(int parentNpcId)
    {
        var gender = GetGender(parentNpcId);

        if (IsMale(gender))
            return "Paternal";

        if (IsFemale(gender))
            return "Maternal";

        return "";
    }

    private string FamilySideFromSlot(string slot)
    {
        if (slot.Equals("Mother", StringComparison.OrdinalIgnoreCase))
            return "Maternal";

        if (slot.Equals("Father", StringComparison.OrdinalIgnoreCase))
            return "Paternal";

        return "";
    }

    private static IReadOnlyList<ParentLink> ParentsOf(
        int childId,
        IReadOnlyList<ParentLink> all)
        => all
            .Where(x =>
                x.ChildNpcId == childId &&
                x.IsCurrent)
            .ToList();

    private static bool IsParentForSiblingMath(ParentLink link)
        => !link.ParentKind.Equals(
               "Step",
               StringComparison.OrdinalIgnoreCase)
           && !link.ParentKind.Equals(
               "Guardian",
               StringComparison.OrdinalIgnoreCase);

    private static bool IsParentForAncestorMath(ParentLink link)
        => !link.ParentKind.Equals(
               "Step",
               StringComparison.OrdinalIgnoreCase)
           && !link.ParentKind.Equals(
               "Guardian",
               StringComparison.OrdinalIgnoreCase);

    private string ParentTitle(
        int targetNpcId,
        string parentKind)
    {
        var baseTitle = Gendered(
            targetNpcId,
            "Father",
            "Mother",
            "Parent");

        if (parentKind.Equals(
                "Step",
                StringComparison.OrdinalIgnoreCase))
        {
            return baseTitle switch
            {
                "Father" => "Stepfather",
                "Mother" => "Stepmother",
                _ => "Stepparent"
            };
        }

        if (parentKind.Equals(
                "Adoptive",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Adoptive " + baseTitle;
        }

        return baseTitle;
    }

    private string ChildTitle(
        int targetNpcId,
        string parentKind)
    {
        var baseTitle = Gendered(
            targetNpcId,
            "Son",
            "Daughter",
            "Child");

        if (parentKind.Equals(
                "Step",
                StringComparison.OrdinalIgnoreCase))
        {
            return baseTitle switch
            {
                "Son" => "Stepson",
                "Daughter" => "Stepdaughter",
                _ => "Stepchild"
            };
        }

        if (parentKind.Equals(
                "Adoptive",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Adoptive " + baseTitle;
        }

        return baseTitle;
    }

    private string SpouseTitle(
        int targetNpcId,
        string unionType)
    {
        if (unionType.Equals(
                "Marriage",
                StringComparison.OrdinalIgnoreCase))
        {
            return Gendered(
                targetNpcId,
                "Husband",
                "Wife",
                "Spouse");
        }

        return Gendered(
            targetNpcId,
            "Male Partner",
            "Female Partner",
            "Partner");
    }

    private string Gendered(
        int npcId,
        string male,
        string female,
        string neutral)
    {
        var gender = GetGender(npcId);

        if (IsMale(gender))
            return male;

        if (IsFemale(gender))
            return female;

        return neutral;
    }

    private static bool IsMale(string gender)
        => gender.Equals("Male", StringComparison.OrdinalIgnoreCase)
           || gender.Equals("Man", StringComparison.OrdinalIgnoreCase)
           || gender.Equals("M", StringComparison.OrdinalIgnoreCase);

    private static bool IsFemale(string gender)
        => gender.Equals("Female", StringComparison.OrdinalIgnoreCase)
           || gender.Equals("Woman", StringComparison.OrdinalIgnoreCase)
           || gender.Equals("F", StringComparison.OrdinalIgnoreCase);

    private string GetGender(int npcId)
    {
        using var conn = OpenMain();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(Gender,'')
            FROM Characters
            WHERE Id=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private string GetName(int npcId)
    {
        using var conn = OpenMain();

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

        return Convert.ToString(cmd.ExecuteScalar())
               ?? $"NPC {npcId}";
    }

    private IReadOnlyList<ParentLink> LoadParents(
        SqliteConnection conn)
    {
        var list = new List<ParentLink>();

        if (!TableExists(conn, "FamilyParentChildLinks"))
            return list;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                ParentNpcId,
                ChildNpcId,
                COALESCE(ParentKind,'Biological'),
                COALESCE(ParentSlot,''),
                COALESCE(IsCurrent,1)
            FROM FamilyParentChildLinks
            WHERE COALESCE(IsCurrent,1)=1;
            """;

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            list.Add(new ParentLink(
                r.GetInt32(0),
                r.GetInt32(1),
                r.GetString(2),
                r.GetString(3),
                Convert.ToInt32(r.GetValue(4)) != 0));
        }

        return list;
    }

    private IReadOnlyList<UnionLink> LoadActiveUnions(
        SqliteConnection conn)
    {
        var list = new List<UnionLink>();

        if (!TableExists(conn, "FamilyUnionLinks"))
            return list;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                Person1NpcId,
                Person2NpcId,
                COALESCE(UnionType,'Marriage'),
                COALESCE(Status,'')
            FROM FamilyUnionLinks
            WHERE lower(COALESCE(Status,'')) IN
                  ('active','married','current')
               OR (
                    COALESCE(Status,'')=''
                    AND COALESCE(EndGameDate,'')=''
                  );
            """;

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            list.Add(new UnionLink(
                r.GetInt32(0),
                r.GetInt32(1),
                r.GetString(2),
                r.GetString(3)));
        }

        return list;
    }

    private static bool AreActivePartners(
        int a,
        int b,
        IReadOnlyList<UnionLink> unions,
        out string unionType)
    {
        var match = unions.FirstOrDefault(x =>
            (x.A == a && x.B == b) ||
            (x.A == b && x.B == a));

        if (match is null)
        {
            unionType = "";
            return false;
        }

        unionType = match.UnionType;
        return true;
    }

    private SqliteConnection OpenRelationships()
    {
        var path = string.IsNullOrWhiteSpace(
            _options.RelationshipsDbPath)
            ? @"D:\ProjectEveData\Database\project_eve_relationships.db"
            : _options.RelationshipsDbPath;

        var conn = new SqliteConnection(
            "Data Source=" + path);
        conn.Open();
        return conn;
    }

    private SqliteConnection OpenMain()
    {
        var conn = new SqliteConnection(
            "Data Source=" + _options.MainDbPath);
        conn.Open();
        return conn;
    }

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

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static string GreatPrefix(int count)
    {
        if (count <= 0)
            return "";

        return string.Concat(
            Enumerable.Repeat("Great-", count));
    }

    private static int SortOrder(string kind)
        => kind switch
        {
            "Parent" => 10,
            "Grandparent" => 20,
            "GreatGrandparent" => 25,
            "Sibling" => 30,
            "HalfSibling" => 31,
            "StepSibling" => 32,
            "AuntUncle" => 40,
            "Cousin" => 50,
            "Spouse" => 60,
            "Child" => 70,
            "Grandchild" => 80,
            "GreatGrandchild" => 85,
            "NieceNephew" => 90,
            _ => 999
        };

    private static KinshipResolution Found(
        int viewer,
        int target,
        string title,
        string kind,
        string sourceKind,
        string side = "")
        => new()
        {
            ViewerNpcId = viewer,
            TargetNpcId = target,
            Title = title,
            Kind = kind,
            SourceKind = sourceKind,
            FamilySide = side,
            IsResolved = true
        };

    private sealed record ParentLink(
        int ParentNpcId,
        int ChildNpcId,
        string ParentKind,
        string ParentSlot,
        bool IsCurrent);

    private sealed record UnionLink(
        int A,
        int B,
        string UnionType,
        string Status);
}

public sealed class KinshipResolution
{
    public int ViewerNpcId { get; set; }
    public int TargetNpcId { get; set; }
    public string ViewerName { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string FamilySide { get; set; } = "";
    public bool IsResolved { get; set; }

    public static KinshipResolution None(
        int viewer,
        int target)
        => new()
        {
            ViewerNpcId = viewer,
            TargetNpcId = target,
            Title = "",
            Kind = "",
            IsResolved = false
        };
}
