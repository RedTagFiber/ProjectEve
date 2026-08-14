using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Core.Phone;

namespace ProjectEve.Phone;

/// <summary>
/// Hidden server-side NPC phone-response scheduler.
///
/// Separates:
/// - noticing the phone
/// - being physically able to answer
/// - wanting to answer
///
/// Work is NEVER a hard "no text" rule. It changes access, workload,
/// notice chance, and willingness/delay.
///
/// Explicit NpcPhoneRuntimeState wins over heuristics whenever available.
/// </summary>
public sealed class NpcPhoneResponseScheduler : IPhoneResponseScheduler
{
    private readonly object _rngLock = new();
    private readonly Random _rng = new();

    private static string DbPath =>
        Environment.GetEnvironmentVariable("EVE_DB_PATH")
        ?? Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "project_eve.db");

    private static string ConnStr =>
        "Data Source=" + DbPath;

    /// <summary>
    /// Development time compression.
    /// 1.0 = real minutes.
    /// 0.04 = 10 simulated minutes -> 24 real seconds.
    ///
    /// Set EVE_PHONE_TIME_SCALE=1 later when the game clock/server
    /// owns full real-time scheduling.
    /// </summary>
    private static double TimeScale
    {
        get
        {
            string? raw =
                Environment.GetEnvironmentVariable(
                    "EVE_PHONE_TIME_SCALE");

            if (double.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
                return Math.Clamp(value, 0.005, 10.0);

            return 0.04;
        }
    }

    public Task<PhoneResponseDecision> PlanInitialAsync(
        PhoneResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var npc = TryLoadNpc(request.NpcId);
        var profile = GetOrCreateProfile(npc, request.NpcId);

        var nowUtc = DateTime.UtcNow;
        var nowLocal = DateTime.Now;

        var runtime =
            NpcPhoneRuntimeStateService.GetActive(
                request.NpcId,
                nowUtc);

        var situation =
            BuildSituation(
                npc,
                profile,
                runtime,
                nowLocal,
                request.Message);

        double firstCheckMinutes =
            EstimateFirstCheckMinutes(
                profile,
                situation);

        return Task.FromResult(
            new PhoneResponseDecision
            {
                Action = "retry_later",
                NoticeState = "unseen",
                NextCheckUtc =
                    AddSimulationMinutes(
                        nowUtc,
                        firstCheckMinutes),
                DecisionCode =
                    situation.InitialCode
            });
    }

    public Task<PhoneResponseDecision> ReconsiderAsync(
        PhoneResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var npc = TryLoadNpc(request.NpcId);
        var profile = GetOrCreateProfile(npc, request.NpcId);

        var nowUtc = DateTime.UtcNow;
        var nowLocal = DateTime.Now;

        var runtime =
            NpcPhoneRuntimeStateService.GetActive(
                request.NpcId,
                nowUtc);

        var situation =
            BuildSituation(
                npc,
                profile,
                runtime,
                nowLocal,
                request.Message);

        string noticeState =
            string.Equals(
                request.NoticeState,
                "seen",
                StringComparison.OrdinalIgnoreCase)
                ? "seen"
                : "unseen";

        // --------------------------------------------------------
        // HARD PHYSICAL BLOCKERS
        // --------------------------------------------------------

        // Driving: text can exist/deliver, but do not let the NPC
        // actively compose a normal reply while driving.
        if (situation.IsDriving)
        {
            return Task.FromResult(
                Retry(
                    noticeState,
                    nowUtc,
                    RandomBetween(3, 10),
                    "driving"));
        }

        // Emergency: strongest blocker. This is especially relevant
        // to Adam/Edward or future public-safety NPCs.
        if (situation.IsEmergency)
        {
            return Task.FromResult(
                Retry(
                    noticeState,
                    nowUtc,
                    RandomBetween(8, 25),
                    "emergency"));
        }

        // Sleeping normally blocks notice/reply. An urgent text is
        // still just a text; it may wake some people, but rarely.
        if (situation.IsSleeping &&
            situation.SleepIsExplicit)
        {
            bool wakesForUrgent =
                situation.Urgency >= 90
                && profile.UrgentResponsiveness >= 75
                && Roll(
                    8
                    + (profile.NightTextComfort * 0.12)
                    + (profile.UrgentResponsiveness * 0.10));

            if (!wakesForUrgent)
            {
                return Task.FromResult(
                    Retry(
                        noticeState,
                        nowUtc,
                        RandomBetween(15, 45),
                        "sleeping"));
            }
        }

        // --------------------------------------------------------
        // NOTICE
        // --------------------------------------------------------

        if (noticeState == "unseen")
        {
            double noticeChance =
                profile.CheckPhoneFrequency * 0.72
                + situation.Urgency * 0.23;

            // Real phone access matters more than being at work.
            noticeChance +=
                (situation.PhoneAccess - 50) * 0.55;

            if (situation.IsWorking)
            {
                noticeChance -=
                    situation.Workload * 0.22;

                noticeChance +=
                    profile.WorkTextComfort * 0.16;
            }

            if (situation.IsInMeeting)
                noticeChance -= 32;

            if (situation.IsSleeping &&
                !situation.SleepIsExplicit)
                noticeChance -= 28;

            noticeChance =
                Math.Clamp(
                    noticeChance,
                    3,
                    98);

            if (!Roll(noticeChance))
            {
                return Task.FromResult(
                    Retry(
                        "unseen",
                        nowUtc,
                        EstimateRecheckMinutes(
                            profile,
                            situation,
                            noticed: false),
                        "not_noticed"));
            }

            noticeState = "seen";
        }

        // --------------------------------------------------------
        // CAN ANSWER RIGHT NOW?
        // --------------------------------------------------------

        double ability =
            72
            + (situation.PhoneAccess - 50) * 0.60;

        if (situation.IsWorking)
        {
            ability -=
                situation.Workload * 0.42;

            ability +=
                profile.WorkTextComfort * 0.28;
        }

        if (situation.IsInMeeting)
            ability -= 42;

        if (situation.IsSleeping)
            ability -=
                situation.SleepIsExplicit
                    ? 45
                    : 22;

        ability +=
            situation.Urgency * 0.12;

        ability =
            Math.Clamp(
                ability,
                2,
                98);

        if (!Roll(ability))
        {
            return Task.FromResult(
                Retry(
                    noticeState,
                    nowUtc,
                    EstimateRecheckMinutes(
                        profile,
                        situation,
                        noticed: true),
                    situation.IsWorking
                        ? "busy_working"
                        : "cannot_answer_now"));
        }

        // --------------------------------------------------------
        // WANTS TO ANSWER?
        // --------------------------------------------------------

        double willingness =
            32
            + profile.ReplyPromptness * 0.30
            + situation.Urgency * 0.30;

        if (npc?.Traits != null)
        {
            float trust =
                npc.Traits.Get("trait.trust");
            float affection =
                npc.Traits.Get("trait.affection");
            float play =
                npc.Traits.Get("trait.playfulness");

            float anger =
                npc.Traits.Get("trait.anger");
            float hurt =
                npc.Traits.Get("trait.hurt");
            float guard =
                npc.Traits.Get("trait.guard");
            float tension =
                npc.Traits.Get("trait.tension");
            float resentment =
                npc.Traits.Get("trait.resentment");

            willingness +=
                (trust - 50) * 0.18;

            willingness +=
                (affection - 50) * 0.20;

            willingness +=
                (play - 50) * 0.08;

            willingness -=
                Math.Max(0, anger - 50) * 0.26;

            willingness -=
                Math.Max(0, hurt - 50) * 0.18;

            willingness -=
                Math.Max(0, guard - 50) * 0.20;

            willingness -=
                Math.Max(0, tension - 50) * 0.12;

            willingness -=
                Math.Max(0, resentment - 50) * 0.25;

            // Some characters intentionally wait when irritated.
            if (anger >= 65 || resentment >= 65)
            {
                willingness -=
                    profile.IgnoreWhenAngry * 0.20;
            }
        }

        if (situation.IsWorking)
        {
            // Work can delay, but never hard-block.
            willingness -=
                situation.Workload * 0.12;

            willingness +=
                profile.WorkTextComfort * 0.10;
        }

        // People who commonly read-and-wait are less likely to answer
        // at the first opportunity.
        willingness -=
            profile.ReadAndWaitTendency * 0.11;

        // Repeated reconsideration gradually raises the chance of a normal
        // reply, while still allowing genuinely ignored messages.
        willingness +=
            Math.Min(
                18,
                request.AttemptCount * 2.5);

        willingness =
            Math.Clamp(
                willingness,
                2,
                98);

        if (Roll(willingness))
        {
            return Task.FromResult(
                new PhoneResponseDecision
                {
                    Action = "reply_now",
                    NoticeState = noticeState,
                    NextCheckUtc = nowUtc,
                    DecisionCode =
                        situation.IsWorking
                            ? "reply_while_working"
                            : "reply_now"
                });
        }

        // --------------------------------------------------------
        // INTENTIONAL WAIT / POSSIBLE NO REPLY
        // --------------------------------------------------------

        bool normalOrLowUrgency =
            situation.Urgency < 65;

        bool lowWillingness =
            willingness < 30;

        // After enough opportunities, some normal messages simply never
        // receive a response. A later NEW text may still restart contact.
        if (request.AttemptCount >= 8
            && normalOrLowUrgency
            && lowWillingness
            && Roll(
                28
                + profile.ReadAndWaitTendency * 0.20
                + profile.IgnoreWhenAngry * 0.14))
        {
            return Task.FromResult(
                new PhoneResponseDecision
                {
                    Action = "leave_unanswered",
                    NoticeState = noticeState,
                    NextCheckUtc = nowUtc,
                    DecisionCode = "chose_not_to_reply"
                });
        }

        return Task.FromResult(
            Retry(
                noticeState,
                nowUtc,
                EstimateRecheckMinutes(
                    profile,
                    situation,
                    noticed: true),
                "seen_waiting"));
    }

    // ============================================================
    // SITUATION
    // ============================================================

    private PhoneSituation BuildSituation(
        SimCharacter? npc,
        PhoneBehaviorProfile profile,
        NpcPhoneRuntimeState? runtime,
        DateTime localNow,
        string message)
    {
        bool inferredWorking =
            IsWorkingNow(
                npc,
                localNow);

        bool inferredSleeping =
            IsLikelySleeping(
                profile,
                localNow);

        int inferredWorkload =
            InferWorkload(
                npc,
                inferredWorking);

        return new PhoneSituation
        {
            Urgency =
                ScoreUrgency(
                    message),

            IsWorking =
                runtime?.IsWorking
                ?? inferredWorking,

            IsSleeping =
                runtime?.IsSleeping
                ?? inferredSleeping,

            SleepIsExplicit =
                runtime != null
                && runtime.IsSleeping.HasValue,

            IsDriving =
                runtime?.IsDriving
                ?? false,

            IsEmergency =
                runtime?.IsEmergency
                ?? false,

            IsInMeeting =
                runtime?.IsInMeeting
                ?? false,

            PhoneAccess =
                Math.Clamp(
                    runtime?.PhoneAccess
                    ?? InferPhoneAccess(
                        npc,
                        inferredWorking),
                    0,
                    100),

            Workload =
                Math.Clamp(
                    runtime?.Workload
                    ?? inferredWorkload,
                    0,
                    100),

            InitialCode =
                runtime != null
                    ? "runtime_state"
                    : inferredWorking
                        ? "working_estimate"
                        : inferredSleeping
                            ? "sleep_estimate"
                            : "normal_estimate"
        };
    }

    private static bool IsWorkingNow(
        SimCharacter? npc,
        DateTime localNow)
    {
        var job = npc?.Job;

        if (job == null
            || string.IsNullOrWhiteSpace(job.JobName))
            return false;

        string today =
            localNow.DayOfWeek switch
            {
                DayOfWeek.Monday => "Mon",
                DayOfWeek.Tuesday => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday => "Thu",
                DayOfWeek.Friday => "Fri",
                DayOfWeek.Saturday => "Sat",
                DayOfWeek.Sunday => "Sun",
                _ => ""
            };

        bool dayMatches = true;

        try
        {
            if (job.WorkDays != null
                && job.WorkDays.Length > 0)
            {
                bool variable =
                    job.WorkDays.Any(
                        d => string.Equals(
                            d,
                            "varies",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            d,
                            "on call",
                            StringComparison.OrdinalIgnoreCase));

                bool exact =
                    job.WorkDays.Any(
                        d => string.Equals(
                            d,
                            today,
                            StringComparison.OrdinalIgnoreCase));

                dayMatches =
                    variable || exact;
            }
        }
        catch
        {
            dayMatches = true;
        }

        if (!dayMatches)
            return false;

        int start =
            Math.Clamp(
                job.StartHour,
                0,
                23);

        int end =
            Math.Clamp(
                job.EndHour,
                0,
                24);

        double hour =
            localNow.Hour
            + localNow.Minute / 60.0;

        if (start == end)
            return false;

        if (start < end)
            return hour >= start
                && hour < end;

        // overnight shift
        return hour >= start
            || hour < end;
    }

    private static int InferWorkload(
        SimCharacter? npc,
        bool working)
    {
        if (!working || npc?.Job == null)
            return 15;

        var job = npc.Job;

        double demand =
            job.StressLoad * 0.32
            + job.SocialDemand * 0.28
            + job.CognitiveDemand * 0.24
            + job.PhysicalDemand * 0.16;

        return (int)Math.Clamp(
            demand,
            15,
            90);
    }

    private static int InferPhoneAccess(
        SimCharacter? npc,
        bool working)
    {
        if (!working)
            return 82;

        if (npc?.Job == null)
            return 58;

        string mode =
            npc.Job.WorkLocationMode
            ?? "";

        string industry =
            npc.Job.IndustryPath
            ?? "";

        if (industry.Contains(
                "public_safety",
                StringComparison.OrdinalIgnoreCase))
            return 45;

        if (mode.Contains(
                "office",
                StringComparison.OrdinalIgnoreCase))
            return 66;

        if (mode.Contains(
                "station",
                StringComparison.OrdinalIgnoreCase))
            return 58;

        return 55;
    }

    private static bool IsLikelySleeping(
        PhoneBehaviorProfile profile,
        DateTime localNow)
    {
        double hour =
            localNow.Hour
            + localNow.Minute / 60.0;

        double sleep =
            profile.SleepStartHour;

        double wake =
            profile.WakeHour;

        if (sleep > wake)
            return hour >= sleep
                || hour < wake;

        return hour >= sleep
            && hour < wake;
    }

    // ============================================================
    // TIMING
    // ============================================================

    private static double EstimateFirstCheckMinutes(
        PhoneBehaviorProfile profile,
        PhoneSituation situation)
    {
        if (situation.IsDriving)
            return RandomStatic(3, 8);

        if (situation.IsEmergency)
            return RandomStatic(8, 20);

        if (situation.IsSleeping)
            return situation.SleepIsExplicit
                ? RandomStatic(15, 40)
                : RandomStatic(6, 18);

        double minutes =
            0.7
            + (100 - profile.CheckPhoneFrequency) / 12.0;

        minutes +=
            (100 - situation.PhoneAccess) / 18.0;

        if (situation.IsWorking)
        {
            minutes +=
                situation.Workload / 18.0;

            minutes -=
                profile.WorkTextComfort / 32.0;
        }

        if (situation.IsInMeeting)
            minutes += 8;

        minutes -=
            situation.Urgency / 25.0;

        return Math.Clamp(
            minutes,
            0.4,
            30);
    }

    private static double EstimateRecheckMinutes(
        PhoneBehaviorProfile profile,
        PhoneSituation situation,
        bool noticed)
    {
        double minutes =
            noticed
                ? 5
                : 3;

        minutes +=
            (100 - profile.ReplyPromptness) / 10.0;

        if (situation.IsWorking)
        {
            minutes +=
                situation.Workload / 8.0;

            minutes -=
                profile.WorkTextComfort / 20.0;
        }

        if (situation.IsInMeeting)
            minutes += 10;

        if (noticed)
            minutes +=
                profile.ReadAndWaitTendency / 14.0;

        minutes -=
            situation.Urgency / 16.0;

        return Math.Clamp(
            minutes,
            1,
            45);
    }

    private static DateTime AddSimulationMinutes(
        DateTime utcNow,
        double simulationMinutes)
    {
        double realMinutes =
            simulationMinutes
            * TimeScale;

        // Avoid zero-delay hot loops.
        realMinutes =
            Math.Max(
                realMinutes,
                0.025); // ~1.5 sec

        return utcNow.AddMinutes(
            realMinutes);
    }

    private PhoneResponseDecision Retry(
        string noticeState,
        DateTime nowUtc,
        double simulationMinutes,
        string code)
        => new()
        {
            Action = "retry_later",
            NoticeState = noticeState,
            NextCheckUtc =
                AddSimulationMinutes(
                    nowUtc,
                    simulationMinutes),
            DecisionCode = code
        };

    // ============================================================
    // PROFILE
    // ============================================================

    private PhoneBehaviorProfile GetOrCreateProfile(
        SimCharacter? npc,
        int npcId)
    {
        EnsureProfileSchema();

        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using (var find = conn.CreateCommand())
        {
            find.CommandText = """
                SELECT NpcId,
                       CheckPhoneFrequency,
                       ReplyPromptness,
                       WorkTextComfort,
                       ReadAndWaitTendency,
                       UrgentResponsiveness,
                       NightTextComfort,
                       IgnoreWhenAngry,
                       InitiatesContact,
                       EmojiUse,
                       DoubleTextTendency,
                       TypicalMessageLength,
                       SleepStartHour,
                       WakeHour
                FROM NpcPhoneBehaviorProfile
                WHERE NpcId=$npc
                LIMIT 1;
                """;

            find.Parameters.AddWithValue(
                "$npc",
                npcId);

            using var r =
                find.ExecuteReader();

            if (r.Read())
            {
                return new PhoneBehaviorProfile
                {
                    NpcId = r.GetInt32(0),
                    CheckPhoneFrequency = r.GetInt32(1),
                    ReplyPromptness = r.GetInt32(2),
                    WorkTextComfort = r.GetInt32(3),
                    ReadAndWaitTendency = r.GetInt32(4),
                    UrgentResponsiveness = r.GetInt32(5),
                    NightTextComfort = r.GetInt32(6),
                    IgnoreWhenAngry = r.GetInt32(7),
                    InitiatesContact = r.GetInt32(8),
                    EmojiUse = r.GetInt32(9),
                    DoubleTextTendency = r.GetInt32(10),
                    TypicalMessageLength = r.GetInt32(11),
                    SleepStartHour = r.GetDouble(12),
                    WakeHour = r.GetDouble(13)
                };
            }
        }

        var created =
            GenerateProfile(
                npc,
                npcId);

        SaveProfile(
            created);

        return created;
    }

    private PhoneBehaviorProfile GenerateProfile(
        SimCharacter? npc,
        int npcId)
    {
        int seed =
            unchecked(
                npcId * 7919
                + (npc?.Age ?? 25) * 101);

        var rng =
            new Random(seed);

        float Trait(
            string id,
            float fallback = 50)
        {
            try
            {
                if (npc?.Traits != null)
                    return npc.Traits.Get(id);
            }
            catch
            {
            }

            return fallback;
        }

        int socialDemand =
            npc?.Job?.SocialDemand
            ?? 50;

        int stress =
            npc?.Job?.StressLoad
            ?? 50;

        int workComfort =
            (int)Math.Clamp(
                72
                - socialDemand * 0.28
                - stress * 0.12
                + Trait("trait.openness") * 0.18
                + rng.Next(-8, 9),
                15,
                85);

        int check =
            (int)Math.Clamp(
                48
                + Trait("trait.openness") * 0.20
                + Trait("trait.playfulness") * 0.12
                - Trait("trait.patience") * 0.05
                + rng.Next(-10, 11),
                25,
                92);

        int promptness =
            (int)Math.Clamp(
                45
                + Trait("trait.affection") * 0.14
                + Trait("trait.openness") * 0.12
                - Trait("trait.guard") * 0.10
                + rng.Next(-10, 11),
                20,
                90);

        int wait =
            (int)Math.Clamp(
                30
                + Trait("trait.guard") * 0.20
                + Trait("trait.patience") * 0.10
                - Trait("trait.playfulness") * 0.08
                + rng.Next(-10, 11),
                10,
                85);

        int ignoreAngry =
            (int)Math.Clamp(
                28
                + Trait("trait.guard") * 0.20
                + Trait("trait.resentment") * 0.18
                + rng.Next(-8, 9),
                10,
                90);

        double wake =
            7.0;

        if (npc?.Job != null
            && npc.Job.StartHour > 0)
        {
            wake =
                Math.Clamp(
                    npc.Job.StartHour - 1.25,
                    4.0,
                    9.5);
        }

        double sleep =
            Math.Clamp(
                wake + 17.25,
                21.0,
                24.0);

        if (sleep >= 24)
            sleep -= 24;

        return new PhoneBehaviorProfile
        {
            NpcId = npcId,

            CheckPhoneFrequency =
                check,

            ReplyPromptness =
                promptness,

            WorkTextComfort =
                workComfort,

            ReadAndWaitTendency =
                wait,

            UrgentResponsiveness =
                ClampInt(
                    62
                    + (int)(Trait("trait.affection") * 0.15)
                    + rng.Next(-7, 8)),

            NightTextComfort =
                ClampInt(
                    28
                    + (int)(Trait("trait.openness") * 0.18)
                    + rng.Next(-10, 11)),

            IgnoreWhenAngry =
                ignoreAngry,

            InitiatesContact =
                ClampInt(
                    30
                    + (int)(Trait("trait.openness") * 0.22)
                    + (int)(Trait("trait.playfulness") * 0.14)
                    + rng.Next(-8, 9)),

            EmojiUse =
                ClampInt(
                    18
                    + (int)(Trait("trait.playfulness") * 0.42)
                    + rng.Next(-12, 13)),

            DoubleTextTendency =
                ClampInt(
                    22
                    + (int)(Trait("trait.anxiety") * 0.14)
                    + (int)(Trait("trait.playfulness") * 0.18)
                    + rng.Next(-10, 11)),

            TypicalMessageLength =
                ClampInt(
                    40
                    + (int)(Trait("trait.openness") * 0.12)
                    + rng.Next(-12, 13)),

            SleepStartHour =
                sleep,

            WakeHour =
                wake
        };
    }

    private void SaveProfile(
        PhoneBehaviorProfile p)
    {
        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using var cmd =
            conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcPhoneBehaviorProfile
            (NpcId,
             CheckPhoneFrequency,
             ReplyPromptness,
             WorkTextComfort,
             ReadAndWaitTendency,
             UrgentResponsiveness,
             NightTextComfort,
             IgnoreWhenAngry,
             InitiatesContact,
             EmojiUse,
             DoubleTextTendency,
             TypicalMessageLength,
             SleepStartHour,
             WakeHour,
             CreatedUtc,
             UpdatedUtc)
            VALUES
            ($npc,$check,$prompt,$work,$wait,
             $urgent,$night,$ignore,$init,$emoji,
             $double,$length,$sleep,$wake,$utc,$utc)
            ON CONFLICT(NpcId) DO UPDATE SET
                CheckPhoneFrequency=excluded.CheckPhoneFrequency,
                ReplyPromptness=excluded.ReplyPromptness,
                WorkTextComfort=excluded.WorkTextComfort,
                ReadAndWaitTendency=excluded.ReadAndWaitTendency,
                UrgentResponsiveness=excluded.UrgentResponsiveness,
                NightTextComfort=excluded.NightTextComfort,
                IgnoreWhenAngry=excluded.IgnoreWhenAngry,
                InitiatesContact=excluded.InitiatesContact,
                EmojiUse=excluded.EmojiUse,
                DoubleTextTendency=excluded.DoubleTextTendency,
                TypicalMessageLength=excluded.TypicalMessageLength,
                SleepStartHour=excluded.SleepStartHour,
                WakeHour=excluded.WakeHour,
                UpdatedUtc=excluded.UpdatedUtc;
            """;

        cmd.Parameters.AddWithValue(
            "$npc",
            p.NpcId);
        cmd.Parameters.AddWithValue(
            "$check",
            p.CheckPhoneFrequency);
        cmd.Parameters.AddWithValue(
            "$prompt",
            p.ReplyPromptness);
        cmd.Parameters.AddWithValue(
            "$work",
            p.WorkTextComfort);
        cmd.Parameters.AddWithValue(
            "$wait",
            p.ReadAndWaitTendency);
        cmd.Parameters.AddWithValue(
            "$urgent",
            p.UrgentResponsiveness);
        cmd.Parameters.AddWithValue(
            "$night",
            p.NightTextComfort);
        cmd.Parameters.AddWithValue(
            "$ignore",
            p.IgnoreWhenAngry);
        cmd.Parameters.AddWithValue(
            "$init",
            p.InitiatesContact);
        cmd.Parameters.AddWithValue(
            "$emoji",
            p.EmojiUse);
        cmd.Parameters.AddWithValue(
            "$double",
            p.DoubleTextTendency);
        cmd.Parameters.AddWithValue(
            "$length",
            p.TypicalMessageLength);
        cmd.Parameters.AddWithValue(
            "$sleep",
            p.SleepStartHour);
        cmd.Parameters.AddWithValue(
            "$wake",
            p.WakeHour);
        cmd.Parameters.AddWithValue(
            "$utc",
            DateTime.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
    }

    private static void EnsureProfileSchema()
    {
        var dir =
            Path.GetDirectoryName(DbPath);

        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using var cmd =
            conn.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcPhoneBehaviorProfile(
                NpcId INTEGER PRIMARY KEY,
                CheckPhoneFrequency INTEGER NOT NULL,
                ReplyPromptness INTEGER NOT NULL,
                WorkTextComfort INTEGER NOT NULL,
                ReadAndWaitTendency INTEGER NOT NULL,
                UrgentResponsiveness INTEGER NOT NULL,
                NightTextComfort INTEGER NOT NULL,
                IgnoreWhenAngry INTEGER NOT NULL,
                InitiatesContact INTEGER NOT NULL,
                EmojiUse INTEGER NOT NULL,
                DoubleTextTendency INTEGER NOT NULL,
                TypicalMessageLength INTEGER NOT NULL,
                SleepStartHour REAL NOT NULL,
                WakeHour REAL NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;

        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // MESSAGE IMPORTANCE
    // ============================================================

    private static int ScoreUrgency(
        string? message)
    {
        string s =
            (message ?? "")
            .Trim()
            .ToLowerInvariant();

        if (s.Length == 0)
            return 10;

        if (ContainsAny(
                s,
                "911",
                "emergency",
                "hospital",
                "ambulance",
                "i'm hurt",
                "im hurt",
                "someone is hurt",
                "call me now"))
            return 98;

        if (ContainsAny(
                s,
                "urgent",
                "need you",
                "please call",
                "call me",
                "are you okay",
                "are you ok",
                "where are you"))
            return 82;

        if (ContainsAny(
                s,
                "meet",
                "tonight",
                "today",
                "plans",
                "when",
                "where"))
            return 58;

        if (s.Contains('?'))
            return 52;

        if (ContainsAny(
                s,
                "hey",
                "hi",
                "hello",
                "lol",
                "lmao"))
            return 28;

        return 40;
    }

    private static bool ContainsAny(
        string text,
        params string[] values)
    {
        foreach (string v in values)
        {
            if (text.Contains(
                    v,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private SimCharacter? TryLoadNpc(
        int npcId)
    {
        try
        {
            return CharacterRepository.LoadCharacter(
                npcId);
        }
        catch
        {
            return null;
        }
    }

    private bool Roll(
        double percent)
    {
        percent =
            Math.Clamp(
                percent,
                0,
                100);

        lock (_rngLock)
        {
            return _rng.NextDouble() * 100.0
                < percent;
        }
    }

    private double RandomBetween(
        double min,
        double max)
    {
        if (max < min)
            (min, max) =
                (max, min);

        lock (_rngLock)
        {
            return min
                + _rng.NextDouble()
                * (max - min);
        }
    }

    private static double RandomStatic(
        double min,
        double max)
    {
        if (max < min)
            (min, max) =
                (max, min);

        return min
            + Random.Shared.NextDouble()
            * (max - min);
    }

    private static int ClampInt(
        int value)
        => Math.Clamp(
            value,
            0,
            100);
}

public sealed class PhoneBehaviorProfile
{
    public int NpcId { get; set; }

    public int CheckPhoneFrequency { get; set; } = 60;
    public int ReplyPromptness { get; set; } = 55;
    public int WorkTextComfort { get; set; } = 45;
    public int ReadAndWaitTendency { get; set; } = 40;
    public int UrgentResponsiveness { get; set; } = 75;
    public int NightTextComfort { get; set; } = 35;
    public int IgnoreWhenAngry { get; set; } = 45;

    public int InitiatesContact { get; set; } = 40;
    public int EmojiUse { get; set; } = 35;
    public int DoubleTextTendency { get; set; } = 35;
    public int TypicalMessageLength { get; set; } = 50;

    public double SleepStartHour { get; set; } = 23.0;
    public double WakeHour { get; set; } = 7.0;
}

internal sealed class PhoneSituation
{
    public int Urgency { get; set; }

    public bool IsWorking { get; set; }
    public bool IsSleeping { get; set; }
    public bool SleepIsExplicit { get; set; }
    public bool IsDriving { get; set; }
    public bool IsEmergency { get; set; }
    public bool IsInMeeting { get; set; }

    public int PhoneAccess { get; set; }
    public int Workload { get; set; }

    public string InitialCode { get; set; } = "";
}
