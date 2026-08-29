using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Data;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Resolves one shared family network from canonical parent/child + union edges.
///
/// Structural family is separate from emotional relationship depth.
/// This service is READ-ONLY.
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

        if (rootNpcId <= 0) return result;

        var path = GetRelationshipsPath();
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        var parentsOf = LoadParentMap(conn);
        var childrenOf = BuildChildrenMap(parentsOf);
        var spousesOf = LoadSpouseMap(conn);
        var overrides = LoadOverrides(conn);

        var people = new Dictionary<int, CanonicalFamilyPerson>();
        var edges = new Dictionary<string, CanonicalFamilyEdge>();

        AddPerson(rootNpcId, "Self", 0, true, false);

        // Parents
        foreach (var parent in Parents(rootNpcId))
        {
            AddPerson(parent.Id, parent.Role, -1, true, false);
            AddEdge(rootNpcId, parent.Id, "ParentChild", parent.Role, ChildRole(rootNpcId), false);
        }

        // Children
        foreach (var childId in Children(rootNpcId))
        {
            var role = GenderedRole(childId, "Son", "Daughter", "Child");
            AddPerson(childId, role, 1, true, false);
            AddEdge(rootNpcId, childId, "ParentChild", role, ParentRole(rootNpcId), false);
        }

        // Spouse(s)
        foreach (var spouseId in Spouses(rootNpcId))
        {
            var role = GenderedRole(spouseId, "Husband", "Wife", "Spouse");
            AddPerson(spouseId, role, 0, true, false);
            AddEdge(rootNpcId, spouseId, "Spouse", role, GenderedRole(rootNpcId, "Husband", "Wife", "Spouse"), false);
        }

        // Siblings inferred from shared parent(s).
        foreach (var siblingId in Siblings(rootNpcId))
        {
            var role = GenderedRole(siblingId, "Brother", "Sister", "Sibling");
            AddPerson(siblingId, role, 0, false, true);
            AddEdge(rootNpcId, siblingId, "Sibling", role, GenderedRole(rootNpcId, "Brother", "Sister", "Sibling"), true);
        }

        // Grandparents: parents of parents.
        foreach (var parent in Parents(rootNpcId))
        {
            foreach (var gp in Parents(parent.Id))
            {
                var role = GenderedRole(gp.Id, "Grandfather", "Grandmother", "Grandparent");
                AddPerson(gp.Id, role, -2, false, true);
                AddEdge(parent.Id, gp.Id, "ParentChild", gp.Role, GenderedRole(parent.Id, "Son", "Daughter", "Child"), true);
                AddEdge(rootNpcId, gp.Id, "Grandparent", role, GenderedRole(rootNpcId, "Grandson", "Granddaughter", "Grandchild"), true);
            }
        }

        // Grandchildren: children of children.
        foreach (var childId in Children(rootNpcId))
        {
            foreach (var gcId in Children(childId))
            {
                var role = GenderedRole(gcId, "Grandson", "Granddaughter", "Grandchild");
                AddPerson(gcId, role, 2, false, true);
                AddEdge(childId, gcId, "ParentChild", GenderedRole(gcId, "Son", "Daughter", "Child"), ParentRole(childId), true);
                AddEdge(rootNpcId, gcId, "Grandchild", role, GenderedRole(rootNpcId, "Grandfather", "Grandmother", "Grandparent"), true);
            }
        }

        // Aunts/uncles + cousins.
        foreach (var parent in Parents(rootNpcId))
        {
            foreach (var auntUncleId in Siblings(parent.Id))
            {
                var auRole = GenderedRole(auntUncleId, "Uncle", "Aunt", "Aunt/Uncle");
                AddPerson(auntUncleId, auRole, -1, false, true);
                AddEdge(rootNpcId, auntUncleId, "AuntUncle", auRole, GenderedRole(rootNpcId, "Nephew", "Niece", "Niece/Nephew"), true);

                foreach (var cousinId in Children(auntUncleId))
                {
                    AddPerson(cousinId, "Cousin", 0, false, true);
                    AddEdge(rootNpcId, cousinId, "Cousin", "Cousin", "Cousin", true);
                }
            }
        }

        // In-laws from spouse + children's spouses.
        foreach (var spouseId in Spouses(rootNpcId))
        {
            foreach (var parent in Parents(spouseId))
            {
                var role = GenderedRole(parent.Id, "Father-in-law", "Mother-in-law", "Parent-in-law");
                AddPerson(parent.Id, role, -1, false, true);
                AddEdge(rootNpcId, parent.Id, "InLaw", role, "Child-in-law", true);
            }
        }

        foreach (var childId in Children(rootNpcId))
        {
            foreach (var childSpouseId in Spouses(childId))
            {
                var role = GenderedRole(childSpouseId, "Son-in-law", "Daughter-in-law", "Child-in-law");
                AddPerson(childSpouseId, role, 1, false, true);
                AddEdge(rootNpcId, childSpouseId, "InLaw", role, ParentRole(rootNpcId) + "-in-law", true);
            }
        }

        // Explicit override rows fill structural gaps that cannot yet be derived.
        foreach (var ov in overrides.Where(x => x.Source == rootNpcId))
        {
            AddPerson(ov.Target, ov.Role, 0, true, false);
            AddEdge(rootNpcId, ov.Target, "Override", ov.Role, "", false);
        }

        // Also respect inverse override rows using reciprocal role logic.
        foreach (var ov in overrides.Where(x => x.Target == rootNpcId))
        {
            var inverse = ReciprocalRole(ov.Role);
            AddPerson(ov.Source, inverse, 0, true, true);
            AddEdge(rootNpcId, ov.Source, "OverrideInverse", inverse, ov.Role, true);
        }

        result.People.AddRange(
            people.Values
                .Where(p => p.NpcId != rootNpcId)
                .OrderBy(p => p.Generation)
                .ThenBy(p => p.RoleFromRoot)
                .ThenBy(p => p.Name));

        result.Edges.AddRange(edges.Values);
        return result;

        IEnumerable<(int Id, string Role)> Parents(int childId)
        {
            if (!parentsOf.TryGetValue(childId, out var list))
                yield break;

            foreach (var item in list)
            {
                var role =
                    item.Slot.Equals("Mother", StringComparison.OrdinalIgnoreCase) ? "Mother" :
                    item.Slot.Equals("Father", StringComparison.OrdinalIgnoreCase) ? "Father" :
                    GenderedRole(item.ParentId, "Father", "Mother", "Parent");

                if (item.Kind.Equals("Step", StringComparison.OrdinalIgnoreCase))
                    role = role switch
                    {
                        "Mother" => "Stepmother",
                        "Father" => "Stepfather",
                        _ => "Stepparent"
                    };

                yield return (item.ParentId, role);
            }
        }

        IEnumerable<int> Children(int parentId) =>
            childrenOf.TryGetValue(parentId, out var set) ? set : Enumerable.Empty<int>();

        IEnumerable<int> Spouses(int npcId) =>
            spousesOf.TryGetValue(npcId, out var set) ? set : Enumerable.Empty<int>();

        IEnumerable<int> Siblings(int npcId)
        {
            var siblingIds = new HashSet<int>();

            foreach (var parent in Parents(npcId))
            {
                foreach (var child in Children(parent.Id))
                {
                    if (child != npcId)
                        siblingIds.Add(child);
                }
            }

            return siblingIds;
        }

        void AddPerson(int npcId, string role, int generation, bool direct, bool inferred)
        {
            if (npcId <= 0) return;

            if (people.TryGetValue(npcId, out var existing))
            {
                // Prefer a more specific role over a generic one.
                if (RoleSpecificity(role) <= RoleSpecificity(existing.RoleFromRoot))
                    return;
            }

            people[npcId] = new CanonicalFamilyPerson
            {
                NpcId = npcId,
                Name = GetDisplayName(npcId),
                RoleFromRoot = role,
                Generation = generation,
                IsDirect = direct,
                IsInferred = inferred
            };
        }

        void AddEdge(int from, int to, string edgeType, string roleFromFrom, string roleFromTo, bool inferred)
        {
            if (from <= 0 || to <= 0 || from == to) return;

            var key = $"{Math.Min(from, to)}:{Math.Max(from, to)}:{edgeType}:{roleFromFrom}:{roleFromTo}";
            if (edges.ContainsKey(key)) return;

            edges[key] = new CanonicalFamilyEdge
            {
                FromNpcId = from,
                ToNpcId = to,
                EdgeType = edgeType,
                RoleFromFrom = roleFromFrom,
                RoleFromTo = roleFromTo,
                IsInferred = inferred
            };
        }
    }

    private Dictionary<int, List<(int ParentId, string Kind, string Slot)>> LoadParentMap(SqliteConnection conn)
    {
        var map = new Dictionary<int, List<(int ParentId, string Kind, string Slot)>>();

        if (!TableExists(conn, "FamilyParentChildLinks"))
            return map;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ParentNpcId, ChildNpcId, COALESCE(ParentKind,''), COALESCE(ParentSlot,'')
            FROM FamilyParentChildLinks
            WHERE IsCurrent = 1;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var parent = Convert.ToInt32(reader.GetValue(0));
            var child = Convert.ToInt32(reader.GetValue(1));
            var kind = reader.GetString(2);
            var slot = reader.GetString(3);

            if (!map.TryGetValue(child, out var list))
            {
                list = new();
                map[child] = list;
            }

            if (!list.Any(x => x.ParentId == parent && x.Kind == kind))
                list.Add((parent, kind, slot));
        }

        return map;
    }

    private static Dictionary<int, HashSet<int>> BuildChildrenMap(
        Dictionary<int, List<(int ParentId, string Kind, string Slot)>> parentsOf)
    {
        var map = new Dictionary<int, HashSet<int>>();

        foreach (var (child, parents) in parentsOf)
        {
            foreach (var parent in parents)
            {
                if (!map.TryGetValue(parent.ParentId, out var children))
                {
                    children = new();
                    map[parent.ParentId] = children;
                }

                children.Add(child);
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
            WHERE lower(Status) = 'active'
              AND lower(UnionType) = 'marriage';
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var a = Convert.ToInt32(reader.GetValue(0));
            var b = Convert.ToInt32(reader.GetValue(1));

            Add(a, b);
            Add(b, a);
        }

        return map;

        void Add(int from, int to)
        {
            if (!map.TryGetValue(from, out var set))
            {
                set = new();
                map[from] = set;
            }
            set.Add(to);
        }
    }

    private static List<(int Source, int Target, string Role)> LoadOverrides(SqliteConnection conn)
    {
        var rows = new List<(int Source, int Target, string Role)>();

        if (!TableExists(conn, "FamilyKinshipOverrides"))
            return rows;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT SourceNpcId, TargetNpcId, KinshipRole
            FROM FamilyKinshipOverrides
            WHERE IsCurrent = 1;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                Convert.ToInt32(reader.GetValue(0)),
                Convert.ToInt32(reader.GetValue(1)),
                reader.IsDBNull(2) ? "" : reader.GetString(2)));
        }

        return rows;
    }

    private string GetDisplayName(int npcId)
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        if (TableExists(conn, "NpcNameProfiles"))
        {
            using var nameCmd = conn.CreateCommand();
            nameCmd.CommandText = """
                SELECT FirstName, MiddleName, CurrentLastName, PreferredName, Suffix
                FROM NpcNameProfiles
                WHERE NpcId = $id;
                """;
            nameCmd.Parameters.AddWithValue("$id", npcId);

            using var reader = nameCmd.ExecuteReader();
            if (reader.Read())
            {
                var first = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var middle = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var last = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var preferred = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var suffix = reader.IsDBNull(4) ? "" : reader.GetString(4);

                var displayFirst = string.IsNullOrWhiteSpace(preferred) ? first : preferred;
                var parts = new[] { displayFirst, middle, last, suffix }
                    .Where(x => !string.IsNullOrWhiteSpace(x));

                var composed = string.Join(" ", parts);
                if (!string.IsNullOrWhiteSpace(composed))
                    return composed;
            }
        }

        if (TableExists(conn, "Characters"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Characters WHERE Id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", npcId);
            var raw = cmd.ExecuteScalar()?.ToString() ?? $"NPC {npcId}";
            return NpcFamilyIdentityIntegritySchema.CleanDraftDisplayName(raw);
        }

        return $"NPC {npcId}";
    }

    private string GenderedRole(int npcId, string male, string female, string neutral)
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        if (!TableExists(conn, "Characters"))
            return neutral;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Gender FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);
        var gender = (cmd.ExecuteScalar()?.ToString() ?? "").Trim().ToLowerInvariant();

        if (gender.StartsWith("m")) return male;
        if (gender.StartsWith("f")) return female;
        return neutral;
    }

    private string ParentRole(int npcId) =>
        GenderedRole(npcId, "Father", "Mother", "Parent");

    private string ChildRole(int npcId) =>
        GenderedRole(npcId, "Son", "Daughter", "Child");

    private static string ReciprocalRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "mother" or "father" or "parent" => "Child",
            "stepmother" or "stepfather" or "stepparent" => "Stepchild",
            "son" or "daughter" or "child" => "Parent",
            "stepson" or "stepdaughter" or "stepchild" => "Stepparent",
            "brother" or "sister" or "sibling" => "Sibling",
            "husband" or "wife" or "spouse" => "Spouse",
            "grandmother" or "grandfather" or "grandparent" => "Grandchild",
            "granddaughter" or "grandson" or "grandchild" => "Grandparent",
            "aunt" or "uncle" or "aunt/uncle" => "Niece/Nephew",
            "niece" or "nephew" or "niece/nephew" => "Aunt/Uncle",
            "cousin" => "Cousin",
            _ => role
        };

    private static int RoleSpecificity(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "mother" or "father" or "son" or "daughter" or
            "brother" or "sister" or "husband" or "wife" or
            "grandmother" or "grandfather" or "granddaughter" or "grandson" or
            "aunt" or "uncle" or "niece" or "nephew" => 3,
            "parent" or "child" or "sibling" or "spouse" or
            "grandparent" or "grandchild" => 2,
            _ => 1
        };

    private string GetRelationshipsPath() =>
        _options.GetType().GetProperty("RelationshipsDbPath")?.GetValue(_options) as string
        ?? @"D:\ProjectEveData\Database\project_eve_relationships.db";

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }
}
