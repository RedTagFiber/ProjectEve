using ProjectEve.NpcStudio.Models;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ProjectEve.NpcStudio.Services;

public sealed class BellefontaineHousingInventoryService
{
    private readonly NpcStudioOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly Lazy<HousingInventoryDocument> _inventory;

    public BellefontaineHousingInventoryService(
        NpcStudioOptions options,
        IWebHostEnvironment environment)
    {
        _options = options;
        _environment = environment;
        _inventory = new Lazy<HousingInventoryDocument>(LoadInventory);
    }

    public IReadOnlyList<HousingUnitDefinition> Units
        => _inventory.Value.HousingUnits;


    public int ResolveHouseholdSize(int npcId)
    {
        if (npcId <= 0)
            return 1;

        using var main = Open();

        using (var cmd = main.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COALESCE(HouseholdId,'')
                FROM Characters
                WHERE Id=$id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);

            var explicitId = Convert.ToString(cmd.ExecuteScalar()) ?? "";

            if (!string.IsNullOrWhiteSpace(explicitId))
            {
                using var count = main.CreateCommand();
                count.CommandText = """
                    SELECT COUNT(*)
                    FROM Characters
                    WHERE HouseholdId=$household;
                    """;
                count.Parameters.AddWithValue("$household", explicitId);

                var explicitCount = Convert.ToInt32(count.ExecuteScalar() ?? 0);

                if (explicitCount > 0)
                    return explicitCount;
            }
        }

        using var rel = OpenRelationshipsReadOnly();

        if (rel is null)
            return 1;

        var union = ResolveActiveUnionPartner(rel, npcId);

        if (union > 0)
            return 2 + CountDependentSharedChildren(main, rel, npcId, union);

        var coparent = ResolveCoParentNpcId(rel, npcId);

        if (coparent > 0)
            return 2 + CountDependentSharedChildren(main, rel, npcId, coparent);

        return 1;
    }

    private static int ResolveActiveUnionPartner(SqliteConnection rel, int npcId)
    {
        if (!TableExists(rel, "FamilyUnionLinks"))
            return 0;

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT Person1NpcId, Person2NpcId
            FROM FamilyUnionLinks
            WHERE (Person1NpcId=$id OR Person2NpcId=$id)
              AND lower(trim(COALESCE(Status,'')))='active'
            ORDER BY Id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return 0;

        var a = r.GetInt32(0);
        var b = r.GetInt32(1);
        return a == npcId ? b : a;
    }

    private static int ResolveCoParentNpcId(SqliteConnection rel, int npcId)
    {
        if (!TableExists(rel, "FamilyParentChildLinks"))
            return 0;

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT other.ParentNpcId
            FROM FamilyParentChildLinks mine
            JOIN FamilyParentChildLinks other
              ON other.ChildNpcId = mine.ChildNpcId
             AND other.ParentNpcId <> mine.ParentNpcId
            WHERE mine.ParentNpcId=$id
              AND mine.IsCurrent=1
              AND other.IsCurrent=1
              AND lower(trim(COALESCE(mine.ParentKind,'')))='biological'
              AND lower(trim(COALESCE(other.ParentKind,'')))='biological'
            ORDER BY other.ParentNpcId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        var value = cmd.ExecuteScalar();

        return value is null || value is DBNull
            ? 0
            : Convert.ToInt32(value);
    }

    private static int CountDependentSharedChildren(
        SqliteConnection main,
        SqliteConnection rel,
        int parentA,
        int parentB)
    {
        if (!TableExists(rel, "FamilyParentChildLinks"))
            return 0;

        var childIds = new List<int>();

        using (var cmd = rel.CreateCommand())
        {
            cmd.CommandText = """
                SELECT DISTINCT a.ChildNpcId
                FROM FamilyParentChildLinks a
                JOIN FamilyParentChildLinks b
                  ON b.ChildNpcId = a.ChildNpcId
                WHERE a.ParentNpcId=$a
                  AND b.ParentNpcId=$b
                  AND a.IsCurrent=1
                  AND b.IsCurrent=1
                  AND lower(trim(COALESCE(a.ParentKind,'')))='biological'
                  AND lower(trim(COALESCE(b.ParentKind,'')))='biological';
                """;
            cmd.Parameters.AddWithValue("$a", parentA);
            cmd.Parameters.AddWithValue("$b", parentB);

            using var r = cmd.ExecuteReader();

            while (r.Read())
                childIds.Add(r.GetInt32(0));
        }

        var dependent = 0;

        foreach (var childId in childIds)
        {
            using var cmd = main.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(Age,0), COALESCE(Status,'')
                FROM Characters
                WHERE Id=$id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", childId);

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                continue;

            var age = r.GetInt32(0);

            // Age 0 means this structural family member has not had
            // Foundation run yet. Count them provisionally so the parents'
            // home is not undersized. Once their real age is generated,
            // adults will naturally stop counting as dependents.
            var ageUnknownStructuralChild = age <= 0;

            if ((age > 0 && age < 18) || ageUnknownStructuralChild)
                dependent++;
        }

        return dependent;
    }

    public HousingSelectionPreview PreviewForNpc(
        int npcId,
        int householdSize = 1,
        int primaryAdultAge = 0)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        using var conn = Open();
        EnsureSchema(conn);

        var existing = LoadExistingAssignment(conn, npcId);

        if (existing is not null)
        {
            return new HousingSelectionPreview
            {
                NpcId = npcId,
                HouseholdKey = existing.HouseholdKey,
                UnitId = existing.UnitId,
                Address = existing.Address,
                HomeLocationId = existing.HomeLocationId,
                PropertyType = existing.PropertyType,
                IsExistingAssignment = true,
                Reason = "Existing canonical housing assignment will be preserved."
            };
        }

        var householdKey = ResolveHouseholdKey(conn, npcId);
        var shared = LoadHouseholdAssignment(conn, householdKey);

        if (shared is not null)
        {
            return new HousingSelectionPreview
            {
                NpcId = npcId,
                HouseholdKey = householdKey,
                UnitId = shared.UnitId,
                Address = shared.Address,
                HomeLocationId = shared.HomeLocationId,
                PropertyType = shared.PropertyType,
                IsExistingAssignment = true,
                Reason = "Another member of this household already owns the housing assignment."
            };
        }

        var occupied = LoadOccupiedUnitIds(conn);

        var candidate = Units
            .Where(x => !occupied.Contains(x.UnitId))
            .OrderBy(x => HousingFitScore(
                x,
                householdSize,
                primaryAdultAge))
            .ThenBy(x => StableOrder(npcId, householdKey, x.UnitId))
            .FirstOrDefault();

        if (candidate is null)
        {
            return new HousingSelectionPreview
            {
                NpcId = npcId,
                HouseholdKey = householdKey,
                Reason = "No available Bellefontaine housing unit remains. Mark Needs Repair."
            };
        }

        return new HousingSelectionPreview
        {
            NpcId = npcId,
            HouseholdKey = householdKey,
            UnitId = candidate.UnitId,
            Address = FormatAddress(candidate),
            HomeLocationId = candidate.Location.WorldLocationId,
            PropertyType = candidate.PropertyType,
            IsExistingAssignment = false,
            Reason = "Available unit selected from canonical Bellefontaine housing inventory. Preview only."
        };
    }

    public HousingAssignmentResult AssignHouseholdUnit(
        int npcId,
        int householdSize = 1,
        int primaryAdultAge = 0)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        using var conn = Open();
        EnsureSchema(conn);
        using var tx = conn.BeginTransaction();

        var existing = LoadExistingAssignment(conn, npcId, tx);

        if (existing is not null)
        {
            tx.Commit();
            return existing;
        }

        var householdKey = ResolveHouseholdKey(conn, npcId, tx);
        var shared = LoadHouseholdAssignment(conn, householdKey, tx);

        if (shared is null)
        {
            var occupied = LoadOccupiedUnitIds(conn, tx);

            var candidate = Units
                .Where(x => !occupied.Contains(x.UnitId))
                .OrderBy(x => HousingFitScore(
                    x,
                    householdSize,
                    primaryAdultAge))
                .ThenBy(x => StableOrder(npcId, householdKey, x.UnitId))
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "No available Bellefontaine housing units remain.");

            shared = new HousingAssignmentResult
            {
                NpcId = npcId,
                HouseholdKey = householdKey,
                UnitId = candidate.UnitId,
                Address = FormatAddress(candidate),
                HomeLocationId = candidate.Location.WorldLocationId,
                PropertyType = candidate.PropertyType
            };

            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO NpcHousingAssignments
                (
                    HouseholdKey,
                    UnitId,
                    Address,
                    HomeLocationId,
                    PropertyType,
                    RootNpcId,
                    CreatedRealAt,
                    UpdatedRealAt
                )
                VALUES
                (
                    $household,
                    $unit,
                    $address,
                    $home,
                    $type,
                    $npc,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                );
                """;
            insert.Parameters.AddWithValue("$household", householdKey);
            insert.Parameters.AddWithValue("$unit", candidate.UnitId);
            insert.Parameters.AddWithValue("$address", shared.Address);
            insert.Parameters.AddWithValue("$home", shared.HomeLocationId);
            insert.Parameters.AddWithValue("$type", candidate.PropertyType);
            insert.Parameters.AddWithValue("$npc", npcId);
            insert.ExecuteNonQuery();
        }

        ApplyHousingToHouseholdMembers(
            conn,
            tx,
            householdKey,
            shared);

        tx.Commit();

        return new HousingAssignmentResult
        {
            NpcId = npcId,
            HouseholdKey = shared.HouseholdKey,
            UnitId = shared.UnitId,
            Address = shared.Address,
            HomeLocationId = shared.HomeLocationId,
            PropertyType = shared.PropertyType
        };
    }

    private string ResolveHouseholdKey(
        SqliteConnection conn,
        int npcId,
        SqliteTransaction? tx = null)
    {
        // 1. Explicit main-DB household always wins.
        if (TableExists(conn, "Characters", tx))
        {
            using var direct = conn.CreateCommand();
            direct.Transaction = tx;
            direct.CommandText = """
                SELECT COALESCE(HouseholdId,'')
                FROM Characters
                WHERE Id=$id
                LIMIT 1;
                """;
            direct.Parameters.AddWithValue("$id", npcId);

            var householdId = Convert.ToString(direct.ExecuteScalar()) ?? "";

            if (!string.IsNullOrWhiteSpace(householdId))
                return $"character-household:{householdId.Trim()}";
        }

        // 2. Relationship DB is the structural truth for unions,
        //    parent-child links, and explicit HouseholdMembers.
        using var rel = OpenRelationshipsReadOnly();

        if (rel is not null)
        {
            var explicitHousehold = TryResolveExplicitRelationshipHousehold(rel, npcId);

            if (!string.IsNullOrWhiteSpace(explicitHousehold))
                return $"relationship-household:{explicitHousehold}";

            var unionKey = TryResolveActiveUnionHousehold(rel, npcId);

            if (!string.IsNullOrWhiteSpace(unionKey))
                return unionKey;

            var coparentKey = TryResolveCoParentHousehold(rel, npcId);

            if (!string.IsNullOrWhiteSpace(coparentKey))
                return coparentKey;
        }

        // Do not merge extended relatives just because they are family.
        return $"npc:{npcId}";
    }

    private SqliteConnection? OpenRelationshipsReadOnly()
    {
        var mainDir = Path.GetDirectoryName(_options.MainDbPath);

        if (string.IsNullOrWhiteSpace(mainDir))
            return null;

        var path = Path.Combine(
            mainDir,
            "project_eve_relationships.db");

        if (!File.Exists(path))
            return null;

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static string TryResolveExplicitRelationshipHousehold(
        SqliteConnection rel,
        int npcId)
    {
        if (!TableExists(rel, "HouseholdMembers"))
            return "";

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(HouseholdId,'')
            FROM HouseholdMembers
            WHERE NpcId=$id
              AND trim(COALESCE(HouseholdId,'')) <> ''
              AND trim(COALESCE(LeftAt,'')) = ''
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static string TryResolveActiveUnionHousehold(
        SqliteConnection rel,
        int npcId)
    {
        if (!TableExists(rel, "FamilyUnionLinks"))
            return "";

        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT Person1NpcId, Person2NpcId
            FROM FamilyUnionLinks
            WHERE (Person1NpcId=$id OR Person2NpcId=$id)
              AND lower(trim(COALESCE(Status,'')))='active'
            ORDER BY Id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return "";

        var a = r.GetInt32(0);
        var b = r.GetInt32(1);
        var low = Math.Min(a,b);
        var high = Math.Max(a,b);

        return $"union:{low}:{high}";
    }

    private static string TryResolveCoParentHousehold(
        SqliteConnection rel,
        int npcId)
    {
        if (!TableExists(rel, "FamilyParentChildLinks"))
            return "";

        // Find another current biological parent who shares at least
        // one child with this NPC. This is a structural household fallback,
        // not a claim about marital status.
        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT other.ParentNpcId
            FROM FamilyParentChildLinks mine
            JOIN FamilyParentChildLinks other
              ON other.ChildNpcId = mine.ChildNpcId
             AND other.ParentNpcId <> mine.ParentNpcId
            WHERE mine.ParentNpcId=$id
              AND mine.IsCurrent=1
              AND other.IsCurrent=1
              AND lower(trim(COALESCE(mine.ParentKind,'')))='biological'
              AND lower(trim(COALESCE(other.ParentKind,'')))='biological'
            ORDER BY other.ParentNpcId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        var otherValue = cmd.ExecuteScalar();

        if (otherValue is null || otherValue is DBNull)
            return "";

        var otherNpcId = Convert.ToInt32(otherValue);
        var low = Math.Min(npcId, otherNpcId);
        var high = Math.Max(npcId, otherNpcId);

        return $"co-parents:{low}:{high}";
    }
    private void ApplyHousingToHouseholdMembers(
        SqliteConnection conn,
        SqliteTransaction tx,
        string householdKey,
        HousingAssignmentResult assignment)
    {
        var memberIds = ResolveHouseholdMembers(
            conn,
            tx,
            householdKey,
            assignment.NpcId);

        foreach (var memberId in memberIds)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE Characters
                SET

                    HomeLocationId = CASE
                        WHEN trim(COALESCE(HomeLocationId,''))=''
                        THEN $home
                        ELSE HomeLocationId
                    END,
                    CurrentLocationId = CASE
                        WHEN trim(COALESCE(CurrentLocationId,''))=''
                        THEN $home
                        ELSE CurrentLocationId
                    END,
                    Location = CASE
                        WHEN trim(COALESCE(Location,''))=''
                        THEN 'Bellefontaine, Ohio'
                        ELSE Location
                    END,
                    Address = CASE
                        WHEN trim(COALESCE(Address,''))=''
                        THEN $address
                        ELSE Address
                    END,
                    UpdatedRealAt=CURRENT_TIMESTAMP
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$household", householdKey);
            cmd.Parameters.AddWithValue("$home", assignment.HomeLocationId);
            cmd.Parameters.AddWithValue("$address", assignment.Address);
            cmd.Parameters.AddWithValue("$id", memberId);
            cmd.ExecuteNonQuery();
        }
    }

    private List<int> ResolveHouseholdMembers(
        SqliteConnection conn,
        SqliteTransaction tx,
        string householdKey,
        int fallbackNpcId)
    {
        var result = new HashSet<int> { fallbackNpcId };

        static bool ParsePair(
            string key,
            string prefix,
            out int a,
            out int b)
        {
            a=0; b=0;

            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = key.Split(':');

            return parts.Length==3 &&
                   int.TryParse(parts[1],out a) &&
                   int.TryParse(parts[2],out b);
        }

        if (ParsePair(householdKey,"union:",out var unionA,out var unionB))
        {
            result.Add(unionA);
            result.Add(unionB);
        }

        if (ParsePair(householdKey,"co-parents:",out var parentA,out var parentB))
        {
            result.Add(parentA);
            result.Add(parentB);

            using var rel = OpenRelationshipsReadOnly();

            if (rel is not null &&
                TableExists(rel,"FamilyParentChildLinks"))
            {
                var childIds = new List<int>();

                using (var childCmd = rel.CreateCommand())
                {
                    childCmd.CommandText = """
                        SELECT DISTINCT a.ChildNpcId
                        FROM FamilyParentChildLinks a
                        JOIN FamilyParentChildLinks b
                          ON b.ChildNpcId=a.ChildNpcId
                        WHERE a.ParentNpcId=$a
                          AND b.ParentNpcId=$b
                          AND a.IsCurrent=1
                          AND b.IsCurrent=1
                          AND lower(trim(COALESCE(a.ParentKind,'')))='biological'
                          AND lower(trim(COALESCE(b.ParentKind,'')))='biological';
                        """;
                    childCmd.Parameters.AddWithValue("$a",parentA);
                    childCmd.Parameters.AddWithValue("$b",parentB);

                    using var r=childCmd.ExecuteReader();

                    while(r.Read())
                        childIds.Add(r.GetInt32(0));
                }

                foreach(var childId in childIds)
                {
                    using var ageCmd=conn.CreateCommand();
                    ageCmd.Transaction=tx;
                    ageCmd.CommandText="""
                        SELECT COALESCE(Age,0)
                        FROM Characters
                        WHERE Id=$id
                        LIMIT 1;
                        """;
                    ageCmd.Parameters.AddWithValue("$id",childId);

                    var age=Convert.ToInt32(ageCmd.ExecuteScalar() ?? 0);

                    if(age<=0 || age<18)
                        result.Add(childId);
                }
            }
        }

        return result.OrderBy(x=>x).ToList();
    }
    private HousingAssignmentResult? LoadExistingAssignment(
        SqliteConnection conn,
        int npcId,
        SqliteTransaction? tx = null)
    {
        var householdKey = ResolveHouseholdKey(conn, npcId, tx);
        var shared = LoadHouseholdAssignment(conn, householdKey, tx);

        if (shared is not null)
        {
            shared.NpcId = npcId;
            return shared;
        }

        return null;
    }

    private HousingAssignmentResult? LoadHouseholdAssignment(
        SqliteConnection conn,
        string householdKey,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT
                UnitId,
                Address,
                HomeLocationId,
                PropertyType
            FROM NpcHousingAssignments
            WHERE HouseholdKey=$household
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$household", householdKey);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return null;

        return new HousingAssignmentResult
        {
            HouseholdKey = householdKey,
            UnitId = r.GetString(0),
            Address = r.GetString(1),
            HomeLocationId = r.GetString(2),
            PropertyType = r.GetString(3)
        };
    }

    private HashSet<string> LoadOccupiedUnitIds(
        SqliteConnection conn,
        SqliteTransaction? tx = null)
    {
        var set = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT UnitId
            FROM NpcHousingAssignments
            WHERE trim(COALESCE(UnitId,'')) <> '';
            """;

        using var r = cmd.ExecuteReader();

        while (r.Read())
            set.Add(r.GetString(0));

        return set;
    }

    private void EnsureSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcHousingAssignments
            (
                HouseholdKey TEXT PRIMARY KEY,
                UnitId TEXT NOT NULL,
                Address TEXT NOT NULL DEFAULT '',
                HomeLocationId TEXT NOT NULL DEFAULT '',
                PropertyType TEXT NOT NULL DEFAULT '',
                RootNpcId INTEGER NOT NULL,
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcHousingAssignments_Unit
            ON NpcHousingAssignments(UnitId);

            CREATE INDEX IF NOT EXISTS IX_NpcHousingAssignments_RootNpc
            ON NpcHousingAssignments(RootNpcId);
            """;
        cmd.ExecuteNonQuery();
    }

    private HousingInventoryDocument LoadInventory()
    {
        var candidates = new[]
        {
            Path.Combine(
                _environment.ContentRootPath,
                "Data",
                "Housing",
                "bellefontaine_housing_inventory.json"),

            Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "Housing",
                "bellefontaine_housing_inventory.json")
        };

        var path = candidates.FirstOrDefault(File.Exists);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new FileNotFoundException(
                "Bellefontaine housing inventory JSON was not found.",
                candidates[0]);
        }

        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<HousingInventoryDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException(
                "Bellefontaine housing inventory JSON could not be parsed.");
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(
            $"Data Source={_options.MainDbPath}");
        conn.Open();
        return conn;
    }

    private static int HousingFitScore(
        HousingUnitDefinition unit,
        int householdSize,
        int primaryAdultAge)
    {
        var score = CapacityPenalty(unit, householdSize) * 10;

        var type = (unit.PropertyType ?? "").Trim();

        // Adults 35+ with a family household should usually live in
        // a house-like property rather than defaulting to an apartment.
        if (primaryAdultAge >= 35 && householdSize >= 2)
        {
            score += type switch
            {
                "House" => -40,
                "RentalHouse" => -32,
                "Townhome" => -24,
                "Duplex" => -18,
                "Apartment" => 35,
                _ => 0
            };
        }
        else if (householdSize >= 3)
        {
            score += type switch
            {
                "House" => -30,
                "RentalHouse" => -25,
                "Townhome" => -18,
                "Duplex" => -12,
                "Apartment" => 25,
                _ => 0
            };
        }
        else if (householdSize == 1 && primaryAdultAge > 0 && primaryAdultAge < 30)
        {
            score += type switch
            {
                "Apartment" => -12,
                "Duplex" => -4,
                _ => 0
            };
        }

        return score;
    }
    private static int CapacityPenalty(
        HousingUnitDefinition unit,
        int householdSize)
    {
        var desiredBedrooms = householdSize switch
        {
            <= 1 => 1,
            2 => 2,
            <= 4 => 3,
            <= 6 => 4,
            _ => 5
        };

        return Math.Abs(
            unit.Housing.Bedrooms - desiredBedrooms);
    }

    private static int StableOrder(
        int npcId,
        string householdKey,
        string unitId)
    {
        var raw = $"{npcId}|{householdKey}|{unitId}";
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));

        return BitConverter.ToInt32(bytes,0) & int.MaxValue;
    }
    private static string FormatAddress(
        HousingUnitDefinition unit)
    {
        var unitText = string.IsNullOrWhiteSpace(unit.Address.Unit)
            ? ""
            : $" Apt {unit.Address.Unit}";

        return $"{unit.Address.Line1}{unitText}, " +
               $"{unit.Address.City}, {unit.Address.State} " +
               $"{unit.Address.PostalCode}";
    }

    private static bool TableExists(
        SqliteConnection conn,
        string table,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=$table;
            """;
        cmd.Parameters.AddWithValue("$table", table);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static HashSet<string> GetColumns(
        SqliteConnection conn,
        string table,
        SqliteTransaction? tx = null)
    {
        var set = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info([{table}]);";

        using var r = cmd.ExecuteReader();

        while (r.Read())
            set.Add(r.GetString(1));

        return set;
    }

    private static string FirstExisting(
        HashSet<string> columns,
        params string[] names)
        => names.FirstOrDefault(columns.Contains) ?? "";
}

public sealed class HousingSelectionPreview
{
    public int NpcId { get; set; }
    public string HouseholdKey { get; set; } = "";
    public string UnitId { get; set; } = "";
    public string Address { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string PropertyType { get; set; } = "";
    public bool IsExistingAssignment { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class HousingAssignmentResult
{
    public int NpcId { get; set; }
    public string HouseholdKey { get; set; } = "";
    public string UnitId { get; set; } = "";
    public string Address { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string PropertyType { get; set; } = "";
}

public sealed class HousingInventoryDocument
{
    public string SchemaVersion { get; set; } = "";
    public string HousingMarket { get; set; } = "";
    public List<HousingUnitDefinition> HousingUnits { get; set; } = new();
}

public sealed class HousingUnitDefinition
{
    public string UnitId { get; set; } = "";
    public string PropertyType { get; set; } = "";
    public HousingAddressDefinition Address { get; set; } = new();
    public HousingLocationDefinition Location { get; set; } = new();
    public HousingDetailsDefinition Housing { get; set; } = new();
}

public sealed class HousingAddressDefinition
{
    public string Line1 { get; set; } = "";
    public string Unit { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public sealed class HousingLocationDefinition
{
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string District { get; set; } = "";
    public string WorldLocationId { get; set; } = "";
}

public sealed class HousingDetailsDefinition
{
    public int Bedrooms { get; set; }
    public double Bathrooms { get; set; }
    public string OwnershipModel { get; set; } = "";
    public double MonthlyRent { get; set; }
    public double EstimatedValue { get; set; }
    public bool AllowsFamilyHousehold { get; set; }
}
