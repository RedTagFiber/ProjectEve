using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Repairs RelationshipStates.FamilyRole as a derived compatibility/display cache.
///
/// Structural truth remains:
/// - FamilyParentChildLinks
/// - FamilyUnionLinks
///
/// Existing subjective relationship values are never overwritten.
/// Existing family rows: only FamilyRole is updated.
/// Missing directed family cache rows: inserted with schema defaults.
/// Stale cache rows: reported only, never deleted automatically.
/// </summary>
public sealed class CanonicalKinshipCacheRepairService
{
    private readonly NpcStudioOptions _options;
    private readonly CanonicalKinshipTitleService _kinship;

    public CanonicalKinshipCacheRepairService(
        NpcStudioOptions options,
        CanonicalKinshipTitleService kinship)
    {
        _options = options;
        _kinship = kinship;
    }

    public KinshipCacheRepairPreview Preview()
    {
        using var conn = OpenRelationships();

        var result = new KinshipCacheRepairPreview();
        var structuralIds = LoadStructuralNpcIds(conn);

        foreach (var viewerId in structuralIds.OrderBy(x => x))
        {
            var family = _kinship.ResolveFamily(viewerId);

            foreach (var row in family)
            {
                if (!row.IsResolved || row.TargetNpcId <= 0)
                    continue;

                var expectedRole = row.Title.Trim();
                if (string.IsNullOrWhiteSpace(expectedRole))
                    continue;

                var existing = FindExistingFamilyCache(
                    conn,
                    viewerId,
                    row.TargetNpcId);

                if (existing is null)
                {
                    result.Items.Add(new KinshipCacheRepairItem
                    {
                        Action = "INSERT",
                        SourceNpcId = viewerId,
                        SourceName = row.ViewerName,
                        TargetNpcId = row.TargetNpcId,
                        TargetName = row.TargetName,
                        ExistingRole = "",
                        ExpectedRole = expectedRole,
                        RelationshipId = BuildRelationshipId(
                            viewerId,
                            row.TargetNpcId)
                    });
                    continue;
                }

                var action = existing.FamilyRole.Equals(
                    expectedRole,
                    StringComparison.OrdinalIgnoreCase)
                    ? "CORRECT"
                    : "UPDATE";

                result.Items.Add(new KinshipCacheRepairItem
                {
                    Action = action,
                    SourceNpcId = viewerId,
                    SourceName = row.ViewerName,
                    TargetNpcId = row.TargetNpcId,
                    TargetName = row.TargetName,
                    ExistingRole = existing.FamilyRole,
                    ExpectedRole = expectedRole,
                    RelationshipId = existing.RelationshipId
                });
            }
        }

        var validPairs = result.Items
            .Select(x => (x.SourceNpcId, x.TargetNpcId))
            .ToHashSet();

        foreach (var stale in LoadFamilyCacheRows(conn))
        {
            if (stale.TargetNpcId <= 0)
                continue;

            if (validPairs.Contains(
                    (stale.SourceNpcId, stale.TargetNpcId)))
                continue;

            result.StaleItems.Add(new KinshipCacheRepairItem
            {
                Action = "STALE-NO-DELETE",
                SourceNpcId = stale.SourceNpcId,
                SourceName = GetName(stale.SourceNpcId),
                TargetNpcId = stale.TargetNpcId,
                TargetName = stale.TargetName,
                ExistingRole = stale.FamilyRole,
                ExpectedRole = "",
                RelationshipId = stale.RelationshipId
            });
        }

        result.Items = result.Items
            .OrderBy(x => ActionOrder(x.Action))
            .ThenBy(x => x.SourceNpcId)
            .ThenBy(x => x.TargetNpcId)
            .ToList();

        return result;
    }

    public KinshipCacheRepairApplyResult Apply()
    {
        var preview = Preview();

        using var conn = OpenRelationships();
        using var tx = conn.BeginTransaction();

        EnsureAuditTable(conn, tx);

        var updated = 0;
        var inserted = 0;

        foreach (var item in preview.Items)
        {
            if (item.Action == "UPDATE")
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE RelationshipStates
                    SET FamilyRole=$role,
                        UpdatedRealAt=CURRENT_TIMESTAMP
                    WHERE RelationshipId=$id;
                    """;
                cmd.Parameters.AddWithValue("$role", item.ExpectedRole);
                cmd.Parameters.AddWithValue("$id", item.RelationshipId);

                var changed = cmd.ExecuteNonQuery();

                if (changed > 0)
                {
                    updated += changed;
                    WriteAudit(
                        conn,
                        tx,
                        "UPDATE",
                        item,
                        "Updated FamilyRole only. Subjective relationship values preserved.");
                }
            }
            else if (item.Action == "INSERT")
            {
                var relationshipId = MakeAvailableRelationshipId(
                    conn,
                    tx,
                    item.RelationshipId,
                    item.SourceNpcId,
                    item.TargetNpcId);

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO RelationshipStates
                    (
                        RelationshipId,
                        SourceCharacterId,
                        TargetCharacterId,
                        TargetName,
                        RelationshipType,
                        FamilyRole,
                        UpdatedRealAt
                    )
                    VALUES
                    (
                        $id,
                        $source,
                        $target,
                        $targetName,
                        'family',
                        $role,
                        CURRENT_TIMESTAMP
                    );
                    """;
                cmd.Parameters.AddWithValue("$id", relationshipId);
                cmd.Parameters.AddWithValue("$source", item.SourceNpcId);
                cmd.Parameters.AddWithValue("$target", item.TargetNpcId);
                cmd.Parameters.AddWithValue("$targetName", item.TargetName);
                cmd.Parameters.AddWithValue("$role", item.ExpectedRole);
                cmd.ExecuteNonQuery();

                item.RelationshipId = relationshipId;
                inserted++;

                WriteAudit(
                    conn,
                    tx,
                    "INSERT",
                    item,
                    "Inserted missing directed family cache row using schema defaults.");
            }
        }

        tx.Commit();

        return new KinshipCacheRepairApplyResult
        {
            Updated = updated,
            Inserted = inserted,
            CorrectAlready = preview.Items.Count(x => x.Action == "CORRECT"),
            StaleNotDeleted = preview.StaleItems.Count,
            Message =
                $"Kinship cache repair complete. Updated {updated}, inserted {inserted}, " +
                $"already correct {preview.Items.Count(x => x.Action == "CORRECT")}, " +
                $"stale rows left untouched {preview.StaleItems.Count}."
        };
    }

    private ExistingCacheRow? FindExistingFamilyCache(
        SqliteConnection conn,
        int sourceNpcId,
        int targetNpcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                RelationshipId,
                COALESCE(FamilyRole,''),
                COALESCE(TargetName,''),
                COALESCE(RelationshipType,'')
            FROM RelationshipStates
            WHERE SourceCharacterId=$source
              AND TargetCharacterId=$target
              AND
              (
                  lower(COALESCE(RelationshipType,'')) IN
                      ('family','married','marriage','spouse')
                  OR trim(COALESCE(FamilyRole,'')) <> ''
              )
            ORDER BY
                CASE
                    WHEN lower(COALESCE(RelationshipType,''))='family'
                    THEN 0
                    ELSE 1
                END,
                RelationshipId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$source", sourceNpcId);
        cmd.Parameters.AddWithValue("$target", targetNpcId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return null;

        return new ExistingCacheRow(
            r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3));
    }

    private IReadOnlyList<CacheRow> LoadFamilyCacheRows(
        SqliteConnection conn)
    {
        var rows = new List<CacheRow>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                RelationshipId,
                SourceCharacterId,
                COALESCE(TargetCharacterId,0),
                COALESCE(TargetName,''),
                COALESCE(FamilyRole,'')
            FROM RelationshipStates
            WHERE lower(COALESCE(RelationshipType,'')) IN
                    ('family','married','marriage','spouse')
               OR trim(COALESCE(FamilyRole,'')) <> '';
            """;

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            rows.Add(new CacheRow(
                r.GetString(0),
                r.GetInt32(1),
                Convert.ToInt32(r.GetValue(2)),
                r.GetString(3),
                r.GetString(4)));
        }

        return rows;
    }

    private HashSet<int> LoadStructuralNpcIds(
        SqliteConnection conn)
    {
        var ids = new HashSet<int>();

        if (TableExists(conn, "FamilyParentChildLinks"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ParentNpcId, ChildNpcId
                FROM FamilyParentChildLinks
                WHERE COALESCE(IsCurrent,1)=1;
                """;

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                ids.Add(r.GetInt32(0));
                ids.Add(r.GetInt32(1));
            }
        }

        if (TableExists(conn, "FamilyUnionLinks"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Person1NpcId, Person2NpcId
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
                ids.Add(r.GetInt32(0));
                ids.Add(r.GetInt32(1));
            }
        }

        return ids;
    }

    private string GetName(int npcId)
    {
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

        return Convert.ToString(cmd.ExecuteScalar())
               ?? $"NPC {npcId}";
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

    private static void EnsureAuditTable(
        SqliteConnection conn,
        SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS KinshipCacheRepairLog
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Action TEXT NOT NULL,
                RelationshipId TEXT NOT NULL DEFAULT '',
                SourceNpcId INTEGER NOT NULL,
                TargetNpcId INTEGER NOT NULL,
                OldFamilyRole TEXT NOT NULL DEFAULT '',
                NewFamilyRole TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void WriteAudit(
        SqliteConnection conn,
        SqliteTransaction tx,
        string action,
        KinshipCacheRepairItem item,
        string notes)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO KinshipCacheRepairLog
            (
                Action,
                RelationshipId,
                SourceNpcId,
                TargetNpcId,
                OldFamilyRole,
                NewFamilyRole,
                Notes,
                CreatedRealAt
            )
            VALUES
            (
                $action,
                $id,
                $source,
                $target,
                $old,
                $new,
                $notes,
                CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$id", item.RelationshipId);
        cmd.Parameters.AddWithValue("$source", item.SourceNpcId);
        cmd.Parameters.AddWithValue("$target", item.TargetNpcId);
        cmd.Parameters.AddWithValue("$old", item.ExistingRole);
        cmd.Parameters.AddWithValue("$new", item.ExpectedRole);
        cmd.Parameters.AddWithValue("$notes", notes);
        cmd.ExecuteNonQuery();
    }

    private static string MakeAvailableRelationshipId(
        SqliteConnection conn,
        SqliteTransaction tx,
        string preferred,
        int source,
        int target)
    {
        var candidate = preferred;
        var suffix = 1;

        while (RelationshipIdExists(
                   conn,
                   tx,
                   candidate))
        {
            candidate = $"rel:{source}:{target}:family-cache-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static bool RelationshipIdExists(
        SqliteConnection conn,
        SqliteTransaction tx,
        string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM RelationshipStates
            WHERE RelationshipId=$id;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        return Convert.ToInt32(
            cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static string BuildRelationshipId(
        int source,
        int target)
        => $"rel:{source}:{target}:family";

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

    private static int ActionOrder(string action)
        => action switch
        {
            "UPDATE" => 10,
            "INSERT" => 20,
            "CORRECT" => 30,
            _ => 99
        };

    private sealed record ExistingCacheRow(
        string RelationshipId,
        string FamilyRole,
        string TargetName,
        string RelationshipType);

    private sealed record CacheRow(
        string RelationshipId,
        int SourceNpcId,
        int TargetNpcId,
        string TargetName,
        string FamilyRole);
}

public sealed class KinshipCacheRepairPreview
{
    public List<KinshipCacheRepairItem> Items { get; set; } = new();
    public List<KinshipCacheRepairItem> StaleItems { get; set; } = new();

    public int UpdateCount =>
        Items.Count(x => x.Action == "UPDATE");

    public int InsertCount =>
        Items.Count(x => x.Action == "INSERT");

    public int CorrectCount =>
        Items.Count(x => x.Action == "CORRECT");
}

public sealed class KinshipCacheRepairItem
{
    public string Action { get; set; } = "";
    public string RelationshipId { get; set; } = "";
    public int SourceNpcId { get; set; }
    public string SourceName { get; set; } = "";
    public int TargetNpcId { get; set; }
    public string TargetName { get; set; } = "";
    public string ExistingRole { get; set; } = "";
    public string ExpectedRole { get; set; } = "";
}

public sealed class KinshipCacheRepairApplyResult
{
    public int Updated { get; set; }
    public int Inserted { get; set; }
    public int CorrectAlready { get; set; }
    public int StaleNotDeleted { get; set; }
    public string Message { get; set; } = "";
}
