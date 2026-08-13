using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Relationships;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// HUMAN EVENT CONSEQUENCE ROUTER
    /// ==============================
    ///
    /// HumanEventEngine chooses WHAT an NPC does.
    /// HumanEventConsequenceRouter decides WHICH Project Eve systems must react.
    ///
    /// This class is deliberately a ROUTER, not a replacement for:
    ///     SmallTownEmploymentSystem
    ///     Money systems
    ///     Police/crime systems
    ///     Health systems
    ///     Family/Friend Web
    ///     Memory/history systems
    ///
    /// It safely performs simple in-memory relationship changes and records every
    /// consequence request in SQLite. Specialized systems can consume those requests
    /// through hooks now, or through the persistent queue later.
    ///
    /// IMPORTANT:
    /// The router NEVER invents missing facts.
    /// If an event needs an amount, medical outcome, legal charge, pregnancy detail,
    /// etc., the owning system/world must supply it.
    /// </summary>
    public static class HumanEventConsequenceRouter
    {
        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static ConsequenceRulesRoot? _rules;

        public static string RulesPath =>
            Environment.GetEnvironmentVariable("EVE_HUMAN_EVENT_CONSEQUENCES")
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "World",
                "Ohio",
                "human_event_consequences.json");

        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            lock (Sync)
            {
                LoadRules();
                EnsureTables();
            }
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _rules = null;
                Initialize();
            }
        }

        /// <summary>
        /// Apply/route consequences for one event that ACTUALLY HAPPENED.
        ///
        /// context carries facts that the event engine intentionally should not invent:
        /// money amount, target awareness, injury severity, legal details, item value,
        /// secret ID, etc.
        ///
        /// hooks lets existing/future Project Eve systems receive the consequence
        /// immediately without this router knowing their internal implementation.
        /// </summary>
        public static HumanEventConsequenceResult Route(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime,
            HumanEventConsequenceContext? context = null,
            IHumanEventConsequenceHooks? hooks = null)
        {
            Initialize();

            context ??= new HumanEventConsequenceContext();
            hooks ??= NullHooks.Instance;

            var result = new HumanEventConsequenceResult
            {
                ActorNpcId = actor.Id,
                TargetNpcId = decision.TargetNpcId,
                EventId = decision.EventId,
                Domain = decision.Domain,
                GameTime = gameTime,
                ForceDeepUpdate = decision.ForceDeepUpdate
            };

            if (!decision.HasEvent)
            {
                result.Success = false;
                result.Messages.Add("Decision contains no event.");
                return result;
            }

            var domain = ResolveDomainRule(decision.Domain);
            var ov = ResolveEventOverride(decision.EventId);

            string severity = !string.IsNullOrWhiteSpace(decision.Severity)
                ? decision.Severity
                : "routine";

            int memoryImportance = ov?.MemoryImportance
                ?? domain.MemoryImportance;

            result.ForceDeepUpdate =
                result.ForceDeepUpdate ||
                (ov?.ForceDeepUpdate ?? false) ||
                string.Equals(severity, "major", StringComparison.OrdinalIgnoreCase);

            var routes = new HashSet<string>(
                domain.Routes ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (ov?.Routes != null && ov.Routes.Count > 0)
            {
                // Event-specific routes replace the broad domain set.
                routes.Clear();
                foreach (string route in ov.Routes)
                    routes.Add(route);
            }

            // ------------------------------------------------------------
            // RELATIONSHIP — immediate only when the affected target is aware.
            // Hidden acts are queued/deferred instead.
            // ------------------------------------------------------------
            if (routes.Contains("relationship"))
            {
                bool targetAware = context.TargetIsAware;

                if (ov?.TargetMustBeAware == true && !targetAware)
                {
                    result.Messages.Add("Relationship change deferred: target is not aware.");
                }
                else
                {
                    var rel = FindRelationship(actor, decision.TargetNpcId);

                    if (rel != null)
                    {
                        if (ov?.RelationshipDelta != null)
                        {
                            ApplyRelationshipDelta(rel, ov.RelationshipDelta);
                            result.AppliedRelationshipChange = true;
                            result.Messages.Add($"Relationship updated for {rel.TargetName}.");

                            hooks.OnRelationshipChanged(
                                actor,
                                rel,
                                decision,
                                gameTime);

                            Queue(
                                actor.Id,
                                decision.TargetNpcId,
                                decision.EventId,
                                "relationship",
                                "persist_relationship_change",
                                gameTime,
                                context,
                                result);
                        }

                        if (!string.IsNullOrWhiteSpace(ov?.RelationshipTypeSet))
                        {
                            rel.RelationshipType = ov!.RelationshipTypeSet!;
                            result.AppliedRelationshipChange = true;
                            result.Messages.Add($"Relationship type set to '{rel.RelationshipType}'.");

                            hooks.OnRelationshipChanged(
                                actor,
                                rel,
                                decision,
                                gameTime);

                            Queue(
                                actor.Id,
                                decision.TargetNpcId,
                                decision.EventId,
                                "relationship",
                                "persist_relationship_change",
                                gameTime,
                                context,
                                result);
                        }
                    }
                    else if ((ov?.RelationshipDelta != null ||
                              !string.IsNullOrWhiteSpace(ov?.RelationshipTypeSet)) &&
                             decision.TargetNpcId.HasValue)
                    {
                        Queue(
                            actor.Id,
                            decision.TargetNpcId,
                            decision.EventId,
                            "relationship",
                            "relationship_missing_or_not_loaded",
                            gameTime,
                            context,
                            result);

                        result.Messages.Add(
                            "Relationship target not loaded; persistent relationship consequence queued.");
                    }
                }
            }

            // ------------------------------------------------------------
            // SPECIALIZED ROUTES
            // ------------------------------------------------------------
            foreach (string route in routes)
            {
                switch (route.ToLowerInvariant())
                {
                    case "relationship":
                        // handled above
                        break;

                    case "employment":
                        RouteSpecialized(
                            "employment",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnEmploymentEvent(actor, decision, context, gameTime));
                        break;

                    case "money":
                        if (ov?.RequiresAmountFromWorld == true && context.Amount == null)
                        {
                            Queue(
                                actor.Id,
                                decision.TargetNpcId,
                                decision.EventId,
                                "money",
                                "amount_required_from_world",
                                gameTime,
                                context,
                                result);

                            result.Messages.Add(
                                "Money consequence queued because no amount was supplied.");
                        }
                        else
                        {
                            RouteSpecialized(
                                "money",
                                actor,
                                decision,
                                gameTime,
                                context,
                                result,
                                () => hooks.OnMoneyEvent(actor, decision, context, gameTime));
                        }
                        break;

                    case "law":
                        RouteSpecialized(
                            "law",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnLawEvent(actor, decision, context, gameTime));
                        break;

                    case "health":
                        RouteSpecialized(
                            "health",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnHealthEvent(actor, decision, context, gameTime));
                        break;

                    case "family":
                        RouteSpecialized(
                            "family",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnFamilyEvent(actor, decision, context, gameTime));
                        break;

                    case "gossip":
                        RouteSpecialized(
                            "gossip",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnGossipEvent(actor, decision, context, gameTime));
                        break;

                    case "reputation":
                        RouteSpecialized(
                            "reputation",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnReputationEvent(actor, decision, context, gameTime));
                        break;

                    case "location":
                        RouteSpecialized(
                            "location",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnLocationEvent(actor, decision, context, gameTime));
                        break;

                    case "activity":
                        RouteSpecialized(
                            "activity",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnActivityEvent(actor, decision, context, gameTime));
                        break;

                    case "history":
                        RouteSpecialized(
                            "history",
                            actor,
                            decision,
                            gameTime,
                            context,
                            result,
                            () => hooks.OnHistoryRequested(
                                actor,
                                BuildHistorySummary(actor, decision, context),
                                severity,
                                decision,
                                gameTime));
                        break;

                    case "memory":
                        // Explicit memory route. Generic notable-memory handling
                        // below still deduplicates by route/result flags.
                        break;

                    default:
                        Queue(
                            actor.Id,
                            decision.TargetNpcId,
                            decision.EventId,
                            route,
                            "unknown_route",
                            gameTime,
                            context,
                            result);
                        break;
                }
            }

            // ------------------------------------------------------------
            // DEFERRED CONSEQUENCES
            // Example:
            //     cheat -> partner_damage_if_discovered
            //     lie   -> relationship_damage_if_discovered
            //
            // We DO NOT damage a relationship before the other person knows.
            // ------------------------------------------------------------
            if (ov?.DeferredConsequences != null)
            {
                foreach (string deferred in ov.DeferredConsequences)
                {
                    Queue(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "deferred",
                        deferred,
                        gameTime,
                        context,
                        result);
                }
            }

            // ------------------------------------------------------------
            // MEMORY / HISTORY SIGNALING
            // ------------------------------------------------------------
            if (memoryImportance > 0)
            {
                string summary = BuildMemorySummary(actor, decision, context);

                hooks.OnMemoryRequested(
                    actor,
                    summary,
                    memoryImportance,
                    decision.Domain,
                    decision,
                    gameTime);

                Queue(
                    actor.Id,
                    decision.TargetNpcId,
                    decision.EventId,
                    "memory",
                    $"importance:{memoryImportance}",
                    gameTime,
                    context,
                    result,
                    payloadOverride: summary);

                result.MemoryRequested = true;
            }

            if (result.ForceDeepUpdate)
            {
                hooks.OnDeepUpdateRequested(
                    actor,
                    decision,
                    gameTime);

                Queue(
                    actor.Id,
                    decision.TargetNpcId,
                    decision.EventId,
                    "deep_update",
                    "important_event",
                    gameTime,
                    context,
                    result);

                result.DeepUpdateRequested = true;
            }

            // Record the event only after consequence routing reached this point.
            HumanEventEngine.RecordExecuted(decision, gameTime);

            RecordAudit(actor, decision, gameTime, context, result);

            result.Success = true;
            return result;
        }

        /// <summary>
        /// Resolve one deferred consequence after discovery or another trigger.
        ///
        /// Example:
        ///     cheat happened Monday, hidden.
        ///     spouse discovers it Friday.
        ///     ResolveDeferred(... "partner_damage_if_discovered" ...)
        ///
        /// This keeps secret behavior realistic.
        /// </summary>
        public static bool ResolveDeferred(
            long queueId,
            SimCharacter actor,
            SimCharacter? target,
            DateTime gameTime,
            IHumanEventConsequenceHooks? hooks = null)
        {
            Initialize();
            hooks ??= NullHooks.Instance;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            string route;
            string action;
            string eventId;
            int? targetNpcId;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Route, Action, EventId, TargetNpcId
                    FROM HumanEventConsequenceQueue
                    WHERE Id=$id AND Status='pending'
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$id", queueId);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return false;

                route = r.GetString(0);
                action = r.GetString(1);
                eventId = r.GetString(2);
                targetNpcId = r.IsDBNull(3) ? null : r.GetInt32(3);
            }

            if (!string.Equals(route, "deferred", StringComparison.OrdinalIgnoreCase))
                return false;

            Relationship? rel = target != null
                ? FindRelationship(actor, target.Id)
                : FindRelationship(actor, targetNpcId);

            if (rel != null)
            {
                ApplyDeferredRelationshipConsequence(rel, action);

                hooks.OnRelationshipChanged(
                    actor,
                    rel,
                    new HumanEventEngine.HumanEventDecision
                    {
                        HasEvent = true,
                        EventId = eventId,
                        ActorNpcId = actor.Id,
                        TargetNpcId = rel.TargetId,
                        Domain = "relationship",
                        ForceDeepUpdate = true
                    },
                    gameTime);
            }

            using (var update = conn.CreateCommand())
            {
                update.CommandText = """
                    UPDATE HumanEventConsequenceQueue
                    SET Status='resolved', ResolvedGameTime=$time
                    WHERE Id=$id;
                    """;
                update.Parameters.AddWithValue("$id", queueId);
                update.Parameters.AddWithValue("$time", gameTime.ToString("o"));
                update.ExecuteNonQuery();
            }

            return true;
        }

        /// <summary>
        /// Fetch queued consequences for a future/specialized subsystem.
        /// Examples:
        ///   route = "employment"
        ///   route = "law"
        ///   route = "health"
        ///   route = "gossip"
        /// </summary>
        public static List<QueuedConsequence> GetPending(
            string route,
            int limit = 100)
        {
            Initialize();

            var list = new List<QueuedConsequence>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ActorNpcId, TargetNpcId, EventId, Route, Action,
                       GameTime, PayloadJson, Status
                FROM HumanEventConsequenceQueue
                WHERE Status='pending' AND Route=$route
                ORDER BY Id
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$route", route);
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new QueuedConsequence
                {
                    Id = r.GetInt64(0),
                    ActorNpcId = r.GetInt32(1),
                    TargetNpcId = r.IsDBNull(2) ? null : r.GetInt32(2),
                    EventId = r.GetString(3),
                    Route = r.GetString(4),
                    Action = r.GetString(5),
                    GameTime = DateTime.TryParse(r.GetString(6), out var dt)
                        ? dt
                        : DateTime.MinValue,
                    PayloadJson = r.IsDBNull(7) ? "" : r.GetString(7),
                    Status = r.GetString(8)
                });
            }

            return list;
        }

        public static void MarkResolved(long queueId, DateTime gameTime)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE HumanEventConsequenceQueue
                SET Status='resolved', ResolvedGameTime=$time
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", queueId);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // ROUTING HELPERS
        // ============================================================

        private static void RouteSpecialized(
            string route,
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime,
            HumanEventConsequenceContext context,
            HumanEventConsequenceResult result,
            Action callHook)
        {
            bool handled = false;

            try
            {
                callHook();
                handled = true;
            }
            catch (NotImplementedException)
            {
                handled = false;
            }
            catch (Exception ex)
            {
                result.Messages.Add($"{route} hook error: {ex.Message}");
                handled = false;
            }

            Queue(
                actor.Id,
                decision.TargetNpcId,
                decision.EventId,
                route,
                handled ? "hook_notified" : "pending_specialized_processing",
                gameTime,
                context,
                result);

            result.RoutedSystems.Add(route);
        }

        private static Relationship? FindRelationship(
            SimCharacter actor,
            int? targetNpcId)
        {
            if (!targetNpcId.HasValue || actor.Relationships == null)
                return null;

            return actor.Relationships.FirstOrDefault(r =>
                r.TargetId == targetNpcId.Value);
        }

        private static void ApplyRelationshipDelta(
            Relationship rel,
            RelationshipDelta delta)
        {
            if (delta.Trust != 0)
                rel.AdjustTrust(delta.Trust);

            if (delta.Respect != 0)
                rel.AdjustRespect(delta.Respect);

            if (delta.Affection != 0)
                rel.AdjustAffection(delta.Affection);

            if (delta.Attraction != 0)
                rel.AdjustAttraction(delta.Attraction);

            if (delta.Tension != 0)
                rel.AdjustTension(delta.Tension);
        }

        private static void ApplyDeferredRelationshipConsequence(
            Relationship rel,
            string action)
        {
            switch (action.ToLowerInvariant())
            {
                case "relationship_damage_if_discovered":
                    rel.AdjustTrust(-10);
                    rel.AdjustRespect(-4);
                    rel.AdjustAffection(-4);
                    rel.AdjustTension(8);
                    break;

                case "major_trust_damage_if_discovered":
                    rel.AdjustTrust(-18);
                    rel.AdjustRespect(-7);
                    rel.AdjustAffection(-8);
                    rel.AdjustTension(12);
                    break;

                case "partner_damage_if_discovered":
                    rel.AdjustTrust(-25);
                    rel.AdjustRespect(-12);
                    rel.AdjustAffection(-18);
                    rel.AdjustAttraction(-10);
                    rel.AdjustTension(25);
                    break;

                case "relationship_damage_if_source_discovered":
                    rel.AdjustTrust(-12);
                    rel.AdjustRespect(-6);
                    rel.AdjustAffection(-5);
                    rel.AdjustTension(10);
                    break;

                default:
                    // Unknown deferred consequence remains a no-op here.
                    // A specialized owning system can still resolve it.
                    break;
            }
        }

        private static string BuildMemorySummary(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceContext context)
        {
            string target = !string.IsNullOrWhiteSpace(context.TargetName)
                ? $" involving {context.TargetName}"
                : "";

            string location = !string.IsNullOrWhiteSpace(decision.LocationId)
                ? $" at {decision.LocationId}"
                : "";

            return $"{actor.Name}: {decision.EventId.Replace('_', ' ')}{target}{location}.";
        }

        private static string BuildHistorySummary(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceContext context)
        {
            string target = !string.IsNullOrWhiteSpace(context.TargetName)
                ? $" with {context.TargetName}"
                : "";

            return $"{actor.Name} experienced '{decision.EventId.Replace('_', ' ')}'{target}.";
        }

        // ============================================================
        // PERSISTENCE
        // ============================================================

        private static void EnsureTables()
        {
            string? dir = Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS HumanEventConsequenceQueue (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActorNpcId INTEGER NOT NULL,
                    TargetNpcId INTEGER,
                    EventId TEXT NOT NULL,
                    Route TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    GameTime TEXT NOT NULL,
                    PayloadJson TEXT,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    ResolvedGameTime TEXT,
                    FOREIGN KEY (ActorNpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_human_consequence_route_status
                    ON HumanEventConsequenceQueue(Route, Status, Id);

                CREATE INDEX IF NOT EXISTS ix_human_consequence_actor
                    ON HumanEventConsequenceQueue(ActorNpcId, Id);

                CREATE TABLE IF NOT EXISTS HumanEventConsequenceAudit (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActorNpcId INTEGER NOT NULL,
                    TargetNpcId INTEGER,
                    EventId TEXT NOT NULL,
                    Domain TEXT NOT NULL,
                    GameTime TEXT NOT NULL,
                    ForceDeepUpdate INTEGER NOT NULL,
                    RelationshipChanged INTEGER NOT NULL,
                    MemoryRequested INTEGER NOT NULL,
                    RoutedSystems TEXT,
                    Messages TEXT,
                    FOREIGN KEY (ActorNpcId) REFERENCES Characters(Id)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        private static void Queue(
            int actorNpcId,
            int? targetNpcId,
            string eventId,
            string route,
            string action,
            DateTime gameTime,
            HumanEventConsequenceContext context,
            HumanEventConsequenceResult result,
            string? payloadOverride = null)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            string payload = payloadOverride ??
                JsonSerializer.Serialize(context, JsonOpts);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventConsequenceQueue
                (ActorNpcId, TargetNpcId, EventId, Route, Action, GameTime, PayloadJson, Status)
                VALUES ($actor, $target, $event, $route, $action, $time, $payload, 'pending');
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$actor", actorNpcId);
            cmd.Parameters.AddWithValue("$target", (object?)targetNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$event", eventId);
            cmd.Parameters.AddWithValue("$route", route);
            cmd.Parameters.AddWithValue("$action", action);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$payload", payload);

            long id = Convert.ToInt64(cmd.ExecuteScalar());

            result.QueueIds.Add(id);
        }

        private static void RecordAudit(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime,
            HumanEventConsequenceContext context,
            HumanEventConsequenceResult result)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventConsequenceAudit
                (ActorNpcId, TargetNpcId, EventId, Domain, GameTime,
                 ForceDeepUpdate, RelationshipChanged, MemoryRequested,
                 RoutedSystems, Messages)
                VALUES
                ($actor, $target, $event, $domain, $time,
                 $deep, $rel, $mem, $routes, $messages);
                """;

            cmd.Parameters.AddWithValue("$actor", actor.Id);
            cmd.Parameters.AddWithValue("$target", (object?)decision.TargetNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$event", decision.EventId);
            cmd.Parameters.AddWithValue("$domain", decision.Domain);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$deep", result.ForceDeepUpdate ? 1 : 0);
            cmd.Parameters.AddWithValue("$rel", result.AppliedRelationshipChange ? 1 : 0);
            cmd.Parameters.AddWithValue("$mem", result.MemoryRequested ? 1 : 0);
            cmd.Parameters.AddWithValue("$routes", string.Join(",", result.RoutedSystems));
            cmd.Parameters.AddWithValue("$messages", string.Join(" | ", result.Messages));

            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // RULE LOADING
        // ============================================================

        private static void LoadRules()
        {
            if (_rules != null)
                return;

            if (!File.Exists(RulesPath))
                throw new FileNotFoundException(
                    "Human event consequence rules not found.",
                    RulesPath);

            _rules = JsonSerializer.Deserialize<ConsequenceRulesRoot>(
                File.ReadAllText(RulesPath),
                JsonOpts)
                ?? throw new InvalidDataException(
                    "Could not deserialize human_event_consequences.json");
        }

        private static DomainConsequenceRule ResolveDomainRule(string domain)
        {
            if (_rules!.DomainDefaults.TryGetValue(domain, out var rule))
                return rule;

            return new DomainConsequenceRule();
        }

        private static EventConsequenceOverride? ResolveEventOverride(string eventId)
        {
            return _rules!.EventOverrides.TryGetValue(eventId, out var rule)
                ? rule
                : null;
        }

        // ============================================================
        // HOOK CONTRACT
        // ============================================================

        /// <summary>
        /// Project Eve systems can implement this interface as they are built.
        ///
        /// The consequence router stays stable while systems behind these hooks
        /// become richer over time.
        /// </summary>
        public interface IHumanEventConsequenceHooks
        {
            void OnRelationshipChanged(
                SimCharacter actor,
                Relationship relationship,
                HumanEventEngine.HumanEventDecision decision,
                DateTime gameTime);

            void OnEmploymentEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnMoneyEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnLawEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnHealthEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnFamilyEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnGossipEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnReputationEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnLocationEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnActivityEvent(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                HumanEventConsequenceContext context,
                DateTime gameTime);

            void OnMemoryRequested(
                SimCharacter actor,
                string summary,
                int importance,
                string category,
                HumanEventEngine.HumanEventDecision decision,
                DateTime gameTime);

            void OnHistoryRequested(
                SimCharacter actor,
                string summary,
                string severity,
                HumanEventEngine.HumanEventDecision decision,
                DateTime gameTime);

            void OnDeepUpdateRequested(
                SimCharacter actor,
                HumanEventEngine.HumanEventDecision decision,
                DateTime gameTime);
        }

        private sealed class NullHooks : IHumanEventConsequenceHooks
        {
            public static readonly NullHooks Instance = new();

            public void OnRelationshipChanged(SimCharacter actor, Relationship relationship, HumanEventEngine.HumanEventDecision decision, DateTime gameTime) { }
            public void OnEmploymentEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnMoneyEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnLawEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnHealthEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnFamilyEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnGossipEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnReputationEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnLocationEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnActivityEvent(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, HumanEventConsequenceContext context, DateTime gameTime) => throw new NotImplementedException();
            public void OnMemoryRequested(SimCharacter actor, string summary, int importance, string category, HumanEventEngine.HumanEventDecision decision, DateTime gameTime) { }
            public void OnHistoryRequested(SimCharacter actor, string summary, string severity, HumanEventEngine.HumanEventDecision decision, DateTime gameTime) => throw new NotImplementedException();
            public void OnDeepUpdateRequested(SimCharacter actor, HumanEventEngine.HumanEventDecision decision, DateTime gameTime) { }
        }

        // ============================================================
        // PUBLIC DATA MODELS
        // ============================================================

        public sealed class HumanEventConsequenceContext
        {
            /// <summary>
            /// True only when the affected person knows the action occurred.
            /// Essential for lies, cheating, hidden theft, secret betrayal, etc.
            /// </summary>
            public bool TargetIsAware { get; set; } = true;

            public string? TargetName { get; set; }

            /// <summary>
            /// Money/value involved. Router never invents this.
            /// </summary>
            public decimal? Amount { get; set; }

            public string? ItemId { get; set; }
            public string? ItemDescription { get; set; }

            public string? InjurySeverity { get; set; }
            public string? MedicalFact { get; set; }

            public string? LegalFact { get; set; }
            public string? FamilyFact { get; set; }
            public string? SecretId { get; set; }

            public string? LocationId { get; set; }

            /// <summary>
            /// Flexible facts for systems we have not built yet.
            /// Examples:
            /// witnessNpcIds
            /// attendancePoints
            /// policeCaseId
            /// pregnancyId
            /// rumorId
            /// </summary>
            public Dictionary<string, string> Facts { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class HumanEventConsequenceResult
        {
            public bool Success { get; set; }
            public int ActorNpcId { get; set; }
            public int? TargetNpcId { get; set; }
            public string EventId { get; set; } = "";
            public string Domain { get; set; } = "";
            public DateTime GameTime { get; set; }

            public bool AppliedRelationshipChange { get; set; }
            public bool MemoryRequested { get; set; }
            public bool ForceDeepUpdate { get; set; }
            public bool DeepUpdateRequested { get; set; }

            public HashSet<string> RoutedSystems { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public List<long> QueueIds { get; set; } = new();
            public List<string> Messages { get; set; } = new();
        }

        public sealed class QueuedConsequence
        {
            public long Id { get; set; }
            public int ActorNpcId { get; set; }
            public int? TargetNpcId { get; set; }
            public string EventId { get; set; } = "";
            public string Route { get; set; } = "";
            public string Action { get; set; } = "";
            public DateTime GameTime { get; set; }
            public string PayloadJson { get; set; } = "";
            public string Status { get; set; } = "";
        }

        // ============================================================
        // JSON RULE MODELS
        // ============================================================

        public sealed class ConsequenceRulesRoot
        {
            public Dictionary<string, DomainConsequenceRule> DomainDefaults { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, EventConsequenceOverride> EventOverrides { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class DomainConsequenceRule
        {
            public List<string> Routes { get; set; } = new();
            public int MemoryImportance { get; set; }
        }

        public sealed class EventConsequenceOverride
        {
            public List<string> Routes { get; set; } = new();

            public RelationshipDelta? RelationshipDelta { get; set; }
            public string? RelationshipTypeSet { get; set; }

            public bool? TargetMustBeAware { get; set; }
            public bool? ForceDeepUpdate { get; set; }
            public bool? RequiresAmountFromWorld { get; set; }

            public int? MemoryImportance { get; set; }

            public List<string> DeferredConsequences { get; set; } = new();
        }

        public sealed class RelationshipDelta
        {
            public int Trust { get; set; }
            public int Respect { get; set; }
            public int Affection { get; set; }
            public int Attraction { get; set; }
            public int Tension { get; set; }
        }
    }
}
