using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Repairs incomplete LEGACY/TEST family structure after migration.
///
/// This is intentionally conservative:
/// - siblings inherit already-known biological parents;
/// - maternal/paternal grandparent overrides are converted to real parent links;
/// - an active marriage is inferred ONLY when two co-parents are both FamilyDraft
///   NPCs and neither already has an active marriage.
///
/// Clean future builds should write these canonical links directly from the
/// Preview -> Confirm manifest and should not need repair inference.
/// </summary>
public sealed class CanonicalFamilyRepairService
{
    private readonly NpcStudioOptions _options;

    public CanonicalFamilyRepairService(NpcStudioOptions options)
    {
        _options = options;
    }

    public void RepairLegacyTestFamily()
    {
        var relPath = GetRelationshipsPath();

        using var rel = new SqliteConnection($"Data Source={relPath}");
        rel.Open();

        if (!TableExists(rel, "FamilyParentChildLinks"))
            return;

        PropagateParentsAcrossSiblingOverrides(rel);
        MaterializeMaternalPaternalGrandparents(rel);
        InferDraftCoParentMarriages(rel);
    }

    private void PropagateParentsAcrossSiblingOverrides(SqliteConnection rel)
    {
        if (!TableExists(rel, "FamilyKinshipOverrides"))
            return;

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT SourceNpcId, TargetNpcId, lower(trim(KinshipRole))
            FROM FamilyKinshipOverrides
            WHERE IsCurrent = 1
              AND lower(trim(KinshipRole)) IN ('brother','sister','sibling');
            """;

        var siblingPairs = new List<(int A, int B)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var a = Convert.ToInt32(reader.GetValue(0));
                var b = Convert.ToInt32(reader.GetValue(1));
                if (a > 0 && b > 0 && a != b)
                    siblingPairs.Add((a, b));
            }
        }

        foreach (var (a, b) in siblingPairs)
        {
            CopyBiologicalParents(rel, a, b);
            CopyBiologicalParents(rel, b, a);
        }
    }

    private static void CopyBiologicalParents(SqliteConnection rel, int fromChild, int toChild)
    {
        using var read = rel.CreateCommand();
        read.CommandText = """
            SELECT ParentNpcId, COALESCE(ParentSlot,'')
            FROM FamilyParentChildLinks
            WHERE ChildNpcId = $child
              AND IsCurrent = 1
              AND lower(ParentKind) = 'biological';
            """;
        read.Parameters.AddWithValue("$child", fromChild);

        var parents = new List<(int ParentId, string Slot)>();
        using (var reader = read.ExecuteReader())
        {
            while (reader.Read())
                parents.Add((Convert.ToInt32(reader.GetValue(0)), reader.GetString(1)));
        }

        foreach (var parent in parents)
            InsertParentChild(rel, parent.ParentId, toChild, "Biological", parent.Slot, "SiblingParentPropagation");
    }

    private static void MaterializeMaternalPaternalGrandparents(SqliteConnection rel)
    {
        if (!TableExists(rel, "FamilyKinshipOverrides"))
            return;

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT SourceNpcId, TargetNpcId, KinshipRole
            FROM FamilyKinshipOverrides
            WHERE IsCurrent = 1
              AND (
                   lower(KinshipRole) LIKE 'maternal grand%'
                   OR lower(KinshipRole) LIKE 'paternal grand%'
                  );
            """;

        var rows = new List<(int Root, int Grandparent, string Role)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                rows.Add((
                    Convert.ToInt32(reader.GetValue(0)),
                    Convert.ToInt32(reader.GetValue(1)),
                    reader.GetString(2)));
        }

        foreach (var row in rows)
        {
            var lower = row.Role.ToLowerInvariant();
            var parentSlot = lower.StartsWith("maternal") ? "Mother" : "Father";
            var grandparentSlot =
                lower.Contains("grandmother") ? "Mother" :
                lower.Contains("grandfather") ? "Father" :
                "";

            var parentId = FindBiologicalParentBySlot(rel, row.Root, parentSlot);
            if (parentId <= 0) continue;

            InsertParentChild(
                rel,
                row.Grandparent,
                parentId,
                "Biological",
                grandparentSlot,
                "GrandparentOverrideMaterialization");
        }
    }

    private void InferDraftCoParentMarriages(SqliteConnection rel)
    {
        // Find children with exactly two active biological parents.
        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT ChildNpcId
            FROM FamilyParentChildLinks
            WHERE IsCurrent = 1
              AND lower(ParentKind) = 'biological'
            GROUP BY ChildNpcId
            HAVING COUNT(DISTINCT ParentNpcId) = 2;
            """;

        var children = new List<int>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                children.Add(Convert.ToInt32(reader.GetValue(0)));
        }

        foreach (var child in children)
        {
            using var pCmd = rel.CreateCommand();
            pCmd.CommandText = """
                SELECT DISTINCT ParentNpcId
                FROM FamilyParentChildLinks
                WHERE ChildNpcId = $child
                  AND IsCurrent = 1
                  AND lower(ParentKind) = 'biological'
                ORDER BY ParentNpcId;
                """;
            pCmd.Parameters.AddWithValue("$child", child);

            var parents = new List<int>();
            using (var reader = pCmd.ExecuteReader())
            {
                while (reader.Read())
                    parents.Add(Convert.ToInt32(reader.GetValue(0)));
            }

            if (parents.Count != 2) continue;

            var a = parents[0];
            var b = parents[1];

            // IMPORTANT: only infer marriage for the current throwaway FamilyDraft
            // shells. This avoids turning every co-parent pair in real data into spouses.
            if (!IsFamilyDraftNpc(a) || !IsFamilyDraftNpc(b))
                continue;

            if (HasActiveMarriage(rel, a) || HasActiveMarriage(rel, b))
                continue;

            InsertMarriage(rel, a, b);
        }
    }

    private bool IsFamilyDraftNpc(int npcId)
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        if (!TableExists(conn, "Characters"))
            return false;

        var hasStatus = ColumnExists(conn, "Characters", "Status");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = hasStatus
            ? "SELECT Name, Status FROM Characters WHERE Id = $id LIMIT 1;"
            : "SELECT Name, '' FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return false;

        var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
        var status = reader.IsDBNull(1) ? "" : reader.GetString(1);

        return status.Equals("FamilyDraft", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("[Family Draft]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasActiveMarriage(SqliteConnection rel, int npcId)
    {
        if (!TableExists(rel, "FamilyUnionLinks"))
            return false;

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1)
            FROM FamilyUnionLinks
            WHERE lower(Status) = 'active'
              AND lower(UnionType) = 'marriage'
              AND (Person1NpcId = $id OR Person2NpcId = $id);
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static void InsertMarriage(SqliteConnection rel, int a, int b)
    {
        if (a <= 0 || b <= 0 || a == b) return;

        var p1 = Math.Min(a, b);
        var p2 = Math.Max(a, b);

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO FamilyUnionLinks
            (
                Person1NpcId, Person2NpcId, UnionType, Status,
                Source, Notes, CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $a, $b, 'Marriage', 'Active',
                'LegacyFamilyDraftRepair',
                'Temporary test-data inference from paired FamilyDraft biological parents.',
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$a", p1);
        cmd.Parameters.AddWithValue("$b", p2);
        cmd.ExecuteNonQuery();
    }

    private static int FindBiologicalParentBySlot(SqliteConnection rel, int childId, string slot)
    {
        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT ParentNpcId
            FROM FamilyParentChildLinks
            WHERE ChildNpcId = $child
              AND IsCurrent = 1
              AND lower(ParentKind) = 'biological'
              AND lower(ParentSlot) = lower($slot)
            ORDER BY UpdatedRealAt DESC, Id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$child", childId);
        cmd.Parameters.AddWithValue("$slot", slot);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static void InsertParentChild(
        SqliteConnection rel,
        int parentId,
        int childId,
        string kind,
        string slot,
        string source)
    {
        if (parentId <= 0 || childId <= 0 || parentId == childId) return;

        try
        {
            using var cmd = rel.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO FamilyParentChildLinks
                (
                    ParentNpcId, ChildNpcId, ParentKind, ParentSlot,
                    FamilyLine, IsCurrent, Source, Notes,
                    CreatedRealAt, UpdatedRealAt
                )
                VALUES
                (
                    $parent, $child, $kind, $slot,
                    '', 1, $source,
                    'Canonical legacy/test family repair.',
                    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );
                """;
            cmd.Parameters.AddWithValue("$parent", parentId);
            cmd.Parameters.AddWithValue("$child", childId);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$slot", slot ?? "");
            cmd.Parameters.AddWithValue("$source", source);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Do not overwrite a contradictory biological slot.
            // FamilyIntegrityGuardService will surface it.
        }
    }

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

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

