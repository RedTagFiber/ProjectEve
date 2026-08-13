using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// NEEDS SYSTEM
    /// ============
    ///
    /// Needs are changing life pressures, not personality traits.
    ///
    /// Examples:
    ///     Hunger rises with time.
    ///     Energy falls while awake and rises during sleep.
    ///     Social need falls when isolated and rises with good contact.
    ///     Stress rises from work, money trouble and major events.
    ///     Groceries slowly run down and drop faster when eating at home.
    ///
    /// ActivityPlanner consumes these needs as tags.
    /// HumanEventEngine can also use the same tags/current-state values.
    ///
    /// Tier 1-4 use the same need model.
    /// </summary>
    public static class NeedsSystem
    {
        private static readonly object Sync = new();

        private static NeedsConfig? _config;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string ConfigPath =>
            Environment.GetEnvironmentVariable("EVE_NEEDS_SYSTEM")
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "World",
                "Ohio",
                "needs_system.json");

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

        public static void EnsureNpc(int npcId, DateTime gameTime)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO NpcNeeds
                (NpcId,Hunger,Energy,Social,Fun,Stress,Hygiene,SleepDebt,Groceries,Comfort,LastUpdateGameTime)
                VALUES
                ($npc,$h,$e,$s,$f,$st,$hy,$sd,$g,$c,$time);
                """;

            var start = _config!.StartingValues;

            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$h", start.Hunger);
            cmd.Parameters.AddWithValue("$e", start.Energy);
            cmd.Parameters.AddWithValue("$s", start.Social);
            cmd.Parameters.AddWithValue("$f", start.Fun);
            cmd.Parameters.AddWithValue("$st", start.Stress);
            cmd.Parameters.AddWithValue("$hy", start.Hygiene);
            cmd.Parameters.AddWithValue("$sd", start.SleepDebt);
            cmd.Parameters.AddWithValue("$g", start.Groceries);
            cmd.Parameters.AddWithValue("$c", start.Comfort);
            cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Cheap need tick. Call from the fast world loop.
        ///
        /// activity should come from ActivityPlanner / WorldActivityEngine.
        /// </summary>
        public static NeedState TickNpc(
            SimCharacter npc,
            DateTime gameTime,
            string? activity = null)
        {
            Initialize();
            EnsureNpc(npc.Id, gameTime);

            NeedState state = GetState(npc.Id)!;

            double elapsedHours =
                Math.Max(
                    0,
                    (gameTime - state.LastUpdateGameTime).TotalHours);

            if (elapsedHours <= 0)
                return state;

            ApplyPassiveRates(state, elapsedHours);

            if (!string.IsNullOrWhiteSpace(activity))
                ApplyActivityRates(state, activity!, elapsedHours);

            Clamp(state);

            state.LastUpdateGameTime = gameTime;
            Save(state);

            return state;
        }

        /// <summary>
        /// Apply a one-time HumanEvent consequence to current needs.
        /// Example: fired, breakup, fight, missed bill.
        /// </summary>
        public static NeedState ApplyEvent(
            int npcId,
            string eventId,
            DateTime gameTime)
        {
            Initialize();
            EnsureNpc(npcId, gameTime);

            NeedState state = GetState(npcId)!;

            if (_config!.EventEffects.TryGetValue(eventId, out var effect))
            {
                ApplyDelta(state, effect);
                Clamp(state);
                state.LastUpdateGameTime = gameTime;
                Save(state);
            }

            return state;
        }

        /// <summary>
        /// Lets other systems apply precise one-time need changes.
        /// </summary>
        public static NeedState ApplyDelta(
            int npcId,
            NeedDelta delta,
            DateTime gameTime)
        {
            Initialize();
            EnsureNpc(npcId, gameTime);

            NeedState state = GetState(npcId)!;
            ApplyDelta(state, delta);
            Clamp(state);
            state.LastUpdateGameTime = gameTime;
            Save(state);

            return state;
        }

        public static NeedState? GetState(int npcId)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Hunger,Energy,Social,Fun,Stress,Hygiene,
                       SleepDebt,Groceries,Comfort,LastUpdateGameTime
                FROM NpcNeeds
                WHERE NpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                return null;

            return new NeedState
            {
                NpcId = npcId,
                Hunger = r.GetDouble(0),
                Energy = r.GetDouble(1),
                Social = r.GetDouble(2),
                Fun = r.GetDouble(3),
                Stress = r.GetDouble(4),
                Hygiene = r.GetDouble(5),
                SleepDebt = r.GetDouble(6),
                Groceries = r.GetDouble(7),
                Comfort = r.GetDouble(8),
                LastUpdateGameTime =
                    DateTime.TryParse(r.GetString(9), out var dt)
                        ? dt
                        : DateTime.MinValue
            };
        }

        /// <summary>
        /// Convert numeric need state into ActivityPlanner/HumanEvent tags.
        /// </summary>
        public static HashSet<string> GetNeedTags(int npcId)
        {
            Initialize();

            NeedState? s = GetState(npcId);
            var tags = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            if (s == null)
                return tags;

            var t = _config!.Thresholds;

            if (s.Hunger >= t.Hungry)
                tags.Add("hungry");

            if (s.Hunger >= t.VeryHungry)
                tags.Add("very_hungry");

            if (s.Energy <= t.LowEnergy)
                tags.Add("low_energy");

            if (s.Energy <= t.Exhausted)
                tags.Add("exhausted");

            if (s.Social <= t.Lonely)
                tags.Add("lonely");

            if (s.Social <= t.VeryLonely)
                tags.Add("very_lonely");

            if (s.Fun <= t.Bored)
                tags.Add("bored");

            if (s.Fun <= t.VeryBored)
                tags.Add("very_bored");

            if (s.Stress >= t.Stressed)
                tags.Add("stressed");

            if (s.Stress >= t.Overwhelmed)
                tags.Add("overwhelmed");

            if (s.Hygiene <= t.NeedsHygiene)
                tags.Add("needs_hygiene");

            if (s.Hygiene <= t.Dirty)
                tags.Add("dirty");

            if (s.SleepDebt >= t.Sleepy)
                tags.Add("sleepy");

            if (s.SleepDebt >= t.SleepDeprived)
                tags.Add("sleep_deprived");

            if (s.Groceries <= t.NeedsGroceries)
                tags.Add("needs_groceries");

            if (s.Groceries <= t.OutOfGroceries)
                tags.Add("out_of_groceries");

            if (s.Comfort <= t.Uncomfortable)
                tags.Add("uncomfortable");

            return tags;
        }

        /// <summary>
        /// Fill ActivityPlanner context automatically from current needs.
        /// Existing context facts are preserved.
        /// </summary>
        public static ActivityPlanner.PlannerContext AddToPlannerContext(
            SimCharacter npc,
            ActivityPlanner.PlannerContext? context = null)
        {
            context ??= new ActivityPlanner.PlannerContext();

            foreach (string tag in GetNeedTags(npc.Id))
                context.Tags.Add(tag);

            return context;
        }

        /// <summary>
        /// Fill HumanEvent context automatically from current needs.
        /// </summary>
        public static HumanEventEngine.HumanEventContext AddToHumanEventContext(
            SimCharacter npc,
            HumanEventEngine.HumanEventContext context)
        {
            foreach (string tag in GetNeedTags(npc.Id))
                context.Tags.Add(tag);

            NeedState? s = GetState(npc.Id);

            if (s != null)
            {
                if (s.Stress >= 60)
                    context.DomainScoreBias["conflict"] =
                        context.DomainScoreBias.TryGetValue("conflict", out var old)
                            ? old + ((s.Stress - 60) * 0.15)
                            : ((s.Stress - 60) * 0.15);

                if (s.Social <= 30)
                    context.DomainScoreBias["friendship"] =
                        context.DomainScoreBias.TryGetValue("friendship", out var old)
                            ? old + ((30 - s.Social) * 0.15)
                            : ((30 - s.Social) * 0.15);

                if (s.Fun <= 30)
                    context.DomainScoreBias["daily_life"] =
                        context.DomainScoreBias.TryGetValue("daily_life", out var old)
                            ? old + 2
                            : 2;
            }

            return context;
        }

        private static void ApplyPassiveRates(
            NeedState state,
            double hours)
        {
            var r = _config!.PassiveRatesPerGameHour;

            state.Hunger += r.Hunger * hours;
            state.Energy += r.Energy * hours;
            state.Social += r.Social * hours;
            state.Fun += r.Fun * hours;
            state.Stress += r.Stress * hours;
            state.Hygiene += r.Hygiene * hours;
            state.SleepDebt += r.SleepDebt * hours;
            state.Groceries += r.Groceries * hours;
            state.Comfort += r.Comfort * hours;
        }

        private static void ApplyActivityRates(
            NeedState state,
            string activity,
            double hours)
        {
            if (_config!.ActivityEffectsPerHour.TryGetValue(activity, out var effect))
            {
                state.Hunger += effect.Hunger * hours;
                state.Energy += effect.Energy * hours;
                state.Social += effect.Social * hours;
                state.Fun += effect.Fun * hours;
                state.Stress += effect.Stress * hours;
                state.Hygiene += effect.Hygiene * hours;
                state.SleepDebt += effect.SleepDebt * hours;
                state.Groceries += effect.Groceries * hours;
                state.Comfort += effect.Comfort * hours;
            }
        }

        private static void ApplyDelta(
            NeedState state,
            NeedDelta d)
        {
            state.Hunger += d.Hunger;
            state.Energy += d.Energy;
            state.Social += d.Social;
            state.Fun += d.Fun;
            state.Stress += d.Stress;
            state.Hygiene += d.Hygiene;
            state.SleepDebt += d.SleepDebt;
            state.Groceries += d.Groceries;
            state.Comfort += d.Comfort;
        }

        private static void Clamp(NeedState s)
        {
            double min = _config!.Tick.ClampMinimum;
            double max = _config.Tick.ClampMaximum;

            s.Hunger = Math.Clamp(s.Hunger, min, max);
            s.Energy = Math.Clamp(s.Energy, min, max);
            s.Social = Math.Clamp(s.Social, min, max);
            s.Fun = Math.Clamp(s.Fun, min, max);
            s.Stress = Math.Clamp(s.Stress, min, max);
            s.Hygiene = Math.Clamp(s.Hygiene, min, max);
            s.SleepDebt = Math.Clamp(s.SleepDebt, min, max);
            s.Groceries = Math.Clamp(s.Groceries, min, max);
            s.Comfort = Math.Clamp(s.Comfort, min, max);
        }

        private static void Save(NeedState s)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NpcNeeds
                SET Hunger=$h,
                    Energy=$e,
                    Social=$s,
                    Fun=$f,
                    Stress=$st,
                    Hygiene=$hy,
                    SleepDebt=$sd,
                    Groceries=$g,
                    Comfort=$c,
                    LastUpdateGameTime=$time
                WHERE NpcId=$npc;
                """;

            cmd.Parameters.AddWithValue("$h", s.Hunger);
            cmd.Parameters.AddWithValue("$e", s.Energy);
            cmd.Parameters.AddWithValue("$s", s.Social);
            cmd.Parameters.AddWithValue("$f", s.Fun);
            cmd.Parameters.AddWithValue("$st", s.Stress);
            cmd.Parameters.AddWithValue("$hy", s.Hygiene);
            cmd.Parameters.AddWithValue("$sd", s.SleepDebt);
            cmd.Parameters.AddWithValue("$g", s.Groceries);
            cmd.Parameters.AddWithValue("$c", s.Comfort);
            cmd.Parameters.AddWithValue("$time", s.LastUpdateGameTime.ToString("o"));
            cmd.Parameters.AddWithValue("$npc", s.NpcId);

            cmd.ExecuteNonQuery();
        }

        private static void LoadConfig()
        {
            if (_config != null)
                return;

            if (!File.Exists(ConfigPath))
                throw new FileNotFoundException(
                    "Needs system config not found.",
                    ConfigPath);

            _config =
                JsonSerializer.Deserialize<NeedsConfig>(
                    File.ReadAllText(ConfigPath),
                    JsonOpts)
                ?? throw new InvalidDataException(
                    "Could not deserialize needs_system.json");
        }

        private static void EnsureTables()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcNeeds (
                    NpcId INTEGER PRIMARY KEY,
                    Hunger REAL NOT NULL,
                    Energy REAL NOT NULL,
                    Social REAL NOT NULL,
                    Fun REAL NOT NULL,
                    Stress REAL NOT NULL,
                    Hygiene REAL NOT NULL,
                    SleepDebt REAL NOT NULL,
                    Groceries REAL NOT NULL,
                    Comfort REAL NOT NULL,
                    LastUpdateGameTime TEXT NOT NULL,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_npc_needs_stress
                    ON NpcNeeds(Stress);

                CREATE INDEX IF NOT EXISTS ix_npc_needs_hunger
                    ON NpcNeeds(Hunger);

                CREATE INDEX IF NOT EXISTS ix_npc_needs_social
                    ON NpcNeeds(Social);
                """;
            cmd.ExecuteNonQuery();
        }

        public sealed class NeedState
        {
            public int NpcId { get; set; }

            /// <summary>0 = full, 100 = starving.</summary>
            public double Hunger { get; set; }

            /// <summary>0 = exhausted, 100 = fully energized.</summary>
            public double Energy { get; set; }

            /// <summary>0 = very lonely, 100 = socially satisfied.</summary>
            public double Social { get; set; }

            /// <summary>0 = very bored, 100 = highly fulfilled/fun.</summary>
            public double Fun { get; set; }

            /// <summary>0 = calm, 100 = overwhelmed.</summary>
            public double Stress { get; set; }

            /// <summary>0 = filthy, 100 = clean.</summary>
            public double Hygiene { get; set; }

            /// <summary>0 = rested, 100 = severe sleep debt.</summary>
            public double SleepDebt { get; set; }

            /// <summary>0 = empty pantry, 100 = stocked.</summary>
            public double Groceries { get; set; }

            /// <summary>0 = highly uncomfortable, 100 = comfortable.</summary>
            public double Comfort { get; set; }

            public DateTime LastUpdateGameTime { get; set; }
        }

        public sealed class NeedDelta
        {
            public double Hunger { get; set; }
            public double Energy { get; set; }
            public double Social { get; set; }
            public double Fun { get; set; }
            public double Stress { get; set; }
            public double Hygiene { get; set; }
            public double SleepDebt { get; set; }
            public double Groceries { get; set; }
            public double Comfort { get; set; }
        }

        public sealed class NeedsConfig
        {
            public TickSettings Tick { get; set; } = new();
            public NeedDelta StartingValues { get; set; } = new();
            public NeedDelta PassiveRatesPerGameHour { get; set; } = new();

            public Dictionary<string, NeedDelta> ActivityEffectsPerHour { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, NeedDelta> EventEffects { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public ThresholdSettings Thresholds { get; set; } = new();
        }

        public sealed class TickSettings
        {
            public int GameMinutesPerTick { get; set; } = 5;
            public double ClampMinimum { get; set; } = 0;
            public double ClampMaximum { get; set; } = 100;
        }

        public sealed class ThresholdSettings
        {
            public double Hungry { get; set; } = 60;
            public double VeryHungry { get; set; } = 82;

            public double LowEnergy { get; set; } = 35;
            public double Exhausted { get; set; } = 15;

            public double Lonely { get; set; } = 30;
            public double VeryLonely { get; set; } = 15;

            public double Bored { get; set; } = 30;
            public double VeryBored { get; set; } = 15;

            public double Stressed { get; set; } = 60;
            public double Overwhelmed { get; set; } = 82;

            public double NeedsHygiene { get; set; } = 35;
            public double Dirty { get; set; } = 15;

            public double Sleepy { get; set; } = 60;
            public double SleepDeprived { get; set; } = 82;

            public double NeedsGroceries { get; set; } = 30;
            public double OutOfGroceries { get; set; } = 10;

            public double Uncomfortable { get; set; } = 25;
        }
    }
}
