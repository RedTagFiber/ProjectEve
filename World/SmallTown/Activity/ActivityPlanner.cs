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
    /// ACTIVITY PLANNER
    /// ================
    ///
    /// Gives a full Tier 1-4 NPC a believable plan between hard obligations.
    ///
    /// HARD OBLIGATIONS win first:
    ///     work
    ///     school
    ///     sleep
    ///     medical emergency
    ///     police detention
    ///     already-running major HumanEvent
    ///
    /// OPTIONAL LIFE fills the gaps:
    ///     meals
    ///     groceries
    ///     errands
    ///     shopping
    ///     visiting friends/family
    ///     cafes
    ///     bars
    ///     church
    ///     hobbies
    ///     exercise
    ///     appointments
    ///     dates
    ///     entertainment
    ///     spontaneous trips
    ///     staying home
    ///
    /// IMPORTANT:
    /// The planner creates WHERE/WHAT opportunity.
    /// HumanEventEngine still decides deeper behavior.
    ///
    /// Example:
    ///     ActivityPlanner sends Sarah to Sinclair Coffee.
    ///     Jessica is already there.
    ///     SocialEncounterEngine creates conversation_opportunity.
    ///     HumanEventEngine decides whether Sarah chats, gossips, argues,
    ///     ignores her, asks a favor, flirts, etc.
    ///
    /// This class is intentionally rule-based and cheap enough to run over
    /// hundreds of NPCs.
    /// </summary>
    public static class ActivityPlanner
    {
        private static readonly object Sync = new();

        private static ActivityPlannerConfig? _config;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string ConfigPath =>
            Environment.GetEnvironmentVariable("EVE_ACTIVITY_PLANNER")
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "World",
                "Ohio",
                "activity_planner.json");

        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            lock (Sync)
            {
                LoadConfig();
                EnsureTables();
            }
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _config = null;
                Initialize();
            }
        }

        // ============================================================
        // VENUES
        // ============================================================

        /// <summary>
        /// Register a real place once, then every NPC can use it.
        ///
        /// category examples:
        ///     home
        ///     cafe
        ///     restaurant
        ///     grocery
        ///     retail
        ///     bar
        ///     church
        ///     park
        ///     gym
        ///     entertainment
        ///     medical
        ///     government
        ///     friend_home
        ///
        /// tags are free-form:
        ///     breakfast
        ///     cheap
        ///     late_night
        ///     family_friendly
        ///     date
        ///     groceries
        /// </summary>
        public static void RegisterVenue(
            string locationId,
            string name,
            string category,
            IEnumerable<string>? tags = null,
            int minimumAge = 0,
            int costLevel = 1,
            int openHour = 0,
            int closeHour = 24,
            bool enabled = true)
        {
            Initialize();

            string tagText = string.Join(
                ",",
                tags ?? Array.Empty<string>());

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ActivityVenue
                (LocationId, Name, Category, Tags, MinimumAge,
                 CostLevel, OpenHour, CloseHour, Enabled)
                VALUES
                ($id,$name,$category,$tags,$age,$cost,$open,$close,$enabled)
                ON CONFLICT(LocationId) DO UPDATE SET
                    Name=$name,
                    Category=$category,
                    Tags=$tags,
                    MinimumAge=$age,
                    CostLevel=$cost,
                    OpenHour=$open,
                    CloseHour=$close,
                    Enabled=$enabled;
                """;

            cmd.Parameters.AddWithValue("$id", locationId);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$category", category);
            cmd.Parameters.AddWithValue("$tags", tagText);
            cmd.Parameters.AddWithValue("$age", Math.Max(0, minimumAge));
            cmd.Parameters.AddWithValue("$cost", Math.Clamp(costLevel, 0, 5));
            cmd.Parameters.AddWithValue("$open", Math.Clamp(openHour, 0, 23));
            cmd.Parameters.AddWithValue("$close", Math.Clamp(closeHour, 1, 24));
            cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Seed a few Project Eve locations already known from the current world.
        /// Safe to call repeatedly.
        /// </summary>
        public static void SeedKnownProjectEveVenues()
        {
            Initialize();

            RegisterVenue(
                "sinclair-coffee",
                "Sinclair Coffee",
                "cafe",
                new[] { "coffee", "breakfast", "lunch", "social", "date" },
                costLevel: 2,
                openHour: 6,
                closeHour: 18);

            RegisterVenue(
                "fire-station",
                "Fire Station",
                "workplace",
                new[] { "fire", "ems" },
                costLevel: 0,
                openHour: 0,
                closeHour: 24);

            RegisterVenue(
                "adam's-house",
                "Adam's House",
                "home",
                new[] { "private_home" },
                costLevel: 0,
                openHour: 0,
                closeHour: 24);

            RegisterVenue(
                "sinclair-parents",
                "Sinclair Parents' Home",
                "home",
                new[] { "family_home", "private_home" },
                costLevel: 0,
                openHour: 0,
                closeHour: 24);
        }

        // ============================================================
        // PLANNING
        // ============================================================

        /// <summary>
        /// Build one next activity.
        ///
        /// PlannerContext should contain actual world facts/needs.
        /// The planner does not magically invent appointments, dates, errands,
        /// groceries, or emergencies.
        /// </summary>
        public static PlannedActivity PlanNext(
            SimCharacter npc,
            DateTime gameTime,
            PlannerContext? context = null,
            Random? rng = null)
        {
            Initialize();

            context ??= new PlannerContext();
            rng ??= Random.Shared;

            if (npc == null)
                return PlannedActivity.None("NPC is null");

            if (npc.Tier >= 5 && !context.AllowTier5Planning)
                return PlannedActivity.None("Tier 5 routine planning disabled");

            // --------------------------------------------------------
            // 1. HARD OBLIGATIONS
            // --------------------------------------------------------
            var hard = ResolveHardObligation(
                npc,
                gameTime,
                context);

            if (hard != null)
            {
                SavePlan(npc.Id, hard, gameTime);
                return hard;
            }

            // --------------------------------------------------------
            // 2. BUILD OPTIONAL CANDIDATES
            // --------------------------------------------------------
            var candidates = new List<ActivityCandidate>();

            foreach (var kv in _config!.ActivityProfiles)
            {
                string activityId = kv.Key;
                ActivityProfile profile = kv.Value;

                var candidate = ScoreActivity(
                    npc,
                    activityId,
                    profile,
                    gameTime,
                    context,
                    rng);

                if (candidate.Allowed)
                    candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                var fallback = CreateHomeFallback(
                    npc,
                    gameTime,
                    rng,
                    "No optional activity candidate passed gates");

                SavePlan(npc.Id, fallback, gameTime);
                return fallback;
            }

            // --------------------------------------------------------
            // 3. WEIGHTED SELECTION
            // --------------------------------------------------------
            double total = candidates.Sum(
                x => Math.Max(0.1, x.Score));

            double roll = rng.NextDouble() * total;

            ActivityCandidate chosen = candidates[0];

            foreach (var c in candidates)
            {
                roll -= Math.Max(0.1, c.Score);

                if (roll <= 0)
                {
                    chosen = c;
                    break;
                }
            }

            PlannedActivity plan = CreatePlan(
                npc,
                chosen,
                gameTime,
                context,
                rng);

            SavePlan(npc.Id, plan, gameTime);

            return plan;
        }

        /// <summary>
        /// Apply the current plan into WorldActivityEngine's physical state table.
        ///
        /// This is what makes planned people actually appear at the venue.
        /// </summary>
        public static void ApplyCurrentPlanToWorld(
            int npcId,
            DateTime gameTime)
        {
            Initialize();

            PlannedActivity? plan = GetCurrentPlan(
                npcId,
                gameTime);

            if (plan == null)
                return;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NpcWorldActivity
                (NpcId, LocationId, Activity, ActivityStartGameTime,
                 LastWorldTickGameTime, IsBusy)
                VALUES
                ($npc,$loc,$act,$start,$now,$busy)
                ON CONFLICT(NpcId) DO UPDATE SET
                    LocationId=$loc,
                    Activity=$act,
                    ActivityStartGameTime=$start,
                    LastWorldTickGameTime=$now,
                    IsBusy=$busy;
                """;

            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$loc", plan.LocationId ?? "");
            cmd.Parameters.AddWithValue("$act", plan.ActivityId ?? "idle");
            cmd.Parameters.AddWithValue("$start", plan.StartGameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$now", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$busy", plan.IsBusy ? 1 : 0);

            cmd.ExecuteNonQuery();
        }

        public static PlannedActivity? GetCurrentPlan(
            int npcId,
            DateTime gameTime)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ActivityId, LocationId, TargetNpcId,
                       StartGameTime, EndGameTime,
                       Reason, IsHardObligation, IsBusy
                FROM NpcActivityPlan
                WHERE NpcId=$npc
                  AND StartGameTime <= $time
                  AND EndGameTime > $time
                ORDER BY Id DESC
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                return null;

            return ReadPlan(r, npcId);
        }

        public static List<PlannedActivity> GetUpcomingPlans(
            int npcId,
            DateTime gameTime,
            int limit = 10)
        {
            Initialize();

            var result = new List<PlannedActivity>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ActivityId, LocationId, TargetNpcId,
                       StartGameTime, EndGameTime,
                       Reason, IsHardObligation, IsBusy
                FROM NpcActivityPlan
                WHERE NpcId=$npc
                  AND EndGameTime >= $time
                ORDER BY StartGameTime
                LIMIT $limit;
                """;

            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

            using var r = cmd.ExecuteReader();

            while (r.Read())
                result.Add(ReadPlan(r, npcId));

            return result;
        }

        // ============================================================
        // HARD OBLIGATIONS
        // ============================================================

        private static PlannedActivity? ResolveHardObligation(
            SimCharacter npc,
            DateTime gameTime,
            PlannerContext context)
        {
            if (context.HardOverride != null)
                return context.HardOverride;

            if (context.HasTag("medical_emergency"))
            {
                return CreateFixed(
                    npc.Id,
                    "medical_emergency",
                    context.RequiredLocationId ?? npc.Location,
                    context.RequiredTargetNpcId,
                    gameTime,
                    gameTime.AddMinutes(120),
                    "medical emergency",
                    hard: true,
                    busy: true);
            }

            if (context.HasTag("police_detained"))
            {
                return CreateFixed(
                    npc.Id,
                    "police_contact",
                    context.RequiredLocationId ?? npc.Location,
                    context.RequiredTargetNpcId,
                    gameTime,
                    gameTime.AddMinutes(120),
                    "police detention/contact",
                    hard: true,
                    busy: true);
            }

            try
            {
                if (npc.Job != null &&
                    !npc.Job.IsUnemployed &&
                    !npc.Job.IsRetired &&
                    npc.Job.IsWorkingAt(gameTime))
                {
                    string location =
                        !string.IsNullOrWhiteSpace(npc.Job.Employer)
                            ? npc.Job.Employer
                            : npc.Location;

                    return CreateFixed(
                        npc.Id,
                        "work",
                        location,
                        null,
                        gameTime,
                        gameTime.AddMinutes(30),
                        "scheduled work shift",
                        hard: true,
                        busy: true);
                }
            }
            catch { }

            if (context.HasTag("school_in_session"))
            {
                return CreateFixed(
                    npc.Id,
                    "school",
                    context.RequiredLocationId ?? "school",
                    null,
                    gameTime,
                    gameTime.AddMinutes(60),
                    "school schedule",
                    hard: true,
                    busy: true);
            }

            int hour = gameTime.Hour;

            if (hour < 6 || hour >= 23)
            {
                return CreateFixed(
                    npc.Id,
                    "sleep",
                    ResolveHome(npc),
                    null,
                    gameTime,
                    gameTime.AddMinutes(60),
                    "normal sleep window",
                    hard: true,
                    busy: true);
            }

            return null;
        }

        // ============================================================
        // OPTIONAL SCORING
        // ============================================================

        private static ActivityCandidate ScoreActivity(
            SimCharacter npc,
            string activityId,
            ActivityProfile profile,
            DateTime gameTime,
            PlannerContext context,
            Random rng)
        {
            var result = new ActivityCandidate
            {
                ActivityId = activityId,
                Allowed = true,
                Score = profile.BaseWeight
            };

            if (profile.MinimumAge > 0 &&
                npc.Age < profile.MinimumAge)
            {
                return result.Reject(
                    $"minimum age {profile.MinimumAge}");
            }

            if (profile.TimeTags.Count > 0 &&
                !profile.TimeTags.Any(
                    tag => TimeTagMatches(tag, gameTime)))
            {
                return result.Reject(
                    "time window does not match");
            }

            if (profile.NeedTags.Count > 0)
            {
                bool hasNeed =
                    profile.NeedTags.Any(context.HasTag);

                if (!hasNeed && profile.HardWhenNeedPresent)
                    return result.Reject(
                        "required need/context missing");

                if (hasNeed)
                {
                    result.Score += 35;
                    result.Reasons.Add("matching current need +35");
                }
            }

            int moneyBand = DetermineSpendLevel(npc);

            if (profile.CostLevel > moneyBand)
            {
                result.Score -=
                    (profile.CostLevel - moneyBand) * 12;

                result.Reasons.Add(
                    "money pressure lowers activity");
            }

            foreach (var tw in profile.TraitWeights)
            {
                float value = GetTrait(
                    npc,
                    tw.Key);

                double contribution =
                    (value - 50.0) * tw.Value;

                result.Score += contribution;

                if (Math.Abs(contribution) >= 1)
                {
                    result.Reasons.Add(
                        $"{tw.Key} => {contribution:+0.0;-0.0}");
                }
            }

            // Basic state needs.
            if (activityId is "meal_home" or "restaurant_meal")
            {
                if (context.HasTag("hungry"))
                    result.Score += 28;

                if (IsMealWindow(gameTime))
                    result.Score += 12;
            }

            if (activityId == "grocery_shopping" &&
                context.HasTag("needs_groceries"))
            {
                result.Score += 30;
            }

            if (activityId == "visit_friend" &&
                context.AvailableFriendNpcIds.Count == 0)
            {
                return result.Reject(
                    "no available friend target");
            }

            if (activityId == "visit_family" &&
                context.AvailableFamilyNpcIds.Count == 0)
            {
                return result.Reject(
                    "no available family target");
            }

            if (activityId == "date" &&
                context.AvailableDateNpcIds.Count == 0)
            {
                return result.Reject(
                    "no actual date target");
            }

            if (activityId == "appointment" &&
                !context.HasTag("has_appointment"))
            {
                return result.Reject(
                    "no appointment exists");
            }

            if (activityId == "church" &&
                !context.HasTag("church_attender") &&
                !TimeTagMatches(
                    "sunday_morning",
                    gameTime))
            {
                result.Score *= 0.15;
            }

            // Do not endlessly repeat the same optional activity.
            if (WasRecentlyDone(
                npc.Id,
                activityId,
                gameTime,
                _config!.Planning.AvoidSameOptionalActivityWithinGameHours))
            {
                result.Score *= 0.30;
                result.Reasons.Add(
                    "recent repeat penalty");
            }

            // Small human variation.
            result.Score +=
                rng.NextDouble() * 6 - 3;

            if (result.Score <= 0)
                result.Allowed = false;

            return result;
        }

        // ============================================================
        // PLAN CREATION
        // ============================================================

        private static PlannedActivity CreatePlan(
            SimCharacter npc,
            ActivityCandidate candidate,
            DateTime gameTime,
            PlannerContext context,
            Random rng)
        {
            ActivityProfile profile =
                _config!.ActivityProfiles[candidate.ActivityId];

            int duration = rng.Next(
                Math.Max(1, profile.DurationMin),
                Math.Max(
                    profile.DurationMin + 1,
                    profile.DurationMax + 1));

            int? targetNpcId = null;
            string locationId;

            switch (candidate.ActivityId)
            {
                case "stay_home":
                case "meal_home":
                    locationId = ResolveHome(npc);
                    break;

                case "visit_friend":
                    targetNpcId =
                        Pick(context.AvailableFriendNpcIds, rng);

                    locationId =
                        context.TargetHomeLocationByNpcId.TryGetValue(
                            targetNpcId ?? -1,
                            out var friendHome)
                        ? friendHome
                        : ResolveHome(npc);

                    break;

                case "visit_family":
                    targetNpcId =
                        Pick(context.AvailableFamilyNpcIds, rng);

                    locationId =
                        context.TargetHomeLocationByNpcId.TryGetValue(
                            targetNpcId ?? -1,
                            out var familyHome)
                        ? familyHome
                        : ResolveHome(npc);

                    break;

                case "date":
                    targetNpcId =
                        Pick(context.AvailableDateNpcIds, rng);

                    locationId =
                        ChooseVenue(
                            npc,
                            candidate.ActivityId,
                            gameTime,
                            profile.CostLevel,
                            rng)
                        ?.LocationId
                        ?? ResolveHome(npc);

                    break;

                case "appointment":
                    locationId =
                        context.RequiredLocationId
                        ?? ChooseVenue(
                            npc,
                            "appointment",
                            gameTime,
                            profile.CostLevel,
                            rng)
                        ?.LocationId
                        ?? npc.Location;

                    targetNpcId =
                        context.RequiredTargetNpcId;

                    break;

                default:
                    locationId =
                        ChooseVenue(
                            npc,
                            candidate.ActivityId,
                            gameTime,
                            profile.CostLevel,
                            rng)
                        ?.LocationId
                        ?? ResolveHome(npc);

                    break;
            }

            return new PlannedActivity
            {
                NpcId = npc.Id,
                ActivityId = candidate.ActivityId,
                LocationId = locationId,
                TargetNpcId = targetNpcId,
                StartGameTime = gameTime,
                EndGameTime = gameTime.AddMinutes(duration),
                Reason = string.Join(
                    " | ",
                    candidate.Reasons),
                IsHardObligation = false,
                IsBusy =
                    candidate.ActivityId is
                    "appointment"
            };
        }

        private static PlannedActivity CreateHomeFallback(
            SimCharacter npc,
            DateTime gameTime,
            Random rng,
            string reason)
        {
            int duration = rng.Next(30, 121);

            return CreateFixed(
                npc.Id,
                "stay_home",
                ResolveHome(npc),
                null,
                gameTime,
                gameTime.AddMinutes(duration),
                reason,
                hard: false,
                busy: false);
        }

        private static PlannedActivity CreateFixed(
            int npcId,
            string activityId,
            string? locationId,
            int? targetNpcId,
            DateTime start,
            DateTime end,
            string reason,
            bool hard,
            bool busy)
        {
            return new PlannedActivity
            {
                NpcId = npcId,
                ActivityId = activityId,
                LocationId = locationId ?? "",
                TargetNpcId = targetNpcId,
                StartGameTime = start,
                EndGameTime = end,
                Reason = reason,
                IsHardObligation = hard,
                IsBusy = busy
            };
        }

        // ============================================================
        // VENUE MATCHING
        // ============================================================

        private static ActivityVenue? ChooseVenue(
            SimCharacter npc,
            string activityId,
            DateTime gameTime,
            int maxPreferredCost,
            Random rng)
        {
            var categories =
                ActivityToVenueCategories(activityId);

            var candidates =
                GetOpenVenues(gameTime)
                .Where(v =>
                    categories.Contains(
                        v.Category,
                        StringComparer.OrdinalIgnoreCase))
                .Where(v =>
                    npc.Age >= v.MinimumAge)
                .ToList();

            if (candidates.Count == 0)
                return null;

            int spendLevel =
                DetermineSpendLevel(npc);

            var affordable =
                candidates
                .Where(v =>
                    v.CostLevel <=
                    Math.Max(
                        spendLevel,
                        maxPreferredCost))
                .ToList();

            if (affordable.Count > 0)
                candidates = affordable;

            return candidates[
                rng.Next(candidates.Count)];
        }

        private static string[] ActivityToVenueCategories(
            string activityId)
        {
            return activityId switch
            {
                "restaurant_meal" =>
                    new[] { "restaurant", "cafe", "fast_food" },

                "grocery_shopping" =>
                    new[] { "grocery", "supermarket" },

                "shopping" =>
                    new[] { "retail", "mall", "store" },

                "errand" =>
                    new[] { "retail", "government", "service", "bank", "post_office" },

                "bar" =>
                    new[] { "bar", "pub" },

                "church" =>
                    new[] { "church", "religious" },

                "hobby" =>
                    new[] { "park", "library", "community", "hobby", "entertainment" },

                "exercise" =>
                    new[] { "gym", "park", "recreation" },

                "appointment" =>
                    new[] { "medical", "service", "government", "office" },

                "date" =>
                    new[] { "restaurant", "cafe", "park", "entertainment", "bar" },

                "movie_entertainment" =>
                    new[] { "entertainment", "cinema" },

                "coffee_cafe" =>
                    new[] { "cafe" },

                "walk_around_town" =>
                    new[] { "park", "downtown", "public" },

                "spontaneous_trip" =>
                    new[] { "travel", "park", "entertainment", "restaurant" },

                _ =>
                    new[] { "public", "park", "cafe" }
            };
        }

        private static List<ActivityVenue> GetOpenVenues(
            DateTime gameTime)
        {
            Initialize();

            var result =
                new List<ActivityVenue>();

            using var conn =
                new SqliteConnection(ConnStr);

            conn.Open();

            using var cmd =
                conn.CreateCommand();

            cmd.CommandText = """
                SELECT LocationId, Name, Category, Tags,
                       MinimumAge, CostLevel, OpenHour, CloseHour
                FROM ActivityVenue
                WHERE Enabled=1;
                """;

            using var r =
                cmd.ExecuteReader();

            while (r.Read())
            {
                var venue =
                    new ActivityVenue
                    {
                        LocationId = r.GetString(0),
                        Name = r.GetString(1),
                        Category = r.GetString(2),
                        Tags = r.IsDBNull(3)
                            ? new()
                            : r.GetString(3)
                                .Split(
                                    ',',
                                    StringSplitOptions.RemoveEmptyEntries |
                                    StringSplitOptions.TrimEntries)
                                .ToHashSet(
                                    StringComparer.OrdinalIgnoreCase),

                        MinimumAge = r.GetInt32(4),
                        CostLevel = r.GetInt32(5),
                        OpenHour = r.GetInt32(6),
                        CloseHour = r.GetInt32(7)
                    };

                if (IsVenueOpen(
                    venue,
                    gameTime.Hour))
                {
                    result.Add(venue);
                }
            }

            return result;
        }

        private static bool IsVenueOpen(
            ActivityVenue venue,
            int hour)
        {
            if (venue.OpenHour == 0 &&
                venue.CloseHour == 24)
            {
                return true;
            }

            if (venue.OpenHour < venue.CloseHour)
            {
                return hour >= venue.OpenHour &&
                       hour < venue.CloseHour;
            }

            // Overnight.
            return hour >= venue.OpenHour ||
                   hour < venue.CloseHour;
        }

        // ============================================================
        // PERSONALITY / MONEY / TIME
        // ============================================================

        private static float GetTrait(
            SimCharacter npc,
            string id)
        {
            try
            {
                if (npc.Traits == null)
                    return 50;

                float v =
                    npc.Traits.Get(id);

                return Math.Abs(v) < 0.001f
                    ? 50
                    : Math.Clamp(v, 0, 100);
            }
            catch
            {
                return 50;
            }
        }

        private static int DetermineSpendLevel(
    SimCharacter npc)
        {
            try
            {
                if (npc.Money == null)
                    return 2;

                decimal available =
                    npc.Money.Cash +
                    npc.Money.Bank -
                    Math.Min(
                        npc.Money.Debt,
                        10000m);

                if (available < 250m)
                    return 1;

                if (available < 2500m)
                    return 2;

                if (available < 15000m)
                    return 3;

                return 4;
            }
            catch
            {
                return 2;
            }
        }

        private static bool IsMealWindow(
            DateTime gameTime)
        {
            int h = gameTime.Hour;

            return
                (h >= 5 && h <= 10) ||
                (h >= 11 && h <= 15) ||
                (h >= 17 && h <= 21);
        }

        private static bool TimeTagMatches(
            string tag,
            DateTime gameTime)
        {
            int h = gameTime.Hour;

            return tag.ToLowerInvariant() switch
            {
                "morning" =>
                    h >= 5 && h < 12,

                "afternoon" =>
                    h >= 12 && h < 17,

                "evening" =>
                    h >= 17 && h < 22,

                "late_night" =>
                    h >= 21 || h < 2,

                "sunday_morning" =>
                    gameTime.DayOfWeek == DayOfWeek.Sunday &&
                    h >= 7 &&
                    h < 13,

                _ =>
                    true
            };
        }

        private static string ResolveHome(
            SimCharacter npc)
        {
            if (!string.IsNullOrWhiteSpace(
                npc.HomeAddress))
            {
                return npc.HomeAddress;
            }

            return !string.IsNullOrWhiteSpace(
                npc.Location)
                ? npc.Location
                : "home";
        }

        private static int? Pick(
            List<int> ids,
            Random rng)
        {
            if (ids == null ||
                ids.Count == 0)
            {
                return null;
            }

            return ids[
                rng.Next(ids.Count)];
        }

        // ============================================================
        // RECENT ACTIVITY / PERSISTENCE
        // ============================================================

        private static bool WasRecentlyDone(
            int npcId,
            string activityId,
            DateTime gameTime,
            int withinHours)
        {
            if (withinHours <= 0)
                return false;

            DateTime cutoff =
                gameTime.AddHours(
                    -withinHours);

            using var conn =
                new SqliteConnection(ConnStr);

            conn.Open();

            using var cmd =
                conn.CreateCommand();

            cmd.CommandText = """
                SELECT 1
                FROM NpcActivityPlan
                WHERE NpcId=$npc
                  AND ActivityId=$activity
                  AND StartGameTime >= $cutoff
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue(
                "$npc",
                npcId);

            cmd.Parameters.AddWithValue(
                "$activity",
                activityId);

            cmd.Parameters.AddWithValue(
                "$cutoff",
                cutoff.ToString("o"));

            return cmd.ExecuteScalar() != null;
        }

        private static void SavePlan(
            int npcId,
            PlannedActivity plan,
            DateTime gameTime)
        {
            using var conn =
                new SqliteConnection(ConnStr);

            conn.Open();

            using var cmd =
                conn.CreateCommand();

            cmd.CommandText = """
                INSERT INTO NpcActivityPlan
                (NpcId, ActivityId, LocationId, TargetNpcId,
                 StartGameTime, EndGameTime, Reason,
                 IsHardObligation, IsBusy, CreatedGameTime)
                VALUES
                ($npc,$activity,$location,$target,
                 $start,$end,$reason,$hard,$busy,$created);
                """;

            cmd.Parameters.AddWithValue(
                "$npc",
                npcId);

            cmd.Parameters.AddWithValue(
                "$activity",
                plan.ActivityId ?? "");

            cmd.Parameters.AddWithValue(
                "$location",
                plan.LocationId ?? "");

            cmd.Parameters.AddWithValue(
                "$target",
                (object?)plan.TargetNpcId ??
                DBNull.Value);

            cmd.Parameters.AddWithValue(
                "$start",
                plan.StartGameTime.ToString("o"));

            cmd.Parameters.AddWithValue(
                "$end",
                plan.EndGameTime.ToString("o"));

            cmd.Parameters.AddWithValue(
                "$reason",
                plan.Reason ?? "");

            cmd.Parameters.AddWithValue(
                "$hard",
                plan.IsHardObligation ? 1 : 0);

            cmd.Parameters.AddWithValue(
                "$busy",
                plan.IsBusy ? 1 : 0);

            cmd.Parameters.AddWithValue(
                "$created",
                gameTime.ToString("o"));

            cmd.ExecuteNonQuery();
        }

        private static PlannedActivity ReadPlan(
            SqliteDataReader r,
            int npcId)
        {
            return new PlannedActivity
            {
                NpcId = npcId,
                ActivityId =
                    r.IsDBNull(0)
                        ? ""
                        : r.GetString(0),

                LocationId =
                    r.IsDBNull(1)
                        ? ""
                        : r.GetString(1),

                TargetNpcId =
                    r.IsDBNull(2)
                        ? null
                        : r.GetInt32(2),

                StartGameTime =
                    DateTime.TryParse(
                        r.IsDBNull(3)
                            ? ""
                            : r.GetString(3),
                        out var start)
                        ? start
                        : DateTime.MinValue,

                EndGameTime =
                    DateTime.TryParse(
                        r.IsDBNull(4)
                            ? ""
                            : r.GetString(4),
                        out var end)
                        ? end
                        : DateTime.MinValue,

                Reason =
                    r.IsDBNull(5)
                        ? ""
                        : r.GetString(5),

                IsHardObligation =
                    !r.IsDBNull(6) &&
                    r.GetInt32(6) != 0,

                IsBusy =
                    !r.IsDBNull(7) &&
                    r.GetInt32(7) != 0
            };
        }

        // ============================================================
        // DB / CONFIG
        // ============================================================

        private static void LoadConfig()
        {
            if (_config != null)
                return;

            if (!File.Exists(
                ConfigPath))
            {
                throw new FileNotFoundException(
                    "Activity planner config not found.",
                    ConfigPath);
            }

            _config =
                JsonSerializer.Deserialize<ActivityPlannerConfig>(
                    File.ReadAllText(
                        ConfigPath),
                    JsonOpts)
                ?? throw new InvalidDataException(
                    "Could not deserialize activity_planner.json");
        }

        private static void EnsureTables()
        {
            using var conn =
                new SqliteConnection(ConnStr);

            conn.Open();

            using var cmd =
                conn.CreateCommand();

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ActivityVenue (
                    LocationId TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Tags TEXT,
                    MinimumAge INTEGER NOT NULL DEFAULT 0,
                    CostLevel INTEGER NOT NULL DEFAULT 1,
                    OpenHour INTEGER NOT NULL DEFAULT 0,
                    CloseHour INTEGER NOT NULL DEFAULT 24,
                    Enabled INTEGER NOT NULL DEFAULT 1
                );

                CREATE INDEX IF NOT EXISTS ix_activity_venue_category
                    ON ActivityVenue(Category, Enabled);

                CREATE TABLE IF NOT EXISTS NpcActivityPlan (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    ActivityId TEXT NOT NULL,
                    LocationId TEXT,
                    TargetNpcId INTEGER,
                    StartGameTime TEXT NOT NULL,
                    EndGameTime TEXT NOT NULL,
                    Reason TEXT,
                    IsHardObligation INTEGER NOT NULL DEFAULT 0,
                    IsBusy INTEGER NOT NULL DEFAULT 0,
                    CreatedGameTime TEXT NOT NULL,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_npc_activity_plan_active
                    ON NpcActivityPlan(
                        NpcId,
                        StartGameTime,
                        EndGameTime);

                CREATE INDEX IF NOT EXISTS ix_npc_activity_plan_location
                    ON NpcActivityPlan(
                        LocationId,
                        StartGameTime,
                        EndGameTime);
                """;

            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // PUBLIC MODELS
        // ============================================================

        public sealed class PlannerContext
        {
            /// <summary>
            /// Real world facts only.
            ///
            /// Examples:
            ///     hungry
            ///     needs_groceries
            ///     has_errand
            ///     has_appointment
            ///     date_available
            ///     church_attender
            ///     medical_emergency
            ///     police_detained
            ///     school_in_session
            /// </summary>
            public HashSet<string> Tags { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public bool AllowTier5Planning { get; set; }

            public List<int> AvailableFriendNpcIds { get; } =
                new();

            public List<int> AvailableFamilyNpcIds { get; } =
                new();

            public List<int> AvailableDateNpcIds { get; } =
                new();

            public Dictionary<int, string> TargetHomeLocationByNpcId { get; } =
                new();

            public string? RequiredLocationId { get; set; }
            public int? RequiredTargetNpcId { get; set; }

            public PlannedActivity? HardOverride { get; set; }

            public bool HasTag(string tag)
                => Tags.Contains(tag);
        }

        public sealed class PlannedActivity
        {
            public int NpcId { get; set; }
            public string ActivityId { get; set; } = "";
            public string LocationId { get; set; } = "";
            public int? TargetNpcId { get; set; }

            public DateTime StartGameTime { get; set; }
            public DateTime EndGameTime { get; set; }

            public string Reason { get; set; } = "";

            public bool IsHardObligation { get; set; }
            public bool IsBusy { get; set; }

            public bool HasActivity =>
                !string.IsNullOrWhiteSpace(
                    ActivityId);

            public static PlannedActivity None(
                string reason)
                => new()
                {
                    ActivityId = "",
                    Reason = reason
                };

            public override string ToString()
            {
                return
                    $"{ActivityId} @ {LocationId} " +
                    $"{StartGameTime:t}-{EndGameTime:t}" +
                    (TargetNpcId.HasValue
                        ? $" with/for NPC {TargetNpcId}"
                        : "");
            }
        }

        public sealed class ActivityVenue
        {
            public string LocationId { get; set; } = "";
            public string Name { get; set; } = "";
            public string Category { get; set; } = "";
            public HashSet<string> Tags { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public int MinimumAge { get; set; }
            public int CostLevel { get; set; }
            public int OpenHour { get; set; }
            public int CloseHour { get; set; }
        }

        private sealed class ActivityCandidate
        {
            public string ActivityId { get; set; } = "";
            public bool Allowed { get; set; }
            public double Score { get; set; }
            public string RejectionReason { get; set; } = "";
            public List<string> Reasons { get; } = new();

            public ActivityCandidate Reject(
                string reason)
            {
                Allowed = false;
                RejectionReason = reason;
                return this;
            }
        }

        // ============================================================
        // CONFIG MODELS
        // ============================================================

        public sealed class ActivityPlannerConfig
        {
            public PlanningSettings Planning { get; set; } =
                new();

            public Dictionary<string, ActivityProfile> ActivityProfiles { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class PlanningSettings
        {
            public int PlanningHorizonGameHours { get; set; } = 6;
            public int ReplanEveryGameMinutes { get; set; } = 30;
            public int MinimumActivityDurationMinutes { get; set; } = 20;
            public int MaximumActivityDurationMinutes { get; set; } = 240;
            public int AvoidSameOptionalActivityWithinGameHours { get; set; } = 8;
            public bool AllowSpontaneousChange { get; set; } = true;
            public double SpontaneousChangeBaseChance { get; set; } = 0.08;
        }

        public sealed class ActivityProfile
        {
            public double BaseWeight { get; set; } = 10;
            public int CostLevel { get; set; } = 1;
            public int DurationMin { get; set; } = 30;
            public int DurationMax { get; set; } = 120;
            public int MinimumAge { get; set; }

            public List<string> NeedTags { get; set; } =
                new();

            public List<string> TimeTags { get; set; } =
                new();

            public bool HardWhenNeedPresent { get; set; }

            public Dictionary<string, double> TraitWeights { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
