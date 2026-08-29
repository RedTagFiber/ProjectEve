using Microsoft.Data.Sqlite;
using ProjectEve.Data;
using ProjectEve.NpcStudio.Models;
using ProjectEve.Relationships;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Family Builder Pass 1.
///
/// Creates only structural draft family NPC shells and canonical directed family
/// relationships. It intentionally does NOT invent full biography, emotional
/// history, memories, jobs, traits, appearance, or TRUE HISTORY.
///
/// Re-running is safe: NpcFamilyBuildMembers remembers the NPC assigned to each
/// root+member key and reuses it instead of creating duplicates.
/// </summary>
public sealed class NpcFamilyBuilderService
{
    private readonly NpcStudioOptions _options;

    public NpcFamilyBuilderService(NpcStudioOptions options)
    {
        _options = options;
    }

    public FamilyBuildResult BuildImmediateFamily(int rootNpcId)
    {
        if (rootNpcId <= 0)
            return Fail("Invalid root NPC.");

        using var conn = new SqliteConnection("Data Source=" + _options.MainDbPath);
        conn.Open();

        EnsureLedger(conn);

        var root = LoadRoot(conn, rootNpcId);
        if (root is null)
            return Fail($"NPC {rootNpcId} was not found.");

        var plan = LoadPlan(conn, rootNpcId);
        if (plan is null)
            return Fail("No saved Family Setup plan exists for this NPC.");

        var result = new FamilyBuildResult { Success = true };

        using var tx = conn.BeginTransaction();

        int? motherId = null;
        int? fatherId = null;

        if (plan.CreateMother)
            motherId = EnsureMember(conn, tx, root, result, "mother", "Mother", "Female", 2);

        if (plan.CreateFather)
            fatherId = EnsureMember(conn, tx, root, result, "father", "Father", "Male", 2);

        var siblingIds = new List<(int Id, string Role)>();

        for (var i = 1; i <= Math.Max(0, plan.BrotherCount); i++)
        {
            var id = EnsureMember(conn, tx, root, result, $"brother-{i}", "Brother", "Male", 2);
            siblingIds.Add((id, "Brother"));
        }

        for (var i = 1; i <= Math.Max(0, plan.SisterCount); i++)
        {
            var id = EnsureMember(conn, tx, root, result, $"sister-{i}", "Sister", "Female", 2);
            siblingIds.Add((id, "Sister"));
        }

        int? mgmId = null;
        int? mgfId = null;
        int? pgmId = null;
        int? pgfId = null;

        if (plan.CreateMaternalGrandmother)
            mgmId = EnsureMember(conn, tx, root, result, "maternal-grandmother", "Maternal Grandmother", "Female", 3);

        if (plan.CreateMaternalGrandfather)
            mgfId = EnsureMember(conn, tx, root, result, "maternal-grandfather", "Maternal Grandfather", "Male", 3);

        if (plan.CreatePaternalGrandmother)
            pgmId = EnsureMember(conn, tx, root, result, "paternal-grandmother", "Paternal Grandmother", "Female", 3);

        if (plan.CreatePaternalGrandfather)
            pgfId = EnsureMember(conn, tx, root, result, "paternal-grandfather", "Paternal Grandfather", "Male", 3);

        UpdatePlanStatus(conn, tx, rootNpcId, "ImmediateFamilyBuilt");
        tx.Commit();

        // Canonical relationship database writes happen through the existing
        // ProjectEve RelationshipRepository gateway.
        if (motherId.HasValue)
            LinkBothWays(rootNpcId, root.Name, root.Gender, motherId.Value, GetName(conn, motherId.Value), "Mother");

        if (fatherId.HasValue)
            LinkBothWays(rootNpcId, root.Name, root.Gender, fatherId.Value, GetName(conn, fatherId.Value), "Father");

        foreach (var sibling in siblingIds)
            LinkBothWays(rootNpcId, root.Name, root.Gender, sibling.Id, GetName(conn, sibling.Id), sibling.Role);

        if (mgmId.HasValue)
            LinkBothWays(rootNpcId, root.Name, root.Gender, mgmId.Value, GetName(conn, mgmId.Value), "Maternal Grandmother");

        if (mgfId.HasValue)
            LinkBothWays(rootNpcId, root.Name, root.Gender, mgfId.Value, GetName(conn, mgfId.Value), "Maternal Grandfather");

        if (pgmId.HasValue)
            LinkBothWays(rootNpcId, root.Name, root.Gender, pgmId.Value, GetName(conn, pgmId.Value), "Paternal Grandmother");

        if (pgfId.HasValue)
            LinkBothWays(rootNpcId, root.Name, root.Gender, pgfId.Value, GetName(conn, pgfId.Value), "Paternal Grandfather");

        // Connect siblings to the same parents when those parent shells exist.
        foreach (var sibling in siblingIds)
        {
            var siblingName = GetName(conn, sibling.Id);

            if (motherId.HasValue)
                LinkParentChild(motherId.Value, GetName(conn, motherId.Value), sibling.Id, siblingName, "Mother", sibling.Role == "Brother" ? "Son" : "Daughter");

            if (fatherId.HasValue)
                LinkParentChild(fatherId.Value, GetName(conn, fatherId.Value), sibling.Id, siblingName, "Father", sibling.Role == "Brother" ? "Son" : "Daughter");
        }

        // Connect each parent to their own parents where available.
        if (motherId.HasValue)
        {
            var motherName = GetName(conn, motherId.Value);

            if (mgmId.HasValue)
                LinkParentChild(mgmId.Value, GetName(conn, mgmId.Value), motherId.Value, motherName, "Mother", "Daughter");

            if (mgfId.HasValue)
                LinkParentChild(mgfId.Value, GetName(conn, mgfId.Value), motherId.Value, motherName, "Father", "Daughter");
        }

        if (fatherId.HasValue)
        {
            var fatherName = GetName(conn, fatherId.Value);

            if (pgmId.HasValue)
                LinkParentChild(pgmId.Value, GetName(conn, pgmId.Value), fatherId.Value, fatherName, "Mother", "Son");

            if (pgfId.HasValue)
                LinkParentChild(pgfId.Value, GetName(conn, pgfId.Value), fatherId.Value, fatherName, "Father", "Son");
        }

        result.Message =
            $"Immediate family structure built. Created {result.CreatedCount}; reused {result.ReusedCount}. " +
            "These are draft family shells only. No TRUE HISTORY or memories were generated.";

        return result;
    }

    private int EnsureMember(
        SqliteConnection conn,
        SqliteTransaction tx,
        RootNpc root,
        FamilyBuildResult result,
        string memberKey,
        string role,
        string gender,
        int tier)
    {
        var existingId = FindLedgerNpcId(conn, tx, root.Id, memberKey);
        if (existingId.HasValue && CharacterExists(conn, tx, existingId.Value))
        {
            result.ReusedCount++;
            result.Members.Add(new FamilyBuildMemberResult
            {
                MemberKey = memberKey,
                NpcId = existingId.Value,
                DisplayName = GetName(conn, existingId.Value, tx),
                FamilyRole = role,
                CreatedNow = false
            });
            return existingId.Value;
        }

        var id = NextCharacterId(conn, tx);
        var displayName = BuildDraftName(root.Name, role);

                var surname = ResolveFamilySurnames(conn, tx, root.Id, root.Name, role);
using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT INTO Characters
            (
                Id, WorldId, NpcKey, Name, DisplayName, FirstName, LastName,
                Age, Gender, Occupation, Employer, Location, Hometown,
                Status, Tier, PersonalityContext, BackstoryShort,
                CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $id, 'smalltown', $key, $name, $name, '', $lastName,
                0, $gender, '', '', $location, $hometown,
                'FamilyDraft', $tier,
                'Family Builder Pass 1 structural shell. Full personality not generated yet.',
                $backstory,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$key", $"family-{root.Id}-{memberKey}");
            cmd.Parameters.AddWithValue("$name", displayName);
            cmd.Parameters.AddWithValue("$lastName", surname.Current);
            cmd.Parameters.AddWithValue("$gender", gender);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$location", root.Location ?? "");
            cmd.Parameters.AddWithValue("$hometown", root.Hometown ?? "");
            cmd.Parameters.AddWithValue(
                "$backstory",
                $"Structural family shell: {role} of {root.Name}. Biography/history not generated.");
            cmd.ExecuteNonQuery();
        }

        using (var nameProfile = conn.CreateCommand())
        {
            nameProfile.Transaction = tx;
            nameProfile.CommandText = """
                INSERT INTO NpcNameProfiles
                (NpcId, FirstName, MiddleName, CurrentLastName, BirthLastName, PreferredName, Suffix, UpdatedRealAt)
                VALUES ($id, , , $current, $birth, , , CURRENT_TIMESTAMP)
                ON CONFLICT(NpcId) DO UPDATE SET
                    CurrentLastName = excluded.CurrentLastName,
                    BirthLastName = excluded.BirthLastName,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;
            nameProfile.Parameters.AddWithValue("$id", id);
            nameProfile.Parameters.AddWithValue("$current", surname.Current);
            nameProfile.Parameters.AddWithValue("$birth", surname.Birth);
            nameProfile.ExecuteNonQuery();
        }

        // Family Builder structural surname lineage is branch-aware.
        using (var physical = conn.CreateCommand())
        {
            physical.Transaction = tx;
            physical.CommandText = """
            INSERT INTO NpcPhysicalProfiles (NpcId, Notes, UpdatedRealAt)
            VALUES ($id, 'Family Builder structural shell. Physical profile not generated yet.', CURRENT_TIMESTAMP)
            ON CONFLICT(NpcId) DO NOTHING;
            """;
            physical.Parameters.AddWithValue("$id", id);
            physical.ExecuteNonQuery();
        }

        using (var life = conn.CreateCommand())
        {
            life.Transaction = tx;
            life.CommandText = """
            INSERT INTO NpcLifeBuildProfiles
            (
                NpcId, DesiredDepthTier, BuildMode, CharacterDirection, HistoryDepth,
                FamilyStatus, HistoryStatus, SubjectiveStatus, PresentLifeStatus,
                PhotoStatus, VoiceStatus, OverallPercent, LockedForCanon, Notes, UpdatedRealAt
            )
            VALUES
            (
                $id, $tier, 'FamilyDraft',
                $direction, 'NotGenerated',
                'StructuralShell', 'NotStarted', 'NotStarted', 'NotStarted',
                'NotStarted', 'NotStarted', 5, 0,
                'Created by Family Builder Pass 1. Must be deepened before canonical biography is considered complete.',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO NOTHING;
            """;
            life.Parameters.AddWithValue("$id", id);
            life.Parameters.AddWithValue("$tier", tier);
            life.Parameters.AddWithValue("$direction", role + " of " + root.Name);
            life.ExecuteNonQuery();
        }

        using (var ledger = conn.CreateCommand())
        {
            ledger.Transaction = tx;
            ledger.CommandText = """
            INSERT INTO NpcFamilyBuildMembers
            (RootNpcId, MemberKey, NpcId, FamilyRole, Status, UpdatedRealAt)
            VALUES ($root, $memberKey, $npcId, $role, 'Created', CURRENT_TIMESTAMP)
            ON CONFLICT(RootNpcId, MemberKey) DO UPDATE SET
                NpcId = excluded.NpcId,
                FamilyRole = excluded.FamilyRole,
                Status = 'Created',
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
            ledger.Parameters.AddWithValue("$root", root.Id);
            ledger.Parameters.AddWithValue("$memberKey", memberKey);
            ledger.Parameters.AddWithValue("$npcId", id);
            ledger.Parameters.AddWithValue("$role", role);
            ledger.ExecuteNonQuery();
        }

        ProjectEveDatabaseSetup.EnsureNpcFolders(id, displayName);

        result.CreatedCount++;
        result.Members.Add(new FamilyBuildMemberResult
        {
            MemberKey = memberKey,
            NpcId = id,
            DisplayName = displayName,
            FamilyRole = role,
            CreatedNow = true
        });

        return id;
    }

    private static void LinkBothWays(
        int rootId,
        string rootName,
        string rootGender,
        int familyId,
        string familyName,
        string familyRole)
    {
        var reverseRole = familyRole switch
        {
            "Mother" or "Father" => ChildRole(rootGender),
            "Brother" or "Sister" => SiblingRole(rootGender),
            "Maternal Grandmother" or "Maternal Grandfather" or
            "Paternal Grandmother" or "Paternal Grandfather" => "Grandchild",
            _ => "Family"
        };

        UpsertStructural(rootId, familyId, familyName, familyRole);
        UpsertStructural(familyId, rootId, rootName, reverseRole);
    }

    private static void LinkParentChild(
        int parentId,
        string parentName,
        int childId,
        string childName,
        string parentRole,
        string childRole)
    {
        UpsertStructural(childId, parentId, parentName, parentRole);
        UpsertStructural(parentId, childId, childName, childRole);
    }

    private static void UpsertStructural(int sourceId, int targetId, string targetName, string familyRole)
    {
        RelationshipRepository.Upsert(
            sourceCharacterId: sourceId,
            targetCharacterId: targetId,
            targetName: targetName,
            relationshipType: "family",
            trust: 50,
            respect: 50,
            affection: 50,
            attraction: 0,
            tension: 0,
            notes: "Structural family relationship created by Family Builder Pass 1. Emotional state is neutral until history establishes it.",
            familyRole: familyRole,
            loyalty: 50,
            anger: 0,
            resentment: 0,
            fear: 0,
            jealousy: 0,
            importance: 90);
    }

    private static string ChildRole(string gender)
        => gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ? "Daughter"
         : gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ? "Son"
         : "Child";

    private static string SiblingRole(string gender)
        => gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ? "Sister"
         : gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ? "Brother"
         : "Sibling";

    private static RootNpc? LoadRoot(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT Id, IFNULL(Name,''), IFNULL(Gender,''), IFNULL(Location,''), IFNULL(Hometown,'')
        FROM Characters
        WHERE Id=$id;
        """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new RootNpc(
            r.GetInt32(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4));
    }

    private static FamilyPlan? LoadPlan(SqliteConnection conn, int rootId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            CreateMother, CreateFather, BrotherCount, SisterCount,
            CreateMaternalGrandmother, CreateMaternalGrandfather,
            CreatePaternalGrandmother, CreatePaternalGrandfather
        FROM NpcFamilyBuildPlans
        WHERE RootNpcId=$id;
        """;
        cmd.Parameters.AddWithValue("$id", rootId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new FamilyPlan(
            Bool(r, 0), Bool(r, 1),
            Int(r, 2), Int(r, 3),
            Bool(r, 4), Bool(r, 5),
            Bool(r, 6), Bool(r, 7));
    }

    private static void EnsureLedger(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS NpcFamilyBuildMembers
        (
            RootNpcId INTEGER NOT NULL,
            MemberKey TEXT NOT NULL,
            NpcId INTEGER NOT NULL,
            FamilyRole TEXT NOT NULL DEFAULT '',
            Status TEXT NOT NULL DEFAULT 'Created',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (RootNpcId, MemberKey),
            UNIQUE (NpcId)
        );

        CREATE INDEX IF NOT EXISTS IX_NpcFamilyBuildMembers_NpcId
            ON NpcFamilyBuildMembers(NpcId);
        """;
        cmd.ExecuteNonQuery();
    }

    private static int? FindLedgerNpcId(
        SqliteConnection conn,
        SqliteTransaction tx,
        int rootId,
        string memberKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        SELECT NpcId
        FROM NpcFamilyBuildMembers
        WHERE RootNpcId=$root AND MemberKey=$key
        LIMIT 1;
        """;
        cmd.Parameters.AddWithValue("$root", rootId);
        cmd.Parameters.AddWithValue("$key", memberKey);

        var value = cmd.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static bool CharacterExists(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM Characters WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static int NextCharacterId(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(Id),0)+1 FROM Characters;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
    }

    private static string GetName(SqliteConnection conn, int id, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IFNULL(Name,'') FROM Characters WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar()?.ToString() ?? $"NPC {id}";
    }

    private static void UpdatePlanStatus(
        SqliteConnection conn,
        SqliteTransaction tx,
        int rootId,
        string status)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        UPDATE NpcFamilyBuildPlans
        SET Status=$status, UpdatedRealAt=CURRENT_TIMESTAMP
        WHERE RootNpcId=$id;
        """;
        cmd.Parameters.AddWithValue("$id", rootId);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.ExecuteNonQuery();
    }

    private static string BuildDraftName(string rootName, string role)
        => $"[Family Draft] {rootName} — {role}";

        private (string Current, string Birth) ResolveFamilySurnames(
        SqliteConnection main,
        SqliteTransaction tx,
        int rootNpcId,
        string rootName,
        string role)
    {
        var rootFallback = LastTokenForSurname(rootName);
        var rootProfile = LoadSurnameProfile(main, tx, rootNpcId);
        var rootCurrent = FirstNonBlankSurname(rootProfile.Current, rootFallback, "Family");
        var rootBirth = FirstNonBlankSurname(rootProfile.Birth, rootCurrent);

        var fatherId = FindBiologicalParentBySlotForSurname(rootNpcId, "Father");
        var motherId = FindBiologicalParentBySlotForSurname(rootNpcId, "Mother");

        var father = LoadSurnameProfile(main, tx, fatherId);
        var mother = LoadSurnameProfile(main, tx, motherId);

        var paternal = FirstNonBlankSurname(father.Birth, father.Current, rootBirth, rootCurrent);
        var maternalKnown = FirstNonBlankSurname(mother.Birth, mother.Current);

        var maternal = !string.IsNullOrWhiteSpace(maternalKnown) &&
                       !maternalKnown.Equals(paternal, StringComparison.OrdinalIgnoreCase)
            ? maternalKnown
            : StableBranchSurname(rootNpcId, "maternal", paternal);

        var paternalGrandmotherBirth = StableBranchSurname(rootNpcId, "paternal-grandmother-birth", paternal, maternal);
        var maternalGrandmotherBirth = StableBranchSurname(rootNpcId, "maternal-grandmother-birth", maternal, paternal);

        var r = (role ?? "").Trim().ToLowerInvariant();

        if (r.Contains("mother's mother") || r.Contains("maternal grandmother"))
            return (maternal, maternalGrandmotherBirth);

        if (r.Contains("mother's father") || r.Contains("maternal grandfather"))
            return (maternal, maternal);

        if (r.Contains("father's mother") || r.Contains("paternal grandmother"))
            return (paternal, paternalGrandmotherBirth);

        if (r.Contains("father's father") || r.Contains("paternal grandfather"))
            return (paternal, paternal);

        if (r.Contains("mother's brother") || r.Contains("mother's sister") || r.Contains("maternal aunt") || r.Contains("maternal uncle"))
            return (maternal, maternal);

        if (r.Contains("father's brother") || r.Contains("father's sister") || r.Contains("paternal aunt") || r.Contains("paternal uncle"))
            return (paternal, paternal);

        if (r == "mother" || r == "stepmother")
            return (rootCurrent, maternal);

        if (r == "father" || r == "stepfather")
            return (paternal, paternal);

        if (r is "brother" or "sister" or "sibling")
            return (rootCurrent, rootBirth);

        return (rootCurrent, rootBirth);
    }

    private static (string Current, string Birth) LoadSurnameProfile(
        SqliteConnection conn,
        SqliteTransaction tx,
        int npcId)
    {
        if (npcId <= 0) return ("", "");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentLastName, ), COALESCE(BirthLastName, ) FROM NpcNameProfiles WHERE NpcId=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? (reader.GetString(0), reader.GetString(1))
            : ("", "");
    }

    private int FindBiologicalParentBySlotForSurname(int childNpcId, string slot)
    {
        var relPath = _options.GetType().GetProperty("RelationshipsDbPath")?.GetValue(_options) as string
            ?? @"D:\ProjectEveData\Database\project_eve_relationships.db";

        using var conn = new SqliteConnection($"Data Source={relPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ParentNpcId FROM FamilyParentChildLinks WHERE ChildNpcId=$child AND IsCurrent=1 AND lower(ParentKind)='biological' AND lower(ParentSlot)=lower($slot) ORDER BY UpdatedRealAt DESC, Id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$child", childNpcId);
        cmd.Parameters.AddWithValue("$slot", slot);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static string StableBranchSurname(int rootNpcId, string branchKey, params string[] avoid)
    {
        string[] pool =
        [
            "Bennett","Carter","Hayes","Mercer","Collins","Parker",
            "Foster","Sullivan","Miller","Reed","Walsh","Turner",
            "Hughes","Morgan","Dalton","Harris","Brooks","Griffin",
            "Mason","Walker","Porter","Dawson","Snyder","Keller"
        ];

        var blocked = avoid
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seed = 17;
        unchecked
        {
            seed = seed * 31 + rootNpcId;
            foreach (var ch in branchKey)
                seed = seed * 31 + ch;
        }

        var start = Math.Abs(seed % pool.Length);

        for (var i = 0; i < pool.Length; i++)
        {
            var candidate = pool[(start + i) % pool.Length];
            if (!blocked.Contains(candidate))
                return candidate;
        }

        return "Family";
    }

    private static string FirstNonBlankSurname(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

    private static string LastTokenForSurname(string value)
    {
        var parts = (value ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[^1];
    }

private static string GuessLastName(string rootName)
    {
        if (string.IsNullOrWhiteSpace(rootName))
            return "";

        var parts = rootName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[^1];
    }

    private static bool Bool(SqliteDataReader r, int i)
        => !r.IsDBNull(i) && Convert.ToInt32(r.GetValue(i)) != 0;

    private static int Int(SqliteDataReader r, int i)
        => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

    private static FamilyBuildResult Fail(string message)
        => new() { Success = false, Message = message };

    private sealed record RootNpc(int Id, string Name, string Gender, string Location, string Hometown);

    private sealed record FamilyPlan(
        bool CreateMother,
        bool CreateFather,
        int BrotherCount,
        int SisterCount,
        bool CreateMaternalGrandmother,
        bool CreateMaternalGrandfather,
        bool CreatePaternalGrandmother,
        bool CreatePaternalGrandfather);
}


