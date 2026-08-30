using Microsoft.Data.Sqlite;
using ProjectEve.Data;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class FullFamilyTreeBuildService
{
    private readonly NpcStudioOptions _options;
    private readonly FullFamilyTreePreviewService _preview;
    private readonly CanonicalKinshipCacheRepairService _cacheRepair;

    public FullFamilyTreeBuildService(
        NpcStudioOptions options,
        FullFamilyTreePreviewService preview,
        CanonicalKinshipCacheRepairService cacheRepair)
    {
        _options = options;
        _preview = preview;
        _cacheRepair = cacheRepair;
    }

    public FullFamilyTreeBuildResult Build(int rootNpcId, FullFamilyTreeDesign design)
    {
        if (rootNpcId <= 0)
            return Fail("Invalid root NPC.");

        if (!File.Exists(_options.MainDbPath))
            return Fail("Main NPC database was not found.");

        var relPath = RelationshipsPath();
        if (!File.Exists(relPath))
            return Fail("Relationship database was not found.");

        var manifest = _preview.Build(rootNpcId, design);

        if (manifest.Warnings.Count > 0 && string.IsNullOrWhiteSpace(manifest.RootName))
            return Fail(string.Join(" ", manifest.Warnings));

        var result = new FullFamilyTreeBuildResult
        {
            Success = true,
            RootNpcId = rootNpcId,
            RootName = manifest.RootName,
            StartedRealAt = DateTimeOffset.Now
        };

        var createdIds = new List<int>();

        using var conn = new SqliteConnection("Data Source=" + _options.MainDbPath);
        conn.Open();

        AttachRelationships(conn, relPath);
        EnsureLedger(conn);

        var root = LoadCharacter(conn, rootNpcId);
        if (root is null)
            return Fail($"NPC {rootNpcId} was not found.");

        using var tx = conn.BeginTransaction();

        try
        {
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["root"] = rootNpcId
            };

            foreach (var row in manifest.Rows)
            {
                var id = EnsureMember(conn, tx, root, row, result, createdIds);
                ids[row.MemberKey] = id;
            }

            LinkParentIfPresent(conn, tx, ids, "mother", "root", "Mother", "Root", "Stage3B: mother -> root", result);
            LinkParentIfPresent(conn, tx, ids, "father", "root", "Father", "Root", "Stage3B: father -> root", result);

            foreach (var key in ids.Keys
                         .Where(x => x.StartsWith("brother-", StringComparison.OrdinalIgnoreCase) ||
                                     x.StartsWith("sister-", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                LinkParentIfPresent(conn, tx, ids, "mother", key, "Mother", "Root", $"Stage3B: mother -> {key}", result);
                LinkParentIfPresent(conn, tx, ids, "father", key, "Father", "Root", $"Stage3B: father -> {key}", result);
            }

            LinkParentIfPresent(conn, tx, ids, "maternal-grandmother", "mother", "Mother", "Maternal", "Stage3B: maternal grandmother -> mother", result);
            LinkParentIfPresent(conn, tx, ids, "maternal-grandfather", "mother", "Father", "Maternal", "Stage3B: maternal grandfather -> mother", result);
            LinkParentIfPresent(conn, tx, ids, "paternal-grandmother", "father", "Mother", "Paternal", "Stage3B: paternal grandmother -> father", result);
            LinkParentIfPresent(conn, tx, ids, "paternal-grandfather", "father", "Father", "Paternal", "Stage3B: paternal grandfather -> father", result);

            foreach (var branch in design.Branches)
            {
                var branchKey = branch.Key;
                if (!ids.ContainsKey(branchKey))
                    continue;

                if (branch.Side.Equals("Maternal", StringComparison.OrdinalIgnoreCase))
                {
                    LinkParentIfPresent(conn, tx, ids, "maternal-grandmother", branchKey, "Mother", "Maternal", $"Stage3B: maternal grandmother -> {branchKey}", result);
                    LinkParentIfPresent(conn, tx, ids, "maternal-grandfather", branchKey, "Father", "Maternal", $"Stage3B: maternal grandfather -> {branchKey}", result);
                }
                else
                {
                    LinkParentIfPresent(conn, tx, ids, "paternal-grandmother", branchKey, "Mother", "Paternal", $"Stage3B: paternal grandmother -> {branchKey}", result);
                    LinkParentIfPresent(conn, tx, ids, "paternal-grandfather", branchKey, "Father", "Paternal", $"Stage3B: paternal grandfather -> {branchKey}", result);
                }

                var spouseKey = branchKey + "-spouse";

                if (branch.IncludeSpouse && ids.ContainsKey(spouseKey))
                {
                    EnsureUnion(
                        conn, tx,
                        ids[branchKey], ids[spouseKey],
                        "Marriage", "Active",
                        $"Stage3B explicit spouse branch: {branchKey}",
                        result);
                }

                for (var i = 1; i <= Math.Clamp(branch.ChildCount, 0, 8); i++)
                {
                    var childKey = $"{branchKey}-child-{i}";
                    if (!ids.ContainsKey(childKey))
                        continue;

                    var bloodSlot = branch.BloodRole.Equals("Aunt", StringComparison.OrdinalIgnoreCase)
                        ? "Mother"
                        : "Father";

                    EnsureParentChild(
                        conn, tx,
                        ids[branchKey], ids[childKey],
                        bloodSlot, branch.Side,
                        $"Stage3B branch child: {branchKey} -> {childKey}",
                        result);

                    if (branch.IncludeSpouse && ids.ContainsKey(spouseKey))
                    {
                        var spouseSlot = bloodSlot == "Mother" ? "Father" : "Mother";

                        EnsureParentChild(
                            conn, tx,
                            ids[spouseKey], ids[childKey],
                            spouseSlot, branch.Side,
                            $"Stage3B spouse child: {spouseKey} -> {childKey}",
                            result);
                    }
                }
            }

            foreach (var grandKey in new[]
            {
                "maternal-grandmother",
                "maternal-grandfather",
                "paternal-grandmother",
                "paternal-grandfather"
            })
            {
                var line = grandKey.StartsWith("maternal-", StringComparison.OrdinalIgnoreCase)
                    ? "Maternal"
                    : "Paternal";

                LinkParentIfPresent(conn, tx, ids, grandKey + "-mother", grandKey, "Mother", line, $"Stage3B: {grandKey}-mother -> {grandKey}", result);
                LinkParentIfPresent(conn, tx, ids, grandKey + "-father", grandKey, "Father", line, $"Stage3B: {grandKey}-father -> {grandKey}", result);
            }

            if (design.IncludeRootSpouse && ids.TryGetValue("root-spouse", out var rootSpouseId))
            {
                EnsureUnion(conn, tx, rootNpcId, rootSpouseId, "Marriage", "Active", "Stage3B explicit root spouse", result);

                for (var i = 1; i <= Math.Clamp(design.RootChildCount, 0, 8); i++)
                {
                    var childKey = $"root-child-{i}";
                    if (!ids.TryGetValue(childKey, out var childId))
                        continue;

                    var rootSlot = ParentSlotForGender(root.Gender, "Parent1");
                    var spouseSlot = rootSlot == "Mother"
                        ? "Father"
                        : rootSlot == "Father"
                            ? "Mother"
                            : "Parent2";

                    EnsureParentChild(conn, tx, rootNpcId, childId, rootSlot, "Root", $"Stage3B root child {i}", result);
                    EnsureParentChild(conn, tx, rootSpouseId, childId, spouseSlot, "Root", $"Stage3B root spouse child {i}", result);
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { }

            result.Success = false;
            result.Message = "Stage 3B rolled back before commit. " + ex.Message;
            result.CompletedRealAt = DateTimeOffset.Now;
            return result;
        }

        foreach (var id in createdIds.Distinct())
        {
            try
            {
                ProjectEveDatabaseSetup.EnsureNpcFolders(id, GetName(id));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"NPC {id} folder setup needs repair: {ex.Message}");
            }
        }

        try
        {
            var cache = _cacheRepair.Apply();
            result.CacheMessage = cache.Message;
        }
        catch (Exception ex)
        {
            result.Warnings.Add(
                "Structural family committed, but relationship cache refresh failed: " + ex.Message);
        }

        result.CompletedRealAt = DateTimeOffset.Now;
        result.Message =
            $"Stage 3B structural family committed. Created {result.CreatedCount}; reused {result.ReusedCount}; " +
            $"parent links added {result.ParentLinksCreated}; unions added {result.UnionsCreated}.";

        return result;
    }

    private int EnsureMember(
        SqliteConnection conn,
        SqliteTransaction tx,
        CharacterRoot root,
        FullFamilyTreePreviewRow row,
        FullFamilyTreeBuildResult result,
        List<int> createdIds)
    {
        if (row.ExistingNpcId.HasValue && CharacterExists(conn, tx, row.ExistingNpcId.Value))
        {
            EnsureLedgerRow(conn, tx, root.Id, row.MemberKey, row.ExistingNpcId.Value, row.RequestedRole, "ReusedExisting");

            result.ReusedCount++;
            result.Members.Add(new FullFamilyTreeBuildMemberResult
            {
                MemberKey = row.MemberKey,
                NpcId = row.ExistingNpcId.Value,
                Role = row.RequestedRole,
                CreatedNow = false,
                CurrentSurname = row.CurrentSurname,
                BirthSurname = row.BirthSurname
            });

            return row.ExistingNpcId.Value;
        }

        var ledgerId = FindLedgerNpcId(conn, tx, root.Id, row.MemberKey);
        if (ledgerId.HasValue && CharacterExists(conn, tx, ledgerId.Value))
        {
            result.ReusedCount++;
            result.Members.Add(new FullFamilyTreeBuildMemberResult
            {
                MemberKey = row.MemberKey,
                NpcId = ledgerId.Value,
                Role = row.RequestedRole,
                CreatedNow = false,
                CurrentSurname = row.CurrentSurname,
                BirthSurname = row.BirthSurname
            });
            return ledgerId.Value;
        }

        var id = NextCharacterId(conn, tx);
        var gender = GenderForMember(row.MemberKey, row.RequestedRole);
        var tier = TierForRole(row.RequestedRole);
        var name = BuildDraftName(row.RequestedRole, row.CurrentSurname, id);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Characters
                (
                    Id, NpcKey, Name, DisplayName, FirstName, LastName,
                    Age, Gender, Occupation, Location, Status, PersonalityContext,
                    Hometown, Tier, UpdatedRealAt, WorldId, Employer,
                    CurrentLocationId, HomeLocationId, WorkLocationId,
                    BackstoryShort, PersonalitySummary, CreatedRealAt,
                    LifeStage, IsDeceased, RaceEthnicity
                )
                VALUES
                (
                    $id, $key, $name, $name, '', $last,
                    0, $gender, '', $location, 'FamilyDraft',
                    'Stage 3B structural family shell. Personality not generated yet.',
                    $hometown, $tier, CURRENT_TIMESTAMP, 'smalltown', '',
                    '', '', '',
                    $backstory, '', CURRENT_TIMESTAMP,
                    '', 0, ''
                );
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$key", $"family-{root.Id}-{row.MemberKey}");
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$last", row.CurrentSurname ?? "");
            cmd.Parameters.AddWithValue("$gender", gender);
            cmd.Parameters.AddWithValue("$location", root.Location ?? "");
            cmd.Parameters.AddWithValue("$hometown", root.Hometown ?? "");
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue(
                "$backstory",
                $"Structural family shell: {row.RequestedRole} of {root.Name}. Biography/history not generated.");
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO NpcNameProfiles
                (
                    NpcId, FirstName, MiddleName, CurrentLastName,
                    BirthLastName, PreferredName, Suffix, UpdatedRealAt
                )
                VALUES
                (
                    $id, '', '', $current,
                    $birth, '', '', CURRENT_TIMESTAMP
                )
                ON CONFLICT(NpcId) DO UPDATE SET
                    CurrentLastName=excluded.CurrentLastName,
                    BirthLastName=excluded.BirthLastName,
                    UpdatedRealAt=CURRENT_TIMESTAMP;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$current", row.CurrentSurname ?? "");
            cmd.Parameters.AddWithValue("$birth", row.BirthSurname ?? "");
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO NpcCreationProvenance
                (
                    NpcId, CreationSourceType, CreatedFromNpcId, CreatedFromNpcName,
                    OriginalRole, CreationBatchId, BuildStatus,
                    CreatedRealAt, UpdatedRealAt
                )
                VALUES
                (
                    $id, 'FullFamilyTreeStage3B', $root, $rootName,
                    $role, $batch, 'StructuralDraft',
                    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                )
                ON CONFLICT(NpcId) DO UPDATE SET
                    CreationSourceType=excluded.CreationSourceType,
                    CreatedFromNpcId=excluded.CreatedFromNpcId,
                    CreatedFromNpcName=excluded.CreatedFromNpcName,
                    OriginalRole=excluded.OriginalRole,
                    CreationBatchId=excluded.CreationBatchId,
                    BuildStatus=excluded.BuildStatus,
                    UpdatedRealAt=CURRENT_TIMESTAMP;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$root", root.Id);
            cmd.Parameters.AddWithValue("$rootName", root.Name);
            cmd.Parameters.AddWithValue("$role", row.RequestedRole);
            cmd.Parameters.AddWithValue("$batch", $"family-{root.Id}-stage3b");
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO NpcLifeBuildProfiles
                (
                    NpcId, DesiredDepthTier, BuildMode, CharacterDirection,
                    HistoryDepth, FamilyStatus, HistoryStatus, SubjectiveStatus,
                    PresentLifeStatus, PhotoStatus, VoiceStatus,
                    OverallPercent, LockedForCanon, Notes, UpdatedRealAt
                )
                VALUES
                (
                    $id, $tier, 'FamilyDraft', $direction,
                    'NotGenerated', 'StructuralShell', 'NotStarted', 'NotStarted',
                    'NotStarted', 'NotStarted', 'NotStarted',
                    5, 0,
                    'Created by Full Family Tree Stage 3B. Awaiting five-phase NPC build.',
                    CURRENT_TIMESTAMP
                )
                ON CONFLICT(NpcId) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$direction", row.RequestedRole + " of " + root.Name);
            cmd.ExecuteNonQuery();
        }

        EnsureLedgerRow(conn, tx, root.Id, row.MemberKey, id, row.RequestedRole, "Created");

        createdIds.Add(id);
        result.CreatedCount++;
        result.Members.Add(new FullFamilyTreeBuildMemberResult
        {
            MemberKey = row.MemberKey,
            NpcId = id,
            Role = row.RequestedRole,
            CreatedNow = true,
            CurrentSurname = row.CurrentSurname,
            BirthSurname = row.BirthSurname
        });

        return id;
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

    private static void EnsureLedgerRow(
        SqliteConnection conn,
        SqliteTransaction tx,
        int rootNpcId,
        string memberKey,
        int npcId,
        string role,
        string status)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO NpcFamilyBuildMembers
            (RootNpcId, MemberKey, NpcId, FamilyRole, Status, UpdatedRealAt)
            VALUES
            ($root, $key, $npc, $role, $status, CURRENT_TIMESTAMP)
            ON CONFLICT(RootNpcId, MemberKey) DO UPDATE SET
                NpcId=excluded.NpcId,
                FamilyRole=excluded.FamilyRole,
                Status=excluded.Status,
                UpdatedRealAt=CURRENT_TIMESTAMP;
            """;
        cmd.Parameters.AddWithValue("$root", rootNpcId);
        cmd.Parameters.AddWithValue("$key", memberKey);
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.ExecuteNonQuery();
    }

    private static int? FindLedgerNpcId(
        SqliteConnection conn,
        SqliteTransaction tx,
        int rootNpcId,
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
        cmd.Parameters.AddWithValue("$root", rootNpcId);
        cmd.Parameters.AddWithValue("$key", memberKey);

        var value = cmd.ExecuteScalar();
        return value is null || value == DBNull.Value
            ? null
            : Convert.ToInt32(value);
    }

    private static void LinkParentIfPresent(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyDictionary<string, int> ids,
        string parentKey,
        string childKey,
        string parentSlot,
        string familyLine,
        string notes,
        FullFamilyTreeBuildResult result)
    {
        if (!ids.TryGetValue(parentKey, out var parentId) ||
            !ids.TryGetValue(childKey, out var childId))
            return;

        EnsureParentChild(
            conn, tx,
            parentId, childId,
            parentSlot, familyLine,
            notes, result);
    }

    private static void EnsureParentChild(
        SqliteConnection conn,
        SqliteTransaction tx,
        int parentId,
        int childId,
        string parentSlot,
        string familyLine,
        string notes,
        FullFamilyTreeBuildResult result)
    {
        if (parentId <= 0 || childId <= 0 || parentId == childId)
            return;

        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = """
                SELECT COUNT(*)
                FROM rel.FamilyParentChildLinks
                WHERE ParentNpcId=$parent
                  AND ChildNpcId=$child
                  AND lower(ParentKind)='biological'
                  AND IsCurrent=1;
                """;
            check.Parameters.AddWithValue("$parent", parentId);
            check.Parameters.AddWithValue("$child", childId);

            if (Convert.ToInt32(check.ExecuteScalar() ?? 0) > 0)
            {
                result.ParentLinksReused++;
                return;
            }
        }

        if (parentSlot is "Mother" or "Father")
        {
            using var slot = conn.CreateCommand();
            slot.Transaction = tx;
            slot.CommandText = """
                SELECT ParentNpcId
                FROM rel.FamilyParentChildLinks
                WHERE ChildNpcId=$child
                  AND IsCurrent=1
                  AND lower(ParentKind)='biological'
                  AND ParentSlot=$slot
                LIMIT 1;
                """;
            slot.Parameters.AddWithValue("$child", childId);
            slot.Parameters.AddWithValue("$slot", parentSlot);

            var occupied = slot.ExecuteScalar();

            if (occupied is not null &&
                occupied != DBNull.Value &&
                Convert.ToInt32(occupied) != parentId)
            {
                throw new InvalidOperationException(
                    $"Cannot assign {parentSlot} for NPC {childId}; " +
                    $"that biological slot is already occupied by NPC {occupied}.");
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO rel.FamilyParentChildLinks
            (
                ParentNpcId, ChildNpcId, ParentKind, ParentSlot,
                FamilyLine, IsCurrent, StartGameDate, EndGameDate,
                Source, Notes, CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $parent, $child, 'Biological', $slot,
                $line, 1, '', '',
                'FullFamilyTreeStage3B', $notes,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$parent", parentId);
        cmd.Parameters.AddWithValue("$child", childId);
        cmd.Parameters.AddWithValue("$slot", parentSlot);
        cmd.Parameters.AddWithValue("$line", familyLine);
        cmd.Parameters.AddWithValue("$notes", notes);
        cmd.ExecuteNonQuery();

        result.ParentLinksCreated++;
    }

    private static void EnsureUnion(
        SqliteConnection conn,
        SqliteTransaction tx,
        int a,
        int b,
        string unionType,
        string status,
        string notes,
        FullFamilyTreeBuildResult result)
    {
        if (a <= 0 || b <= 0 || a == b)
            return;

        var p1 = Math.Min(a, b);
        var p2 = Math.Max(a, b);

        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = """
                SELECT COUNT(*)
                FROM rel.FamilyUnionLinks
                WHERE Person1NpcId=$p1
                  AND Person2NpcId=$p2
                  AND UnionType=$type
                  AND lower(Status) IN ('active','married','current');
                """;
            check.Parameters.AddWithValue("$p1", p1);
            check.Parameters.AddWithValue("$p2", p2);
            check.Parameters.AddWithValue("$type", unionType);

            if (Convert.ToInt32(check.ExecuteScalar() ?? 0) > 0)
            {
                result.UnionsReused++;
                return;
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO rel.FamilyUnionLinks
            (
                Person1NpcId, Person2NpcId, UnionType, Status,
                StartGameDate, EndGameDate, Source, Notes,
                CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $p1, $p2, $type, $status,
                '', '', 'FullFamilyTreeStage3B', $notes,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$p1", p1);
        cmd.Parameters.AddWithValue("$p2", p2);
        cmd.Parameters.AddWithValue("$type", unionType);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$notes", notes);
        cmd.ExecuteNonQuery();

        result.UnionsCreated++;
    }

    private static CharacterRoot? LoadCharacter(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                Id,
                COALESCE(Name,''),
                COALESCE(Gender,''),
                COALESCE(Location,''),
                COALESCE(Hometown,'')
            FROM Characters
            WHERE Id=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;

        return new CharacterRoot(
            r.GetInt32(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4));
    }

    private static bool CharacterExists(
        SqliteConnection conn,
        SqliteTransaction tx,
        int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM Characters WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static int NextCharacterId(
        SqliteConnection conn,
        SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(Id),0)+1 FROM Characters;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
    }

    private static string GenderForMember(string key, string role)
    {
        var k = key.ToLowerInvariant();
        var r = role.ToLowerInvariant();

        if (k == "mother" ||
            k.Contains("grandmother") ||
            k.Contains("-aunt-") ||
            k.StartsWith("sister-") ||
            k.EndsWith("-mother") ||
            r.Contains(" aunt") ||
            r == "aunt")
            return "Female";

        if (k == "father" ||
            k.Contains("grandfather") ||
            k.Contains("-uncle-") ||
            k.StartsWith("brother-") ||
            k.EndsWith("-father") ||
            r.Contains(" uncle") ||
            r == "uncle")
            return "Male";

        if (k.EndsWith("-spouse"))
        {
            if (k.Contains("-uncle-"))
                return "Female";
            if (k.Contains("-aunt-"))
                return "Male";
        }

        return "";
    }

    private static int TierForRole(string role)
    {
        var r = role.ToLowerInvariant();

        if (r.Contains("mother") ||
            r.Contains("father") ||
            r.Contains("brother") ||
            r.Contains("sister"))
            return 2;

        if (r.Contains("grand") ||
            r.Contains("aunt") ||
            r.Contains("uncle"))
            return 3;

        if (r.Contains("cousin") ||
            r.Contains("spouse"))
            return 4;

        return 4;
    }

    private static string BuildDraftName(string role, string surname, int id)
    {
        var cleanRole = string.IsNullOrWhiteSpace(role) ? "Family Member" : role.Trim();
        var cleanSurname = string.IsNullOrWhiteSpace(surname) ? "" : " " + surname.Trim();
        return $"[Family Draft {id}] {cleanRole}{cleanSurname}";
    }

    private static string ParentSlotForGender(string gender, string fallback)
    {
        if (gender.Equals("Female", StringComparison.OrdinalIgnoreCase))
            return "Mother";
        if (gender.Equals("Male", StringComparison.OrdinalIgnoreCase))
            return "Father";
        return fallback;
    }

    private static void AttachRelationships(SqliteConnection conn, string path)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "ATTACH DATABASE $path AS rel;";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.ExecuteNonQuery();
    }

    private string RelationshipsPath()
        => string.IsNullOrWhiteSpace(_options.RelationshipsDbPath)
            ? @"D:\ProjectEveData\Database\project_eve_relationships.db"
            : _options.RelationshipsDbPath;

    private string GetName(int npcId)
    {
        using var conn = new SqliteConnection("Data Source=" + _options.MainDbPath);
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

        return Convert.ToString(cmd.ExecuteScalar()) ?? $"NPC {npcId}";
    }

    private static FullFamilyTreeBuildResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message,
            CompletedRealAt = DateTimeOffset.Now
        };

    private sealed record CharacterRoot(
        int Id,
        string Name,
        string Gender,
        string Location,
        string Hometown);
}

public sealed class FullFamilyTreeBuildResult
{
    public bool Success { get; set; }
    public int RootNpcId { get; set; }
    public string RootName { get; set; } = "";
    public int CreatedCount { get; set; }
    public int ReusedCount { get; set; }
    public int ParentLinksCreated { get; set; }
    public int ParentLinksReused { get; set; }
    public int UnionsCreated { get; set; }
    public int UnionsReused { get; set; }
    public string Message { get; set; } = "";
    public string CacheMessage { get; set; } = "";
    public DateTimeOffset StartedRealAt { get; set; }
    public DateTimeOffset CompletedRealAt { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<FullFamilyTreeBuildMemberResult> Members { get; set; } = new();
}

public sealed class FullFamilyTreeBuildMemberResult
{
    public string MemberKey { get; set; } = "";
    public int NpcId { get; set; }
    public string Role { get; set; } = "";
    public bool CreatedNow { get; set; }
    public string CurrentSurname { get; set; } = "";
    public string BirthSurname { get; set; } = "";
}
