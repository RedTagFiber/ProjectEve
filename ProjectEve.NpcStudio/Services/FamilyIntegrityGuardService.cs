using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Read-only family integrity checks used before any family Confirm/Build.
/// A later write pass must refuse to commit when IsSafe == false.
/// </summary>
public sealed class FamilyIntegrityGuardService
{
    private readonly NpcStudioOptions _options;

    public FamilyIntegrityGuardService(NpcStudioOptions options)
    {
        _options = options;
    }

    public FamilyIntegrityReport ValidateNpc(int npcId)
    {
        var report = new FamilyIntegrityReport { NpcId = npcId };

        var relationshipsPath =
            _options.GetType().GetProperty("RelationshipsDbPath")?.GetValue(_options) as string
            ?? @"D:\ProjectEveData\Database\project_eve_relationships.db";

        using var conn = new SqliteConnection($"Data Source={relationshipsPath}");
        conn.Open();

        CheckSelfParentLinks(conn, npcId, report);
        CheckBiologicalParentSlots(conn, npcId, report);
        CheckActiveSpouses(conn, npcId, report);
        CheckDuplicateParentChildLinks(conn, npcId, report);
        CheckDuplicateRelationshipRows(conn, npcId, report);

        if (report.Errors.Count == 0)
            report.PassedChecks.Add("No blocking family-integrity errors found.");

        return report;
    }

    private static void CheckSelfParentLinks(SqliteConnection conn, int npcId, FamilyIntegrityReport report)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1)
            FROM FamilyParentChildLinks
            WHERE ParentNpcId = ChildNpcId
              AND (ParentNpcId = $id OR ChildNpcId = $id);
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        if (Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0)
            report.Errors.Add("Self parent/child link exists.");
        else
            report.PassedChecks.Add("No self parent/child links.");
    }

    private static void CheckBiologicalParentSlots(SqliteConnection conn, int npcId, FamilyIntegrityReport report)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ParentSlot, COUNT(1)
            FROM FamilyParentChildLinks
            WHERE ChildNpcId = $id
              AND IsCurrent = 1
              AND lower(ParentKind) = 'biological'
              AND ParentSlot IN ('Mother','Father')
            GROUP BY ParentSlot
            HAVING COUNT(1) > 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        var found = false;
        while (reader.Read())
        {
            found = true;
            report.Errors.Add($"More than one active biological {reader.GetString(0)} is linked.");
        }

        if (!found)
            report.PassedChecks.Add("Biological mother/father slots are unique.");
    }

    private static void CheckActiveSpouses(SqliteConnection conn, int npcId, FamilyIntegrityReport report)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1)
            FROM FamilyUnionLinks
            WHERE Status = 'Active'
              AND lower(UnionType) = 'marriage'
              AND (Person1NpcId = $id OR Person2NpcId = $id);
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        if (count > 1)
            report.Errors.Add($"NPC has {count} active marriages. Normal build must block until resolved.");
        else
            report.PassedChecks.Add($"Active marriage count is valid ({count}).");
    }

    private static void CheckDuplicateParentChildLinks(SqliteConnection conn, int npcId, FamilyIntegrityReport report)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ParentNpcId, ChildNpcId, ParentKind, COUNT(1)
            FROM FamilyParentChildLinks
            WHERE ParentNpcId = $id OR ChildNpcId = $id
            GROUP BY ParentNpcId, ChildNpcId, ParentKind
            HAVING COUNT(1) > 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            report.Errors.Add("Duplicate canonical parent/child links exist.");
        else
            report.PassedChecks.Add("No duplicate canonical parent/child links.");
    }

    private static void CheckDuplicateRelationshipRows(SqliteConnection conn, int npcId, FamilyIntegrityReport report)
    {
        using var tableCheck = conn.CreateCommand();
        tableCheck.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='RelationshipStates';";
        if (Convert.ToInt32(tableCheck.ExecuteScalar() ?? 0) == 0) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TargetCharacterId, lower(trim(RelationshipType)), COUNT(1)
            FROM RelationshipStates
            WHERE SourceCharacterId = $id
              AND TargetCharacterId IS NOT NULL
            GROUP BY TargetCharacterId, lower(trim(RelationshipType))
            HAVING COUNT(1) > 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            report.Warnings.Add("Duplicate directed relationship rows exist for at least one target/type.");
        else
            report.PassedChecks.Add("No duplicate directed relationship target/type rows.");
    }
}
