using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Relationships;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// HUMAN EVENT ENGINE
    /// ==================
    ///
    /// Human Event Domains.json answers:
    ///     "What are humans capable of doing/experiencing?"
    ///
    /// HumanEventEngine answers:
    ///     "Which of those events make sense for THIS NPC, RIGHT NOW?"
    ///
    /// IMPORTANT DESIGN RULES
    /// ----------------------
    /// 1. An event existing in the catalog NEVER makes it automatically available.
    /// 2. HARD GATES run before personality scoring.
    /// 3. Job authority is a hard gate. A cashier cannot arrest somebody because
    ///    "arrest" happens to be in the human event list.
    /// 4. Traits are preferences/probability shapers, not commands.
    /// 5. Current pressure/opportunity can temporarily outweigh normal personality,
    ///    but severe behavior still needs the correct opportunity/context.
    /// 6. Tier 1-4 all use this same engine/data. Tier controls how often deep
    ///    deliberation runs, NOT what the NPC is made of.
    /// 7. Major events can force a deep update immediately regardless of tier timer.
    /// 8. Tier 5 normally does not deliberate. If it becomes directly relevant,
    ///    promote/materialize the same person rather than rerolling them.
    ///
    /// The engine SELECTS an event. It does not directly rewrite every Project Eve
    /// subsystem. The returned decision contains consequence tags so Money, Job,
    /// Relationship, Memory, Police, Health, etc. can apply the event cleanly.
    /// </summary>
    public static class HumanEventEngine
    {
        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static HumanEventRulesRoot? _rules;
        private static List<HumanEventDefinition>? _catalog;

        public static string RulesPath =>
            Environment.GetEnvironmentVariable("EVE_HUMAN_EVENT_RULES")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "World", "Ohio", "human_event_rules.json");

        /// <summary>
        /// The user currently keeps the master event vocabulary under Memory.
        /// Multiple common spellings are accepted so the engine is not brittle.
        /// EVE_HUMAN_EVENT_DOMAINS overrides all automatic paths.
        /// </summary>
        public static string DomainsPath
        {
            get
            {
                string? env = Environment.GetEnvironmentVariable("EVE_HUMAN_EVENT_DOMAINS");
                if (!string.IsNullOrWhiteSpace(env))
                    return env;

                string[] candidates =
                {
                    Path.Combine(AppContext.BaseDirectory, "Memory", "Human Event Domains.json"),
                    Path.Combine(AppContext.BaseDirectory, "Memory", "HumanEventDomains.json"),
                    Path.Combine(AppContext.BaseDirectory, "Memory", "human_event_domains.json"),
                    Path.Combine(AppContext.BaseDirectory, "Data", "World", "Ohio", "Human Event Domains.json"),
                    Path.Combine(AppContext.BaseDirectory, "Data", "World", "Ohio", "human_event_domains.json")
                };

                foreach (string path in candidates)
                    if (File.Exists(path))
                        return path;

                // Return the preferred path for a useful error message.
                return candidates[0];
            }
        }

        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            lock (Sync)
            {
                LoadRules();
                LoadCatalog();
                EnsureTables();
            }
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _rules = null;
                _catalog = null;
                Initialize();
            }
        }

        /// <summary>
        /// Evaluates every relevant catalog event and returns the chosen event or
        /// a deliberate "no event" result.
        ///
        /// Pass HumanEventContext.Tags from the world/activity system:
        /// examples: in_store, merchandise_accessible, active_conflict,
        /// scheduled_to_work, romantic_opportunity, patient_present.
        /// </summary>
        public static HumanEventDecision Decide(
            SimCharacter actor,
            HumanEventContext context,
            Random? rng = null)
        {
            if (actor == null)
                return HumanEventDecision.None("actor is null");

            Initialize();
            rng ??= Random.Shared;

            // Tier 5 is intentionally not a routine thinking agent.
            if (context.SimulationTier >= 5 && !context.AllowTier5Deliberation)
                return HumanEventDecision.None("Tier 5 routine deliberation disabled");

            var evaluations = new List<HumanEventEvaluation>();

            foreach (var evt in _catalog!)
            {
                if (!context.DomainFilterAllows(evt.Domain))
                    continue;

                var evaluation = Evaluate(actor, evt, context);
                evaluations.Add(evaluation);
            }

            var viable = evaluations
                .Where(x => x.PassedHardGates && x.Score >= _rules!.Engine.MinimumScoreToSelect)
                .OrderByDescending(x => x.Score)
                .Take(Math.Max(1, _rules!.Engine.MaxCandidatesPerDecision))
                .ToList();

            if (viable.Count == 0)
            {
                return HumanEventDecision.None(
                    "No event passed hard gates + minimum score",
                    evaluations.OrderByDescending(x => x.Score).Take(12).ToList());
            }

            // "Nothing noteworthy happens" is a real outcome.
            double noEventWeight = Math.Max(0.01, _rules.Engine.NoEventBaseWeight);
            double eventWeightTotal = viable.Sum(v => Math.Max(0.1, v.Score - _rules.Engine.MinimumScoreToSelect + 1));

            double roll = rng.NextDouble() * (noEventWeight + eventWeightTotal);
            if (roll < noEventWeight)
            {
                return HumanEventDecision.None(
                    "No-event outcome won weighted selection",
                    viable.Take(12).ToList());
            }

            roll -= noEventWeight;

            HumanEventEvaluation chosen = viable[0];
            foreach (var item in viable)
            {
                roll -= Math.Max(0.1, item.Score - _rules.Engine.MinimumScoreToSelect + 1);
                if (roll <= 0)
                {
                    chosen = item;
                    break;
                }
            }

            bool forceDeep = IsDeepUpdateEvent(chosen.Event.EventId)
                             || string.Equals(chosen.Severity, "major", StringComparison.OrdinalIgnoreCase);

            return new HumanEventDecision
            {
                HasEvent = true,
                EventId = chosen.Event.EventId,
                Domain = chosen.Event.Domain,
                ActorNpcId = actor.Id,
                TargetNpcId = context.Target?.Id,
                LocationId = context.LocationId ?? actor.Location ?? "",
                Score = chosen.Score,
                Severity = chosen.Severity,
                ForceDeepUpdate = forceDeep,
                Reason = string.Join(" | ", chosen.Reasons),
                Evaluation = chosen,
                DebugTopCandidates = viable.Take(12).ToList()
            };
        }

        /// <summary>
        /// Evaluate one named event. Useful for debugging:
        ///
        /// HumanEventEngine.EvaluateEvent(npc, "robbery", context)
        ///
        /// lets you see exactly why the NPC could/could not consider it.
        /// </summary>
        public static HumanEventEvaluation EvaluateEvent(
            SimCharacter actor,
            string eventId,
            HumanEventContext context)
        {
            Initialize();

            var evt = _catalog!.FirstOrDefault(x =>
                string.Equals(x.EventId, eventId, StringComparison.OrdinalIgnoreCase));

            if (evt == null)
            {
                return HumanEventEvaluation.Rejected(
                    new HumanEventDefinition { EventId = eventId, Domain = "unknown" },
                    $"Event '{eventId}' not found in Human Event Domains.json");
            }

            return Evaluate(actor, evt, context);
        }

        public static HumanEventEvaluation Evaluate(
            SimCharacter actor,
            HumanEventDefinition evt,
            HumanEventContext context)
        {
            Initialize();

            var profile = _rules!.DomainProfiles.TryGetValue(evt.Domain, out var dp)
                ? dp
                : new DomainProfile { BaseScore = 18, Severity = "routine" };

            _rules.EventOverrides.TryGetValue(evt.EventId, out var ov);
            ov ??= new EventOverride();

            var result = new HumanEventEvaluation
            {
                Event = evt,
                PassedHardGates = true,
                Score = ov.BaseScore ?? profile.BaseScore,
                Severity = ov.Severity ?? profile.Severity ?? "routine"
            };

            result.Reasons.Add($"base {result.Score:0.0} ({evt.Domain})");

            // ------------------------------------------------------------
            // HARD GATE 1: age / actor / target / family / role context
            // ------------------------------------------------------------
            if ((profile.RequiresAdultActor || ov.RequiresAdultActor == true) && actor.Age < 18)
                return Reject(result, "requires adult actor");

            bool requiresTarget = ov.RequiresTarget == true || profile.RequiresTargetForMostEvents;
            if (requiresTarget && context.Target == null)
                return Reject(result, "requires target NPC");

            if (profile.RequiresFamilyTargetForMostEvents)
            {
                if (context.Target == null)
                    return Reject(result, "family event requires target");

                var rel = FindRelationship(actor, context.Target.Id);
                if (rel == null || !IsFamilyRelationship(rel.RelationshipType))
                    return Reject(result, "target is not a known family relationship");
            }

            if (profile.RequiresFamilyContext && !context.HasAnyTag("family_context", "pregnancy_context", "child_context"))
                return Reject(result, "requires family/parenthood context");

            // ------------------------------------------------------------
            // HARD GATE 2: job / professional authority
            // ------------------------------------------------------------
            string jobCategory = actor.Job?.IndustryPath ?? "";
            string jobTitle = actor.Job?.JobName ?? actor.Occupation ?? "";
            int careerLevel = ParseCareerLevel(actor.Job?.TitleLevel);

            bool hasJob = actor.Job != null
                          && !string.IsNullOrWhiteSpace(actor.Job.JobName)
                          && !actor.Job.IsUnemployed
                          && !actor.Job.IsRetired;

            if (profile.MustHaveJobForMostEvents && IsClearlyWorkAction(evt.EventId) && !hasJob)
                return Reject(result, "work event requires current job");

            if (ov.AllowedAnyJob == true && !hasJob)
                return Reject(result, "event requires any current job");

            if (ov.AllowedJobCategoriesAny.Count > 0 &&
                !ov.AllowedJobCategoriesAny.Contains(jobCategory, StringComparer.OrdinalIgnoreCase))
                return Reject(result, $"job category '{jobCategory}' not allowed");

            if (ov.AllowedJobTitlesAny.Count > 0 &&
                !ov.AllowedJobTitlesAny.Contains(jobTitle, StringComparer.OrdinalIgnoreCase))
                return Reject(result, $"job title '{jobTitle}' not allowed");

            if (ov.MinimumCareerLevel.HasValue && careerLevel < ov.MinimumCareerLevel.Value)
                return Reject(result, $"career level {careerLevel} < required {ov.MinimumCareerLevel}");

            if (ov.RequireOnDuty == true && !context.IsOnDuty)
                return Reject(result, "requires actor to be on duty");

            // Generic fallback gates protect newly-added events too.
            string? keywordReject = CheckKeywordRoleGate(evt.EventId, jobCategory, careerLevel);
            if (keywordReject != null)
                return Reject(result, keywordReject);

            // ------------------------------------------------------------
            // HARD GATE 3: world opportunity / exact context
            // ------------------------------------------------------------
            foreach (string tag in ov.RequiredContextTags)
                if (!context.HasTag(tag))
                    return Reject(result, $"missing required context '{tag}'");

            if (ov.RequiredContextAny.Count > 0 &&
                !ov.RequiredContextAny.Any(context.HasTag))
                return Reject(result, $"needs one of: {string.Join(", ", ov.RequiredContextAny)}");

            if (profile.RequiresConflictOpportunity &&
                !context.HasAnyTag("active_conflict", "argument_recent", "threat_present"))
                return Reject(result, "conflict event without conflict opportunity");

            if (profile.RequiresCrimeOpportunity &&
                !context.HasAnyTag(
                    "crime_opportunity", "money_accessible", "merchandise_accessible",
                    "robbery_opportunity", "workplace_property_accessible",
                    "victim_vulnerable", "target_property_accessible"))
                return Reject(result, "crime event without crime opportunity");

            if (profile.RequiresInformation &&
                !context.HasAnyTag("knows_shareable_information", "knows_secret", "rumor_available"))
                return Reject(result, "information/reputation event without information");

            if (profile.StateDriven && !context.StateDrivenEventIds.Contains(evt.EventId))
                return Reject(result, "state-driven event is not active in world state");

            // ------------------------------------------------------------
            // HARD GATE 4: rare trait thresholds
            // Most traits are SOFT scoring. Thresholds exist only when a severe
            // event should be almost unthinkable below a certain disposition.
            // ------------------------------------------------------------
            foreach (var kv in ov.TraitThresholds)
            {
                float value = GetTrait(actor, kv.Key);
                if (value < kv.Value)
                    return Reject(result, $"{kv.Key} {value:0} < required {kv.Value:0}");
            }

            // ------------------------------------------------------------
            // SOFT SCORE 1: permanent personality
            // ------------------------------------------------------------
            var combinedWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in profile.TraitWeights)
                combinedWeights[kv.Key] = kv.Value;

            foreach (var kv in ov.TraitWeights)
                combinedWeights[kv.Key] = kv.Value;

            foreach (var kv in combinedWeights)
            {
                float trait = GetTrait(actor, kv.Key);
                double contribution = (trait - 50.0) * kv.Value;
                result.Score += contribution;

                if (Math.Abs(contribution) >= 1.0)
                    result.Reasons.Add($"{kv.Key} {trait:0} => {contribution:+0.0;-0.0}");
            }

            // ------------------------------------------------------------
            // SOFT SCORE 2: explicit current state / opportunity biases
            // World systems can add:
            // context.ScoreBias["call_off"] = +18
            // context.ScoreBias["physical_fight"] = -20
            // ------------------------------------------------------------
            if (context.ScoreBias.TryGetValue(evt.EventId, out double exactBias))
            {
                result.Score += exactBias;
                result.Reasons.Add($"world state => {exactBias:+0.0;-0.0}");
            }

            if (context.DomainScoreBias.TryGetValue(evt.Domain, out double domainBias))
            {
                result.Score += domainBias;
                result.Reasons.Add($"domain state => {domainBias:+0.0;-0.0}");
            }

            // ------------------------------------------------------------
            // SOFT SCORE 3: goal / need / want / fear word alignment
            // This is intentionally a small nudge, not an LLM semantic guess.
            // ------------------------------------------------------------
            double motive = MotivationAlignment(actor, evt.EventId);
            if (Math.Abs(motive) >= 0.1)
            {
                result.Score += motive;
                result.Reasons.Add($"goal/need/want/fear alignment => {motive:+0.0;-0.0}");
            }

            // ------------------------------------------------------------
            // SOFT SCORE 4: relationship state
            // ------------------------------------------------------------
            if (context.Target != null)
            {
                var rel = FindRelationship(actor, context.Target.Id);
                if (rel != null)
                {
                    double relationshipScore = RelationshipFit(evt.EventId, evt.Domain, rel);
                    result.Score += relationshipScore;

                    if (Math.Abs(relationshipScore) >= 1.0)
                        result.Reasons.Add(
                            $"relationship {rel.RelationshipType} T{rel.Trust}/A{rel.Affection}/X{rel.Tension} => {relationshipScore:+0.0;-0.0}");

                    if (ov.RequiredRelationshipTypesAny.Count > 0 &&
                        !ov.RequiredRelationshipTypesAny.Contains(rel.RelationshipType, StringComparer.OrdinalIgnoreCase))
                        return Reject(result, $"relationship '{rel.RelationshipType}' not allowed");
                }
                else if (ov.RequiredRelationshipTypesAny.Count > 0)
                {
                    return Reject(result, "required relationship type but no relationship exists");
                }
            }

            // ------------------------------------------------------------
            // SOFT SCORE 5: work-role relevance
            // A valid work event gets a small bonus while on duty.
            // Authority was already handled as a hard gate above.
            // ------------------------------------------------------------
            if (evt.Domain == "work" && hasJob)
            {
                result.Score += context.IsOnDuty ? 6 : 1;
                result.Reasons.Add(context.IsOnDuty ? "on-duty work fit +6" : "has job +1");
            }

            // ------------------------------------------------------------
            // SOFT SCORE 6: cooldown / repeated behavior
            // ------------------------------------------------------------
            int cooldown = string.Equals(result.Severity, "major", StringComparison.OrdinalIgnoreCase)
                ? _rules.Engine.MajorEventCooldownGameMinutes
                : _rules.Engine.DefaultCooldownGameMinutes;

            var recent = GetRecentEvent(actor.Id, evt.EventId, context.Target?.Id);
            if (recent != null && context.GameTime.HasValue)
            {
                double minutes = (context.GameTime.Value - recent.Value.When).TotalMinutes;
                if (minutes >= 0 && minutes < cooldown)
                {
                    result.Score *= _rules.Engine.RepeatPenalty;
                    result.Reasons.Add($"repeat cooldown x{_rules.Engine.RepeatPenalty:0.00}");
                }

                if (context.Target != null && recent.Value.TargetNpcId == context.Target.Id)
                {
                    result.Score *= _rules.Engine.SameTargetRepeatPenalty;
                    result.Reasons.Add($"same target repeat x{_rules.Engine.SameTargetRepeatPenalty:0.00}");
                }
            }

            // Explicit caller-supplied random jitter can be disabled for tests.
            if (context.EnableScoreJitter)
            {
                double jitter = context.Random.NextDouble()
                    * (_rules.Engine.RandomJitterMax - _rules.Engine.RandomJitterMin)
                    + _rules.Engine.RandomJitterMin;
                result.Score += jitter;
                result.Reasons.Add($"human variance {jitter:+0.0;-0.0}");
            }

            result.Score = Math.Round(result.Score, 2);
            return result;
        }

        /// <summary>
        /// Call after the event is actually accepted/executed by the world.
        /// This provides cooldown/history without pretending every evaluated event happened.
        /// </summary>
        public static void RecordExecuted(HumanEventDecision decision, DateTime gameTime)
        {
            if (!decision.HasEvent) return;
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO HumanEventHistory
                (ActorNpcId, TargetNpcId, EventId, Domain, LocationId, GameTime, Score, Severity, Reason)
                VALUES ($actor, $target, $event, $domain, $loc, $time, $score, $severity, $reason);
                """;
            cmd.Parameters.AddWithValue("$actor", decision.ActorNpcId);
            cmd.Parameters.AddWithValue("$target", (object?)decision.TargetNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$event", decision.EventId);
            cmd.Parameters.AddWithValue("$domain", decision.Domain);
            cmd.Parameters.AddWithValue("$loc", decision.LocationId ?? "");
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$score", decision.Score);
            cmd.Parameters.AddWithValue("$severity", decision.Severity ?? "");
            cmd.Parameters.AddWithValue("$reason", decision.Reason ?? "");
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // INTERNAL LOADING
        // ============================================================

        private static void LoadRules()
        {
            if (_rules != null) return;

            if (!File.Exists(RulesPath))
                throw new FileNotFoundException("Human event rules not found.", RulesPath);

            _rules = JsonSerializer.Deserialize<HumanEventRulesRoot>(
                File.ReadAllText(RulesPath), JsonOpts)
                ?? throw new InvalidDataException("Could not deserialize human_event_rules.json");
        }

        private static void LoadCatalog()
        {
            if (_catalog != null) return;

            if (!File.Exists(DomainsPath))
                throw new FileNotFoundException(
                    "Human Event Domains.json not found. Set EVE_HUMAN_EVENT_DOMAINS or place it under Memory.",
                    DomainsPath);

            using JsonDocument doc = JsonDocument.Parse(
                File.ReadAllText(DomainsPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("human_event_domains", out JsonElement domains))
                root = domains;

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Human Event Domains.json must contain an object of domain arrays.");

            var list = new List<HumanEventDefinition>();

            foreach (JsonProperty domain in root.EnumerateObject())
            {
                if (domain.Name.StartsWith("_", StringComparison.Ordinal))
                    continue;

                if (domain.Value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement item in domain.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        continue;

                    string id = item.GetString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    list.Add(new HumanEventDefinition
                    {
                        EventId = id,
                        Domain = domain.Name
                    });
                }
            }

            _catalog = list
                .GroupBy(x => x.EventId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static void EnsureTables()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS HumanEventHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActorNpcId INTEGER NOT NULL,
                    TargetNpcId INTEGER,
                    EventId TEXT NOT NULL,
                    Domain TEXT NOT NULL,
                    LocationId TEXT,
                    GameTime TEXT NOT NULL,
                    Score REAL NOT NULL,
                    Severity TEXT,
                    Reason TEXT,
                    FOREIGN KEY (ActorNpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_human_event_actor_time
                    ON HumanEventHistory(ActorNpcId, GameTime);

                CREATE INDEX IF NOT EXISTS ix_human_event_actor_event
                    ON HumanEventHistory(ActorNpcId, EventId, GameTime);
                """;
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // GATE / SCORE HELPERS
        // ============================================================

        private static HumanEventEvaluation Reject(HumanEventEvaluation result, string reason)
        {
            result.PassedHardGates = false;
            result.RejectionReason = reason;
            result.Reasons.Add($"REJECT: {reason}");
            return result;
        }

        private static float GetTrait(SimCharacter actor, string canonical)
        {
            // Unknown trait means neutral 50, not zero.
            // That avoids punishing an NPC just because a new trait has not yet
            // been added to TraitRegistry.
            if (actor.Traits == null)
                return 50;

            if (!_rules!.TraitAliases.TryGetValue(canonical, out var aliases))
                aliases = new List<string> { canonical };

            bool found = false;
            float best = 50;

            foreach (string id in aliases)
            {
                try
                {
                    float value = actor.Traits.Get(id);

                    // Trait systems commonly return 0 for unknown IDs.
                    // Treat a real nonzero value as found. A literal zero can still
                    // be represented by defining that canonical key in TraitRegistry.
                    if (Math.Abs(value) > 0.001f)
                    {
                        best = value;
                        found = true;
                        break;
                    }
                }
                catch { }
            }

            return found ? Math.Clamp(best, 0, 100) : 50;
        }

        private static Relationship? FindRelationship(SimCharacter actor, int targetId)
        {
            return actor.Relationships?.FirstOrDefault(r => r.TargetId == targetId);
        }

        private static bool IsFamilyRelationship(string? type)
        {
            string t = (type ?? "").ToLowerInvariant();
            string[] family =
            {
                "family","parent","mother","father","child","son","daughter","sibling",
                "brother","sister","spouse","husband","wife","cousin","aunt","uncle",
                "grandparent","grandmother","grandfather","in_law","brother_in_law",
                "sister_in_law","step_parent","step_child"
            };

            return family.Any(x => t.Contains(x, StringComparison.OrdinalIgnoreCase));
        }

        private static double RelationshipFit(string eventId, string domain, Relationship rel)
        {
            double score = 0;

            string id = eventId.ToLowerInvariant();

            if (domain is "friendship" or "family" or "romance")
            {
                score += (rel.Affection - 50) * 0.10;
                score += (rel.Trust - 50) * 0.06;
            }

            if (domain == "conflict")
            {
                score += (rel.Tension - 30) * 0.12;
                score += (50 - rel.Trust) * 0.08;
            }

            if (id.Contains("apolog", StringComparison.OrdinalIgnoreCase))
                score += Math.Max(0, rel.Affection - 50) * 0.10;

            if (id.Contains("betray", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("cheat", StringComparison.OrdinalIgnoreCase))
                score += Math.Max(0, 50 - rel.Affection) * 0.07;

            if (id.Contains("help", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("defend", StringComparison.OrdinalIgnoreCase))
                score += Math.Max(0, rel.Affection - 50) * 0.10;

            return score;
        }

        private static double MotivationAlignment(SimCharacter actor, string eventId)
        {
            string[] words = Tokenize(eventId);
            if (words.Length == 0) return 0;

            double score = 0;

            if (ContainsAny(actor.Goal, words)) score += 4;
            if (ContainsAny(actor.Need, words)) score += 4;
            if (ContainsAny(actor.Want, words)) score += 3;
            if (ContainsAny(actor.Fear, words)) score -= 3;

            return score;
        }

        private static string[] Tokenize(string s)
        {
            return Regex.Split((s ?? "").ToLowerInvariant().Replace("_", " "), @"\W+")
                .Where(x => x.Length >= 4)
                .Distinct()
                .ToArray();
        }

        private static bool ContainsAny(string? text, string[] words)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.ToLowerInvariant();
            return words.Any(t.Contains);
        }

        private static int ParseCareerLevel(string? level)
        {
            return (level ?? "").Trim().ToLowerInvariant() switch
            {
                "entry" => 1,
                "mid" => 2,
                "senior" => 3,
                "lead" => 3,
                "supervisor" => 3,
                "manager" => 4,
                "owner" => 5,
                "executive" => 5,
                _ => 1
            };
        }

        private static bool IsClearlyWorkAction(string eventId)
        {
            string id = eventId.ToLowerInvariant();
            string[] words =
            {
                "clock_","shift","coworker","manager","work_","job_","promotion",
                "raise","fired","quit","overtime","writeup","write_up","call_off",
                "no_call_no_show"
            };

            return words.Any(id.Contains);
        }

        private static string? CheckKeywordRoleGate(string eventId, string jobCategory, int careerLevel)
        {
            var gates = _rules!.KeywordRoleGates;
            string id = eventId.ToLowerInvariant();

            if (gates.PoliceKeywords.Any(k => MatchesRoleKeyword(id, k)) &&
                !gates.PoliceCategories.Contains(jobCategory, StringComparer.OrdinalIgnoreCase))
                return $"police-authority event requires police job; actor category is '{jobCategory}'";

            if (gates.MedicalKeywords.Any(k => MatchesRoleKeyword(id, k)) &&
                !gates.MedicalCategories.Contains(jobCategory, StringComparer.OrdinalIgnoreCase))
                return $"medical-authority event requires medical job; actor category is '{jobCategory}'";

            if (gates.TeacherKeywords.Any(k => MatchesRoleKeyword(id, k)) &&
                !gates.TeacherCategories.Contains(jobCategory, StringComparer.OrdinalIgnoreCase))
                return $"teaching-authority event requires education job; actor category is '{jobCategory}'";

            if (gates.ManagerKeywords.Any(k => MatchesRoleKeyword(id, k)) &&
                careerLevel < gates.ManagerCareerLevelMinimum)
                return $"manager-authority event requires career level {gates.ManagerCareerLevelMinimum}+";

            return null;
        }


        private static bool MatchesRoleKeyword(string eventId, string keyword)
        {
            // Exact action or a more-specific action name such as arrest_suspect.
            // Does NOT treat "arrested" as the police action "arrest".
            if (string.Equals(eventId, keyword, StringComparison.OrdinalIgnoreCase))
                return true;

            return eventId.StartsWith(keyword + "_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDeepUpdateEvent(string eventId)
        {
            string id = eventId.ToLowerInvariant();
            return _rules!.DeepUpdateEventPatterns.Any(pattern =>
                id.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static RecentEvent? GetRecentEvent(int actorNpcId, string eventId, int? targetNpcId)
        {
            try
            {
                using var conn = new SqliteConnection(ConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT TargetNpcId, GameTime
                    FROM HumanEventHistory
                    WHERE ActorNpcId=$actor AND EventId=$event
                    ORDER BY Id DESC
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$actor", actorNpcId);
                cmd.Parameters.AddWithValue("$event", eventId);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;

                int? target = reader.IsDBNull(0) ? null : reader.GetInt32(0);
                if (!DateTime.TryParse(reader.GetString(1), out var when))
                    return null;

                return new RecentEvent(target, when);
            }
            catch
            {
                return null;
            }
        }

        private readonly record struct RecentEvent(int? TargetNpcId, DateTime When);

        // ============================================================
        // PUBLIC MODELS
        // ============================================================

        public sealed class HumanEventContext
        {
            /// <summary>
            /// Tier controls call frequency outside this engine.
            /// Tier 1-4 all have full NPC data.
            /// </summary>
            public int SimulationTier { get; set; } = 4;
            public bool AllowTier5Deliberation { get; set; }

            public SimCharacter? Target { get; set; }
            public string? LocationId { get; set; }
            public bool IsOnDuty { get; set; }
            public string CurrentActivity { get; set; } = "";

            /// <summary>
            /// World facts / opportunities. Examples:
            /// active_conflict, in_store, merchandise_accessible,
            /// scheduled_to_work, romantic_opportunity, patient_present.
            /// </summary>
            public HashSet<string> Tags { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// State-driven events such as illness, pregnancy, death, job loss,
            /// etc. should only be considered when the owning system says the
            /// state actually exists.
            /// </summary>
            public HashSet<string> StateDrivenEventIds { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Allows a world system to nudge an exact event without bypassing gates.
            /// Example: financial system can add +18 to "borrow_money".
            /// </summary>
            public Dictionary<string, double> ScoreBias { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Bias all events in one domain (e.g. social opportunity).
            /// </summary>
            public Dictionary<string, double> DomainScoreBias { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Optional domain restriction for a decision pass.
            /// Empty means all domains.
            /// </summary>
            public HashSet<string> AllowedDomains { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public DateTime? GameTime { get; set; }

            /// <summary>
            /// Leave false in deterministic tests. Set true in live simulation
            /// for small human variation between otherwise similar choices.
            /// </summary>
            public bool EnableScoreJitter { get; set; } = true;
            public Random Random { get; set; } = Random.Shared;

            public bool HasTag(string tag) => Tags.Contains(tag);

            public bool HasAnyTag(params string[] tags)
                => tags.Any(HasTag);

            public bool DomainFilterAllows(string domain)
                => AllowedDomains.Count == 0 || AllowedDomains.Contains(domain);
        }

        public sealed class HumanEventDecision
        {
            public bool HasEvent { get; set; }
            public string EventId { get; set; } = "";
            public string Domain { get; set; } = "";
            public int ActorNpcId { get; set; }
            public int? TargetNpcId { get; set; }
            public string? LocationId { get; set; }
            public double Score { get; set; }
            public string Severity { get; set; } = "";
            public bool ForceDeepUpdate { get; set; }
            public string Reason { get; set; } = "";
            public HumanEventEvaluation? Evaluation { get; set; }
            public List<HumanEventEvaluation> DebugTopCandidates { get; set; } = new();

            public static HumanEventDecision None(
                string reason,
                List<HumanEventEvaluation>? debug = null)
                => new()
                {
                    HasEvent = false,
                    Reason = reason,
                    DebugTopCandidates = debug ?? new()
                };
        }

        public sealed class HumanEventEvaluation
        {
            public HumanEventDefinition Event { get; set; } = new();
            public bool PassedHardGates { get; set; }
            public string? RejectionReason { get; set; }
            public double Score { get; set; }
            public string Severity { get; set; } = "routine";
            public List<string> Reasons { get; set; } = new();

            public static HumanEventEvaluation Rejected(
                HumanEventDefinition evt,
                string reason)
                => new()
                {
                    Event = evt,
                    PassedHardGates = false,
                    RejectionReason = reason,
                    Reasons = new List<string> { $"REJECT: {reason}" }
                };

            public override string ToString()
            {
                if (!PassedHardGates)
                    return $"{Event.EventId}: REJECTED — {RejectionReason}";
                return $"{Event.EventId}: {Score:0.0} — {string.Join(" | ", Reasons)}";
            }
        }

        public sealed class HumanEventDefinition
        {
            public string EventId { get; set; } = "";
            public string Domain { get; set; } = "";
        }

        // ============================================================
        // JSON RULE MODELS
        // ============================================================

        public sealed class HumanEventRulesRoot
        {
            public EngineSettings Engine { get; set; } = new();
            public Dictionary<string, List<string>> TraitAliases { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, DomainProfile> DomainProfiles { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, EventOverride> EventOverrides { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public KeywordRoleGates KeywordRoleGates { get; set; } = new();
            public List<string> DeepUpdateEventPatterns { get; set; } = new();
        }

        public sealed class EngineSettings
        {
            public double MinimumScoreToSelect { get; set; } = 28;
            public double NoEventBaseWeight { get; set; } = 32;
            public int MaxCandidatesPerDecision { get; set; } = 40;
            public int DefaultCooldownGameMinutes { get; set; } = 30;
            public int MajorEventCooldownGameMinutes { get; set; } = 180;
            public double RepeatPenalty { get; set; } = 0.45;
            public double SameTargetRepeatPenalty { get; set; } = 0.60;
            public double RandomJitterMin { get; set; } = -4;
            public double RandomJitterMax { get; set; } = 4;
        }

        public sealed class DomainProfile
        {
            public double BaseScore { get; set; } = 18;
            public string? Severity { get; set; } = "routine";
            public Dictionary<string, double> TraitWeights { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public bool MustHaveJobForMostEvents { get; set; }
            public bool RequiresTargetForMostEvents { get; set; }
            public bool RequiresFamilyTargetForMostEvents { get; set; }
            public bool RequiresAdultActor { get; set; }
            public bool RequiresFamilyContext { get; set; }
            public bool RequiresConflictOpportunity { get; set; }
            public bool RequiresCrimeOpportunity { get; set; }
            public bool RequiresInformation { get; set; }
            public bool StateDriven { get; set; }
            public bool RoleSensitive { get; set; }
        }

        public sealed class EventOverride
        {
            public double? BaseScore { get; set; }
            public string? Severity { get; set; }

            public bool? RequiresTarget { get; set; }
            public bool? RequiresAdultActor { get; set; }
            public bool? AllowedAnyJob { get; set; }
            public bool? RequireOnDuty { get; set; }
            public int? MinimumCareerLevel { get; set; }

            public List<string> AllowedJobCategoriesAny { get; set; } = new();
            public List<string> AllowedJobTitlesAny { get; set; } = new();
            public List<string> RequiredContextTags { get; set; } = new();
            public List<string> RequiredContextAny { get; set; } = new();
            public List<string> RequiredRelationshipTypesAny { get; set; } = new();

            public Dictionary<string, double> TraitWeights { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, double> TraitThresholds { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class KeywordRoleGates
        {
            public List<string> PoliceKeywords { get; set; } = new();
            public List<string> PoliceCategories { get; set; } = new();

            public List<string> MedicalKeywords { get; set; } = new();
            public List<string> MedicalCategories { get; set; } = new();

            public List<string> TeacherKeywords { get; set; } = new();
            public List<string> TeacherCategories { get; set; } = new();

            public List<string> ManagerKeywords { get; set; } = new();
            public int ManagerCareerLevelMinimum { get; set; } = 3;
        }
    }
}
