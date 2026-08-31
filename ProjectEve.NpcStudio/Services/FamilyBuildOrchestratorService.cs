using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Durable family-build orchestration ledger.
///
/// Foundation pass only:
/// - discovers the canonical family graph
/// - creates/resumes a run
/// - records five phases per NPC
/// - records final shared-family-history phase
/// - tracks elapsed time, skipped failures and repair items
///
/// This service intentionally does NOT execute AI/canon phase work yet.
/// Later phase runners plug into this ledger one at a time.
/// </summary>
public sealed class FamilyBuildOrchestratorService
{
    private readonly NpcStudioOptions _options;
    private readonly CanonicalFamilyGraphService _familyGraph;
    private readonly NpcFoundationBuildService _foundation;

    public FamilyBuildOrchestratorService(
        NpcStudioOptions options,
        CanonicalFamilyGraphService familyGraph,
        NpcFoundationBuildService foundation)
    {
        _options = options;
        _familyGraph = familyGraph;
        _foundation = foundation;
    }

    public FamilyBuildRunSnapshot PrepareOrResume(int rootNpcId)
    {
        if (rootNpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(rootNpcId));

        EnsureSchema();

        var graph = _familyGraph.Resolve(rootNpcId);
        var ordered = BuildOrderedMembers(rootNpcId, graph);

        using var conn = Open();

        var runId = FindOpenRunId(conn, rootNpcId);

        // First-run guard:
        // FindOpenRunId can return empty text when no open run exists.
        if (string.IsNullOrWhiteSpace(runId))
            runId = CreateRun(conn, rootNpcId);

        EnsureRunMembersAndPhases(conn, runId, ordered);
        EnsureSharedHistoryPhase(conn, runId, rootNpcId);

        RefreshRunStageStatus(conn, runId);

        return LoadSnapshot(conn, runId);
    }

    public FamilyBuildRunSnapshot LoadOrPrepare(int rootNpcId)
        => PrepareOrResume(rootNpcId);

    /// <summary>
    /// Executes only Phase 1 (Foundation) for the authoritative family roster.
    /// Completed rows are preserved. Pending, failed, or interrupted rows are
    /// resumed one NPC at a time. A failure is written to Needs Repair and does
    /// not stop the rest of the family.
    /// </summary>
    public async Task<FamilyBuildRunSnapshot> RunFoundationPhaseAsync(
        int rootNpcId,
        Func<FamilyBuildProgressSnapshot,Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var run = PrepareOrResume(rootNpcId);

        var totalPhase1 = run.Phases.Count(x => x.PhaseNumber == 1);

        async Task ReportAsync(
            FamilyBuildMemberSnapshot member,
            string step,
            string detail = "")
        {
            if (progress is null)
                return;

            var latest = Snapshot(run.RunId);

            var completed = latest.Phases.Count(x =>
                x.PhaseNumber == 1 &&
                (x.Status.Equals("Complete",StringComparison.OrdinalIgnoreCase) ||
                 x.Status.Equals("SkippedExisting",StringComparison.OrdinalIgnoreCase)));

            var failed = latest.Phases.Count(x =>
                x.PhaseNumber == 1 &&
                x.Status.Equals("Failed",StringComparison.OrdinalIgnoreCase));

            var latestMember = latest.Members.FirstOrDefault(x =>
                x.NpcId == member.NpcId);

            await progress(new FamilyBuildProgressSnapshot
            {
                RunId = run.RunId,
                NpcId = member.NpcId,
                NpcName = latestMember?.Name ?? member.Name,
                Age = latestMember?.Age ?? 0,
                FamilyRole = latestMember?.Role ?? member.Role,
                Step = step,
                Detail = detail,
                Total = totalPhase1,
                Completed = completed,
                Failed = failed,
                Remaining = Math.Max(0,totalPhase1 - completed - failed),
                UpdatedRealAt = DateTimeOffset.Now
            });
        }

        foreach (var member in run.Members
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.NpcId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var phase = run.Phases.FirstOrDefault(x =>
                x.NpcId == member.NpcId &&
                x.PhaseNumber == 1);

            if (phase is null)
                continue;

            if (phase.Status.Equals(
                    "Complete",
                    StringComparison.OrdinalIgnoreCase) ||
                phase.Status.Equals(
                    "SkippedExisting",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MarkPhaseStarted(
                run.RunId,
                member.NpcId,
                1,
                "Foundation");

            await ReportAsync(
                member,
                "Starting",
                "Phase 1 started for this NPC.");

            try
            {
                // STRICT SERIAL PHASE 1:
                // Do not touch the next NPC until this NPC has completed its
                // entire Foundation preview + validation + canonical commit.
                await ReportAsync(
                    member,
                    "Building Preview",
                    "Generating and locking Foundation identity, appearance, current life, phone, vehicle, finance and housing preview.");

                var preview =
                    await _foundation.BuildPreviewAsync(
                        member.NpcId,
                        cancellationToken);

                await ReportAsync(
                    member,
                    "Validating",
                    "Checking the locked Foundation preview for blocking errors and family consistency.");

                if (preview.Profile?.Proposal is null)
                {
                    throw new InvalidOperationException(
                        $"NPC {member.NpcId} Foundation preview did not produce a proposal.");
                }

                var blocking = preview.Warnings
                    .Where(x => x.StartsWith(
                        "BLOCK:",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (blocking.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"NPC {member.NpcId} Foundation preview is blocked: " +
                        string.Join(" | ", blocking));
                }

                await ReportAsync(
                    member,
                    "Committing",
                    "Writing missing-only Foundation canon for this NPC.");

                var result =
                    await _foundation.CommitFoundationForFamilyAsync(
                        member.NpcId,
                        cancellationToken);

                MarkPhaseComplete(
                    run.RunId,
                    member.NpcId,
                    1,
                    result.Message);

                await ReportAsync(
                    member,
                    "Complete",
                    result.Message);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MarkPhaseFailedAndContinue(
                    run.RunId,
                    member.NpcId,
                    1,
                    "Foundation",
                    ex.ToString());

                await ReportAsync(
                    member,
                    "Failed",
                    ex.Message);
            }

            // Reload durable status after each NPC so a later interruption can
            // resume from exactly what has already completed.
            run = Snapshot(run.RunId);
        }

        var final = Snapshot(run.RunId);

        var phase1Complete = final.Phases.Count(x =>
            x.PhaseNumber == 1 &&
            (x.Status.Equals(
                 "Complete",
                 StringComparison.OrdinalIgnoreCase) ||
             x.Status.Equals(
                 "SkippedExisting",
                 StringComparison.OrdinalIgnoreCase)));

        var phase1Failed = final.Phases.Count(x =>
            x.PhaseNumber == 1 &&
            x.Status.Equals(
                "Failed",
                StringComparison.OrdinalIgnoreCase));

        if (phase1Complete == totalPhase1 &&
            phase1Failed == 0)
        {
            using var conn = Open();
            TouchRun(
                conn,
                run.RunId,
                "ReadyForPhase2");

            final = Snapshot(run.RunId);
        }

        return final;
    }

    public void MarkPhaseStarted(
        string runId,
        int npcId,
        int phaseNumber,
        string phaseName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcBuildPhaseRuns
            SET Status='Running',
                StartedRealAt=COALESCE(StartedRealAt,CURRENT_TIMESTAMP),
                LastMessage=$message,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE RunId=$run
              AND NpcId=$npc
              AND PhaseNumber=$phase;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue("$phase", phaseNumber);
        cmd.Parameters.AddWithValue("$message", $"{phaseName} started.");
        cmd.ExecuteNonQuery();

        TouchRun(conn, runId, "Running");
    }

    public void MarkPhaseComplete(
        string runId,
        int npcId,
        int phaseNumber,
        string message,
        bool skippedExisting = false)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcBuildPhaseRuns
            SET Status=$status,
                CompletedRealAt=CURRENT_TIMESTAMP,
                LastMessage=$message,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE RunId=$run
              AND NpcId=$npc
              AND PhaseNumber=$phase;
            """;
        cmd.Parameters.AddWithValue(
            "$status",
            skippedExisting ? "SkippedExisting" : "Complete");
        cmd.Parameters.AddWithValue("$message", message ?? "");
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue("$phase", phaseNumber);
        cmd.ExecuteNonQuery();

        // A successful retry closes any previous repair item for the same
        // NPC/phase. This keeps Needs Repair equal to CURRENT failures.
        using (var repair = conn.CreateCommand())
        {
            repair.CommandText = """
                UPDATE NpcBuildRepairItems
                SET Status='Resolved',
                    UpdatedRealAt=CURRENT_TIMESTAMP
                WHERE RunId=$run
                  AND NpcId=$npc
                  AND PhaseNumber=$phase
                  AND Status='Open';
                """;
            repair.Parameters.AddWithValue("$run", runId);
            repair.Parameters.AddWithValue("$npc", npcId);
            repair.Parameters.AddWithValue("$phase", phaseNumber);
            repair.ExecuteNonQuery();
        }

        TouchRun(conn, runId, "Running");
    }

    public void MarkPhaseFailedAndContinue(
        string runId,
        int npcId,
        int phaseNumber,
        string phaseName,
        string error)
    {
        using var conn = Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE NpcBuildPhaseRuns
                SET Status='Failed',
                    CompletedRealAt=CURRENT_TIMESTAMP,
                    LastMessage=$message,
                    ErrorText=$error,
                    UpdatedRealAt=CURRENT_TIMESTAMP
                WHERE RunId=$run
                  AND NpcId=$npc
                  AND PhaseNumber=$phase;
                """;
            cmd.Parameters.AddWithValue(
                "$message",
                $"{phaseName} failed and was skipped. Build may continue.");
            cmd.Parameters.AddWithValue("$error", error ?? "");
            cmd.Parameters.AddWithValue("$run", runId);
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$phase", phaseNumber);
            cmd.ExecuteNonQuery();
        }

        using (var repair = conn.CreateCommand())
        {
            repair.CommandText = """
                INSERT INTO NpcBuildRepairItems
                (
                    RepairId, RunId, NpcId, PhaseNumber, PhaseName,
                    ErrorText, Status, CreatedRealAt, UpdatedRealAt
                )
                VALUES
                (
                    $id, $run, $npc, $phase, $name,
                    $error, 'Open', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                )
                ON CONFLICT(RunId,NpcId,PhaseNumber) DO UPDATE SET
                    PhaseName=excluded.PhaseName,
                    ErrorText=excluded.ErrorText,
                    Status='Open',
                    UpdatedRealAt=CURRENT_TIMESTAMP;
                """;
            repair.Parameters.AddWithValue(
                "$id",
                $"repair-{Guid.NewGuid():N}");
            repair.Parameters.AddWithValue("$run", runId);
            repair.Parameters.AddWithValue("$npc", npcId);
            repair.Parameters.AddWithValue("$phase", phaseNumber);
            repair.Parameters.AddWithValue("$name", phaseName ?? "");
            repair.Parameters.AddWithValue("$error", error ?? "");
            repair.ExecuteNonQuery();
        }

        TouchRun(conn, runId, "Running");
    }

    public FamilyBuildRunSnapshot Snapshot(string runId)
    {
        using var conn = Open();
        return LoadSnapshot(conn, runId);
    }

    public void FinishRunIfNoPending(string runId)
    {
        using var conn = Open();

        using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*)
            FROM NpcBuildPhaseRuns
            WHERE RunId=$run
              AND Status IN ('Pending','Running');
            """;
        check.Parameters.AddWithValue("$run", runId);

        var remaining = Convert.ToInt32(check.ExecuteScalar());

        if (remaining != 0)
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcBuildRuns
            SET Status='Complete',
                CompletedRealAt=CURRENT_TIMESTAMP,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE RunId=$run;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.ExecuteNonQuery();
    }

    private IReadOnlyList<FamilyBuildMemberPlan> BuildOrderedMembers(
        int rootNpcId,
        CanonicalFamilyGraph graph)
    {
        var list = new List<FamilyBuildMemberPlan>();
        var seen = new HashSet<int>();

        void Add(
            int npcId,
            string name,
            string role,
            int order)
        {
            if (npcId <= 0 || !seen.Add(npcId))
                return;

            list.Add(new FamilyBuildMemberPlan
            {
                NpcId = npcId,
                Name = string.IsNullOrWhiteSpace(name)
                    ? $"NPC {npcId}"
                    : name,
                Role = string.IsNullOrWhiteSpace(role)
                    ? "Family"
                    : role,
                SortOrder = order
            });
        }

        // The root is always the first NPC built.
        Add(
            rootNpcId,
            graph.People.FirstOrDefault(p => p.NpcId == rootNpcId)?.Name
                ?? LoadCharacterName(rootNpcId),
            "Primary",
            0);

        // Stage 3B's root+member-key ledger is the authoritative roster.
        // This prevents the broader canonical graph from pulling unrelated
        // family members into this particular five-phase family build.
        using var conn = Open();

        if (!TableExists(conn, "NpcFamilyBuildMembers"))
        {
            // Compatibility fallback for older families created before Stage 3B.
            AddGraphFallbackMembers(graph, rootNpcId, Add);

            return list
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToList();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                m.MemberKey,
                m.NpcId,
                COALESCE(NULLIF(c.DisplayName,''), NULLIF(c.Name,''), 'NPC ' || CAST(m.NpcId AS TEXT)),
                COALESCE(NULLIF(m.FamilyRole,''), 'Family')
            FROM NpcFamilyBuildMembers m
            LEFT JOIN Characters c ON c.Id=m.NpcId
            WHERE m.RootNpcId=$root
            ORDER BY m.MemberKey;
            """;
        cmd.Parameters.AddWithValue("$root", rootNpcId);

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            var memberKey = r.GetString(0);
            var npcId = r.GetInt32(1);
            var name = r.GetString(2);
            var role = r.GetString(3);

            Add(
                npcId,
                name,
                role,
                LedgerSortOrder(memberKey));
        }

        return list
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static int LedgerSortOrder(string memberKey)
    {
        var key = (memberKey ?? "").Trim().ToLowerInvariant();

        if (key == "mother")
            return 100;

        if (key == "father")
            return 101;

        if (key.StartsWith("brother-"))
            return 200 + NumericSuffix(key);

        if (key.StartsWith("sister-"))
            return 220 + NumericSuffix(key);

        if (key == "maternal-grandmother")
            return 300;

        if (key == "maternal-grandfather")
            return 301;

        if (key == "paternal-grandmother")
            return 310;

        if (key == "paternal-grandfather")
            return 311;

        if (IsDirectAuntUncle(key))
            return 400 + BranchSortOffset(key);

        if (key.EndsWith("-spouse"))
            return 500 + BranchSortOffset(key);

        if (key.Contains("-child-"))
            return 600 + BranchSortOffset(key) * 10 + NumericSuffix(key);

        if (key.EndsWith("-father") || key.EndsWith("-mother"))
            return 800 + GreatGrandparentSortOffset(key);

        return 9000 + Math.Abs(StableHash(key) % 900);
    }

    private static bool IsDirectAuntUncle(string key)
    {
        if (!(key.StartsWith("maternal-aunt-") ||
              key.StartsWith("maternal-uncle-") ||
              key.StartsWith("paternal-aunt-") ||
              key.StartsWith("paternal-uncle-")))
            return false;

        return !key.Contains("-child-") &&
               !key.EndsWith("-spouse");
    }

    private static int BranchSortOffset(string key)
    {
        var side =
            key.StartsWith("maternal-")
                ? 0
                : 40;

        var role =
            key.Contains("-uncle-")
                ? 0
                : 20;

        return side + role + NumericBranchOrdinal(key);
    }

    private static int GreatGrandparentSortOffset(string key)
    {
        var branch = 0;

        if (key.StartsWith("maternal-grandmother-"))
            branch = 0;
        else if (key.StartsWith("maternal-grandfather-"))
            branch = 10;
        else if (key.StartsWith("paternal-grandmother-"))
            branch = 20;
        else if (key.StartsWith("paternal-grandfather-"))
            branch = 30;

        return branch + (key.EndsWith("-mother") ? 1 : 0);
    }

    private static int NumericBranchOrdinal(string key)
    {
        var parts = key.Split(
            '-',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        foreach (var part in parts.Reverse())
        {
            if (int.TryParse(part, out var value))
                return value;
        }

        return 0;
    }

    private static int NumericSuffix(string key)
        => NumericBranchOrdinal(key);

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;

            foreach (var c in value ?? "")
                hash = hash * 31 + c;

            return hash;
        }
    }

    private void AddGraphFallbackMembers(
        CanonicalFamilyGraph graph,
        int rootNpcId,
        Action<int, string, string, int> add)
    {
        var familyPeople = graph.People
            .Where(p => p.NpcId != rootNpcId)
            .ToList();

        var spouses = familyPeople
            .Where(p =>
                p.RoleFromRoot.Contains(
                    "Wife",
                    StringComparison.OrdinalIgnoreCase) ||
                p.RoleFromRoot.Contains(
                    "Husband",
                    StringComparison.OrdinalIgnoreCase) ||
                p.RoleFromRoot.Equals(
                    "Spouse",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name);

        foreach (var p in spouses)
            add(p.NpcId, p.Name, p.RoleFromRoot, 100);

        var children = familyPeople
            .Where(p =>
                p.RoleFromRoot.Contains(
                    "Son",
                    StringComparison.OrdinalIgnoreCase) ||
                p.RoleFromRoot.Contains(
                    "Daughter",
                    StringComparison.OrdinalIgnoreCase) ||
                p.RoleFromRoot.Equals(
                    "Child",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name);

        var childOrder = 200;

        foreach (var p in children)
            add(p.NpcId, p.Name, p.RoleFromRoot, childOrder++);

        var others = familyPeople
            .OrderBy(p => p.Generation)
            .ThenBy(p => p.RoleFromRoot)
            .ThenBy(p => p.Name);

        var otherOrder = 1000;

        foreach (var p in others)
            add(p.NpcId, p.Name, p.RoleFromRoot, otherOrder++);
    }

    private string LoadCharacterName(int npcId)
    {
        using var conn = Open();
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

    private static bool TableExists(
        SqliteConnection conn,
        string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table'
              AND name=$name;
            """;
        cmd.Parameters.AddWithValue("$name", tableName);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void EnsureRunMembersAndPhases(
        SqliteConnection conn,
        string runId,
        IReadOnlyList<FamilyBuildMemberPlan> members)
    {
        foreach (var member in members)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO NpcBuildRunMembers
                    (
                        RunId, NpcId, DisplayName, FamilyRole,
                        SortOrder, Status, UpdatedRealAt
                    )
                    VALUES
                    (
                        $run, $npc, $name, $role,
                        $sort, 'Pending', CURRENT_TIMESTAMP
                    )
                    ON CONFLICT(RunId,NpcId) DO UPDATE SET
                        DisplayName=excluded.DisplayName,
                        FamilyRole=excluded.FamilyRole,
                        SortOrder=excluded.SortOrder,
                        UpdatedRealAt=CURRENT_TIMESTAMP;
                    """;
                cmd.Parameters.AddWithValue("$run", runId);
                cmd.Parameters.AddWithValue("$npc", member.NpcId);
                cmd.Parameters.AddWithValue("$name", member.Name);
                cmd.Parameters.AddWithValue("$role", member.Role);
                cmd.Parameters.AddWithValue("$sort", member.SortOrder);
                cmd.ExecuteNonQuery();
            }

            foreach (var phase in StandardPhases)
            {
                using var phaseCmd = conn.CreateCommand();
                phaseCmd.CommandText = """
                    INSERT INTO NpcBuildPhaseRuns
                    (
                        RunId, NpcId, PhaseNumber, PhaseName,
                        Status, LastMessage, UpdatedRealAt
                    )
                    VALUES
                    (
                        $run, $npc, $number, $name,
                        'Pending', '', CURRENT_TIMESTAMP
                    )
                    ON CONFLICT(RunId,NpcId,PhaseNumber) DO NOTHING;
                    """;
                phaseCmd.Parameters.AddWithValue("$run", runId);
                phaseCmd.Parameters.AddWithValue("$npc", member.NpcId);
                phaseCmd.Parameters.AddWithValue("$number", phase.Number);
                phaseCmd.Parameters.AddWithValue("$name", phase.Name);
                phaseCmd.ExecuteNonQuery();
            }
        }
    }

    private static void EnsureSharedHistoryPhase(
        SqliteConnection conn,
        string runId,
        int rootNpcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcBuildPhaseRuns
            (
                RunId, NpcId, PhaseNumber, PhaseName,
                Status, LastMessage, UpdatedRealAt
            )
            VALUES
            (
                $run, $npc, 6, 'Shared Family History',
                'Pending', '', CURRENT_TIMESTAMP
            )
            ON CONFLICT(RunId,NpcId,PhaseNumber) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$npc", rootNpcId);
        cmd.ExecuteNonQuery();
    }

    private FamilyBuildRunSnapshot LoadSnapshot(
        SqliteConnection conn,
        string runId)
    {
        var snapshot = new FamilyBuildRunSnapshot
        {
            RunId = runId
        };

        using (var run = conn.CreateCommand())
        {
            run.CommandText = """
                SELECT RootNpcId, Status, StartedRealAt,
                       IFNULL(CompletedRealAt,'')
                FROM NpcBuildRuns
                WHERE RunId=$run;
                """;
            run.Parameters.AddWithValue("$run", runId);

            using var r = run.ExecuteReader();
            if (!r.Read())
                throw new InvalidOperationException(
                    $"Family build run '{runId}' was not found.");

            snapshot.RootNpcId = r.GetInt32(0);
            snapshot.Status = r.GetString(1);
            snapshot.StartedRealAt = r.GetString(2);
            snapshot.CompletedRealAt = r.GetString(3);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    m.NpcId,
                    CASE
                        WHEN trim(COALESCE(np.FirstName,'')) <> ''
                        THEN trim(
                            COALESCE(np.FirstName,'') ||
                            CASE
                                WHEN trim(COALESCE(np.MiddleName,'')) <> ''
                                THEN ' ' || trim(np.MiddleName)
                                ELSE ''
                            END ||
                            CASE
                                WHEN trim(COALESCE(np.CurrentLastName,'')) <> ''
                                THEN ' ' || trim(np.CurrentLastName)
                                WHEN trim(COALESCE(c.LastName,'')) <> ''
                                THEN ' ' || trim(c.LastName)
                                ELSE ''
                            END
                        )
                        WHEN trim(COALESCE(c.Name,'')) <> ''
                             AND c.Name NOT LIKE '[Family Draft %]%'
                        THEN trim(c.Name)
                        WHEN trim(COALESCE(c.DisplayName,'')) <> ''
                             AND c.DisplayName NOT LIKE '[Family Draft %]%'
                        THEN trim(c.DisplayName)
                        WHEN trim(COALESCE(m.DisplayName,'')) <> ''
                        THEN trim(m.DisplayName)
                        ELSE 'NPC ' || CAST(m.NpcId AS TEXT)
                    END AS CanonicalDisplayName,
                    m.FamilyRole,
                    m.SortOrder,
                    m.Status,
                    COALESCE(c.Age,0)
                FROM NpcBuildRunMembers m
                LEFT JOIN Characters c
                    ON c.Id=m.NpcId
                LEFT JOIN NpcNameProfiles np
                    ON np.NpcId=m.NpcId
                WHERE m.RunId=$run
                ORDER BY m.SortOrder, CanonicalDisplayName;
                """;
            cmd.Parameters.AddWithValue("$run", runId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                snapshot.Members.Add(new FamilyBuildMemberSnapshot
                {
                    NpcId = r.GetInt32(0),
                    Name = r.GetString(1),
                    Role = r.GetString(2),
                    SortOrder = r.GetInt32(3),
                    Status = r.GetString(4),
                    Age = r.GetInt32(5)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT NpcId, PhaseNumber, PhaseName, Status,
                       IFNULL(StartedRealAt,''),
                       IFNULL(CompletedRealAt,''),
                       IFNULL(LastMessage,''),
                       IFNULL(ErrorText,'')
                FROM NpcBuildPhaseRuns
                WHERE RunId=$run
                ORDER BY
                    CASE WHEN PhaseNumber=6 THEN 999999 ELSE NpcId END,
                    PhaseNumber;
                """;
            cmd.Parameters.AddWithValue("$run", runId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                snapshot.Phases.Add(new FamilyBuildPhaseSnapshot
                {
                    NpcId = r.GetInt32(0),
                    PhaseNumber = r.GetInt32(1),
                    PhaseName = r.GetString(2),
                    Status = r.GetString(3),
                    StartedRealAt = r.GetString(4),
                    CompletedRealAt = r.GetString(5),
                    Message = r.GetString(6),
                    Error = r.GetString(7)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT RepairId, NpcId, PhaseNumber, PhaseName,
                       ErrorText, Status
                FROM NpcBuildRepairItems
                WHERE RunId=$run
                ORDER BY NpcId, PhaseNumber;
                """;
            cmd.Parameters.AddWithValue("$run", runId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                snapshot.Repairs.Add(new FamilyBuildRepairSnapshot
                {
                    RepairId = r.GetString(0),
                    NpcId = r.GetInt32(1),
                    PhaseNumber = r.GetInt32(2),
                    PhaseName = r.GetString(3),
                    Error = r.GetString(4),
                    Status = r.GetString(5)
                });
            }
        }

        return snapshot;
    }

    private static void RefreshRunStageStatus(
        SqliteConnection conn,
        string runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcBuildRuns
            SET Status =
                CASE
                    WHEN EXISTS
                    (
                        SELECT 1
                        FROM NpcBuildPhaseRuns
                        WHERE RunId=$run
                          AND PhaseNumber=1
                          AND Status='Failed'
                    )
                    THEN 'NeedsRepair'

                    WHEN
                    (
                        SELECT COUNT(*)
                        FROM NpcBuildPhaseRuns
                        WHERE RunId=$run
                          AND PhaseNumber=1
                    ) > 0

                    AND
                    (
                        SELECT COUNT(*)
                        FROM NpcBuildPhaseRuns
                        WHERE RunId=$run
                          AND PhaseNumber=1
                          AND Status IN ('Complete','SkippedExisting')
                    ) =
                    (
                        SELECT COUNT(*)
                        FROM NpcBuildPhaseRuns
                        WHERE RunId=$run
                          AND PhaseNumber=1
                    )

                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM NpcBuildPhaseRuns
                        WHERE RunId=$run
                          AND PhaseNumber>=2
                          AND Status <> 'Pending'
                    )
                    THEN 'ReadyForPhase2'

                    ELSE Status
                END,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE RunId=$run;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.ExecuteNonQuery();
    }
    private string? FindOpenRunId(
        SqliteConnection conn,
        int rootNpcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT RunId
            FROM NpcBuildRuns
            WHERE RootNpcId=$root
              AND Status <> 'Complete'
            ORDER BY StartedRealAt DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$root", rootNpcId);
        return Convert.ToString(cmd.ExecuteScalar());
    }

    private static string CreateRun(
        SqliteConnection conn,
        int rootNpcId)
    {
        var runId = $"family-build-{rootNpcId}-{Guid.NewGuid():N}";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcBuildRuns
            (
                RunId, RootNpcId, Status,
                StartedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $run, $root, 'Prepared',
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$root", rootNpcId);
        cmd.ExecuteNonQuery();

        return runId;
    }

    private static void TouchRun(
        SqliteConnection conn,
        string runId,
        string status)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcBuildRuns
            SET Status=$status,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE RunId=$run;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(
            "Data Source=" + _options.MainDbPath);
        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcBuildRuns
            (
                RunId TEXT PRIMARY KEY,
                RootNpcId INTEGER NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Prepared',
                StartedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CompletedRealAt TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcBuildRuns_Root
                ON NpcBuildRuns(RootNpcId, StartedRealAt);

            CREATE TABLE IF NOT EXISTS NpcBuildRunMembers
            (
                RunId TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                DisplayName TEXT NOT NULL DEFAULT '',
                FamilyRole TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Pending',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (RunId, NpcId)
            );

            CREATE TABLE IF NOT EXISTS NpcBuildPhaseRuns
            (
                RunId TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                PhaseNumber INTEGER NOT NULL,
                PhaseName TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Pending',
                StartedRealAt TEXT NOT NULL DEFAULT '',
                CompletedRealAt TEXT NOT NULL DEFAULT '',
                LastMessage TEXT NOT NULL DEFAULT '',
                ErrorText TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (RunId, NpcId, PhaseNumber)
            );

            CREATE INDEX IF NOT EXISTS IX_NpcBuildPhaseRuns_Status
                ON NpcBuildPhaseRuns(RunId, Status, PhaseNumber);

            CREATE TABLE IF NOT EXISTS NpcBuildRepairItems
            (
                RepairId TEXT PRIMARY KEY,
                RunId TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                PhaseNumber INTEGER NOT NULL,
                PhaseName TEXT NOT NULL DEFAULT '',
                ErrorText TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Open',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (RunId, NpcId, PhaseNumber)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static readonly (int Number, string Name)[] StandardPhases =
    {
        (1, "Foundation"),
        (2, "Traits"),
        (3, "Photos"),
        (4, "AI Summary"),
        (5, "Personal History")
    };
}

public sealed class FamilyBuildProgressSnapshot
{
    public string RunId { get; set; } = "";
    public int NpcId { get; set; }
    public string NpcName { get; set; } = "";
    public int Age { get; set; }
    public string FamilyRole { get; set; } = "";
    public string Step { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Remaining { get; set; }
    public DateTimeOffset UpdatedRealAt { get; set; }
}

public sealed class FamilyBuildMemberPlan
{
    public int NpcId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class FamilyBuildRunSnapshot
{
    public string RunId { get; set; } = "";
    public int RootNpcId { get; set; }
    public string Status { get; set; } = "";
    public string StartedRealAt { get; set; } = "";
    public string CompletedRealAt { get; set; } = "";
    public List<FamilyBuildMemberSnapshot> Members { get; } = new();
    public List<FamilyBuildPhaseSnapshot> Phases { get; } = new();
    public List<FamilyBuildRepairSnapshot> Repairs { get; } = new();
}

public sealed class FamilyBuildMemberSnapshot
{
    public int NpcId { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Role { get; set; } = "";
    public int SortOrder { get; set; }
    public string Status { get; set; } = "";
}

public sealed class FamilyBuildPhaseSnapshot
{
    public int NpcId { get; set; }
    public int PhaseNumber { get; set; }
    public string PhaseName { get; set; } = "";
    public string Status { get; set; } = "";
    public string StartedRealAt { get; set; } = "";
    public string CompletedRealAt { get; set; } = "";
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
}

public sealed class FamilyBuildRepairSnapshot
{
    public string RepairId { get; set; } = "";
    public int NpcId { get; set; }
    public int PhaseNumber { get; set; }
    public string PhaseName { get; set; } = "";
    public string Error { get; set; } = "";
    public string Status { get; set; } = "";
}
