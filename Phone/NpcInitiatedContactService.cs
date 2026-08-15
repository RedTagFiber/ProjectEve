using Microsoft.Data.Sqlite;
using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Conversations;
using ProjectEve.Core.Knowledge;
using ProjectEve.Core.Phone;
using ProjectEve.Core.Time;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Phone;

/// <summary>
/// Phase 16 NPC initiated communication.
///
/// ProjectEve owns:
/// - why the NPC wants contact
/// - whether the NPC commits to contact
/// - what facts the source NPC is allowed to use
/// - game-time scheduling
/// - exact generated outbound text in the conversation transcript
///
/// PhoneOS only delivers staged text into the phone database.
/// </summary>
public sealed class NpcInitiatedContactService : INpcInitiatedContactService
{
    private readonly INpcKnowledgeService _knowledge;
    private readonly IGameTimeService _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    public NpcInitiatedContactService(
        INpcKnowledgeService knowledge,
        IGameTimeService clock)
    {
        _knowledge = knowledge;
        _clock = clock;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        ConversationManager.Initialize();
        EnsureSchema();
    }

    public async Task<NpcInitiatedScheduleResult> ScheduleAsync(
        NpcInitiatedContactRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateScheduleRequest(request);

        string channel = NormalizeChannel(request.Channel);
        if (channel != "text")
        {
            return new NpcInitiatedScheduleResult
            {
                Scheduled = false,
                DueGameTime = request.DueGameTime,
                Decision = "call_channel_reserved_for_future_phase"
            };
        }

        var npc = CharacterRepository.LoadCharacter(request.NpcId);
        if (npc == null)
            throw new InvalidOperationException(
                $"ProjectEve could not load NPC {request.NpcId}.");

        string npcName = !string.IsNullOrWhiteSpace(request.NpcNameHint)
            ? request.NpcNameHint.Trim()
            : npc.Name;

        var validated = await BuildValidatedContextAsync(
            request,
            cancellationToken);

        double score = ContactCommitScore(npc, request);
        bool committed = request.ForceCommit ||
                         DeterministicRoll(
                             request.PlayerId,
                             request.NpcId,
                             request.SourceKey,
                             request.DueGameTime) < score;

        if (!committed)
        {
            return new NpcInitiatedScheduleResult
            {
                Scheduled = false,
                DueGameTime = request.DueGameTime,
                Decision = "npc_did_not_commit_to_contact",
                ContactScore = score
            };
        }

        long triggerId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            if (!string.IsNullOrWhiteSpace(request.SourceKey))
            {
                var existing = LoadBySourceKey(conn, request.SourceKey.Trim());
                if (existing != null)
                {
                    return new NpcInitiatedScheduleResult
                    {
                        Scheduled = existing.Status is "scheduled" or "generated" or "delivered",
                        TriggerId = existing.Id,
                        GameEventId = existing.GameEventId,
                        DueGameTime = existing.DueGameTime,
                        Decision = "existing_source_key",
                        ContactScore = existing.ContactScore
                    };
                }
            }

            triggerId = InsertTrigger(
                conn,
                request,
                npcName,
                channel,
                validated,
                score);
        }
        finally
        {
            _gate.Release();
        }

        long gameEventId;

        try
        {
            gameEventId = await _clock.SchedulePlayerEventAsync(
                new GameEventScheduleRequest
                {
                    PlayerId = request.PlayerId,
                    EventType = "npc_initiated_contact_due",
                    Title = "Phone notification",
                    GameTime = request.DueGameTime,
                    InterruptFastForward = true,
                    SourceKey = $"npc-init-contact:{triggerId}:0",
                    DataJson = $"{{\"triggerId\":{triggerId}}}"
                },
                cancellationToken);
        }
        catch
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                MarkTriggerStatus(
                    triggerId,
                    "failed",
                    "game_event_schedule_failed");
            }
            finally
            {
                _gate.Release();
            }

            throw;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetGameEventId(triggerId, gameEventId);
        }
        finally
        {
            _gate.Release();
        }

        return new NpcInitiatedScheduleResult
        {
            Scheduled = true,
            TriggerId = triggerId,
            GameEventId = gameEventId,
            DueGameTime = request.DueGameTime,
            Decision = request.ForceCommit
                ? "forced_world_commitment"
                : "npc_committed_to_contact",
            ContactScore = score
        };
    }

    public async Task<int> EnsureSpontaneousCheckInsAsync(
        NpcSpontaneousContactDayRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string playerId = Clean(request.PlayerId, "");
        if (playerId.Length == 0)
            return 0;

        DateTimeOffset now =
            request.GameTime == default ? _clock.Now : request.GameTime;

        int max = Math.Clamp(
            request.MaxSpontaneousContactsPerDay,
            0,
            4);

        if (max == 0 || request.Contacts.Count == 0)
            return 0;

        var dayKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            if (HasDailySeed(conn, playerId, dayKey))
                return 0;

            InsertDailySeed(conn, playerId, dayKey);
        }
        finally
        {
            _gate.Release();
        }

        var scored = new List<SpontaneousCandidateScore>();

        foreach (var c in request.Contacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (c.NpcId <= 0 || c.IsBlocked)
                continue;

            var npc = CharacterRepository.LoadCharacter(c.NpcId);
            if (npc == null)
                continue;

            var profile = LoadPhoneProfile(c.NpcId);
            double score = SpontaneousScore(
                npc,
                profile,
                c.ContactTier);

            double roll = StablePercent(
                $"spontaneous-roll|{playerId}|{c.NpcId}|{dayKey}");

            double chance = Math.Clamp(
                score * 0.35,
                2,
                48);

            if (roll > chance)
                continue;

            scored.Add(new SpontaneousCandidateScore
            {
                Candidate = c,
                Score = score,
                TieBreak = StablePercent(
                    $"spontaneous-order|{playerId}|{c.NpcId}|{dayKey}"),
                Profile = profile
            });
        }

        var chosen = scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.TieBreak)
            .Take(max)
            .ToList();

        int scheduled = 0;

        foreach (var item in chosen)
        {
            var due = SpontaneousDueTime(
                now,
                item.Profile,
                playerId,
                item.Candidate.NpcId,
                dayKey);

            var result = await ScheduleAsync(
                new NpcInitiatedContactRequest
                {
                    PlayerId = playerId,
                    PlayerName = Clean(request.PlayerName, "Player"),
                    NpcId = item.Candidate.NpcId,
                    NpcNameHint = item.Candidate.NpcName,
                    Channel = "text",
                    Kind = "spontaneous_check_in",
                    Motive = "check_in",
                    DueGameTime = due,
                    Urgency = 15,
                    Commitment = 75,
                    ContextText =
                        "No external event is being asserted. " +
                        "This NPC simply chose to check in with the player.",
                    ForceCommit = true,
                    AllowUnknownNumber = false,
                    MaxMessageCharacters = 260,
                    SourceKey =
                        $"spontaneous:{playerId}:{item.Candidate.NpcId}:{dayKey}"
                },
                cancellationToken);

            if (result.Scheduled)
                scheduled++;
        }

        return scheduled;
    }

    public async Task<IReadOnlyList<NpcInitiatedOutboundMessage>> ProcessDueAsync(
        DateTimeOffset gameTime,
        CancellationToken cancellationToken = default)
    {
        List<TriggerRow> due;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            due = LoadDue(conn, gameTime);
        }
        finally
        {
            _gate.Release();
        }

        var outbound = new List<NpcInitiatedOutboundMessage>();

        foreach (var row in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (row.Status.Equals(
                    "generated",
                    StringComparison.OrdinalIgnoreCase))
            {
                outbound.Add(ToOutbound(row));
                continue;
            }

            if (!row.Channel.Equals(
                    "text",
                    StringComparison.OrdinalIgnoreCase))
            {
                await MarkSkippedAsync(
                    row.Id,
                    "channel_not_implemented",
                    cancellationToken);

                continue;
            }

            var activeId = ConversationManager.GetActiveSessionId(
                row.PlayerId,
                row.NpcId,
                row.PlayerName);

            if (activeId.HasValue)
            {
                var active = ConversationManager.GetSession(activeId.Value);

                if (active != null &&
                    active.Status.Equals(
                        "open",
                        StringComparison.OrdinalIgnoreCase) &&
                    !active.Channel.Equals(
                        "text",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await PostponeAsync(
                        row,
                        gameTime.AddMinutes(10),
                        "same_pair_active_in_person",
                        cancellationToken);

                    continue;
                }
            }

            try
            {
                var staged = await GenerateAndStageAsync(
                    row,
                    gameTime,
                    cancellationToken);

                outbound.Add(staged);
            }
            catch
            {
                await PostponeAsync(
                    row,
                    gameTime.AddMinutes(5),
                    "generation_retry",
                    cancellationToken);
            }
        }

        return outbound;
    }

    public async Task MarkDeliveredAsync(
        long triggerId,
        long phoneMessageId,
        CancellationToken cancellationToken = default)
    {
        long? eventId = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            var row = LoadById(conn, triggerId);
            if (row == null)
                return;

            eventId = row.GameEventId;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NpcInitiatedContactTrigger
                SET Status='delivered',
                    PhoneMessageId=$phone,
                    DeliveredGameTime=$game,
                    DecisionCode='delivered',
                    UpdatedRealUtc=$real
                WHERE Id=$id;
                """;

            cmd.Parameters.AddWithValue("$phone", phoneMessageId);
            cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", triggerId);

            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }

        if (eventId.HasValue)
        {
            try
            {
                await _clock.MarkEventHandledAsync(
                    eventId.Value,
                    cancellationToken);
            }
            catch
            {
            }
        }
    }

    public async Task MarkSkippedAsync(
        long triggerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        long? eventId = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            var row = LoadById(conn, triggerId);
            if (row == null)
                return;

            eventId = row.GameEventId;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NpcInitiatedContactTrigger
                SET Status='skipped',
                    DecisionCode=$reason,
                    UpdatedRealUtc=$real
                WHERE Id=$id;
                """;

            cmd.Parameters.AddWithValue(
                "$reason",
                Clean(reason, "skipped"));

            cmd.Parameters.AddWithValue(
                "$real",
                DateTime.UtcNow.ToString("O"));

            cmd.Parameters.AddWithValue("$id", triggerId);

            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }

        if (eventId.HasValue)
        {
            try
            {
                await _clock.MarkEventHandledAsync(
                    eventId.Value,
                    cancellationToken);
            }
            catch
            {
            }
        }
    }

    public async Task<IReadOnlyList<NpcInitiatedContactAudit>> GetPendingAsync(
        string playerId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                SELECT Id,PlayerId,NpcId,NpcName,Kind,Channel,
                       DueGameTime,Status,DecisionCode,
                       GameEventId,PhoneMessageId,GeneratedText
                FROM NpcInitiatedContactTrigger
                WHERE PlayerId=$player
                  AND Status IN ('scheduled','generated')
                ORDER BY DueGameTime,Id
                LIMIT $limit;
                """;

            cmd.Parameters.AddWithValue(
                "$player",
                Clean(playerId, ""));

            cmd.Parameters.AddWithValue(
                "$limit",
                Math.Clamp(limit, 1, 500));

            var rows = new List<NpcInitiatedContactAudit>();

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                rows.Add(new NpcInitiatedContactAudit
                {
                    Id = r.GetInt64(0),
                    PlayerId = r.GetString(1),
                    NpcId = r.GetInt32(2),
                    NpcName = r.GetString(3),
                    Kind = r.GetString(4),
                    Channel = r.GetString(5),
                    DueGameTime = ParseTime(r.GetString(6)),
                    Status = r.GetString(7),
                    DecisionCode = r.GetString(8),
                    GameEventId = r.IsDBNull(9)
                        ? null
                        : r.GetInt64(9),
                    PhoneMessageId = r.IsDBNull(10)
                        ? null
                        : r.GetInt64(10),
                    GeneratedText = r.GetString(11)
                });
            }

            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<NpcInitiatedOutboundMessage> GenerateAndStageAsync(
        TriggerRow row,
        DateTimeOffset gameTime,
        CancellationToken cancellationToken)
    {
        var npc = CharacterRepository.LoadCharacter(row.NpcId)
            ?? throw new InvalidOperationException(
                $"ProjectEve could not load NPC {row.NpcId}.");

        var closed = await ConversationManager.EndOpenSectionsExceptAsync(
            row.PlayerId,
            npc.Id,
            row.PlayerName,
            "text",
            "phone",
            "NPC initiated phone conversation",
            cancellationToken);

        foreach (var x in closed)
        {
            if (x.EventId <= 0)
                continue;

            try
            {
                await _knowledge.ImportConversationEventAsync(
                    x.EventId,
                    cancellationToken);
            }
            catch
            {
            }
        }

        long sessionId = ConversationManager.StartOrResume(
            row.PlayerId,
            npc.Id,
            npc.Name,
            row.PlayerName,
            "text",
            "phone",
            gameTime.LocalDateTime);

        string personalKnowledge =
            await _knowledge.BuildPromptContextAsync(
                npc.Id,
                row.PlayerId,
                row.PlayerName,
                cancellationToken: cancellationToken);

        string conversationContext = ConversationPromptContext.Build(
            npc,
            row.PlayerId,
            row.PlayerName,
            sessionId,
            "text",
            "phone",
            personalKnowledgeContext: personalKnowledge);

        string previousNpcText = ConversationManager
            .GetTranscript(sessionId)
            .Where(x => x.Role.Equals(
                "npc",
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.MessageText)
            .LastOrDefault() ?? "";

        var profile = LoadPhoneProfile(row.NpcId);

        string phoneStyle = PhoneStyle(profile);

        var generated = await NpcInitiatedTextEngine.GenerateAsync(
            npc,
            row.PlayerName,
            row.Kind,
            row.Motive,
            row.ValidatedContext,
            conversationContext,
            previousNpcText,
            phoneStyle,
            row.MaxMessageCharacters,
            cancellationToken);

        if (!generated.Source.Equals(
                "ai_initiated",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(generated.Text))
        {
            throw new InvalidOperationException(
                generated.Error ??
                "Outbound message generation failed.");
        }

        ConversationManager.AppendNpc(
            sessionId,
            npc.Id,
            npc.Name,
            generated.Text,
            gameTime.LocalDateTime);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                UPDATE NpcInitiatedContactTrigger
                SET Status='generated',
                    GeneratedText=$text,
                    ConversationSessionId=$session,
                    GeneratedGameTime=$game,
                    DecisionCode='generated',
                    UpdatedRealUtc=$real
                WHERE Id=$id
                  AND Status='scheduled';
                """;

            cmd.Parameters.AddWithValue(
                "$text",
                generated.Text);

            cmd.Parameters.AddWithValue(
                "$session",
                sessionId);

            cmd.Parameters.AddWithValue(
                "$game",
                gameTime.ToString("O"));

            cmd.Parameters.AddWithValue(
                "$real",
                DateTime.UtcNow.ToString("O"));

            cmd.Parameters.AddWithValue(
                "$id",
                row.Id);

            cmd.ExecuteNonQuery();

            var refreshed = LoadById(conn, row.Id)
                ?? throw new InvalidOperationException(
                    "Staged trigger disappeared.");

            return ToOutbound(refreshed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> BuildValidatedContextAsync(
        NpcInitiatedContactRequest request,
        CancellationToken cancellationToken)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.ContextText))
        {
            parts.Add(
                "SIMULATION/AUTHORED CONTEXT:\n" +
                request.ContextText.Trim());
        }

        if (request.ClaimId.HasValue)
        {
            var claims = await _knowledge.GetKnowledgeAsync(
                request.NpcId,
                request.PlayerId,
                500,
                cancellationToken);

            var claim = claims.FirstOrDefault(
                x => x.Id == request.ClaimId.Value);

            if (claim == null ||
                claim.HolderNpcId != request.NpcId)
            {
                throw new InvalidOperationException(
                    $"NPC {request.NpcId} does not own knowledge claim {request.ClaimId.Value}.");
            }

            parts.Add(
                "PERSONAL KNOWLEDGE CLAIM THIS NPC ACTUALLY HOLDS:\n" +
                claim.ClaimText);
        }

        if (request.ConversationPlanId.HasValue)
        {
            var plan = LoadConversationPlan(
                request.ConversationPlanId.Value,
                request.PlayerId,
                request.NpcId);

            if (plan == null)
            {
                throw new InvalidOperationException(
                    $"Conversation plan {request.ConversationPlanId.Value} does not belong to this player/NPC.");
            }

            parts.Add(
                "EXISTING CONVERSATION PLAN:\n" +
                plan.Description +
                (string.IsNullOrWhiteSpace(plan.TimeText)
                    ? ""
                    : "\nTime wording: " + plan.TimeText) +
                (string.IsNullOrWhiteSpace(plan.Location)
                    ? ""
                    : "\nLocation: " + plan.Location));
        }

        if (parts.Count == 0 &&
            NormalizeKind(request.Kind) == "spontaneous_check_in")
        {
            parts.Add(
                "No external event is being asserted. " +
                "This is simply a natural check-in chosen by the NPC.");
        }

        return string.Join("\n\n", parts);
    }

    private double ContactCommitScore(
        SimCharacter npc,
        NpcInitiatedContactRequest request)
    {
        var profile = LoadPhoneProfile(npc.Id);

        double score =
            request.Commitment * 0.48 +
            request.Urgency * 0.24 +
            profile.InitiatesContact * 0.28;

        float Trait(string id, float fallback = 50)
        {
            try
            {
                return npc.Traits?.Get(id) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        score += (Trait("trait.affection") - 50) * 0.10;
        score += (Trait("trait.trust") - 50) * 0.07;
        score += (Trait("trait.playfulness") - 50) * 0.04;

        score -= Math.Max(
            0,
            Trait("trait.guard") - 50) * 0.06;

        score -= Math.Max(
            0,
            Trait("trait.resentment") - 50) * 0.08;

        string kind = NormalizeKind(request.Kind);

        if (kind is "emergency" or "warning")
            score += 24;
        else if (kind is "promise" or "reminder")
            score += 16;
        else if (kind == "apology")
            score += 5;

        return Math.Clamp(score, 2, 99);
    }

    private double SpontaneousScore(
        SimCharacter npc,
        PhoneProfileSnapshot profile,
        int contactTier)
    {
        float Trait(string id, float fallback = 50)
        {
            try
            {
                return npc.Traits?.Get(id) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        double tierBoost = Math.Clamp(
            contactTier,
            1,
            5) switch
        {
            1 => 16,
            2 => 9,
            3 => 2,
            4 => -6,
            _ => -12
        };

        double score =
            profile.InitiatesContact * 0.55 +
            Trait("trait.affection") * 0.13 +
            Trait("trait.trust") * 0.10 +
            Trait("trait.playfulness") * 0.08 -
            Trait("trait.guard") * 0.05 -
            Math.Max(0, Trait("trait.anger") - 50) * 0.07 -
            Math.Max(0, Trait("trait.resentment") - 50) * 0.08 +
            tierBoost;

        return Math.Clamp(score, 5, 95);
    }

    private DateTimeOffset SpontaneousDueTime(
        DateTimeOffset now,
        PhoneProfileSnapshot profile,
        string playerId,
        int npcId,
        string dayKey)
    {
        double wake = Math.Clamp(
            profile.WakeHour,
            4,
            11);

        double sleep = profile.SleepStartHour;

        if (sleep < 12)
            sleep += 24;

        double start = Math.Max(
            wake + 1.0,
            8.0);

        double end = Math.Min(
            sleep - 1.0,
            22.0);

        if (end <= start + 1)
            end = start + 3;

        double p = StablePercent(
            $"spontaneous-time|{playerId}|{npcId}|{dayKey}") / 100.0;

        double hour = start + ((end - start) * p);

        int h = ((int)Math.Floor(hour)) % 24;
        int m = (int)Math.Round(
            (hour - Math.Floor(hour)) * 60.0);

        if (m >= 60)
        {
            h = (h + 1) % 24;
            m = 0;
        }

        var candidate = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            h,
            m,
            0,
            now.Offset);

        if (candidate <= now)
        {
            int delay = 20 + (int)Math.Round(
                StablePercent(
                    $"spontaneous-late|{playerId}|{npcId}|{dayKey}"));

            candidate = now.AddMinutes(
                Math.Clamp(delay, 20, 120));
        }

        return candidate;
    }

    private PhoneProfileSnapshot LoadPhoneProfile(int npcId)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                SELECT InitiatesContact,EmojiUse,DoubleTextTendency,
                       TypicalMessageLength,SleepStartHour,WakeHour
                FROM NpcPhoneBehaviorProfile
                WHERE NpcId=$npc
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue("$npc", npcId);

            using var r = cmd.ExecuteReader();

            if (r.Read())
            {
                return new PhoneProfileSnapshot
                {
                    InitiatesContact = r.GetInt32(0),
                    EmojiUse = r.GetInt32(1),
                    DoubleTextTendency = r.GetInt32(2),
                    TypicalMessageLength = r.GetInt32(3),
                    SleepStartHour = r.GetDouble(4),
                    WakeHour = r.GetDouble(5)
                };
            }
        }
        catch
        {
        }

        return new PhoneProfileSnapshot();
    }

    private static string PhoneStyle(
        PhoneProfileSnapshot p)
    {
        string emoji = p.EmojiUse switch
        {
            >= 70 => "often comfortable using emoji",
            >= 40 => "sometimes uses emoji",
            _ => "rarely uses emoji"
        };

        string doubleText = p.DoubleTextTendency switch
        {
            >= 70 => "comfortable sending a second short line",
            >= 40 => "may split a thought into two messages",
            _ => "usually sends one compact message"
        };

        string length = p.TypicalMessageLength switch
        {
            >= 70 => "often writes somewhat longer texts",
            >= 40 => "usually medium-length texts",
            _ => "usually concise"
        };

        return $"{emoji}; {doubleText}; {length}.";
    }

    private ConversationPlanSnapshot? LoadConversationPlan(
        long planId,
        string playerId,
        int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT Description,TimeText,Location,Status
            FROM ConversationPlan
            WHERE Id=$id
              AND PlayerId=$player
              AND NpcId=$npc
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue("$id", planId);
        cmd.Parameters.AddWithValue("$player", playerId);
        cmd.Parameters.AddWithValue("$npc", npcId);

        try
        {
            using var r = cmd.ExecuteReader();

            if (!r.Read())
                return null;

            if (!r.GetString(3).Equals(
                    "planned",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new ConversationPlanSnapshot
            {
                Description = r.GetString(0),
                TimeText = r.GetString(1),
                Location = r.GetString(2)
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task PostponeAsync(
        TriggerRow row,
        DateTimeOffset newDue,
        string reason,
        CancellationToken cancellationToken)
    {
        long retryEventId =
            await _clock.SchedulePlayerEventAsync(
                new GameEventScheduleRequest
                {
                    PlayerId = row.PlayerId,
                    EventType = "npc_initiated_contact_due",
                    Title = "Phone notification",
                    GameTime = newDue,
                    InterruptFastForward = true,
                    SourceKey =
                        $"npc-init-contact:{row.Id}:{row.RetryCount + 1}",
                    DataJson =
                        $"{{\"triggerId\":{row.Id}}}"
                },
                cancellationToken);

        if (row.GameEventId.HasValue)
        {
            try
            {
                await _clock.MarkEventHandledAsync(
                    row.GameEventId.Value,
                    cancellationToken);
            }
            catch
            {
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                UPDATE NpcInitiatedContactTrigger
                SET DueGameTime=$due,
                    GameEventId=$event,
                    RetryCount=RetryCount+1,
                    DecisionCode=$reason,
                    UpdatedRealUtc=$real
                WHERE Id=$id
                  AND Status='scheduled';
                """;

            cmd.Parameters.AddWithValue(
                "$due",
                newDue.ToString("O"));

            cmd.Parameters.AddWithValue(
                "$event",
                retryEventId);

            cmd.Parameters.AddWithValue(
                "$reason",
                reason);

            cmd.Parameters.AddWithValue(
                "$real",
                DateTime.UtcNow.ToString("O"));

            cmd.Parameters.AddWithValue(
                "$id",
                row.Id);

            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }
    }

    private long InsertTrigger(
        SqliteConnection conn,
        NpcInitiatedContactRequest request,
        string npcName,
        string channel,
        string validatedContext,
        double score)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcInitiatedContactTrigger
            (PlayerId,PlayerName,NpcId,NpcName,Channel,Kind,Motive,
             DueGameTime,Urgency,Commitment,ContactScore,
             ContextText,ValidatedContext,ClaimId,ConversationPlanId,
             AllowUnknownNumber,MaxMessageCharacters,SourceKey,
             Status,DecisionCode,RetryCount,GeneratedText,
             ConversationSessionId,GameEventId,PhoneMessageId,
             CreatedGameTime,GeneratedGameTime,DeliveredGameTime,
             CreatedRealUtc,UpdatedRealUtc)
            VALUES
            ($player,$playerName,$npc,$npcName,$channel,$kind,$motive,
             $due,$urgency,$commitment,$score,
             $context,$validated,$claim,$plan,
             $unknown,$maxChars,$source,
             'scheduled','scheduled',0,'',
             NULL,NULL,NULL,
             $createdGame,NULL,NULL,
             $real,$real);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue(
            "$player",
            request.PlayerId);

        cmd.Parameters.AddWithValue(
            "$playerName",
            request.PlayerName);

        cmd.Parameters.AddWithValue(
            "$npc",
            request.NpcId);

        cmd.Parameters.AddWithValue(
            "$npcName",
            npcName);

        cmd.Parameters.AddWithValue(
            "$channel",
            channel);

        cmd.Parameters.AddWithValue(
            "$kind",
            NormalizeKind(request.Kind));

        cmd.Parameters.AddWithValue(
            "$motive",
            Clean(request.Motive, "follow_up"));

        cmd.Parameters.AddWithValue(
            "$due",
            request.DueGameTime.ToString("O"));

        cmd.Parameters.AddWithValue(
            "$urgency",
            Math.Clamp(request.Urgency, 0, 100));

        cmd.Parameters.AddWithValue(
            "$commitment",
            Math.Clamp(request.Commitment, 0, 100));

        cmd.Parameters.AddWithValue(
            "$score",
            score);

        cmd.Parameters.AddWithValue(
            "$context",
            Clean(request.ContextText, ""));

        cmd.Parameters.AddWithValue(
            "$validated",
            validatedContext);

        cmd.Parameters.AddWithValue(
            "$claim",
            request.ClaimId.HasValue
                ? request.ClaimId.Value
                : (object)DBNull.Value);

        cmd.Parameters.AddWithValue(
            "$plan",
            request.ConversationPlanId.HasValue
                ? request.ConversationPlanId.Value
                : (object)DBNull.Value);

        cmd.Parameters.AddWithValue(
            "$unknown",
            request.AllowUnknownNumber ? 1 : 0);

        cmd.Parameters.AddWithValue(
            "$maxChars",
            Math.Clamp(
                request.MaxMessageCharacters,
                40,
                1200));

        cmd.Parameters.AddWithValue(
            "$source",
            string.IsNullOrWhiteSpace(request.SourceKey)
                ? DBNull.Value
                : request.SourceKey.Trim());

        cmd.Parameters.AddWithValue(
            "$createdGame",
            _clock.Now.ToString("O"));

        cmd.Parameters.AddWithValue(
            "$real",
            DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(
            cmd.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private List<TriggerRow> LoadDue(
        SqliteConnection conn,
        DateTimeOffset gameTime)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT Id,PlayerId,PlayerName,NpcId,NpcName,Channel,Kind,Motive,
                   DueGameTime,Urgency,Commitment,ContactScore,
                   ValidatedContext,ClaimId,ConversationPlanId,
                   AllowUnknownNumber,MaxMessageCharacters,SourceKey,
                   Status,DecisionCode,RetryCount,GeneratedText,
                   ConversationSessionId,GameEventId,PhoneMessageId
            FROM NpcInitiatedContactTrigger
            WHERE (
                    Status='generated'
                  )
               OR (
                    Status='scheduled'
                    AND DueGameTime <= $game
                  )
            ORDER BY DueGameTime,Id;
            """;

        cmd.Parameters.AddWithValue(
            "$game",
            gameTime.ToString("O"));

        var rows = new List<TriggerRow>();

        using var r = cmd.ExecuteReader();

        while (r.Read())
            rows.Add(ReadTrigger(r));

        return rows;
    }

    private TriggerRow? LoadById(
        SqliteConnection conn,
        long id)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText =
            TriggerSelect +
            " WHERE Id=$id LIMIT 1;";

        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();

        return r.Read()
            ? ReadTrigger(r)
            : null;
    }

    private TriggerRow? LoadBySourceKey(
        SqliteConnection conn,
        string sourceKey)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText =
            TriggerSelect +
            " WHERE SourceKey=$source LIMIT 1;";

        cmd.Parameters.AddWithValue(
            "$source",
            sourceKey);

        using var r = cmd.ExecuteReader();

        return r.Read()
            ? ReadTrigger(r)
            : null;
    }

    private const string TriggerSelect = """
        SELECT Id,PlayerId,PlayerName,NpcId,NpcName,Channel,Kind,Motive,
               DueGameTime,Urgency,Commitment,ContactScore,
               ValidatedContext,ClaimId,ConversationPlanId,
               AllowUnknownNumber,MaxMessageCharacters,SourceKey,
               Status,DecisionCode,RetryCount,GeneratedText,
               ConversationSessionId,GameEventId,PhoneMessageId
        FROM NpcInitiatedContactTrigger
        """;

    private static TriggerRow ReadTrigger(
        SqliteDataReader r)
        => new()
        {
            Id = r.GetInt64(0),
            PlayerId = r.GetString(1),
            PlayerName = r.GetString(2),
            NpcId = r.GetInt32(3),
            NpcName = r.GetString(4),
            Channel = r.GetString(5),
            Kind = r.GetString(6),
            Motive = r.GetString(7),
            DueGameTime = ParseTime(r.GetString(8)),
            Urgency = r.GetInt32(9),
            Commitment = r.GetInt32(10),
            ContactScore = r.GetDouble(11),
            ValidatedContext = r.GetString(12),
            ClaimId = r.IsDBNull(13)
                ? null
                : r.GetInt64(13),
            ConversationPlanId = r.IsDBNull(14)
                ? null
                : r.GetInt64(14),
            AllowUnknownNumber = r.GetInt32(15) != 0,
            MaxMessageCharacters = r.GetInt32(16),
            SourceKey = r.IsDBNull(17)
                ? ""
                : r.GetString(17),
            Status = r.GetString(18),
            DecisionCode = r.GetString(19),
            RetryCount = r.GetInt32(20),
            GeneratedText = r.GetString(21),
            ConversationSessionId = r.IsDBNull(22)
                ? null
                : r.GetInt64(22),
            GameEventId = r.IsDBNull(23)
                ? null
                : r.GetInt64(23),
            PhoneMessageId = r.IsDBNull(24)
                ? null
                : r.GetInt64(24)
        };

    private static NpcInitiatedOutboundMessage ToOutbound(
        TriggerRow row)
        => new()
        {
            TriggerId = row.Id,
            GameEventId = row.GameEventId,
            PlayerId = row.PlayerId,
            PlayerName = row.PlayerName,
            NpcId = row.NpcId,
            NpcName = row.NpcName,
            Channel = row.Channel,
            Kind = row.Kind,
            Motive = row.Motive,
            Text = row.GeneratedText,
            ConversationSessionId =
                row.ConversationSessionId ?? 0,
            GameTime = row.DueGameTime,
            AllowUnknownNumber =
                row.AllowUnknownNumber,
            SourceClaimId = row.ClaimId,
            ConversationPlanId =
                row.ConversationPlanId
        };

    private void SetGameEventId(
        long triggerId,
        long eventId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            UPDATE NpcInitiatedContactTrigger
            SET GameEventId=$event,
                UpdatedRealUtc=$real
            WHERE Id=$id;
            """;

        cmd.Parameters.AddWithValue(
            "$event",
            eventId);

        cmd.Parameters.AddWithValue(
            "$real",
            DateTime.UtcNow.ToString("O"));

        cmd.Parameters.AddWithValue(
            "$id",
            triggerId);

        cmd.ExecuteNonQuery();
    }

    private void MarkTriggerStatus(
        long triggerId,
        string status,
        string decision)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            UPDATE NpcInitiatedContactTrigger
            SET Status=$status,
                DecisionCode=$decision,
                UpdatedRealUtc=$real
            WHERE Id=$id;
            """;

        cmd.Parameters.AddWithValue(
            "$status",
            status);

        cmd.Parameters.AddWithValue(
            "$decision",
            decision);

        cmd.Parameters.AddWithValue(
            "$real",
            DateTime.UtcNow.ToString("O"));

        cmd.Parameters.AddWithValue(
            "$id",
            triggerId);

        cmd.ExecuteNonQuery();
    }

    private static bool HasDailySeed(
        SqliteConnection conn,
        string playerId,
        string dayKey)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT 1
            FROM NpcSpontaneousContactDay
            WHERE PlayerId=$player
              AND GameDay=$day
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$player",
            playerId);

        cmd.Parameters.AddWithValue(
            "$day",
            dayKey);

        return cmd.ExecuteScalar() != null;
    }

    private static void InsertDailySeed(
        SqliteConnection conn,
        string playerId,
        string dayKey)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT OR IGNORE INTO NpcSpontaneousContactDay
            (PlayerId,GameDay,CreatedRealUtc)
            VALUES($player,$day,$real);
            """;

        cmd.Parameters.AddWithValue(
            "$player",
            playerId);

        cmd.Parameters.AddWithValue(
            "$day",
            dayKey);

        cmd.Parameters.AddWithValue(
            "$real",
            DateTime.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
    }

    private static double DeterministicRoll(
        string playerId,
        int npcId,
        string sourceKey,
        DateTimeOffset due)
    {
        string key =
            $"contact-commit|{playerId}|{npcId}|{sourceKey}|{due:O}";

        return StablePercent(key);
    }

    private static double StablePercent(
        string value)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(value ?? ""));

        uint x = BitConverter.ToUInt32(
            bytes,
            0);

        return (x / (double)uint.MaxValue) * 100.0;
    }

    private static string NormalizeChannel(
        string? channel)
        => channel?.Trim().ToLowerInvariant() switch
        {
            "call" => "call",
            _ => "text"
        };

    private static string NormalizeKind(
        string? kind)
        => kind?.Trim().ToLowerInvariant() switch
        {
            "spontaneous_check_in" => "spontaneous_check_in",
            "check_in" => "spontaneous_check_in",
            "reminder" => "reminder",
            "promise" => "promise",
            "apology" => "apology",
            "invitation" => "invitation",
            "invite" => "invitation",
            "warning" => "warning",
            "gossip" => "gossip",
            "work" => "work",
            "family" => "family",
            "emergency" => "emergency",
            "relationship" => "relationship",
            _ => "follow_up"
        };

    private static DateTimeOffset ParseTime(
        string value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
                ? parsed
                : DateTimeOffset.Now;

    private static string Clean(
        string? value,
        string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

    private void ValidateScheduleRequest(
        NpcInitiatedContactRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(
                nameof(request));

        if (string.IsNullOrWhiteSpace(request.PlayerId))
        {
            throw new ArgumentException(
                "PlayerId is required.",
                nameof(request));
        }

        if (request.NpcId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.NpcId));
        }

        if (request.DueGameTime == default)
            request.DueGameTime = _clock.Now;

        request.PlayerId =
            request.PlayerId.Trim();

        request.PlayerName =
            Clean(request.PlayerName, "Player");

        request.Urgency =
            Math.Clamp(
                request.Urgency,
                0,
                100);

        request.Commitment =
            Math.Clamp(
                request.Commitment,
                0,
                100);

        request.MaxMessageCharacters =
            Math.Clamp(
                request.MaxMessageCharacters,
                40,
                1200);
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcInitiatedContactTrigger(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlayerId TEXT NOT NULL,
                PlayerName TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                NpcName TEXT NOT NULL,
                Channel TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Motive TEXT NOT NULL,
                DueGameTime TEXT NOT NULL,
                Urgency INTEGER NOT NULL,
                Commitment INTEGER NOT NULL,
                ContactScore REAL NOT NULL,
                ContextText TEXT NOT NULL DEFAULT '',
                ValidatedContext TEXT NOT NULL DEFAULT '',
                ClaimId INTEGER NULL,
                ConversationPlanId INTEGER NULL,
                AllowUnknownNumber INTEGER NOT NULL DEFAULT 0,
                MaxMessageCharacters INTEGER NOT NULL DEFAULT 420,
                SourceKey TEXT NULL,
                Status TEXT NOT NULL DEFAULT 'scheduled',
                DecisionCode TEXT NOT NULL DEFAULT '',
                RetryCount INTEGER NOT NULL DEFAULT 0,
                GeneratedText TEXT NOT NULL DEFAULT '',
                ConversationSessionId INTEGER NULL,
                GameEventId INTEGER NULL,
                PhoneMessageId INTEGER NULL,
                CreatedGameTime TEXT NOT NULL,
                GeneratedGameTime TEXT NULL,
                DeliveredGameTime TEXT NULL,
                CreatedRealUtc TEXT NOT NULL,
                UpdatedRealUtc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcInitiatedContact_SourceKey
            ON NpcInitiatedContactTrigger(SourceKey)
            WHERE SourceKey IS NOT NULL;

            CREATE INDEX IF NOT EXISTS IX_NpcInitiatedContact_Due
            ON NpcInitiatedContactTrigger(Status,DueGameTime,PlayerId);

            CREATE TABLE IF NOT EXISTS NpcSpontaneousContactDay(
                PlayerId TEXT NOT NULL,
                GameDay TEXT NOT NULL,
                CreatedRealUtc TEXT NOT NULL,
                PRIMARY KEY(PlayerId,GameDay)
            );
            """;

        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(
            "Data Source=" + _dbPath);

        conn.Open();

        using var pragma = conn.CreateCommand();

        pragma.CommandText =
            "PRAGMA busy_timeout=5000;";

        pragma.ExecuteNonQuery();

        return conn;
    }

    private sealed class TriggerRow
    {
        public long Id { get; set; }

        public string PlayerId { get; set; } = "";

        public string PlayerName { get; set; } = "";

        public int NpcId { get; set; }

        public string NpcName { get; set; } = "";

        public string Channel { get; set; } = "";

        public string Kind { get; set; } = "";

        public string Motive { get; set; } = "";

        public DateTimeOffset DueGameTime { get; set; }

        public int Urgency { get; set; }

        public int Commitment { get; set; }

        public double ContactScore { get; set; }

        public string ValidatedContext { get; set; } = "";

        public long? ClaimId { get; set; }

        public long? ConversationPlanId { get; set; }

        public bool AllowUnknownNumber { get; set; }

        public int MaxMessageCharacters { get; set; }

        public string SourceKey { get; set; } = "";

        public string Status { get; set; } = "";

        public string DecisionCode { get; set; } = "";

        public int RetryCount { get; set; }

        public string GeneratedText { get; set; } = "";

        public long? ConversationSessionId { get; set; }

        public long? GameEventId { get; set; }

        public long? PhoneMessageId { get; set; }
    }

    private sealed class PhoneProfileSnapshot
    {
        public int InitiatesContact { get; set; } = 40;

        public int EmojiUse { get; set; } = 30;

        public int DoubleTextTendency { get; set; } = 30;

        public int TypicalMessageLength { get; set; } = 45;

        public double SleepStartHour { get; set; } = 23.0;

        public double WakeHour { get; set; } = 7.0;
    }

    private sealed class ConversationPlanSnapshot
    {
        public string Description { get; set; } = "";

        public string TimeText { get; set; } = "";

        public string Location { get; set; } = "";
    }

    private sealed class SpontaneousCandidateScore
    {
        public NpcSpontaneousContactCandidate Candidate { get; set; } = new();

        public double Score { get; set; }

        public double TieBreak { get; set; }

        public PhoneProfileSnapshot Profile { get; set; } = new();
    }
}