using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Data;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Canonical family resolver.
///
/// STAGE 1:
///   - Parent / child
///   - Spouse
///   - Sibling derived from shared canonical parent links
///
/// Family labels are NEVER stored as global truth.  They are resolved from
/// the CURRENT root NPC's point of view every time.
/// </summary>
public sealed class CanonicalFamilyGraphService
{
    private readonly NpcStudioOptions _options;

    public CanonicalFamilyGraphService(NpcStudioOptions options)
    {
        _options = options;
    }

    public CanonicalFamilyGraph Resolve(int rootNpcId)
    {
        var result = new CanonicalFamilyGraph
        {
            RootNpcId = rootNpcId,
            RootName = GetDisplayName(rootNpcId)
        };

        if (rootNpcId <= 0)
            return result;

        using var conn = new SqliteConnection($"Data Source={GetRelationshipsPath()}");
        conn.Open();

        var parentsOf = LoadParentMap(conn);
        var childrenOf = BuildChildrenMap(parentsOf);
        var spousesOf = LoadSpouseMap(conn);

        IEnumerable<ParentLink> Parents(int childId) =>
            parentsOf.TryGetValue(childId, out var list)
                ? list
                : Enumerable.Empty<ParentLink>();

        IEnumerable<int> Children(int parentId) =>
            childrenOf.TryGetValue(parentId, out var list)
                ? list
                : Enumerable.Empty<int>();

        IEnumerable<int> Spouses(int npcId) =>
            spousesOf.TryGetValue(npcId, out var list)
                ? list
                : Enumerable.Empty<int>();

        IEnumerable<int> Siblings(int npcId)
        {
            var ids = new HashSet<int>();

            foreach (var parent in Parents(npcId))
            {
                foreach (var child in Children(parent.ParentNpcId))
                {
                    if (child != npcId)
                        ids.Add(child);
                }
            }

            return ids;
        }

        var people = new Dictionary<int, CanonicalFamilyPerson>();
        var edges = new Dictionary<string, CanonicalFamilyEdge>();

        void AddPerson(
            int npcId,
            string role,
            int generation,
            bool direct,
            bool inferred,
            string familySide = "Other",
            int branchAnchorNpcId = 0)
        {
            if (npcId <= 0 || npcId == rootNpcId)
                return;

            // Stage 1 should never discover competing legitimate roles.
            // If malformed structural data produces two roles, preserve the
            // first structural result and report the contradiction.
            if (people.TryGetValue(npcId, out var existing))
            {
                if (!string.Equals(existing.RoleFromRoot, role, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add(
                        $"KINSHIP CONFLICT root={rootNpcId} target={npcId}: " +
                        $"'{existing.RoleFromRoot}' vs '{role}'.");
                }
                return;
            }

            people[npcId] = new CanonicalFamilyPerson
            {
                NpcId = npcId,
                Name = GetDisplayName(npcId),
                RoleFromRoot = role,
                Generation = generation,
                IsDirect = direct,
                IsInferred = inferred,
                FamilySide = familySide,
                BranchAnchorNpcId = branchAnchorNpcId
            };
        }

        void AddEdge(
            int from,
            int to,
            string edgeType,
            string roleFromFrom,
            string roleFromTo,
            bool inferred,
            string familySide = "Other",
            int branchAnchorNpcId = 0)
        {
            if (from <= 0 || to <= 0 || from == to)
                return;

            var low = Math.Min(from, to);
            var high = Math.Max(from, to);
            var key = $"{low}:{high}:{edgeType}";

            if (edges.ContainsKey(key))
                return;

            edges[key] = new CanonicalFamilyEdge
            {
                FromNpcId = from,
                ToNpcId = to,
                EdgeType = edgeType,
                RoleFromFrom = roleFromFrom,
                RoleFromTo = roleFromTo,
                IsInferred = inferred,
                FamilySide = familySide,
                BranchAnchorNpcId = branchAnchorNpcId
            };
        }

        // DIRECT PARENTS.
        foreach (var parent in Parents(rootNpcId))
        {
            var role = ParentRole(parent);
            var side =
                role.Equals("Father", StringComparison.OrdinalIgnoreCase) ? "Father" :
                role.Equals("Mother", StringComparison.OrdinalIgnoreCase) ? "Mother" :
                "Other";

            AddPerson(parent.ParentNpcId, role, -1, true, false, side, parent.ParentNpcId);
            AddEdge(
                rootNpcId,
                parent.ParentNpcId,
                "ParentChild",
                role,
                ChildRole(rootNpcId),
                false,
                side,
                parent.ParentNpcId);
        }

        // DIRECT CHILDREN.
        foreach (var childId in Children(rootNpcId).Distinct())
        {
            var role = ChildRole(childId);
            AddPerson(childId, role, 1, true, false);
            AddEdge(
                rootNpcId,
                childId,
                "ParentChild",
                role,
                ParentRoleForNpc(rootNpcId),
                false);
        }

        // DIRECT SPOUSE.
        foreach (var spouseId in Spouses(rootNpcId).Distinct())
        {
            var role = GenderedRole(spouseId, "Husband", "Wife", "Spouse");
            AddPerson(spouseId, role, 0, true, false);
            AddEdge(
                rootNpcId,
                spouseId,
                "Spouse",
                role,
                GenderedRole(rootNpcId, "Husband", "Wife", "Spouse"),
                false);
        }

        // SIBLINGS: derived only from a shared canonical parent.
        foreach (var siblingId in Siblings(rootNpcId).Distinct())
        {
            var role = GenderedRole(siblingId, "Brother", "Sister", "Sibling");
            AddPerson(siblingId, role, 0, false, true, "Shared");
            AddEdge(
                rootNpcId,
                siblingId,
                "Sibling",
                role,
                GenderedRole(rootNpcId, "Brother", "Sister", "Sibling"),
                true,
                "Shared");
        }

        result.People.AddRange(
            people.Values
                .OrderBy(p => p.Generation)
                .ThenBy(p => p.RoleFromRoot)
                .ThenBy(p => p.Name));

        result.Edges.AddRange(edges.Values);
        return result;
    }

    private Dictionary<int, List<ParentLink>> LoadParentMap(SqliteConnection conn)
    {
        var map = new Dictionary<int, List<ParentLink>>();

        if (!TableExists(conn, "FamilyParentChildLinks"))
            return map;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ParentNpcId,
                   ChildNpcId,
                   COALESCE(ParentKind,'Biological'),
                   COALESCE(ParentSlot,'')
            FROM FamilyParentChildLinks
            WHERE IsCurrent = 1
            ORDER BY ChildNpcId, ParentSlot, ParentNpcId;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new ParentLink(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3));

            if (!map.TryGetValue(row.ChildNpcId, out var list))
            {
                list = new List<ParentLink>();
                map[row.ChildNpcId] = list;
            }

            if (!list.Any(x =>
                    x.ParentNpcId == row.ParentNpcId &&
                    x.ParentKind.Equals(row.ParentKind, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(row);
            }
        }

        return map;
    }

    private static Dictionary<int, HashSet<int>> BuildChildrenMap(
        Dictionary<int, List<ParentLink>> parentsOf)
    {
        var map = new Dictionary<int, HashSet<int>>();

        foreach (var (childId, parents) in parentsOf)
        {
            foreach (var parent in parents)
            {
                if (!map.TryGetValue(parent.ParentNpcId, out var children))
                {
                    children = new HashSet<int>();
                    map[parent.ParentNpcId] = children;
                }

                children.Add(childId);
            }
        }

        return map;
    }

    private static Dictionary<int, HashSet<int>> LoadSpouseMap(SqliteConnection conn)
    {
        var map = new Dictionary<int, HashSet<int>>();

        if (!TableExists(conn, "FamilyUnionLinks"))
            return map;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Person1NpcId, Person2NpcId
            FROM FamilyUnionLinks
            WHERE lower(COALESCE(Status,'')) IN ('active','married','current')
               OR (COALESCE(Status,'') = '' AND COALESCE(EndGameDate,'') = '');
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var a = reader.GetInt32(0);
            var b = reader.GetInt32(1);

            if (!map.TryGetValue(a, out var aSet))
            {
                aSet = new HashSet<int>();
                map[a] = aSet;
            }

            if (!map.TryGetValue(b, out var bSet))
            {
                bSet = new HashSet<int>();
                map[b] = bSet;
            }

            aSet.Add(b);
            bSet.Add(a);
        }

        return map;
    }

    private string ParentRole(ParentLink link)
    {
        var slot = (link.ParentSlot ?? "").Trim();

        if (slot.Equals("Father", StringComparison.OrdinalIgnoreCase))
            return link.ParentKind.Equals("Step", StringComparison.OrdinalIgnoreCase)
                ? "Stepfather"
                : "Father";

        if (slot.Equals("Mother", StringComparison.OrdinalIgnoreCase))
            return link.ParentKind.Equals("Step", StringComparison.OrdinalIgnoreCase)
                ? "Stepmother"
                : "Mother";

        var baseRole = GenderedRole(link.ParentNpcId, "Father", "Mother", "Parent");

        if (link.ParentKind.Equals("Step", StringComparison.OrdinalIgnoreCase))
        {
            return baseRole switch
            {
                "Father" => "Stepfather",
                "Mother" => "Stepmother",
                _ => "Stepparent"
            };
        }

        return baseRole;
    }

    private string ParentRoleForNpc(int npcId) =>
        GenderedRole(npcId, "Father", "Mother", "Parent");

    private string ChildRole(int npcId) =>
        GenderedRole(npcId, "Son", "Daughter", "Child");

    private string GenderedRole(int npcId, string male, string female, string neutral)
    {
        var gender = GetGender(npcId);

        if (gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ||
            gender.Equals("Man", StringComparison.OrdinalIgnoreCase) ||
            gender.Equals("M", StringComparison.OrdinalIgnoreCase))
            return male;

        if (gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ||
            gender.Equals("Woman", StringComparison.OrdinalIgnoreCase) ||
            gender.Equals("F", StringComparison.OrdinalIgnoreCase))
            return female;

        return neutral;
    }

    private string GetGender(int npcId)
    {
        if (!File.Exists(_options.MainDbPath))
            return "";

        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        if (!TableExists(conn, "Characters"))
            return "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(Gender,'') FROM Characters WHERE Id=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private string GetDisplayName(int npcId)
    {
        if (!File.Exists(_options.MainDbPath))
            return $"NPC {npcId}";

        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        if (!TableExists(conn, "Characters"))
            return $"NPC {npcId}";

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

        return Convert.ToString(cmd.ExecuteScalar()) ?? $"NPC {npcId}";
    }

    private string GetRelationshipsPath()
    {
        var property = _options.GetType().GetProperty("RelationshipsDbPath");
        var configured = property?.GetValue(_options) as string;

        return string.IsNullOrWhiteSpace(configured)
            ? @"D:\ProjectEveData\Database\project_eve_relationships.db"
            : configured;
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private sealed record ParentLink(
        int ParentNpcId,
        int ChildNpcId,
        string ParentKind,
        string ParentSlot);
}
