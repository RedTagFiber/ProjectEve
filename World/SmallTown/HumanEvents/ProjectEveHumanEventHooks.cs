using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Relationships;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// PROJECT EVE HUMAN EVENT HOOKS
    /// =============================
    ///
    /// Concrete bridge between HumanEventConsequenceRouter and the Project Eve
    /// systems that already exist.
    ///
    /// HumanEventEngine:
    ///     chooses what the NPC does.
    ///
    /// HumanEventConsequenceRouter:
    ///     decides which systems must change.
    ///
    /// ProjectEveHumanEventHooks:
    ///     performs the Project Eve-specific changes.
    ///
    /// IMPORTANT DESIGN RULES
    /// ----------------------
    /// 1. This adapter does NOT decide behavior.
    /// 2. It does NOT invent facts the world has not supplied.
    /// 3. Employment uses SmallTownEmploymentSystem.
    /// 4. Money uses the existing MoneyProfile + CharacterRepository.SaveMoney.
    /// 5. Relationship changes are already applied by the router; this class persists them.
    /// 6. Memory uses SimCharacter.Remember when a compatible overload exists.
    /// 7. Family/Friend Web changes use FamilyFriendWebSystem when the event actually
    ///    changes the structural relationship.
    /// 8. Police, health, reputation, gossip, etc. are recorded as pending hook work
    ///    until their owning Project Eve systems exist.
    /// 9. Tier 1-4 receive identical consequence handling.
    /// </summary>
    public sealed class ProjectEveHumanEventHooks :
        HumanEventConsequenceRouter.IHumanEventConsequenceHooks
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public ProjectEveHumanEventHooks()
        {
            EnsureTables();
        }

        // ============================================================
        // RELATIONSHIPS
        // ============================================================

        public void OnRelationshipChanged(
            SimCharacter actor,
            Relationship relationship,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime)
        {
            if (actor == null || relationship == null)
                return;

            // The router has already changed the live Relationship object.
            // Our job is persistence.
            bool persisted = TryPersistCharacterState(actor);

            if (!persisted)
            {
                QueueHookWork(
                    actor.Id,
                    relationship.TargetId,
                    decision.EventId,
                    "relationship_persist",
                    new
                    {
                        relationship.TargetId,
                        relationship.TargetName,
                        relationship.RelationshipType,
                        relationship.Trust,
                        relationship.Respect,
                        relationship.Affection,
                        relationship.Attraction,
                        relationship.Tension,
                        relationship.Notes
                    },
                    gameTime);
            }

            // Structural relationship changes should also update the Family/Friend Web.
            // Emotional changes such as an argument do NOT change web tier/type by themselves.
            if (IsStructuralRelationshipEvent(decision.EventId) &&
                relationship.TargetId.HasValue)
            {
                int tier = GetExistingWebTier(actor.Id, relationship.TargetId.Value) ?? 2;

                try
                {
                    FamilyFriendWebSystem.Link(
                        actor.Id,
                        relationship.TargetId.Value,
                        tier,
                        relationship.RelationshipType,
                        historyOnly: false,
                        source: "human_event",
                        notes: $"Updated by {decision.EventId}",
                        mirrorIntoRelationships: true);
                }
                catch
                {
                    QueueHookWork(
                        actor.Id,
                        relationship.TargetId,
                        decision.EventId,
                        "family_friend_web_update",
                        new
                        {
                            tier,
                            relationship.RelationshipType
                        },
                        gameTime);
                }
            }
        }

        // ============================================================
        // EMPLOYMENT
        // ============================================================

        public void OnEmploymentEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            string id = Normalize(decision.EventId);

            switch (id)
            {
                case "call_off":
                    SmallTownEmploymentSystem.RecordCallOff(
                        actor,
                        gameTime.Date,
                        ReadFact(context, "reason", "human_event_call_off"),
                        lateNotice: ReadBool(context, "lateNotice"),
                        noCallNoShow: false);
                    return;

                case "no_call_no_show":
                    SmallTownEmploymentSystem.RecordCallOff(
                        actor,
                        gameTime.Date,
                        ReadFact(context, "reason", "no_call_no_show"),
                        lateNotice: true,
                        noCallNoShow: true);
                    return;

                case "get_fired":
                case "fired":
                    SmallTownEmploymentSystem.TerminateEmployment(
                        actor,
                        ReadFact(context, "reason", "fired"));
                    return;

                case "quit":
                case "walk_out":
                    SmallTownEmploymentSystem.TerminateEmployment(
                        actor,
                        ReadFact(context, "reason", id));
                    return;

                case "clock_in":
                case "clock_out":
                case "work_normal_shift":
                    // Presence/schedule facts belong to the world/schedule system.
                    // No employment-record mutation is necessary here.
                    return;

                case "get_promotion":
                case "promotion":
                case "get_raise":
                case "receive_warning":
                case "receive_writeup":
                case "get_demoted":
                case "lose_hours":
                case "cover_shift":
                case "work_overtime":
                    // These need richer employment methods than the current bridge exposes.
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "employment",
                        context,
                        gameTime);
                    return;

                default:
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "employment",
                        context,
                        gameTime);
                    return;
            }
        }

        // ============================================================
        // MONEY
        // ============================================================

        public void OnMoneyEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            if (actor.Money == null)
            {
                QueueHookWork(
                    actor.Id,
                    decision.TargetNpcId,
                    decision.EventId,
                    "money_missing_profile",
                    context,
                    gameTime);
                return;
            }

            decimal? amount = context.Amount;
            string id = Normalize(decision.EventId);

            if (NeedsAmount(id) && !amount.HasValue)
            {
                QueueHookWork(
                    actor.Id,
                    decision.TargetNpcId,
                    decision.EventId,
                    "money_amount_missing",
                    context,
                    gameTime);
                return;
            }

            decimal value = amount ?? 0m;
            decimal cash = GetMoneyNumber(actor.Money, "Cash");
            decimal bank = GetMoneyNumber(actor.Money, "Bank");
            decimal debt = GetMoneyNumber(actor.Money, "Debt");
            bool changed = false;

            switch (id)
            {
                case "receive_paycheck":
                case "find_money":
                case "inherit_money":
                    bank += value;
                    changed = true;
                    break;

                case "pay_bill":
                case "pay_rent":
                case "buy_gift":
                case "buy_luxury":
                case "give_charity":
                case "gamble_money":
                case "lose_money":
                    Spend(ref cash, ref bank, ref debt, value);
                    changed = true;
                    break;

                case "borrow_money":
                    cash += value;
                    debt += value;
                    changed = true;
                    break;

                case "repay_debt":
                    Spend(ref cash, ref bank, ref debt, value);
                    debt = Math.Max(0m, debt - value);
                    changed = true;
                    break;

                case "lend_money":
                    Spend(ref cash, ref bank, ref debt, value);
                    changed = true;
                    break;

                case "steal_money":
                    cash += value;
                    changed = true;
                    break;

                case "overdraft":
                case "miss_bill":
                case "miss_rent":
                case "hide_debt":
                case "bankruptcy_risk":
                case "eviction_risk":
                case "foreclosure_risk":
                case "financial_argument":
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "money_state",
                        context,
                        gameTime);
                    return;

                default:
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "money",
                        context,
                        gameTime);
                    return;
            }

            if (changed)
            {
                bool cashOk = SetMoneyNumber(actor.Money, "Cash", cash);
                bool bankOk = SetMoneyNumber(actor.Money, "Bank", bank);
                bool debtOk = SetMoneyNumber(actor.Money, "Debt", debt);

                if (!cashOk || !bankOk || !debtOk)
                {
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "money_property_mismatch",
                        new { cash, bank, debt },
                        gameTime);
                    return;
                }

                CharacterRepository.SaveMoney(actor);

                QueueAudit(
                    actor.Id,
                    decision.EventId,
                    "money_applied",
                    new
                    {
                        amount = value,
                        cash,
                        bank,
                        debt
                    },
                    gameTime);
            }
        }

        // ============================================================
        // LAW / HEALTH / REPUTATION
        // ============================================================

        public void OnLawEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            // Project Eve does not yet have a finalized police/court system exposed
            // through the current integration. Never fake one here.
            QueueHookWork(
                actor.Id,
                decision.TargetNpcId,
                decision.EventId,
                "law",
                context,
                gameTime);
        }

        public void OnHealthEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            // Same rule: injury/illness consequences need a real health model.
            QueueHookWork(
                actor.Id,
                decision.TargetNpcId,
                decision.EventId,
                "health",
                context,
                gameTime);
        }

        public void OnReputationEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            QueueHookWork(
                actor.Id,
                decision.TargetNpcId,
                decision.EventId,
                "reputation",
                context,
                gameTime);
        }

        // ============================================================
        // FAMILY / GOSSIP
        // ============================================================

        public void OnFamilyEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            string id = Normalize(decision.EventId);

            // Existing Relationship changes are already handled by the router.
            // Structural family changes need dedicated family/household logic.
            switch (id)
            {
                case "marry":
                case "divorce":
                case "pregnancy":
                case "birth":
                case "death":
                case "have_child":
                case "child_leaves_home":
                case "move_back_home":
                case "custody_conflict":
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "family_structure",
                        context,
                        gameTime);
                    break;

                default:
                    // Family visit/argument/support may need no structural DB mutation
                    // beyond relationship + memory.
                    QueueAudit(
                        actor.Id,
                        decision.EventId,
                        "family_event_seen",
                        context,
                        gameTime);
                    break;
            }
        }

        public void OnGossipEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            // The Family/Friend Web gives us the graph, but we have not yet created
            // the telephone-game propagation engine. Queue the exact rumor/secret fact
            // instead of spreading knowledge omnisciently.
            QueueHookWork(
                actor.Id,
                decision.TargetNpcId,
                decision.EventId,
                "gossip",
                context,
                gameTime);
        }

        // ============================================================
        // LOCATION / ACTIVITY
        // ============================================================

        public void OnLocationEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            string? destination = ReadOptionalFact(context, "destinationLocationId")
                                  ?? context.LocationId;

            if (!string.IsNullOrWhiteSpace(destination))
            {
                actor.Location = destination;

                // Character state persistence is repository-version dependent.
                // Try it safely and preserve work if unavailable.
                if (!TryPersistCharacterState(actor))
                {
                    QueueHookWork(
                        actor.Id,
                        decision.TargetNpcId,
                        decision.EventId,
                        "location_persist",
                        new { destination },
                        gameTime);
                }

                return;
            }

            QueueHookWork(
                actor.Id,
                decision.TargetNpcId,
                decision.EventId,
                "location_missing_destination",
                context,
                gameTime);
        }

        public void OnActivityEvent(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            DateTime gameTime)
        {
            // Mundane activity is intentionally cheap.
            // Do not create a memory or DB row for every coffee, shower, queue, etc.
            QueueAudit(
                actor.Id,
                decision.EventId,
                "activity",
                new
                {
                    context.LocationId,
                    decision.TargetNpcId
                },
                gameTime);
        }

        // ============================================================
        // MEMORY / HISTORY
        // ============================================================

        public void OnMemoryRequested(
            SimCharacter actor,
            string summary,
            int importance,
            string category,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime)
        {
            bool remembered = TryRemember(
                actor,
                summary,
                importance,
                category);

            if (!remembered)
            {
                QueueHookWork(
                    actor.Id,
                    decision.TargetNpcId,
                    decision.EventId,
                    "memory",
                    new
                    {
                        summary,
                        importance,
                        category
                    },
                    gameTime);
            }
        }

        public void OnHistoryRequested(
            SimCharacter actor,
            string summary,
            string severity,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime)
        {
            // The current Project Eve HistoryRecord shape has changed across versions.
            // Keep a durable human-event history record without compiling against a
            // constructor/property set that may not match the user's current branch.
            EnsureTables();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventLifeHistory
                (NpcId, EventId, Summary, Severity, GameTime)
                VALUES ($npc, $event, $summary, $severity, $time);
                """;
            cmd.Parameters.AddWithValue("$npc", actor.Id);
            cmd.Parameters.AddWithValue("$event", decision.EventId);
            cmd.Parameters.AddWithValue("$summary", summary ?? "");
            cmd.Parameters.AddWithValue("$severity", severity ?? "");
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // DEEP UPDATE
        // ============================================================

        public void OnDeepUpdateRequested(
            SimCharacter actor,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime)
        {
            EnsureTables();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NpcDeepUpdateRequest
                (NpcId, EventId, RequestedGameTime, Status)
                VALUES ($npc, $event, $time, 'pending');
                """;
            cmd.Parameters.AddWithValue("$npc", actor.Id);
            cmd.Parameters.AddWithValue("$event", decision.EventId);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Scheduler can pull these and immediately run the NPC's next deep pass.
        /// </summary>
        public static List<DeepUpdateRequest> GetPendingDeepUpdates(int limit = 100)
        {
            EnsureTables();

            var result = new List<DeepUpdateRequest>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, NpcId, EventId, RequestedGameTime
                FROM NpcDeepUpdateRequest
                WHERE Status='pending'
                ORDER BY Id
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new DeepUpdateRequest
                {
                    Id = r.GetInt64(0),
                    NpcId = r.GetInt32(1),
                    EventId = r.GetString(2),
                    RequestedGameTime = DateTime.TryParse(r.GetString(3), out var dt)
                        ? dt
                        : DateTime.MinValue
                });
            }

            return result;
        }

        public static void MarkDeepUpdateHandled(long id)
        {
            EnsureTables();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NpcDeepUpdateRequest
                SET Status='handled'
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // PENDING HOOK WORK
        // ============================================================

        /// <summary>
        /// Gives future systems (police, health, gossip, family structure, etc.)
        /// a durable list of work created before those systems existed.
        /// </summary>
        public static List<PendingHookWork> GetPendingHookWork(
            string system,
            int limit = 100)
        {
            EnsureTables();

            var result = new List<PendingHookWork>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ActorNpcId, TargetNpcId, EventId, SystemName,
                       PayloadJson, GameTime
                FROM HumanEventHookPending
                WHERE Status='pending' AND SystemName=$system
                ORDER BY Id
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$system", system);
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new PendingHookWork
                {
                    Id = r.GetInt64(0),
                    ActorNpcId = r.GetInt32(1),
                    TargetNpcId = r.IsDBNull(2) ? null : r.GetInt32(2),
                    EventId = r.GetString(3),
                    SystemName = r.GetString(4),
                    PayloadJson = r.IsDBNull(5) ? "" : r.GetString(5),
                    GameTime = DateTime.TryParse(r.GetString(6), out var dt)
                        ? dt
                        : DateTime.MinValue
                });
            }

            return result;
        }

        public static void MarkHookWorkHandled(long id)
        {
            EnsureTables();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE HumanEventHookPending
                SET Status='handled'
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static string Normalize(string? value)
            => (value ?? "").Trim().ToLowerInvariant();

        private static bool NeedsAmount(string eventId)
        {
            return eventId is
                "receive_paycheck" or
                "pay_bill" or
                "pay_rent" or
                "buy_gift" or
                "buy_luxury" or
                "give_charity" or
                "borrow_money" or
                "repay_debt" or
                "lend_money" or
                "steal_money" or
                "gamble_money" or
                "lose_money" or
                "find_money" or
                "inherit_money";
        }

        private static void Spend(
            ref decimal cash,
            ref decimal bank,
            ref decimal debt,
            decimal amount)
        {
            amount = Math.Max(0m, amount);

            decimal fromCash = Math.Min(cash, amount);
            cash -= fromCash;
            amount -= fromCash;

            if (amount <= 0m)
                return;

            decimal fromBank = Math.Min(bank, amount);
            bank -= fromBank;
            amount -= fromBank;

            if (amount > 0m)
                debt += amount;
        }

        private static decimal GetMoneyNumber(object money, string propertyName)
        {
            try
            {
                PropertyInfo? p = money.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);

                object? value = p?.GetValue(money);
                return value == null
                    ? 0m
                    : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0m;
            }
        }

        private static bool SetMoneyNumber(
            object money,
            string propertyName,
            decimal value)
        {
            try
            {
                PropertyInfo? p = money.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);

                if (p == null || !p.CanWrite)
                    return false;

                Type targetType = Nullable.GetUnderlyingType(p.PropertyType)
                                  ?? p.PropertyType;

                object converted = targetType == typeof(decimal) ? value
                    : targetType == typeof(double) ? (double)value
                    : targetType == typeof(float) ? (float)value
                    : targetType == typeof(long) ? (long)Math.Round(value)
                    : targetType == typeof(int) ? (int)Math.Round(value)
                    : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

                p.SetValue(money, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadFact(
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            string key,
            string fallback)
        {
            return context.Facts.TryGetValue(key, out string? value)
                   && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private static string? ReadOptionalFact(
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            string key)
        {
            return context.Facts.TryGetValue(key, out string? value)
                   && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static bool ReadBool(
            HumanEventConsequenceRouter.HumanEventConsequenceContext context,
            string key)
        {
            if (!context.Facts.TryGetValue(key, out string? value))
                return false;

            return bool.TryParse(value, out bool b) && b;
        }

        private static bool IsStructuralRelationshipEvent(string eventId)
        {
            string id = Normalize(eventId);

            return id is
                "relationship_started" or
                "break_up" or
                "marry" or
                "divorce" or
                "reconcile" or
                "friendship_ends" or
                "make_friend";
        }

        private static int? GetExistingWebTier(int ownerNpcId, int targetNpcId)
        {
            try
            {
                var edge = FamilyFriendWebSystem.GetWeb(ownerNpcId)
                    .FirstOrDefault(x => x.TargetNpcId == targetNpcId);

                return edge?.WebTier;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// CharacterRepository changed during Project Eve development.
        /// Instead of hard-binding this adapter to one optional persistence method,
        /// try common signatures safely. Core methods such as SaveMoney remain direct.
        /// </summary>
        private static bool TryPersistCharacterState(SimCharacter actor)
        {
            try
            {
                Type t = typeof(CharacterRepository);

                string[] methodNames =
                {
                    "SaveCharacterState",
                    "SaveRelationships",
                    "SaveRelationshipState"
                };

                foreach (string name in methodNames)
                {
                    MethodInfo? m = t
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(x =>
                        {
                            if (!string.Equals(x.Name, name, StringComparison.Ordinal))
                                return false;

                            var p = x.GetParameters();
                            return p.Length == 1 &&
                                   p[0].ParameterType.IsAssignableFrom(typeof(SimCharacter));
                        });

                    if (m != null)
                    {
                        m.Invoke(null, new object[] { actor });
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// SimCharacter.Remember existed before HumanEventEngine, but its overloads
        /// may evolve. This adapter calls a compatible public overload if one exists.
        /// </summary>
        private static bool TryRemember(
            SimCharacter actor,
            string summary,
            int importance,
            string category)
        {
            try
            {
                var methods = actor.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => string.Equals(m.Name, "Remember", StringComparison.Ordinal))
                    .OrderByDescending(m => m.GetParameters().Length)
                    .ToList();

                foreach (MethodInfo m in methods)
                {
                    ParameterInfo[] p = m.GetParameters();

                    // Try to map common Remember signatures by parameter name/type.
                    object?[] args = new object?[p.Length];
                    bool compatible = true;

                    for (int i = 0; i < p.Length; i++)
                    {
                        string pname = p[i].Name?.ToLowerInvariant() ?? "";
                        Type pt = Nullable.GetUnderlyingType(p[i].ParameterType)
                                  ?? p[i].ParameterType;

                        if (pt == typeof(string))
                        {
                            if (pname.Contains("category"))
                                args[i] = category;
                            else if (pname.Contains("summary") ||
                                     pname.Contains("memory") ||
                                     pname.Contains("text") ||
                                     pname.Contains("content"))
                                args[i] = summary;
                            else
                                args[i] = i == 0 ? summary : category;
                        }
                        else if (pt == typeof(int))
                        {
                            args[i] = importance;
                        }
                        else if (pt == typeof(float))
                        {
                            args[i] = (float)importance;
                        }
                        else if (pt == typeof(double))
                        {
                            args[i] = (double)importance;
                        }
                        else if (pt == typeof(DateTime))
                        {
                            args[i] = DateTime.Now;
                        }
                        else if (p[i].HasDefaultValue)
                        {
                            args[i] = p[i].DefaultValue;
                        }
                        else
                        {
                            compatible = false;
                            break;
                        }
                    }

                    if (!compatible)
                        continue;

                    m.Invoke(actor, args);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void QueueHookWork(
            int actorNpcId,
            int? targetNpcId,
            string eventId,
            string system,
            object payload,
            DateTime gameTime)
        {
            EnsureTables();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventHookPending
                (ActorNpcId, TargetNpcId, EventId, SystemName,
                 PayloadJson, GameTime, Status)
                VALUES
                ($actor, $target, $event, $system, $payload, $time, 'pending');
                """;
            cmd.Parameters.AddWithValue("$actor", actorNpcId);
            cmd.Parameters.AddWithValue("$target", (object?)targetNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$event", eventId ?? "");
            cmd.Parameters.AddWithValue("$system", system ?? "");
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(payload, JsonOpts));
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        private static void QueueAudit(
            int actorNpcId,
            string eventId,
            string action,
            object payload,
            DateTime gameTime)
        {
            EnsureTables();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventHookAudit
                (ActorNpcId, EventId, Action, PayloadJson, GameTime)
                VALUES ($actor, $event, $action, $payload, $time);
                """;
            cmd.Parameters.AddWithValue("$actor", actorNpcId);
            cmd.Parameters.AddWithValue("$event", eventId ?? "");
            cmd.Parameters.AddWithValue("$action", action ?? "");
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(payload, JsonOpts));
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        private static void EnsureTables()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS HumanEventHookPending (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActorNpcId INTEGER NOT NULL,
                    TargetNpcId INTEGER,
                    EventId TEXT NOT NULL,
                    SystemName TEXT NOT NULL,
                    PayloadJson TEXT,
                    GameTime TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    FOREIGN KEY (ActorNpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_human_hook_pending_system
                    ON HumanEventHookPending(SystemName, Status, Id);

                CREATE TABLE IF NOT EXISTS HumanEventHookAudit (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActorNpcId INTEGER NOT NULL,
                    EventId TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    PayloadJson TEXT,
                    GameTime TEXT NOT NULL,
                    FOREIGN KEY (ActorNpcId) REFERENCES Characters(Id)
                );

                CREATE TABLE IF NOT EXISTS HumanEventLifeHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    EventId TEXT NOT NULL,
                    Summary TEXT NOT NULL,
                    Severity TEXT,
                    GameTime TEXT NOT NULL,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE TABLE IF NOT EXISTS NpcDeepUpdateRequest (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    EventId TEXT NOT NULL,
                    RequestedGameTime TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_deep_update_pending
                    ON NpcDeepUpdateRequest(Status, Id);
                """;
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // PUBLIC RECORD TYPES
        // ============================================================

        public sealed class DeepUpdateRequest
        {
            public long Id { get; set; }
            public int NpcId { get; set; }
            public string EventId { get; set; } = "";
            public DateTime RequestedGameTime { get; set; }
        }

        public sealed class PendingHookWork
        {
            public long Id { get; set; }
            public int ActorNpcId { get; set; }
            public int? TargetNpcId { get; set; }
            public string EventId { get; set; } = "";
            public string SystemName { get; set; } = "";
            public string PayloadJson { get; set; } = "";
            public DateTime GameTime { get; set; }
        }
    }
}
