using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Characters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{    

    /// <summary>
    /// HUMAN EVENT SCHEDULER
    /// =====================
    ///
    /// This class controls WHEN NPCs get a deep HumanEvent pass.
    ///
    /// It does NOT decide behavior itself.
    ///
    /// Pipeline:
    ///
    ///     game clock advances
    ///         ->
    ///     HumanEventScheduler.RunDueUpdates(...)
    ///         ->
    ///     load due NPC
    ///         ->
    ///     build real world context
    ///         ->
    ///     HumanEventEngine.Decide(...)
    ///         ->
    ///     HumanEventConsequenceRouter.Route(...)
    ///         ->
    ///     ProjectEveHumanEventHooks
    ///
    /// Tier cadence:
    ///     Tier 1 = every 30 game minutes
    ///     Tier 2 = every 60 game minutes
    ///     Tier 3 = every 120 game minutes
    ///     Tier 4 = every 180-300 game minutes
    ///     Tier 5 = no routine deep pass
    ///
    /// IMPORTANT:
    /// Tier 1-4 have the SAME full NPC data.
    /// Tier controls routine deliberation frequency only.
    ///
    /// Major events bypass the cadence through NpcDeepUpdateRequest.
    /// </summary>
    public static class HumanEventScheduler
    {
        private static readonly object Sync = new();

        private static SchedulerConfig? _config;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string ConfigPath =>
            Environment.GetEnvironmentVariable("EVE_HUMAN_EVENT_SCHEDULER")
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "World",
                "Ohio",
                "human_event_scheduler.json");

        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        /// <summary>
        /// Called once during world boot after DatabaseInitializer.
        /// Creates scheduler state tables and loads JSON cadence.
        /// </summary>
        public static void Initialize()
        {
            lock (Sync)
            {
                LoadConfig();
                EnsureTables();
            }
        }

        public static void ReloadConfig()
        {
            lock (Sync)
            {
                _config = null;
                LoadConfig();
            }
        }

        /// <summary>
        /// Register every existing full NPC from Project Eve's Characters table.
        ///
        /// Tier 1-4 are scheduled.
        /// Tier 5 is deliberately ignored.
        ///
        /// Safe to call at boot; existing scheduler rows are preserved.
        /// </summary>
        public static int RegisterExistingFullPopulation(
            DateTime gameTime,
            Random? rng = null)
        {
            Initialize();
            rng ??= Random.Shared;

            var people = new List<(int NpcId, int Tier)>();

            using (var conn = new SqliteConnection(ConnStr))
            {
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, Tier
                    FROM Characters
                    WHERE Tier BETWEEN 1 AND 4
                    ORDER BY Id;
                    """;

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    people.Add((
                        r.GetInt32(0),
                        r.IsDBNull(1) ? 4 : ClampTier(r.GetInt32(1))));
                }
            }

            foreach (var person in people)
                RegisterNpc(person.NpcId, person.Tier, gameTime, rng);

            return people.Count;
        }

        /// <summary>
        /// Ensures an NPC has a scheduler row.
        ///
        /// Call this when:
        /// - an NPC is first materialized
        /// - Tier 5 is promoted into Tier 1-4
        /// - population bake finishes
        ///
        /// The first due time may be randomized so 200 NPCs do not all think
        /// at exactly the same minute.
        /// </summary>
        public static void RegisterNpc(
            int npcId,
            int tier,
            DateTime gameTime,
            Random? rng = null)
        {
            Initialize();

            if (tier >= 5)
            {
                RemoveNpc(npcId);
                return;
            }

            rng ??= Random.Shared;

            DateTime nextDue = ComputeNextDue(
                tier,
                gameTime,
                rng,
                firstRegistration: true);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventSchedule
                (NpcId, Tier, LastDeepUpdateGameTime, NextDeepUpdateGameTime,
                 LastEventId, LastDecisionHadEvent, Enabled)
                VALUES
                ($npc, $tier, NULL, $next, NULL, 0, 1)
                ON CONFLICT(NpcId)
                DO UPDATE SET
                    Tier=excluded.Tier,
                    NextDeepUpdateGameTime=CASE
                        WHEN HumanEventSchedule.Enabled=0
                            THEN excluded.NextDeepUpdateGameTime
                        ELSE HumanEventSchedule.NextDeepUpdateGameTime
                    END,
                    Enabled=1;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$next", nextDue.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Update the scheduler's directional simulation priority for this NPC.
        /// This does not change NPC data depth.
        /// </summary>
        public static void SetTier(
            int npcId,
            int tier,
            DateTime gameTime,
            Random? rng = null)
        {
            Initialize();

            if (tier >= 5)
            {
                RemoveNpc(npcId);
                return;
            }

            rng ??= Random.Shared;
            DateTime next = ComputeNextDue(tier, gameTime, rng, firstRegistration: false);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventSchedule
                (NpcId, Tier, NextDeepUpdateGameTime, LastDecisionHadEvent, Enabled)
                VALUES ($npc, $tier, $next, 0, 1)
                ON CONFLICT(NpcId)
                DO UPDATE SET
                    Tier=$tier,
                    NextDeepUpdateGameTime=$next,
                    Enabled=1;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$next", next.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public static void RemoveNpc(int npcId)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM HumanEventSchedule
                WHERE NpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Main scheduler entry point.
        ///
        /// The game clock can call this frequently. The method only processes NPCs
        /// whose NextDeepUpdateGameTime <= gameTime.
        ///
        /// worldFactsBuilder is the critical bridge to the actual world state.
        ///
        /// It should add facts such as:
        ///     in_store
        ///     active_conflict
        ///     merchandise_accessible
        ///     romantic_opportunity
        ///     childcare_problem
        ///     patient_present
        ///
        /// The scheduler itself does NOT invent those facts.
        /// </summary>
        public static SchedulerPassResult RunDueUpdates(
            DateTime gameTime,
            ProjectEveHumanEventHooks hooks,
            Func<SimCharacter, HumanEventEngine.HumanEventContext, HumanEventEngine.HumanEventContext>? worldFactsBuilder = null,
            Func<SimCharacter, SimCharacter?>? targetResolver = null,
            Random? rng = null)
        {
            Initialize();
            rng ??= Random.Shared;

            var result = new SchedulerPassResult
            {
                GameTime = gameTime
            };

            // First process major-event immediate requests.
            ProcessImmediateDeepUpdates(
                gameTime,
                hooks,
                worldFactsBuilder,
                targetResolver,
                rng,
                result);

            int max = Math.Max(
                1,
                _config!.Processing.MaximumNpcUpdatesPerSchedulerPass);

            var due = GetDueNpcRows(gameTime, max);

            foreach (var row in due)
            {
                SimCharacter? npc = TryLoadCharacter(row.NpcId);

                if (npc == null)
                {
                    result.FailedLoads++;

                    if (_config.Processing.SkipNpcIfCharacterCannotLoad)
                    {
                        ScheduleAfterFailure(
                            row.NpcId,
                            row.Tier,
                            gameTime,
                            rng,
                            "character_load_failed");

                        continue;
                    }

                    continue;
                }

                var target = targetResolver?.Invoke(npc);

                HumanEventEngine.HumanEventContext ctx =
                    HumanEventWorldBridge.CreateContext(
                        npc,
                        gameTime,
                        row.Tier,
                        target,
                        npc.Location);

                // World/activity system adds real opportunities/state.
                if (worldFactsBuilder != null)
                    ctx = worldFactsBuilder(npc, ctx) ?? ctx;

                var decision = HumanEventEngine.Decide(
                    npc,
                    ctx,
                    rng);

                result.NpcsProcessed++;

                if (decision.HasEvent)
                {
                    var consequenceContext =
                        BuildConsequenceContext(
                            npc,
                            target,
                            decision,
                            ctx);

                    var consequenceResult =
                        HumanEventConsequenceRouter.Route(
                            npc,
                            decision,
                            gameTime,
                            consequenceContext,
                            hooks);

                    result.EventsChosen++;

                    if (consequenceResult.Success)
                        result.EventsRouted++;

                    if (decision.ForceDeepUpdate)
                        result.MajorEvents++;
                }
                else
                {
                    result.NoEventDecisions++;
                }

                SaveScheduleResult(
                    row.NpcId,
                    row.Tier,
                    gameTime,
                    decision,
                    rng);

                if (_config.Debug.WriteSchedulerAudit)
                {
                    WriteAudit(
                        row.NpcId,
                        row.Tier,
                        gameTime,
                        decision,
                        "routine");
                }
            }

            return result;
        }

        /// <summary>
        /// Convenience overload for when the caller already has a context provider.
        /// </summary>
        public static SchedulerPassResult RunDueUpdates(
            DateTime gameTime,
            ProjectEveHumanEventHooks hooks,
            Func<SimCharacter, int, DateTime, HumanEventEngine.HumanEventContext> contextProvider,
            Random? rng = null)
        {
            Initialize();
            rng ??= Random.Shared;

            var result = new SchedulerPassResult
            {
                GameTime = gameTime
            };

            ProcessImmediateDeepUpdatesWithProvider(
                gameTime,
                hooks,
                contextProvider,
                rng,
                result);

            int max = Math.Max(
                1,
                _config!.Processing.MaximumNpcUpdatesPerSchedulerPass);

            var due = GetDueNpcRows(gameTime, max);

            foreach (var row in due)
            {
                SimCharacter? npc = TryLoadCharacter(row.NpcId);

                if (npc == null)
                {
                    result.FailedLoads++;
                    ScheduleAfterFailure(
                        row.NpcId,
                        row.Tier,
                        gameTime,
                        rng,
                        "character_load_failed");
                    continue;
                }

                HumanEventEngine.HumanEventContext ctx =
                    contextProvider(npc, row.Tier, gameTime);

                var decision = HumanEventEngine.Decide(
                    npc,
                    ctx,
                    rng);

                result.NpcsProcessed++;

                if (decision.HasEvent)
                {
                    var consequenceContext =
                        BuildConsequenceContext(
                            npc,
                            ctx.Target,
                            decision,
                            ctx);

                    var consequenceResult =
                        HumanEventConsequenceRouter.Route(
                            npc,
                            decision,
                            gameTime,
                            consequenceContext,
                            hooks);

                    result.EventsChosen++;

                    if (consequenceResult.Success)
                        result.EventsRouted++;

                    if (decision.ForceDeepUpdate)
                        result.MajorEvents++;
                }
                else
                {
                    result.NoEventDecisions++;
                }

                SaveScheduleResult(
                    row.NpcId,
                    row.Tier,
                    gameTime,
                    decision,
                    rng);

                if (_config.Debug.WriteSchedulerAudit)
                    WriteAudit(row.NpcId, row.Tier, gameTime, decision, "routine");
            }

            return result;
        }

        // ============================================================
        // IMMEDIATE / MAJOR-EVENT PASSES
        // ============================================================

        private static void ProcessImmediateDeepUpdates(
            DateTime gameTime,
            ProjectEveHumanEventHooks hooks,
            Func<SimCharacter, HumanEventEngine.HumanEventContext, HumanEventEngine.HumanEventContext>? worldFactsBuilder,
            Func<SimCharacter, SimCharacter?>? targetResolver,
            Random rng,
            SchedulerPassResult result)
        {
            int max = Math.Max(
                1,
                _config!.Processing.MaximumImmediateDeepUpdatesPerPass);

            var requests =
                ProjectEveHumanEventHooks.GetPendingDeepUpdates(max);

            foreach (var req in requests)
            {
                SimCharacter? npc = TryLoadCharacter(req.NpcId);

                if (npc == null)
                {
                    result.FailedLoads++;
                    continue;
                }

                int tier = GetRegisteredTier(req.NpcId) ?? ClampTier(npc.Tier);

                var target = targetResolver?.Invoke(npc);

                var ctx =
                    HumanEventWorldBridge.CreateContext(
                        npc,
                        gameTime,
                        tier,
                        target,
                        npc.Location);

                ctx.Tags.Add("major_event_followup");
                ctx.Tags.Add($"trigger_{req.EventId}");

                if (worldFactsBuilder != null)
                    ctx = worldFactsBuilder(npc, ctx) ?? ctx;

                var decision =
                    HumanEventEngine.Decide(
                        npc,
                        ctx,
                        rng);

                result.ImmediateDeepUpdates++;

                if (decision.HasEvent)
                {
                    var consequenceContext =
                        BuildConsequenceContext(
                            npc,
                            target,
                            decision,
                            ctx);

                    HumanEventConsequenceRouter.Route(
                        npc,
                        decision,
                        gameTime,
                        consequenceContext,
                        hooks);

                    result.EventsChosen++;
                    result.EventsRouted++;
                }
                else
                {
                    result.NoEventDecisions++;
                }

                SaveScheduleResult(
                    npc.Id,
                    tier,
                    gameTime,
                    decision,
                    rng);

                ProjectEveHumanEventHooks.MarkDeepUpdateHandled(req.Id);

                if (_config.Debug.WriteSchedulerAudit)
                    WriteAudit(npc.Id, tier, gameTime, decision, "immediate");
            }
        }

        private static void ProcessImmediateDeepUpdatesWithProvider(
            DateTime gameTime,
            ProjectEveHumanEventHooks hooks,
            Func<SimCharacter, int, DateTime, HumanEventEngine.HumanEventContext> contextProvider,
            Random rng,
            SchedulerPassResult result)
        {
            int max = Math.Max(
                1,
                _config!.Processing.MaximumImmediateDeepUpdatesPerPass);

            var requests =
                ProjectEveHumanEventHooks.GetPendingDeepUpdates(max);

            foreach (var req in requests)
            {
                SimCharacter? npc = TryLoadCharacter(req.NpcId);

                if (npc == null)
                {
                    result.FailedLoads++;
                    continue;
                }

                int tier = GetRegisteredTier(req.NpcId) ?? ClampTier(npc.Tier);

                var ctx = contextProvider(
                    npc,
                    tier,
                    gameTime);

                ctx.Tags.Add("major_event_followup");
                ctx.Tags.Add($"trigger_{req.EventId}");

                var decision =
                    HumanEventEngine.Decide(
                        npc,
                        ctx,
                        rng);

                result.ImmediateDeepUpdates++;

                if (decision.HasEvent)
                {
                    var consequenceContext =
                        BuildConsequenceContext(
                            npc,
                            ctx.Target,
                            decision,
                            ctx);

                    HumanEventConsequenceRouter.Route(
                        npc,
                        decision,
                        gameTime,
                        consequenceContext,
                        hooks);

                    result.EventsChosen++;
                    result.EventsRouted++;
                }
                else
                {
                    result.NoEventDecisions++;
                }

                SaveScheduleResult(
                    npc.Id,
                    tier,
                    gameTime,
                    decision,
                    rng);

                ProjectEveHumanEventHooks.MarkDeepUpdateHandled(req.Id);

                if (_config.Debug.WriteSchedulerAudit)
                    WriteAudit(npc.Id, tier, gameTime, decision, "immediate");
            }
        }

        // ============================================================
        // SCHEDULE STATE
        // ============================================================

        private static List<ScheduleRow> GetDueNpcRows(
            DateTime gameTime,
            int limit)
        {
            var list = new List<ScheduleRow>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT NpcId, Tier, NextDeepUpdateGameTime
                FROM HumanEventSchedule
                WHERE Enabled=1
                  AND Tier BETWEEN 1 AND 4
                  AND NextDeepUpdateGameTime <= $time
                ORDER BY NextDeepUpdateGameTime, NpcId
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new ScheduleRow
                {
                    NpcId = r.GetInt32(0),
                    Tier = r.GetInt32(1),
                    NextDue = DateTime.TryParse(
                        r.GetString(2),
                        out var dt)
                        ? dt
                        : gameTime
                });
            }

            return list;
        }

        private static void SaveScheduleResult(
            int npcId,
            int tier,
            DateTime gameTime,
            HumanEventEngine.HumanEventDecision decision,
            Random rng)
        {
            DateTime next =
                ComputeNextDue(
                    tier,
                    gameTime,
                    rng,
                    firstRegistration: false);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventSchedule
                (NpcId, Tier, LastDeepUpdateGameTime, NextDeepUpdateGameTime,
                 LastEventId, LastDecisionHadEvent, Enabled)
                VALUES
                ($npc, $tier, $last, $next, $event, $had, 1)
                ON CONFLICT(NpcId)
                DO UPDATE SET
                    Tier=$tier,
                    LastDeepUpdateGameTime=$last,
                    NextDeepUpdateGameTime=$next,
                    LastEventId=$event,
                    LastDecisionHadEvent=$had,
                    Enabled=1;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$last", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$next", next.ToString("o"));
            cmd.Parameters.AddWithValue(
                "$event",
                decision.HasEvent
                    ? decision.EventId
                    : "");
            cmd.Parameters.AddWithValue(
                "$had",
                decision.HasEvent ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        private static void ScheduleAfterFailure(
            int npcId,
            int tier,
            DateTime gameTime,
            Random rng,
            string reason)
        {
            DateTime next =
                ComputeNextDue(
                    tier,
                    gameTime,
                    rng,
                    firstRegistration: false);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE HumanEventSchedule
                SET NextDeepUpdateGameTime=$next
                WHERE NpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$next", next.ToString("o"));
            cmd.ExecuteNonQuery();

            WriteRawAudit(
                npcId,
                tier,
                gameTime,
                "",
                false,
                reason,
                "failure");
        }

        private static DateTime ComputeNextDue(
            int tier,
            DateTime gameTime,
            Random rng,
            bool firstRegistration)
        {
            var cadence = GetTierCadence(tier);

            int min = Math.Max(1, cadence.MinimumGameMinutes);
            int max = Math.Max(min, cadence.MaximumGameMinutes);

            int minutes;

            if (firstRegistration &&
                _config!.Processing.RandomizeFirstDueTime)
            {
                minutes = rng.Next(1, max + 1);
            }
            else if (min == max)
            {
                minutes = min;
            }
            else
            {
                minutes = rng.Next(min, max + 1);
            }

            return gameTime.AddMinutes(minutes);
        }

        private static TierCadence GetTierCadence(int tier)
        {
            return tier switch
            {
                1 => _config!.TierCadence.Tier1,
                2 => _config!.TierCadence.Tier2,
                3 => _config!.TierCadence.Tier3,
                4 => _config!.TierCadence.Tier4,
                _ => _config!.TierCadence.Tier4
            };
        }

        private static int? GetRegisteredTier(int npcId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Tier
                FROM HumanEventSchedule
                WHERE NpcId=$npc
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);

            object? value = cmd.ExecuteScalar();

            if (value == null || value == DBNull.Value)
                return null;

            return Convert.ToInt32(value);
        }

        // ============================================================
        // CHARACTER LOADING / CONTEXT
        // ============================================================

        private static SimCharacter? TryLoadCharacter(int npcId)
        {
            try
            {
                // CharacterFactory.LoadCharacter already restores the existing
                // Project Eve character systems for full NPCs.
                return CharacterFactory.LoadCharacter(npcId);
            }
            catch
            {
                return null;
            }
        }

        private static HumanEventConsequenceRouter.HumanEventConsequenceContext
            BuildConsequenceContext(
                SimCharacter actor,
                SimCharacter? target,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventEngine.HumanEventContext eventContext)
        {
            var ctx =
                new HumanEventConsequenceRouter.HumanEventConsequenceContext
                {
                    TargetIsAware = target != null,
                    TargetName = target?.Name,
                    LocationId =
                        decision.LocationId
                        ?? eventContext.LocationId
                        ?? actor.Location
                };

            // Copy useful world tags into consequence facts.
            // This keeps one event trace understandable after the decision.
            foreach (string tag in eventContext.Tags)
                ctx.Facts[$"tag:{tag}"] = "true";

            return ctx;
        }

        // ============================================================
        // DEBUG / AUDIT
        // ============================================================

        private static void WriteAudit(
            int npcId,
            int tier,
            DateTime gameTime,
            HumanEventEngine.HumanEventDecision decision,
            string passType)
        {
            WriteRawAudit(
                npcId,
                tier,
                gameTime,
                decision.EventId,
                decision.HasEvent,
                _config!.Debug.StoreTopDecisionReason
                    ? decision.Reason
                    : "",
                passType);
        }

        private static void WriteRawAudit(
            int npcId,
            int tier,
            DateTime gameTime,
            string eventId,
            bool hadEvent,
            string reason,
            string passType)
        {
            if (!_config!.Debug.WriteSchedulerAudit)
                return;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventSchedulerAudit
                (NpcId, Tier, GameTime, EventId,
                 HadEvent, Reason, PassType)
                VALUES
                ($npc, $tier, $time, $event,
                 $had, $reason, $pass);
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$event", eventId ?? "");
            cmd.Parameters.AddWithValue("$had", hadEvent ? 1 : 0);
            cmd.Parameters.AddWithValue("$reason", reason ?? "");
            cmd.Parameters.AddWithValue("$pass", passType ?? "");
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // CONFIG / DB
        // ============================================================

        private static void LoadConfig()
        {
            if (_config != null)
                return;

            if (!File.Exists(ConfigPath))
                throw new FileNotFoundException(
                    "Human event scheduler config not found.",
                    ConfigPath);

            _config =
                JsonSerializer.Deserialize<SchedulerConfig>(
                    File.ReadAllText(ConfigPath),
                    JsonOpts)
                ?? throw new InvalidDataException(
                    "Could not deserialize human_event_scheduler.json");
        }

        private static void EnsureTables()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS HumanEventSchedule (
                    NpcId INTEGER PRIMARY KEY,
                    Tier INTEGER NOT NULL,
                    LastDeepUpdateGameTime TEXT,
                    NextDeepUpdateGameTime TEXT NOT NULL,
                    LastEventId TEXT,
                    LastDecisionHadEvent INTEGER NOT NULL DEFAULT 0,
                    Enabled INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_human_event_schedule_due
                    ON HumanEventSchedule(
                        Enabled,
                        Tier,
                        NextDeepUpdateGameTime);

                CREATE TABLE IF NOT EXISTS HumanEventSchedulerAudit (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    Tier INTEGER NOT NULL,
                    GameTime TEXT NOT NULL,
                    EventId TEXT,
                    HadEvent INTEGER NOT NULL,
                    Reason TEXT,
                    PassType TEXT,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_human_scheduler_audit_npc_time
                    ON HumanEventSchedulerAudit(NpcId, GameTime);
                """;
            cmd.ExecuteNonQuery();
        }

        private static int ClampTier(int tier)
        {
            if (tier < 1)
                return 1;
            if (tier > 4)
                return 4;
            return tier;
        }

        // ============================================================
        // PUBLIC HELPERS
        // ============================================================

        public static DateTime? GetNextDueTime(int npcId)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT NextDeepUpdateGameTime
                FROM HumanEventSchedule
                WHERE NpcId=$npc
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);

            object? value = cmd.ExecuteScalar();

            if (value == null || value == DBNull.Value)
                return null;

            return DateTime.TryParse(
                Convert.ToString(value),
                out var dt)
                ? dt
                : null;
        }

        public static int CountRegisteredTier(int tier)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM HumanEventSchedule
                WHERE Tier=$tier AND Enabled=1;
                """;
            cmd.Parameters.AddWithValue("$tier", tier);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ============================================================
        // MODELS
        // ============================================================

        private sealed class ScheduleRow
        {
            public int NpcId { get; set; }
            public int Tier { get; set; }
            public DateTime NextDue { get; set; }
        }

        public sealed class SchedulerPassResult
        {
            public DateTime GameTime { get; set; }

            public int NpcsProcessed { get; set; }
            public int ImmediateDeepUpdates { get; set; }
            public int EventsChosen { get; set; }
            public int EventsRouted { get; set; }
            public int NoEventDecisions { get; set; }
            public int MajorEvents { get; set; }
            public int FailedLoads { get; set; }

            public override string ToString()
            {
                return
                    $"HumanEventScheduler {GameTime:g}: " +
                    $"processed {NpcsProcessed}, " +
                    $"immediate {ImmediateDeepUpdates}, " +
                    $"events {EventsChosen}, " +
                    $"no-event {NoEventDecisions}, " +
                    $"failed loads {FailedLoads}";
            }
        }

        public sealed class SchedulerConfig
        {
            public TierCadenceConfig TierCadence { get; set; } = new();
            public ProcessingConfig Processing { get; set; } = new();
            public EventSelectionConfig EventSelection { get; set; } = new();
            public DebugConfig Debug { get; set; } = new();
        }

        public sealed class TierCadenceConfig
        {
            public TierCadence Tier1 { get; set; } =
                new() { MinimumGameMinutes = 30, MaximumGameMinutes = 30 };

            public TierCadence Tier2 { get; set; } =
                new() { MinimumGameMinutes = 60, MaximumGameMinutes = 60 };

            public TierCadence Tier3 { get; set; } =
                new() { MinimumGameMinutes = 120, MaximumGameMinutes = 120 };

            public TierCadence Tier4 { get; set; } =
                new() { MinimumGameMinutes = 180, MaximumGameMinutes = 300 };

            public Tier5Cadence Tier5 { get; set; } =
                new() { Enabled = false };
        }

        public sealed class TierCadence
        {
            public int MinimumGameMinutes { get; set; }
            public int MaximumGameMinutes { get; set; }
        }

        public sealed class Tier5Cadence
        {
            public bool Enabled { get; set; }
        }

        public sealed class ProcessingConfig
        {
            public int MaximumNpcUpdatesPerSchedulerPass { get; set; } = 50;
            public int MaximumImmediateDeepUpdatesPerPass { get; set; } = 50;
            public int MinimumRealMillisecondsBetweenSchedulerCalls { get; set; }
            public bool RandomizeFirstDueTime { get; set; } = true;
            public bool SkipNpcIfCharacterCannotLoad { get; set; } = true;
            public bool RecordNoEventDecision { get; set; } = true;
        }

        public sealed class EventSelectionConfig
        {
            public bool AllowNoEvent { get; set; } = true;
            public bool OnePrimaryEventPerDeepPass { get; set; } = true;
            public bool MajorEventCanRequestImmediateFollowupDeepPass { get; set; } = true;
        }

        public sealed class DebugConfig
        {
            public bool WriteSchedulerAudit { get; set; } = true;
            public bool StoreTopDecisionReason { get; set; } = true;
        }
    }
}
