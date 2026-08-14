using Microsoft.Data.Sqlite;
using ProjectEve.Core.Scene;
using ProjectEve.Core.Time;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Scene;

/// <summary>
/// Phase 11 spatial / physical interaction engine.
///
/// It converts natural player/NPC physical cues into server-owned position and
/// contact state. It intentionally resolves MOVEMENT immediately, while contact
/// actions become pending attempts until the counterpart clearly accepts/avoids/
/// rejects them. Hesitation/freeze never completes contact.
/// </summary>
public sealed class SceneSpatialInteractionService : ISceneSpatialInteractionService
{
    private readonly IScenePerceptionService _perception;
    private readonly IGameTimeService _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

    public SceneSpatialInteractionService(
        IScenePerceptionService perception,
        IGameTimeService clock)
    {
        _perception = perception;
        _clock = clock;
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<SceneSpatialTurnPreparation> PrepareActorTurnAsync(
        SceneSpatialTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.SceneId, request.ActorCharacterKey);

        string action = Clean(request.ActionText);
        string speech = Clean(request.SpeechText);
        string voice = NormalizeVoice(request.VoiceLevel);
        var addressed = new HashSet<int>(request.AddressedNpcIds.Where(x => x > 0));
        var events = new List<SceneSpatialEventSummary>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var members = LoadMembers(request.SceneId);
            var actor = FindMember(members, request.ActorCharacterKey);
            if (actor == null)
            {
                return new SceneSpatialTurnPreparation
                {
                    ActionText = action,
                    SpeechText = speech,
                    VoiceLevel = voice,
                    AddressedNpcIds = addressed.ToArray()
                };
            }

            var speechDelivery = ParseSpeechDelivery(speech, action, members, actor);
            speech = speechDelivery.CleanSpeech;
            if (speechDelivery.VoiceLevel != null)
                voice = speechDelivery.VoiceLevel;
            if (speechDelivery.Target?.NpcId is int speechNpc && speechNpc > 0)
                addressed.Add(speechNpc);

            if (action.Length > 0)
            {
                var resolved = await ResolveCueLockedAsync(
                    request.SceneId,
                    actor,
                    Clean(request.ActorName, actor.DisplayName),
                    action,
                    "action",
                    members,
                    cancellationToken);

                action = resolved.ResolvedText;
                if (resolved.Event != null)
                {
                    events.Add(resolved.Event);
                    if (resolved.Target?.NpcId is int targetNpc && targetNpc > 0)
                        addressed.Add(targetNpc);
                }
            }

            return new SceneSpatialTurnPreparation
            {
                ActionText = action,
                SpeechText = speech,
                VoiceLevel = voice,
                AddressedNpcIds = addressed.ToArray(),
                SpatialEvents = events
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SceneSpatialCueResult> ApplyNpcCueAsync(
        SceneSpatialCueRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.SceneId, request.ActorCharacterKey);
        string cue = Clean(request.CueText);
        if (cue.Length == 0)
            return new SceneSpatialCueResult { ResolvedText = cue };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var members = LoadMembers(request.SceneId);
            var actor = FindMember(members, request.ActorCharacterKey);
            if (actor == null)
                return new SceneSpatialCueResult { ResolvedText = cue };

            var resolved = await ResolveCueLockedAsync(
                request.SceneId,
                actor,
                Clean(request.ActorName, actor.DisplayName),
                cue,
                Clean(request.CueKind, "action"),
                members,
                cancellationToken);

            return new SceneSpatialCueResult
            {
                ChangedWorldState = resolved.Event != null,
                ResolvedText = resolved.ResolvedText,
                SpatialEvent = resolved.Event
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<double> GetDisplayDistanceAsync(
        string sceneId,
        string observerCharacterKey,
        string otherCharacterKey,
        double physicalDistanceFeet,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId) ||
            string.IsNullOrWhiteSpace(observerCharacterKey) ||
            string.IsNullOrWhiteSpace(otherCharacterKey))
            return physicalDistanceFeet;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var pair = LoadPair(sceneId, observerCharacterKey, otherCharacterKey);
            return pair?.State.Equals("active", StringComparison.OrdinalIgnoreCase) == true
                ? 0.0
                : Math.Max(0, physicalDistanceFeet);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> BuildActorSpatialContextAsync(
        string sceneId,
        string actorCharacterKey,
        CancellationToken cancellationToken = default)
    {
        Validate(sceneId, actorCharacterKey);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var members = LoadMembers(sceneId);
            var actor = FindMember(members, actorCharacterKey);
            if (actor == null)
                return "(no spatial state)";

            var lines = new List<string>();
            foreach (var other in members
                .Where(x => x.IsActive && !KeyEquals(x.CharacterKey, actor.CharacterKey))
                .OrderBy(x => Distance(actor, x))
                .Take(12))
            {
                double physical = Distance(actor, other);
                var pair = LoadPair(sceneId, actor.CharacterKey, other.CharacterKey);
                bool active = pair?.State.Equals("active", StringComparison.OrdinalIgnoreCase) == true;
                string band = SceneDistanceBands.FromFeet(physical, active);
                double display = active ? 0 : physical;
                string contact = pair == null || pair.State == "none"
                    ? "none"
                    : $"{pair.ContactKind}/{pair.State}/{pair.ReactionState}";

                lines.Add($"{other.DisplayName}: {display:0.#} ft [{band}], contact={contact}");
            }

            return lines.Count == 0 ? "(alone)" : string.Join("\n", lines);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScenePairInteractionState?> GetPairStateAsync(
        string sceneId,
        string characterAKey,
        string characterBKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var row = LoadPair(sceneId, characterAKey, characterBKey);
            if (row == null)
                return null;

            return new ScenePairInteractionState
            {
                SceneId = row.SceneId,
                CharacterAKey = row.CharacterAKey,
                CharacterBKey = row.CharacterBKey,
                InitiatorCharacterKey = row.InitiatorCharacterKey,
                ContactKind = row.ContactKind,
                State = row.State,
                ReactionState = row.ReactionState,
                UpdatedGameTime = row.UpdatedGameTime
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ResolvedCue> ResolveCueLockedAsync(
        string sceneId,
        PresenceRow actor,
        string actorName,
        string original,
        string cueKind,
        List<PresenceRow> members,
        CancellationToken cancellationToken)
    {
        string lower = Normalize(original);
        var pending = LoadPendingForActor(sceneId, actor.CharacterKey);

        // -------------------------------------------------------------
        // A reaction to an already pending contact has priority.
        // -------------------------------------------------------------
        if (pending != null)
        {
            var other = FindMember(members, OtherKey(pending, actor.CharacterKey));
            if (other != null)
            {
                if (IsFreezeCue(lower))
                {
                    UpdatePairState(pending, IsHesitationCue(lower) ? "hesitant" : "frozen",
                        IsHesitationCue(lower) ? "hesitant" : "frozen");

                    string resolved = IsHesitationCue(lower)
                        ? $"{actorName} hesitates at close range without completing the contact."
                        : $"{actorName} goes still at close range without completing the contact.";

                    return NewResolved(sceneId, actor, other, original, resolved,
                        IsHesitationCue(lower) ? SceneSpatialIntentKinds.Hesitate : SceneSpatialIntentKinds.Freeze,
                        pending.ContactKind, pending.State, Distance(actor, other), Distance(actor, other));
                }

                if (IsRejectCue(lower))
                {
                    double before = Distance(actor, other);
                    string state = ContainsAny(lower, "dodge", "duck", "sidestep", "slip", "avoid")
                        ? "avoided"
                        : "rejected";
                    string reaction = state == "avoided" ? "avoided" : "refused";
                    UpdatePairState(pending, state, reaction);

                    // A physical rejection normally creates space unless the cue
                    // explicitly says only turn/head movement.
                    double after = before;
                    if (ContainsAny(lower, "step back", "backs away", "back away", "pulls away",
                        "moves away", "retreat", "pushes away", "shoves away", "breaks away"))
                    {
                        after = Math.Max(3.0, before + 2.0);
                        await MoveRelativeAsync(sceneId, actor, other, after, cancellationToken);
                    }

                    string resolved = after > before + 0.05
                        ? $"{actorName} creates distance from {other.DisplayName}, moving from {before:0.#} ft to {after:0.#} ft."
                        : $"{actorName} avoids/refuses the attempted {Humanize(pending.ContactKind)} without completing contact.";

                    return NewResolved(sceneId, actor, other, original, resolved,
                        SceneSpatialIntentKinds.ContactReject, pending.ContactKind, state, before, after);
                }

                if (IsExplicitReciprocalCue(lower, pending.ContactKind))
                {
                    double before = Distance(actor, other);
                    UpdatePairState(pending, "active", "mutual");
                    if (before > 1.05)
                        await MoveRelativeAsync(sceneId, actor, other, 1.0, cancellationToken);

                    string resolved = $"{actorName} reciprocates the {Humanize(pending.ContactKind)}; physical contact is now active.";
                    return NewResolved(sceneId, actor, other, original, resolved,
                        SceneSpatialIntentKinds.ContactAccept, pending.ContactKind, "active", before, 0.0);
                }
            }
        }

        // -------------------------------------------------------------
        // Break an existing active contact.
        // -------------------------------------------------------------
        var active = LoadActiveForActor(sceneId, actor.CharacterKey);
        if (active != null && IsBreakContactCue(lower))
        {
            var other = FindMember(members, OtherKey(active, actor.CharacterKey));
            if (other != null)
            {
                double before = 0.0;
                UpdatePairState(active, "broken", "withdrawn");
                await MoveRelativeAsync(sceneId, actor, other, 2.0, cancellationToken);
                string resolved = $"{actorName} breaks the {Humanize(active.ContactKind)} and creates about 2 ft of space from {other.DisplayName}.";
                return NewResolved(sceneId, actor, other, original, resolved,
                    SceneSpatialIntentKinds.ContactBreak, active.ContactKind, "broken", before, 2.0);
            }
        }

        // -------------------------------------------------------------
        // Determine target, movement, or a new contact attempt.
        // -------------------------------------------------------------
        var target = ResolveTarget(members, actor, original, pending, active);
        string contactKind = ClassifyContact(lower);
        string movement = ClassifyMovement(lower);

        if (target == null)
            return new ResolvedCue { ResolvedText = original };

        double current = Distance(actor, target);

        if (contactKind != SceneContactKinds.None)
        {
            double reach = ContactReach(contactKind);
            double after = current;
            if (current > reach)
            {
                after = Math.Max(1.0, reach);
                await MoveRelativeAsync(sceneId, actor, target, after, cancellationToken);
            }

            var pair = UpsertPendingContact(
                sceneId,
                actor.CharacterKey,
                target.CharacterKey,
                actor.CharacterKey,
                contactKind);

            string verb = ContactAttemptPhrase(contactKind);
            string resolved = current > after + 0.05
                ? $"{actorName} closes from {current:0.#} ft to {after:0.#} ft and {verb} {target.DisplayName}. Contact is attempted, not yet resolved."
                : $"{actorName} {verb} {target.DisplayName}. Contact is attempted, not yet resolved.";

            return NewResolved(sceneId, actor, target, original, resolved,
                SceneSpatialIntentKinds.ContactAttempt, contactKind, pair.State, current, after);
        }

        if (movement != SceneSpatialIntentKinds.None)
        {
            double desired = DesiredDistance(movement, current);
            await MoveRelativeAsync(sceneId, actor, target, desired, cancellationToken);

            string resolved = movement switch
            {
                SceneSpatialIntentKinds.MoveCloser or SceneSpatialIntentKinds.Approach =>
                    $"{actorName} moves closer to {target.DisplayName}, from {current:0.#} ft to {desired:0.#} ft.",
                SceneSpatialIntentKinds.LeanIn =>
                    $"{actorName} leans closer to {target.DisplayName}, narrowing the space from {current:0.#} ft to {desired:0.#} ft.",
                SceneSpatialIntentKinds.LeanAway =>
                    $"{actorName} subtly leans away from {target.DisplayName}, increasing the space from {current:0.#} ft to {desired:0.#} ft.",
                SceneSpatialIntentKinds.SquareUp =>
                    $"{actorName} squares up close to {target.DisplayName}, closing from {current:0.#} ft to {desired:0.#} ft.",
                SceneSpatialIntentKinds.StandClose =>
                    $"{actorName} moves into very close range with {target.DisplayName}, from {current:0.#} ft to {desired:0.#} ft.",
                SceneSpatialIntentKinds.Retreat or SceneSpatialIntentKinds.Flee or SceneSpatialIntentKinds.MoveAway =>
                    $"{actorName} creates distance from {target.DisplayName}, moving from {current:0.#} ft to {desired:0.#} ft.",
                _ => $"{actorName} changes position relative to {target.DisplayName}, from {current:0.#} ft to {desired:0.#} ft."
            };

            return NewResolved(sceneId, actor, target, original, resolved,
                movement, SceneContactKinds.None, "moved", current, desired);
        }

        return new ResolvedCue { ResolvedText = original };
    }

    private async Task MoveRelativeAsync(
        string sceneId,
        PresenceRow actor,
        PresenceRow target,
        double desiredDistance,
        CancellationToken cancellationToken)
    {
        desiredDistance = Math.Clamp(desiredDistance, 0.75, 40.0);

        double dx = actor.XFeet - target.XFeet;
        double dy = actor.YFeet - target.YFeet;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len < 0.05)
        {
            // Put the actor behind their current facing vector rather than
            // creating an undefined position.
            double radians = (actor.FacingDegrees + 180.0) * Math.PI / 180.0;
            dx = Math.Cos(radians);
            dy = Math.Sin(radians);
            len = 1.0;
        }

        double ux = dx / len;
        double uy = dy / len;

        actor.XFeet = target.XFeet + ux * desiredDistance;
        actor.YFeet = target.YFeet + uy * desiredDistance;
        actor.FacingDegrees = Facing(actor.XFeet, actor.YFeet, target.XFeet, target.YFeet);

        await _perception.UpsertPresenceAsync(new ScenePresenceUpdate
        {
            SceneId = sceneId,
            CharacterKey = actor.CharacterKey,
            NpcId = actor.NpcId,
            PlayerId = actor.PlayerId,
            DisplayName = actor.DisplayName,
            IsPlayer = actor.IsPlayer,
            XFeet = actor.XFeet,
            YFeet = actor.YFeet,
            FacingDegrees = actor.FacingDegrees,
            RoomZone = actor.RoomZone,
            AcousticZone = actor.AcousticZone,
            Attention = actor.Attention,
            Activity = actor.Activity,
            Concealment = actor.Concealment,
            IsActive = actor.IsActive
        }, cancellationToken);
    }

    private ResolvedCue NewResolved(
        string sceneId,
        PresenceRow actor,
        PresenceRow target,
        string original,
        string resolved,
        string intent,
        string contact,
        string status,
        double? before,
        double? after)
    {
        long id = InsertEvent(sceneId, actor.CharacterKey, target.CharacterKey,
            intent, contact, status, before, after, original, resolved);

        return new ResolvedCue
        {
            ResolvedText = resolved,
            Target = target,
            Event = new SceneSpatialEventSummary
            {
                Id = id,
                SceneId = sceneId,
                ActorCharacterKey = actor.CharacterKey,
                TargetCharacterKey = target.CharacterKey,
                IntentKind = intent,
                ContactKind = contact,
                Status = status,
                PreviousDistanceFeet = before,
                NewDistanceFeet = after,
                OriginalText = original,
                ResolvedText = resolved,
                GameTime = _clock.Now
            }
        };
    }

    private SpeechDelivery ParseSpeechDelivery(
        string speech,
        string action,
        List<PresenceRow> members,
        PresenceRow actor)
    {
        if (speech.Length == 0)
            return new SpeechDelivery(speech, null, null);

        string working = speech.Trim();
        string lower = Normalize(working + " " + action);
        string? voice = null;

        var prefixes = new (string Pattern, string Voice)[]
        {
            (@"^\s*\(?\s*whisper(?:ing)?\s*\)?\s*(?:to\s+[^:]+)?\s*[:\-]?\s*", "whisper"),
            (@"^\s*\(?\s*quietly\s*\)?\s*[:\-]?\s*", "quiet"),
            (@"^\s*\(?\s*softly\s*\)?\s*[:\-]?\s*", "quiet"),
            (@"^\s*\(?\s*(?:shout(?:ing)?|yell(?:ing)?|scream(?:ing)?)\s*\)?\s*[:\-]?\s*", "shout")
        };

        foreach (var item in prefixes)
        {
            var match = Regex.Match(working, item.Pattern, RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            voice = item.Voice;
            working = working[match.Length..].Trim();
            break;
        }

        if (voice == null)
        {
            if (ContainsAny(lower, "whisper", "in your ear", "in his ear", "in her ear"))
                voice = "whisper";
            else if (ContainsAny(lower, "very quiet", "quietly", "softly", "lower my voice", "lowers his voice", "lowers her voice"))
                voice = "quiet";
            else if (ContainsAny(lower, "raise my voice", "raises his voice", "raises her voice"))
                voice = "raised";
            else if (ContainsAny(lower, "shout", "yell", "scream"))
                voice = "shout";
        }

        var target = ResolveTarget(members, actor, speech + " " + action, null, null);
        return new SpeechDelivery(working, voice, target);
    }

    private static string ClassifyMovement(string lower)
    {
        if (ContainsAny(lower, "flee", "runs away", "run away")) return SceneSpatialIntentKinds.Flee;
        if (ContainsAny(lower, "retreat", "backs away", "back away", "steps away", "step away", "moves away", "move away", "create distance")) return SceneSpatialIntentKinds.MoveAway;
        if (ContainsAny(lower, "leans away", "lean away", "leans back", "lean back")) return SceneSpatialIntentKinds.LeanAway;
        if (ContainsWord(lower, "whisper") && ContainsWord(lower, "ear")) return SceneSpatialIntentKinds.StandClose;
        if (ContainsAny(lower, "square up", "squares up", "gets in his face", "gets in her face", "get in his face", "get in her face", "gets in your face")) return SceneSpatialIntentKinds.SquareUp;
        if (ContainsAny(lower, "leans closer", "lean closer", "leans in", "lean in")) return SceneSpatialIntentKinds.LeanIn;
        if (ContainsAny(lower, "moves closer", "move closer", "steps closer", "step closer", "walks closer", "walk closer", "approaches", "approach", "comes closer", "come closer")) return SceneSpatialIntentKinds.MoveCloser;
        return SceneSpatialIntentKinds.None;
    }

    private static string ClassifyContact(string lower)
    {
        // Specific / compound patterns first.
        if (ContainsAny(lower, "whisper in his ear", "whisper in her ear", "whisper in your ear")) return SceneContactKinds.None;
        if (ContainsAny(lower, "make out", "making out")) return SceneContactKinds.MakeOut;
        if (ContainsAny(lower, "long kiss", "deep kiss")) return SceneContactKinds.LongKiss;
        if (ContainsAny(lower, "cheek kiss", "kiss his cheek", "kiss her cheek")) return SceneContactKinds.CheekKiss;
        if (ContainsAny(lower, "forehead kiss", "kiss his forehead", "kiss her forehead")) return SceneContactKinds.ForeheadKiss;
        if (ContainsAny(lower, "forehead touch", "touch foreheads")) return SceneContactKinds.ForeheadTouch;
        if (ContainsWord(lower, "kiss")) return SceneContactKinds.Kiss;
        if (ContainsAny(lower, "long hug", "tight hug")) return SceneContactKinds.LongHug;
        if (ContainsAny(lower, "side hug")) return SceneContactKinds.SideHug;
        if (ContainsWord(lower, "hug") || ContainsWord(lower, "embrace")) return SceneContactKinds.Hug;
        if (ContainsAny(lower, "hold hands", "holds hands", "holding hands", "take his hand", "take her hand", "takes his hand", "takes her hand")) return SceneContactKinds.HoldingHands;
        if (ContainsAny(lower, "shake hands", "handshake")) return SceneContactKinds.Handshake;
        if (ContainsAny(lower, "high five")) return SceneContactKinds.HighFive;
        if (ContainsAny(lower, "fist bump")) return SceneContactKinds.FistBump;
        if (ContainsAny(lower, "touch his arm", "touch her arm", "touches his arm", "touches her arm")) return SceneContactKinds.TouchArm;
        if (ContainsAny(lower, "touch his shoulder", "touch her shoulder", "touches his shoulder", "touches her shoulder")) return SceneContactKinds.TouchShoulder;
        if (ContainsAny(lower, "touch his hand", "touch her hand", "touches his hand", "touches her hand")) return SceneContactKinds.HandTouch;
        if (ContainsAny(lower, "arm around his waist", "arm around her waist")) return SceneContactKinds.ArmAroundWaist;
        if (ContainsAny(lower, "arm around his shoulders", "arm around her shoulders")) return SceneContactKinds.ArmAroundShoulders;
        if (ContainsAny(lower, "chest bump", "bumps chest")) return SceneContactKinds.ChestBump;
        if (ContainsAny(lower, "poke his chest", "poke her chest", "pokes his chest", "pokes her chest")) return SceneContactKinds.PokeChest;
        if (ContainsAny(lower, "shoulder bump")) return SceneContactKinds.ShoulderBump;
        if (ContainsAny(lower, "grab his wrist", "grab her wrist", "grabs his wrist", "grabs her wrist")) return SceneContactKinds.GrabWrist;
        if (ContainsAny(lower, "grab his arm", "grab her arm", "grabs his arm", "grabs her arm")) return SceneContactKinds.GrabArm;
        if (ContainsAny(lower, "grab his shirt", "grab her shirt", "grabs his shirt", "grabs her shirt", "grab clothing")) return SceneContactKinds.GrabClothing;
        if (ContainsAny(lower, "chokehold")) return SceneContactKinds.Chokehold;
        if (ContainsAny(lower, "headlock")) return SceneContactKinds.Headlock;
        if (ContainsAny(lower, "arm lock", "armlock")) return SceneContactKinds.ArmLock;
        if (ContainsAny(lower, "wrist lock")) return SceneContactKinds.WristLock;
        if (ContainsAny(lower, "joint lock")) return SceneContactKinds.JointLock;
        if (ContainsAny(lower, "body lock")) return SceneContactKinds.BodyLock;
        if (ContainsAny(lower, "fight clinch", "clinch")) return SceneContactKinds.FightClinch;
        if (ContainsAny(lower, "tackle")) return SceneContactKinds.Tackle;
        if (ContainsAny(lower, "takedown", "take down")) return SceneContactKinds.Takedown;
        if (ContainsAny(lower, "wrestle")) return SceneContactKinds.Wrestle;
        if (ContainsAny(lower, "grapple")) return SceneContactKinds.Grapple;
        if (ContainsAny(lower, "restrain")) return SceneContactKinds.Restrain;
        if (ContainsAny(lower, "pin him", "pin her", "pins him", "pins her")) return SceneContactKinds.Pin;
        if (ContainsAny(lower, "shove")) return SceneContactKinds.Shove;
        if (ContainsWord(lower, "push") || ContainsWord(lower, "pushes")) return SceneContactKinds.Push;
        if (ContainsWord(lower, "pull") || ContainsWord(lower, "pulls")) return SceneContactKinds.Pull;
        if (ContainsWord(lower, "drag") || ContainsWord(lower, "drags")) return SceneContactKinds.Drag;
        if (ContainsAny(lower, "uppercut")) return SceneContactKinds.Uppercut;
        if (ContainsAny(lower, "headbutt")) return SceneContactKinds.Headbutt;
        if (ContainsAny(lower, "elbow")) return SceneContactKinds.ElbowStrike;
        if (ContainsAny(lower, "knee strike", "knees him", "knees her")) return SceneContactKinds.KneeStrike;
        if (ContainsAny(lower, "round kick", "roundhouse")) return SceneContactKinds.RoundKick;
        if (ContainsAny(lower, "side kick")) return SceneContactKinds.SideKick;
        if (ContainsAny(lower, "front kick")) return SceneContactKinds.FrontKick;
        if (ContainsWord(lower, "kick") || ContainsWord(lower, "kicks")) return SceneContactKinds.Kick;
        if (ContainsWord(lower, "slap") || ContainsWord(lower, "slaps")) return SceneContactKinds.Slap;
        if (ContainsAny(lower, "backhand")) return SceneContactKinds.Backhand;
        if (ContainsAny(lower, "jab")) return SceneContactKinds.Jab;
        if (ContainsAny(lower, "hook punch", "left hook", "right hook")) return SceneContactKinds.Hook;
        if (ContainsWord(lower, "punch") || ContainsWord(lower, "punches")) return SceneContactKinds.Punch;
        if (ContainsWord(lower, "grab") || ContainsWord(lower, "grabs")) return SceneContactKinds.Grab;

        // Adult-intimacy vocabulary is intentionally represented as physical
        // state, but still uses the exact same pending -> reciprocal/withdrawn
        // resolution rule as every other close contact.
        if (ContainsAny(lower, "oral sex")) return SceneContactKinds.OralSex;
        if (ContainsAny(lower, "vaginal sex")) return SceneContactKinds.VaginalSex;
        if (ContainsAny(lower, "anal sex")) return SceneContactKinds.AnalSex;
        if (ContainsAny(lower, "manual sex")) return SceneContactKinds.ManualSex;
        if (ContainsAny(lower, "mutual masturbation")) return SceneContactKinds.MutualMasturbation;
        if (ContainsAny(lower, "sexual touch", "touch sexually")) return SceneContactKinds.SexualTouch;
        if (ContainsAny(lower, "sexual contact")) return SceneContactKinds.SexualContact;
        if (ContainsAny(lower, "intimate touch", "touch intimately")) return SceneContactKinds.IntimateTouch;
        if (ContainsAny(lower, "sexual embrace")) return SceneContactKinds.SexualEmbrace;

        return SceneContactKinds.None;
    }

    private static bool IsExplicitReciprocalCue(string lower, string contactKind)
    {
        if (ContainsAny(lower, "accepts", "welcomes", "reciprocates", "returns the", "back", "pulls closer", "leans into"))
        {
            string human = Humanize(contactKind);
            string root = contactKind.Contains("kiss", StringComparison.OrdinalIgnoreCase) ? "kiss" :
                          contactKind.Contains("hug", StringComparison.OrdinalIgnoreCase) || contactKind == SceneContactKinds.Embrace ? "hug" :
                          contactKind == SceneContactKinds.HoldingHands ? "hand" :
                          contactKind == SceneContactKinds.Handshake ? "hand" :
                          contactKind.Contains("sex", StringComparison.OrdinalIgnoreCase) || SceneContactKinds.AdultOnly.Contains(contactKind) ? "sexual" :
                          human.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? human;

            return lower.Contains(root, StringComparison.OrdinalIgnoreCase) ||
                   ContainsAny(lower, "accepts", "welcomes", "reciprocates", "pulls closer", "leans into");
        }
        return false;
    }

    private static bool IsRejectCue(string lower)
        => ContainsWord(lower, "no") || ContainsAny(lower,
            "refuse", "reject", "turns away", "turn away", "pulls away", "pull away",
            "steps back", "step back", "backs away", "back away", "moves away", "move away",
            "dodges", "dodge", "ducks", "duck", "sidesteps", "sidestep", "slips", "slip",
            "blocks", "block", "parries", "parry", "pushes away", "push away", "shoves away", "shove away");

    private static bool IsFreezeCue(string lower)
        => ContainsAny(lower, "freezes", "freeze", "goes still", "stiffens", "stiffens up", "hesitates", "hesitate", "pauses uncertainly");

    private static bool IsHesitationCue(string lower)
        => ContainsAny(lower, "hesitates", "hesitate", "pauses uncertainly", "uncertain");

    private static bool IsBreakContactCue(string lower)
        => ContainsAny(lower, "let go", "lets go", "release", "releases", "break contact", "breaks contact", "pull away", "pulls away", "separate", "steps back", "backs away");

    private static double DesiredDistance(string movement, double current)
        => movement switch
        {
            SceneSpatialIntentKinds.LeanIn => Math.Max(0.9, current - 0.6),
            SceneSpatialIntentKinds.LeanAway => Math.Min(40, current + 0.8),
            SceneSpatialIntentKinds.SquareUp => 1.0,
            SceneSpatialIntentKinds.StandClose => 1.0,
            SceneSpatialIntentKinds.Flee => Math.Min(40, Math.Max(12, current + 10)),
            SceneSpatialIntentKinds.Retreat => Math.Min(40, current + 5),
            SceneSpatialIntentKinds.MoveAway => Math.Min(40, current + 3),
            _ => Math.Max(1.0, current - 2.0)
        };

    private static double ContactReach(string kind)
    {
        if (kind is SceneContactKinds.Kick or SceneContactKinds.FrontKick or SceneContactKinds.SideKick or SceneContactKinds.RoundKick)
            return 3.5;
        if (kind == SceneContactKinds.Tackle)
            return 5.0;
        if (kind is SceneContactKinds.Punch or SceneContactKinds.Jab or SceneContactKinds.Cross or SceneContactKinds.Hook or SceneContactKinds.Uppercut)
            return 2.0;
        return 1.0;
    }

    private static string ContactAttemptPhrase(string kind)
        => kind switch
        {
            SceneContactKinds.Hug or SceneContactKinds.LongHug or SceneContactKinds.SideHug or SceneContactKinds.Embrace => "moves in to attempt a hug with",
            SceneContactKinds.Kiss or SceneContactKinds.LongKiss or SceneContactKinds.CheekKiss or SceneContactKinds.ForeheadKiss => "leans in to attempt a kiss with",
            SceneContactKinds.HoldingHands => "reaches to take the hand of",
            SceneContactKinds.Handshake => "offers a handshake to",
            SceneContactKinds.ChestBump => "crowds forward into a chest-bump attempt with",
            SceneContactKinds.Shove or SceneContactKinds.Push => "reaches into contact range and attempts to shove",
            SceneContactKinds.Punch or SceneContactKinds.Jab or SceneContactKinds.Cross or SceneContactKinds.Hook or SceneContactKinds.Uppercut => "steps into striking range and throws a punch at",
            SceneContactKinds.Kick or SceneContactKinds.FrontKick or SceneContactKinds.SideKick or SceneContactKinds.RoundKick => "moves into striking range and throws a kick at",
            SceneContactKinds.Tackle => "drives forward in a tackle attempt at",
            SceneContactKinds.Grab or SceneContactKinds.GrabArm or SceneContactKinds.GrabWrist or SceneContactKinds.GrabClothing => "reaches to grab",
            SceneContactKinds.Grapple or SceneContactKinds.FightClinch or SceneContactKinds.Wrestle => "moves into grappling range with",
            _ when SceneContactKinds.AdultOnly.Contains(kind) => "initiates an adult intimate contact attempt with",
            _ => $"attempts {Humanize(kind)} with"
        };

    private PresenceRow? ResolveTarget(
        List<PresenceRow> members,
        PresenceRow actor,
        string text,
        PairRow? pending,
        PairRow? active)
    {
        var others = members
            .Where(x => x.IsActive && !KeyEquals(x.CharacterKey, actor.CharacterKey))
            .ToList();
        if (others.Count == 0) return null;

        string lower = " " + Normalize(text) + " ";

        var named = others
            .Select(x => new { Row = x, Score = NameMatchScore(lower, x.DisplayName) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Distance(actor, x.Row))
            .FirstOrDefault();
        if (named != null) return named.Row;

        if (pending != null)
        {
            var row = FindMember(others, OtherKey(pending, actor.CharacterKey));
            if (row != null) return row;
        }
        if (active != null)
        {
            var row = FindMember(others, OtherKey(active, actor.CharacterKey));
            if (row != null) return row;
        }

        return others.OrderBy(x => Distance(actor, x)).First();
    }

    private static int NameMatchScore(string lowerText, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return 0;
        string full = Normalize(displayName);
        if (lowerText.Contains(" " + full + " ", StringComparison.OrdinalIgnoreCase))
            return 100 + full.Length;

        var first = full.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first) && first.Length >= 2 &&
            lowerText.Contains(" " + first + " ", StringComparison.OrdinalIgnoreCase))
            return 50 + first.Length;

        return 0;
    }

    private PairRow UpsertPendingContact(
        string sceneId,
        string one,
        string two,
        string initiator,
        string contactKind)
    {
        var (a, b) = CanonicalPair(one, two);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ScenePhysicalContact
(SceneId,CharacterAKey,CharacterBKey,InitiatorCharacterKey,ContactKind,State,ReactionState,StartedGameTime,UpdatedGameTime,UpdatedRealUtc)
VALUES($scene,$a,$b,$initiator,$kind,'pending','pending',$game,$game,$real)
ON CONFLICT(SceneId,CharacterAKey,CharacterBKey) DO UPDATE SET
    InitiatorCharacterKey=excluded.InitiatorCharacterKey,
    ContactKind=excluded.ContactKind,
    State='pending',
    ReactionState='pending',
    StartedGameTime=excluded.StartedGameTime,
    UpdatedGameTime=excluded.UpdatedGameTime,
    UpdatedRealUtc=excluded.UpdatedRealUtc;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$a", a);
        cmd.Parameters.AddWithValue("$b", b);
        cmd.Parameters.AddWithValue("$initiator", initiator);
        cmd.Parameters.AddWithValue("$kind", contactKind);
        cmd.Parameters.AddWithValue("$game", DbTime(_clock.Now));
        cmd.Parameters.AddWithValue("$real", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return LoadPair(sceneId, one, two)!;
    }

    private void UpdatePairState(PairRow row, string state, string reaction)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE ScenePhysicalContact
SET State=$state, ReactionState=$reaction, UpdatedGameTime=$game, UpdatedRealUtc=$real
WHERE SceneId=$scene AND CharacterAKey=$a AND CharacterBKey=$b;";
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$reaction", reaction);
        cmd.Parameters.AddWithValue("$game", DbTime(_clock.Now));
        cmd.Parameters.AddWithValue("$real", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$scene", row.SceneId);
        cmd.Parameters.AddWithValue("$a", row.CharacterAKey);
        cmd.Parameters.AddWithValue("$b", row.CharacterBKey);
        cmd.ExecuteNonQuery();
        row.State = state;
        row.ReactionState = reaction;
    }

    private PairRow? LoadPair(string sceneId, string one, string two)
    {
        if (string.IsNullOrWhiteSpace(one) || string.IsNullOrWhiteSpace(two)) return null;
        var (a, b) = CanonicalPair(one, two);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT SceneId,CharacterAKey,CharacterBKey,InitiatorCharacterKey,ContactKind,State,ReactionState,UpdatedGameTime
FROM ScenePhysicalContact
WHERE SceneId=$scene AND CharacterAKey=$a AND CharacterBKey=$b
LIMIT 1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$a", a);
        cmd.Parameters.AddWithValue("$b", b);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadPair(r);
    }

    private PairRow? LoadPendingForActor(string sceneId, string actorKey)
        => LoadActorPair(sceneId, actorKey, "pending", "hesitant", "frozen");

    private PairRow? LoadActiveForActor(string sceneId, string actorKey)
        => LoadActorPair(sceneId, actorKey, "active");

    private PairRow? LoadActorPair(string sceneId, string actorKey, params string[] states)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        var stateParams = new List<string>();
        for (int i = 0; i < states.Length; i++)
        {
            string p = "$s" + i;
            stateParams.Add(p);
            cmd.Parameters.AddWithValue(p, states[i]);
        }
        cmd.CommandText = $@"
SELECT SceneId,CharacterAKey,CharacterBKey,InitiatorCharacterKey,ContactKind,State,ReactionState,UpdatedGameTime
FROM ScenePhysicalContact
WHERE SceneId=$scene
  AND (CharacterAKey=$actor OR CharacterBKey=$actor)
  AND State IN ({string.Join(",", stateParams)})
ORDER BY UpdatedGameTime DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$actor", actorKey);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadPair(r) : null;
    }

    private static PairRow ReadPair(SqliteDataReader r)
        => new()
        {
            SceneId = r.GetString(0),
            CharacterAKey = r.GetString(1),
            CharacterBKey = r.GetString(2),
            InitiatorCharacterKey = r.GetString(3),
            ContactKind = r.GetString(4),
            State = r.GetString(5),
            ReactionState = r.GetString(6),
            UpdatedGameTime = ParseTime(r.GetString(7))
        };

    private List<PresenceRow> LoadMembers(string sceneId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT CharacterKey,NpcId,PlayerId,DisplayName,IsPlayer,
       XFeet,YFeet,FacingDegrees,RoomZone,AcousticZone,
       Attention,Activity,Concealment,IsActive
FROM ScenePresence
WHERE SceneId=$scene AND IsActive=1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        var rows = new List<PresenceRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new PresenceRow
            {
                CharacterKey = r.GetString(0),
                NpcId = r.IsDBNull(1) ? null : r.GetInt32(1),
                PlayerId = r.IsDBNull(2) ? null : r.GetString(2),
                DisplayName = r.GetString(3),
                IsPlayer = r.GetInt32(4) != 0,
                XFeet = r.GetDouble(5),
                YFeet = r.GetDouble(6),
                FacingDegrees = r.GetDouble(7),
                RoomZone = r.GetString(8),
                AcousticZone = r.GetString(9),
                Attention = r.GetDouble(10),
                Activity = r.GetString(11),
                Concealment = r.GetDouble(12),
                IsActive = r.GetInt32(13) != 0
            });
        }
        return rows;
    }

    private long InsertEvent(
        string sceneId,
        string actorKey,
        string? targetKey,
        string intent,
        string contact,
        string status,
        double? before,
        double? after,
        string original,
        string resolved)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO SceneSpatialInteractionEvent
(SceneId,ActorCharacterKey,TargetCharacterKey,IntentKind,ContactKind,Status,
 PreviousDistanceFeet,NewDistanceFeet,OriginalText,ResolvedText,GameTime,CreatedRealUtc)
VALUES($scene,$actor,$target,$intent,$contact,$status,$before,$after,$original,$resolved,$game,$real);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$actor", actorKey);
        cmd.Parameters.AddWithValue("$target", (object?)targetKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$intent", intent);
        cmd.Parameters.AddWithValue("$contact", contact);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$before", (object?)before ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$after", (object?)after ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$original", original);
        cmd.Parameters.AddWithValue("$resolved", resolved);
        cmd.Parameters.AddWithValue("$game", DbTime(_clock.Now));
        cmd.Parameters.AddWithValue("$real", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SceneSpatialInteractionEvent
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SceneId TEXT NOT NULL,
    ActorCharacterKey TEXT NOT NULL,
    TargetCharacterKey TEXT NULL,
    IntentKind TEXT NOT NULL DEFAULT 'none',
    ContactKind TEXT NOT NULL DEFAULT 'none',
    Status TEXT NOT NULL DEFAULT 'observed',
    PreviousDistanceFeet REAL NULL,
    NewDistanceFeet REAL NULL,
    OriginalText TEXT NOT NULL DEFAULT '',
    ResolvedText TEXT NOT NULL DEFAULT '',
    GameTime TEXT NOT NULL,
    CreatedRealUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_SceneSpatialInteractionEvent_Scene
ON SceneSpatialInteractionEvent(SceneId,Id DESC);

CREATE TABLE IF NOT EXISTS ScenePhysicalContact
(
    SceneId TEXT NOT NULL,
    CharacterAKey TEXT NOT NULL,
    CharacterBKey TEXT NOT NULL,
    InitiatorCharacterKey TEXT NOT NULL,
    ContactKind TEXT NOT NULL DEFAULT 'none',
    State TEXT NOT NULL DEFAULT 'none',
    ReactionState TEXT NOT NULL DEFAULT 'unknown',
    StartedGameTime TEXT NOT NULL,
    UpdatedGameTime TEXT NOT NULL,
    UpdatedRealUtc TEXT NOT NULL,
    PRIMARY KEY(SceneId,CharacterAKey,CharacterBKey)
);

CREATE INDEX IF NOT EXISTS IX_ScenePhysicalContact_Active
ON ScenePhysicalContact(SceneId,State,CharacterAKey,CharacterBKey);
";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static PresenceRow? FindMember(IEnumerable<PresenceRow> members, string key)
        => members.FirstOrDefault(x => KeyEquals(x.CharacterKey, key));

    private static double Distance(PresenceRow a, PresenceRow b)
    {
        double dx = a.XFeet - b.XFeet;
        double dy = a.YFeet - b.YFeet;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Facing(double ax, double ay, double tx, double ty)
    {
        double degrees = Math.Atan2(ty - ay, tx - ax) * 180.0 / Math.PI;
        if (degrees < 0) degrees += 360;
        return degrees;
    }

    private static (string A, string B) CanonicalPair(string one, string two)
        => string.Compare(one, two, StringComparison.OrdinalIgnoreCase) <= 0
            ? (one.Trim(), two.Trim())
            : (two.Trim(), one.Trim());

    private static string OtherKey(PairRow row, string actor)
        => KeyEquals(row.CharacterAKey, actor) ? row.CharacterBKey : row.CharacterAKey;

    private static bool KeyEquals(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Clean(string? value, string fallback = "")
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Normalize(string? value)
        => MultiSpace.Replace((value ?? "").Trim().ToLowerInvariant(), " ");

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsWord(string text, string word)
        => Regex.IsMatch(text, @"\b" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase);

    private static string NormalizeVoice(string? value)
    {
        string v = Clean(value, "normal").ToLowerInvariant();
        return v is "whisper" or "quiet" or "normal" or "raised" or "shout" ? v : "normal";
    }

    private static string Humanize(string value)
        => (value ?? "none").Replace('_', ' ');

    private static string DbTime(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private static void Validate(string sceneId, string actorKey)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("SceneId is required.", nameof(sceneId));
        if (string.IsNullOrWhiteSpace(actorKey))
            throw new ArgumentException("ActorCharacterKey is required.", nameof(actorKey));
    }

    private sealed class PresenceRow
    {
        public string CharacterKey { get; set; } = "";
        public int? NpcId { get; set; }
        public string? PlayerId { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsPlayer { get; set; }
        public double XFeet { get; set; }
        public double YFeet { get; set; }
        public double FacingDegrees { get; set; }
        public string RoomZone { get; set; } = "main";
        public string AcousticZone { get; set; } = "main";
        public double Attention { get; set; }
        public string Activity { get; set; } = "idle";
        public double Concealment { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class PairRow
    {
        public string SceneId { get; set; } = "";
        public string CharacterAKey { get; set; } = "";
        public string CharacterBKey { get; set; } = "";
        public string InitiatorCharacterKey { get; set; } = "";
        public string ContactKind { get; set; } = SceneContactKinds.None;
        public string State { get; set; } = "none";
        public string ReactionState { get; set; } = "unknown";
        public DateTimeOffset UpdatedGameTime { get; set; }
    }

    private sealed class ResolvedCue
    {
        public string ResolvedText { get; set; } = "";
        public PresenceRow? Target { get; set; }
        public SceneSpatialEventSummary? Event { get; set; }
    }

    private sealed record SpeechDelivery(string CleanSpeech, string? VoiceLevel, PresenceRow? Target);
}
