using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Imports existing family RelationshipStates into the new canonical family structure.
/// Additive + idempotent. It never deletes legacy relationship rows.
/// </summary>
public sealed class CanonicalFamilyMigrationService
{
    private readonly NpcStudioOptions _options;

    public CanonicalFamilyMigrationService(NpcStudioOptions options)
    {
        _options = options;
    }

    public CanonicalFamilyMigrationReport ImportLegacyFamilyRelationships()
    {
        var report = new CanonicalFamilyMigrationReport();
        var path = GetRelationshipsPath();

        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        if (!TableExists(conn, "RelationshipStates"))
        {
            report.Warnings.Add("RelationshipStates table was not found.");
            return report;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                SourceCharacterId,
                TargetCharacterId,
                COALESCE(TargetName,''),
                COALESCE(RelationshipType,''),
                COALESCE(FamilyRole,'')
            FROM RelationshipStates
            WHERE TargetCharacterId IS NOT NULL
              AND (
                    trim(COALESCE(FamilyRole,'')) <> ''
                    OR lower(COALESCE(RelationshipType,'')) IN
                       ('mother','father','parent','son','daughter','child',
                        'brother','sister','sibling','husband','wife','spouse',
                        'grandmother','grandfather','grandparent',
                        'granddaughter','grandson','grandchild',
                        'aunt','uncle','cousin','niece','nephew',
                        'stepmother','stepfather',
                        'stepson','stepdaughter','stepchild')
                  );
            """;

        var rows = new List<(int Source, int Target, string TargetName, string Type, string FamilyRole)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    Convert.ToInt32(reader.GetValue(0)),
                    Convert.ToInt32(reader.GetValue(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        foreach (var row in rows)
        {
            var role = NormalizeRole(
                string.IsNullOrWhiteSpace(row.FamilyRole) ? row.Type : row.FamilyRole);

            if (row.Source <= 0 || row.Target <= 0 || row.Source == row.Target)
            {
                report.SkippedRows++;
                continue;
            }

            switch (role)
            {
                // Target is parent of Source.
                case "Mother":
                    if (InsertParentChild(conn, row.Target, row.Source, "Biological", "Mother"))
                        report.ParentChildLinksAdded++;
                    break;

                case "Father":
                    if (InsertParentChild(conn, row.Target, row.Source, "Biological", "Father"))
                        report.ParentChildLinksAdded++;
                    break;

                case "Parent":
                    if (InsertParentChild(conn, row.Target, row.Source, "Biological", ""))
                        report.ParentChildLinksAdded++;
                    break;

                case "Stepmother":
                    if (InsertParentChild(conn, row.Target, row.Source, "Step", "Mother"))
                        report.ParentChildLinksAdded++;
                    break;

                case "Stepfather":
                    if (InsertParentChild(conn, row.Target, row.Source, "Step", "Father"))
                        report.ParentChildLinksAdded++;
                    break;

                // Target is child of Source.
                case "Son":
                case "Daughter":
                case "Child":
                    if (InsertParentChild(conn, row.Source, row.Target, "Biological", ""))
                        report.ParentChildLinksAdded++;
                    break;

                case "Stepson":
                case "Stepdaughter":
                case "Stepchild":
                    if (InsertParentChild(conn, row.Source, row.Target, "Step", ""))
                        report.ParentChildLinksAdded++;
                    break;

                // Marriage is a symmetric union.
                case "Husband":
                case "Wife":
                case "Spouse":
                    if (InsertUnion(conn, row.Source, row.Target, "Marriage", "Active"))
                        report.UnionLinksAdded++;
                    break;

                default:
                    // Sibling/grandparent/cousin/etc. can be retained as a
                    // directed structural override until parent links are rich
                    // enough to derive them.
                    if (!string.IsNullOrWhiteSpace(role) &&
                        InsertOverride(conn, row.Source, row.Target, role))
                    {
                        report.KinshipOverridesAdded++;
                    }
                    else
                    {
                        report.SkippedRows++;
                    }
                    break;
            }
        }

        return report;
    }

    private static bool InsertParentChild(
        SqliteConnection conn,
        int parentId,
        int childId,
        string parentKind,
        string parentSlot)
    {
        if (parentId == childId) return false;

        try
        {
            using var cmd = conn.CreateCommand();
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
                    '', 1, 'LegacyRelationshipImport',
                    'Imported from existing RelationshipStates.',
                    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );
                """;
            cmd.Parameters.AddWithValue("$parent", parentId);
            cmd.Parameters.AddWithValue("$child", childId);
            cmd.Parameters.AddWithValue("$kind", parentKind);
            cmd.Parameters.AddWithValue("$slot", parentSlot);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException)
        {
            // A hard unique biological slot can intentionally reject a
            // contradictory legacy row. Leave it untouched for the integrity
            // validator to surface instead of overwriting canonical truth.
            return false;
        }
    }

    private static bool InsertUnion(
        SqliteConnection conn,
        int a,
        int b,
        string unionType,
        string status)
    {
        if (a == b) return false;

        var p1 = Math.Min(a, b);
        var p2 = Math.Max(a, b);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO FamilyUnionLinks
            (
                Person1NpcId, Person2NpcId, UnionType, Status,
                Source, Notes, CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $p1, $p2, $type, $status,
                'LegacyRelationshipImport',
                'Imported from existing RelationshipStates.',
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$p1", p1);
        cmd.Parameters.AddWithValue("$p2", p2);
        cmd.Parameters.AddWithValue("$type", unionType);
        cmd.Parameters.AddWithValue("$status", status);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static bool InsertOverride(
        SqliteConnection conn,
        int source,
        int target,
        string role)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO FamilyKinshipOverrides
            (
                SourceNpcId, TargetNpcId, KinshipRole,
                IsCurrent, Source, Notes,
                CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $source, $target, $role,
                1, 'LegacyRelationshipImport',
                'Imported from existing RelationshipStates.',
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$target", target);
        cmd.Parameters.AddWithValue("$role", role);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static string NormalizeRole(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return "";

        var compact = value
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return compact switch
        {
            "mother" => "Mother",
            "father" => "Father",
            "parent" => "Parent",
            "stepmother" => "Stepmother",
            "stepfather" => "Stepfather",
            "son" => "Son",
            "daughter" => "Daughter",
            "child" => "Child",
            "stepson" => "Stepson",
            "stepdaughter" => "Stepdaughter",
            "stepchild" => "Stepchild",
            "brother" => "Brother",
            "sister" => "Sister",
            "sibling" => "Sibling",
            "husband" => "Husband",
            "wife" => "Wife",
            "spouse" => "Spouse",
            "grandmother" => "Grandmother",
            "grandfather" => "Grandfather",
            "grandparent" => "Grandparent",
            "granddaughter" => "Granddaughter",
            "grandson" => "Grandson",
            "grandchild" => "Grandchild",
            "aunt" => "Aunt",
            "uncle" => "Uncle",
            "cousin" => "Cousin",
            "niece" => "Niece",
            "nephew" => "Nephew",
            _ => value
        };
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
}

