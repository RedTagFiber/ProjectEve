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
        // Preserve which root-parent branch owns each grandparent.
        foreach (var parent in Parents(rootNpcId))
        {
            var branch = ParentBranchLabel(parent.Role);

            foreach (var gp in Parents(parent.Id))
            {
                var baseRole = GenderedRole(gp.Id, "Father", "Mother", "Parent");
                var role = $"{branch}'s {baseRole}";

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
        // Preserve which root-parent branch owns the relative.
        foreach (var parent in Parents(rootNpcId))
        {
            var branch = ParentBranchLabel(parent.Role);

            foreach (var auntUncleId in Siblings(parent.Id))
            {
                var siblingRole = GenderedRole(auntUncleId, "Brother", "Sister", "Sibling");
                var auRole = $"{branch}'s {siblingRole}";

                AddPerson(auntUncleId, auRole, -1, false, true);
                AddEdge(rootNpcId, auntUncleId, "AuntUncle", auRole, GenderedRole(rootNpcId, "Nephew", "Niece", "Niece/Nephew"), true);

                foreach (var cousinId in Children(auntUncleId))
                {
                    var cousinRole = $"{branch}'s {siblingRole}'s Child";
                    AddPerson(cousinId, cousinRole, 0, false, true);
                    AddEdge(rootNpcId, cousinId, "Cousin", cousinRole, "Cousin", true);
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

        // Explicit override rows fill STRUCTURAL GAPS ONLY.
        //
        // Important:
        // If the canonical parent/child/spouse graph has already resolved this
        // person from the CURRENT root NPC's point of view, that root-relative
        // role must win.
        //
        // Example:
        // Thomas -> Adam can be Grandson,
        // while Adam -> Thomas must independently resolve as Grandfather.
        //
        // A historical/migrated override must never force the same label in
        // both directions.
        foreach (var ov in overrides.Where(x => x.Source == rootNpcId))
        {
            if (!people.ContainsKey(ov.Target))
            {
                AddPerson(ov.Target, ov.Role, 0, true, false);
                AddEdge(rootNpcId, ov.Target, "Override", ov.Role, "", false);
            }
        }

        // Inverse override rows are also gap-fillers only.
        // ReciprocalRole is used only when the structural graph cannot derive
        // the relationship for the current root NPC.
        foreach (var ov in overrides.Where(x => x.Target == rootNpcId))
        {
            if (!people.ContainsKey(ov.Source))
            {
                var inverse = ReciprocalRole(ov.Role);
                AddPerson(ov.Source, inverse, 0, true, true);
                AddEdge(rootNpcId, ov.Source, "OverrideInverse", inverse, ov.Role, true);
            }
        }

        AssignFamilySides();

        void AssignFamilySides()
        {
            var motherAnchors = Parents(rootNpcId)
                .Where(p => p.Role.Equals("Mother", StringComparison.OrdinalIgnoreCase) ||
                            p.Role.Equals("Stepmother", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .Distinct()
                .ToHashSet();

            var fatherAnchors = Parents(rootNpcId)
                .Where(p => p.Role.Equals("Father", StringComparison.OrdinalIgnoreCase) ||
                            p.Role.Equals("Stepfather", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .Distinct()
                .ToHashSet();

            var fatherSide = BuildBranch(fatherAnchors, motherAnchors);
            var motherSide = BuildBranch(motherAnchors, fatherAnchors);

            foreach (var person in people.Values)
            {
                var inFather = fatherSide.Contains(person.NpcId);
                var inMother = motherSide.Contains(person.NpcId);

                if (inFather && inMother)
                {
                    person.FamilySide = "Shared";
                    person.BranchAnchorNpcId = 0;
                }
                else if (inFather)
                {
                    person.FamilySide = "Father";
                    person.BranchAnchorNpcId = fatherAnchors.FirstOrDefault();
                }
                else if (inMother)
                {
                    person.FamilySide = "Mother";
                    person.BranchAnchorNpcId = motherAnchors.FirstOrDefault();
                }
                else
                {
                    person.FamilySide = "Other";
                    person.BranchAnchorNpcId = 0;
                }
            }

            // The actual root parents must always own their branch, even
            // if legacy spouse/child links make them reachable from both.
            foreach (var id in fatherAnchors)
            {
                if (people.TryGetValue(id, out var p))
                {
                    p.FamilySide = "Father";
                    p.BranchAnchorNpcId = id;
                }
            }

            foreach (var id in motherAnchors)
            {
                if (people.TryGetValue(id, out var p))
                {
                    p.FamilySide = "Mother";
                    p.BranchAnchorNpcId = id;
                }
            }
        }

        HashSet<int> BuildBranch(HashSet<int> anchors, HashSet<int> blockedRootParents)
        {
            var branch = new HashSet<int>();
            var queue = new Queue<int>();

            foreach (var anchorId in anchors)
            {
                if (anchorId <= 0) continue;
                branch.Add(anchorId);
                queue.Enqueue(anchorId);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                IEnumerable<int> StructuralNeighbors()
                {
                    foreach (var p in Parents(current)) yield return p.Id;
                    foreach (var c in Children(current)) yield return c;
                    foreach (var spouse in Spouses(current)) yield return spouse;
                }

                foreach (var next in StructuralNeighbors().Distinct())
                {
                    if (next <= 0 || next == rootNpcId) continue;
                    if (blockedRootParents.Contains(next)) continue;

                    // Resolve only the people this graph actually exposes.
                    if (!people.ContainsKey(next) && !anchors.Contains(next))
                        continue;

                    if (branch.Add(next))
                        queue.Enqueue(next);
                }
            }

            return branch;
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

            var incoming = new CanonicalFamilyPerson
            {
                NpcId = npcId,
                Name = GetDisplayName(npcId),
                RoleFromRoot = role,
                Generation = generation,
                IsDirect = direct,
                IsInferred = inferred
            };

            if (people.TryGetValue(npcId, out var existing))
            {
                if (!ShouldReplaceRole(existing, incoming))
                    return;
            }

            people[npcId] = incoming;
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

    private static bool ShouldReplaceRole(
        CanonicalFamilyPerson existing,
        CanonicalFamilyPerson incoming)
    {
        // 1. Direct canonical structure still beats inferred structure.
        if (existing.IsDirect != incoming.IsDirect)
            return incoming.IsDirect && !existing.IsDirect;

        // 2. At equal directness, non-inferred beats inferred.
        if (existing.IsInferred != incoming.IsInferred)
            return !incoming.IsInferred && existing.IsInferred;

        // 3. Compare semantic role confidence BEFORE generation distance.
        //
        // This is critical for grandparents. A bad sibling path may discover
        // Thomas as "Brother" at generation 0 before the true parent-of-parent
        // path discovers him as "Father's Father" at generation -2.
        //
        // The branch-specific grandparent role must win.
        var existingPriority = RolePriority(existing.RoleFromRoot);
        var incomingPriority = RolePriority(incoming.RoleFromRoot);

        if (incomingPriority != existingPriority)
            return incomingPriority > existingPriority;

        // 4. At equal semantic confidence, prefer the closer generation.
        var existingDepth = Math.Abs(existing.Generation);
        var incomingDepth = Math.Abs(incoming.Generation);

        if (incomingDepth != existingDepth)
            return incomingDepth < existingDepth;

        // 5. Final fallback: use the existing specificity test.
        return RoleSpecificity(incoming.RoleFromRoot) >
               RoleSpecificity(existing.RoleFromRoot);
    }

    private static int RolePriority(string? role)
    {
        var value = (role ?? "").Trim().ToLowerInvariant();

        // Parent-of-parent roles are the strongest inferred grandparent proof.
        // Keep the branch label because it tells us exactly whose parent it is:
        // Father's Father, Father's Mother, Mother's Father, Mother's Mother.
        if ((value.StartsWith("father's ") || value.StartsWith("mother's ")) &&
            (value.EndsWith(" father") ||
             value.EndsWith(" mother") ||
             value.EndsWith(" parent")))
            return 98;

        return value switch
        {
            "husband" or "wife" or "spouse" or "partner" => 100,

            "father" or "mother" or "parent" or
            "stepfather" or "stepmother" or "stepparent" => 95,

            "son" or "daughter" or "child" or
            "stepson" or "stepdaughter" or "stepchild" => 95,

            "brother" or "sister" or "sibling" or
            "stepbrother" or "stepsister" or
            "half-brother" or "half-sister" => 90,

            "grandfather" or "grandmother" or "grandparent" or
            "grandson" or "granddaughter" or "grandchild" => 88,

            "uncle" or "aunt" or
            "niece" or "nephew" or
            "cousin" => 75,

            "father-in-law" or "mother-in-law" or "parent-in-law" or
            "son-in-law" or "daughter-in-law" or "child-in-law" => 65,

            // Other branch descriptions remain useful but are below the
            // dedicated parent-of-parent grandparent pattern above.
            _ when value.Contains("'s ") => 80,

            _ => 0
        };
    }
    private static string ParentBranchLabel(string parentRole) =>
        parentRole.Trim().ToLowerInvariant() switch
        {
            "mother" or "stepmother" => "Mother",
            "father" or "stepfather" => "Father",
            _ => "Parent"
        };

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
        role.Contains("'s ", StringComparison.OrdinalIgnoreCase)
            ? 4
            :
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





