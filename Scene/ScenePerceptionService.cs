using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProjectEve.Core.Scene;
using ProjectEve.Core.Time;

namespace ProjectEve.Scene;

/// <summary>
/// Server-owned scene presence + perception engine.
/// It stores physical scene truth (who is where) separately from observer perception.
/// </summary>
public sealed class ScenePerceptionService : IScenePerceptionService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IGameTimeService _clock;
    private readonly string _dbPath;

    public ScenePerceptionService(IGameTimeService clock)
    {
        _clock = clock;
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public event Action<string>? SceneChanged;

    public async Task UpsertSceneAsync(
        SceneDefinition scene,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scene.SceneId))
            throw new ArgumentException("SceneId is required.", nameof(scene));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ActiveScene
                    (SceneId,LocationId,DisplayName,AmbientNoise,VisualClutter,
                     DefaultRoomZone,DefaultAcousticZone,UpdatedGameTime,UpdatedRealUtc)
                VALUES
                    ($scene,$location,$name,$noise,$clutter,$room,$acoustic,$game,$real)
                ON CONFLICT(SceneId) DO UPDATE SET
                    LocationId=excluded.LocationId,
                    DisplayName=excluded.DisplayName,
                    AmbientNoise=excluded.AmbientNoise,
                    VisualClutter=excluded.VisualClutter,
                    DefaultRoomZone=excluded.DefaultRoomZone,
                    DefaultAcousticZone=excluded.DefaultAcousticZone,
                    UpdatedGameTime=excluded.UpdatedGameTime,
                    UpdatedRealUtc=excluded.UpdatedRealUtc;
                """;
            cmd.Parameters.AddWithValue("$scene", scene.SceneId.Trim());
            cmd.Parameters.AddWithValue("$location", Clean(scene.LocationId, "unknown"));
            cmd.Parameters.AddWithValue("$name", Clean(scene.DisplayName, scene.LocationId));
            cmd.Parameters.AddWithValue("$noise", Clamp01(scene.AmbientNoise));
            cmd.Parameters.AddWithValue("$clutter", Clamp01(scene.VisualClutter));
            cmd.Parameters.AddWithValue("$room", Clean(scene.DefaultRoomZone, "main"));
            cmd.Parameters.AddWithValue("$acoustic", Clean(scene.DefaultAcousticZone, "main"));
            cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }

        Publish(scene.SceneId);
    }

    public async Task UpsertPresenceAsync(
        ScenePresenceUpdate presence,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presence.SceneId))
            throw new ArgumentException("SceneId is required.", nameof(presence));
        if (string.IsNullOrWhiteSpace(presence.CharacterKey))
            throw new ArgumentException("CharacterKey is required.", nameof(presence));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            var scene = LoadScene(conn, presence.SceneId)
                ?? throw new InvalidOperationException($"Scene '{presence.SceneId}' has not been registered.");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ScenePresence
                    (SceneId,CharacterKey,NpcId,PlayerId,DisplayName,IsPlayer,
                     XFeet,YFeet,FacingDegrees,RoomZone,AcousticZone,
                     Attention,Activity,Concealment,IsActive,UpdatedGameTime,UpdatedRealUtc)
                VALUES
                    ($scene,$key,$npc,$player,$name,$isPlayer,
                     $x,$y,$facing,$room,$acoustic,
                     $attention,$activity,$concealment,$active,$game,$real)
                ON CONFLICT(SceneId,CharacterKey) DO UPDATE SET
                    NpcId=excluded.NpcId,
                    PlayerId=excluded.PlayerId,
                    DisplayName=excluded.DisplayName,
                    IsPlayer=excluded.IsPlayer,
                    XFeet=excluded.XFeet,
                    YFeet=excluded.YFeet,
                    FacingDegrees=excluded.FacingDegrees,
                    RoomZone=excluded.RoomZone,
                    AcousticZone=excluded.AcousticZone,
                    Attention=excluded.Attention,
                    Activity=excluded.Activity,
                    Concealment=excluded.Concealment,
                    IsActive=excluded.IsActive,
                    UpdatedGameTime=excluded.UpdatedGameTime,
                    UpdatedRealUtc=excluded.UpdatedRealUtc;
                """;
            cmd.Parameters.AddWithValue("$scene", presence.SceneId.Trim());
            cmd.Parameters.AddWithValue("$key", presence.CharacterKey.Trim());
            cmd.Parameters.AddWithValue("$npc", presence.NpcId.HasValue ? (object)presence.NpcId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$player", string.IsNullOrWhiteSpace(presence.PlayerId) ? DBNull.Value : (object)presence.PlayerId.Trim());
            cmd.Parameters.AddWithValue("$name", Clean(presence.DisplayName, presence.CharacterKey));
            cmd.Parameters.AddWithValue("$isPlayer", presence.IsPlayer ? 1 : 0);
            cmd.Parameters.AddWithValue("$x", presence.XFeet);
            cmd.Parameters.AddWithValue("$y", presence.YFeet);
            cmd.Parameters.AddWithValue("$facing", NormalizeDegrees(presence.FacingDegrees));
            cmd.Parameters.AddWithValue("$room", Clean(presence.RoomZone, scene.DefaultRoomZone));
            cmd.Parameters.AddWithValue("$acoustic", Clean(presence.AcousticZone, scene.DefaultAcousticZone));
            cmd.Parameters.AddWithValue("$attention", Clamp01(presence.Attention));
            cmd.Parameters.AddWithValue("$activity", Clean(presence.Activity, "idle"));
            cmd.Parameters.AddWithValue("$concealment", Clamp01(presence.Concealment));
            cmd.Parameters.AddWithValue("$active", presence.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }

        Publish(presence.SceneId);
    }

    public async Task RemovePresenceAsync(
        string sceneId,
        string characterKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(characterKey))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ScenePresence WHERE SceneId=$scene AND CharacterKey=$key;";
            cmd.Parameters.AddWithValue("$scene", sceneId.Trim());
            cmd.Parameters.AddWithValue("$key", characterKey.Trim());
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }

        Publish(sceneId);
    }

    public async Task SetBarrierAsync(
        SceneBarrierUpdate barrier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barrier.SceneId) ||
            string.IsNullOrWhiteSpace(barrier.CharacterAKey) ||
            string.IsNullOrWhiteSpace(barrier.CharacterBKey))
            throw new ArgumentException("SceneId and both character keys are required.", nameof(barrier));

        var (a, b) = CanonicalPair(barrier.CharacterAKey, barrier.CharacterBKey);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SceneBarrier
                    (SceneId,CharacterAKey,CharacterBKey,Label,AcousticPenalty,VisualPenalty,UpdatedRealUtc)
                VALUES($scene,$a,$b,$label,$audio,$visual,$real)
                ON CONFLICT(SceneId,CharacterAKey,CharacterBKey) DO UPDATE SET
                    Label=excluded.Label,
                    AcousticPenalty=excluded.AcousticPenalty,
                    VisualPenalty=excluded.VisualPenalty,
                    UpdatedRealUtc=excluded.UpdatedRealUtc;
                """;
            cmd.Parameters.AddWithValue("$scene", barrier.SceneId.Trim());
            cmd.Parameters.AddWithValue("$a", a);
            cmd.Parameters.AddWithValue("$b", b);
            cmd.Parameters.AddWithValue("$label", Clean(barrier.Label, "barrier"));
            cmd.Parameters.AddWithValue("$audio", Clamp01(barrier.AcousticPenalty));
            cmd.Parameters.AddWithValue("$visual", Clamp01(barrier.VisualPenalty));
            cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }

        Publish(barrier.SceneId);
    }

    public async Task<IReadOnlyList<ScenePerceivedPresence>> GetPerceivedPresenceAsync(
        string sceneId,
        string observerCharacterKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            var scene = LoadScene(conn, sceneId);
            if (scene is null)
                return Array.Empty<ScenePerceivedPresence>();

            var members = LoadMembers(conn, sceneId);
            var observer = members.FirstOrDefault(x => KeyEquals(x.CharacterKey, observerCharacterKey));
            if (observer is null || !observer.IsActive)
                return Array.Empty<ScenePerceivedPresence>();

            var result = new List<ScenePerceivedPresence>();
            foreach (var target in members.Where(x => x.IsActive))
            {
                var distance = Distance(observer, target);
                if (KeyEquals(target.CharacterKey, observer.CharacterKey))
                {
                    result.Add(ToPerceived(target, 0, 1, "you"));
                    continue;
                }

                var barrier = LoadBarrier(conn, sceneId, observer.CharacterKey, target.CharacterKey);
                var visibility = ComputeVisibility(scene, observer, target, barrier);

                // Never reveal a hidden/unperceived member in presentation state.
                if (visibility < 0.42)
                    continue;

                result.Add(ToPerceived(target, distance, visibility, PresenceNote(distance, visibility, barrier)));
            }

            return result
                .OrderBy(x => x.DistanceFeet)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(12) // practical UI cap: 10 NPCs + 2 players
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScenePerceptionResult> ResolveSpeechAsync(
        SceneSpeechEvent speech,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(speech.SceneId) || string.IsNullOrWhiteSpace(speech.SpeakerCharacterKey))
            throw new ArgumentException("SceneId and SpeakerCharacterKey are required.", nameof(speech));

        var eventKey = string.IsNullOrWhiteSpace(speech.EventKey)
            ? "speech:" + Guid.NewGuid().ToString("N")
            : speech.EventKey.Trim();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            var scene = LoadScene(conn, speech.SceneId)
                ?? throw new InvalidOperationException($"Scene '{speech.SceneId}' has not been registered.");
            var members = LoadMembers(conn, speech.SceneId);
            var speaker = members.FirstOrDefault(x => KeyEquals(x.CharacterKey, speech.SpeakerCharacterKey))
                ?? throw new InvalidOperationException($"Speaker '{speech.SpeakerCharacterKey}' is not in scene '{speech.SceneId}'.");

            var observers = new List<SceneListenerPerception>();
            foreach (var listener in members.Where(x => x.IsActive && !KeyEquals(x.CharacterKey, speaker.CharacterKey)))
            {
                var barrier = LoadBarrier(conn, speech.SceneId, speaker.CharacterKey, listener.CharacterKey);
                var distance = Distance(speaker, listener);
                var intended = speech.IntendedListenerKeys.Any(x => KeyEquals(x, listener.CharacterKey));
                var score = ComputeHearingScore(scene, speaker, listener, barrier, speech.VoiceLevel, intended);
                var probability = Math.Clamp((score - 0.10) / 0.82, 0, 1);

                // Stable roll: same event + observer always produces the same result.
                // A nearby intended listener normally hears direct speech unless a real
                // acoustic blocker/noise problem defeats it; bystanders still get chance.
                var roll = StableUnit(eventKey + "|" + listener.CharacterKey);
                var directRange = VoiceRange(speech.VoiceLevel) * 0.85;
                var barrierPenalty = Clamp01(barrier?.AcousticPenalty ?? 0);
                var directIntended = intended && distance <= directRange &&
                                     score >= 0.35 && barrierPenalty < 0.65;
                var heard = directIntended || (intended
                    ? score >= 0.22 && roll <= Math.Min(1, probability + 0.28)
                    : roll <= probability);

                string quality;
                string perceived;
                double confidence;

                if (!heard || score < 0.20)
                {
                    quality = "none";
                    perceived = "";
                    confidence = score;
                }
                else if (score >= 0.66)
                {
                    quality = "clear";
                    perceived = speech.Text.Trim();
                    confidence = score;
                }
                else if (score >= 0.44)
                {
                    quality = "partial";
                    perceived = FragmentText(speech.Text, eventKey + "|partial|" + listener.CharacterKey, 0.64);
                    confidence = score;
                }
                else
                {
                    quality = "fragment";
                    perceived = FragmentText(speech.Text, eventKey + "|fragment|" + listener.CharacterKey, 0.34);
                    confidence = score;
                }

                var row = new SceneListenerPerception
                {
                    ObserverCharacterKey = listener.CharacterKey,
                    DisplayName = listener.DisplayName,
                    DistanceFeet = distance,
                    Quality = quality,
                    Confidence = confidence,
                    PerceivedText = perceived,
                    BarrierLabel = barrier?.Label ?? ""
                };
                observers.Add(row);

                if (row.Perceived)
                    InsertEvidence(conn, eventKey, speech.SceneId, "speech", speaker.CharacterKey, row);
            }

            return new ScenePerceptionResult
            {
                EventKey = eventKey,
                EventKind = "speech",
                SourceCharacterKey = speaker.CharacterKey,
                Observers = observers
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScenePerceptionResult> ResolveVisualAsync(
        SceneVisualEvent visual,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(visual.SceneId) || string.IsNullOrWhiteSpace(visual.ActorCharacterKey))
            throw new ArgumentException("SceneId and ActorCharacterKey are required.", nameof(visual));

        var eventKey = string.IsNullOrWhiteSpace(visual.EventKey)
            ? "visual:" + Guid.NewGuid().ToString("N")
            : visual.EventKey.Trim();
        var kind = visual.VisualKind.Equals("body_language", StringComparison.OrdinalIgnoreCase)
            ? "body_language"
            : "action";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            var scene = LoadScene(conn, visual.SceneId)
                ?? throw new InvalidOperationException($"Scene '{visual.SceneId}' has not been registered.");
            var members = LoadMembers(conn, visual.SceneId);
            var actor = members.FirstOrDefault(x => KeyEquals(x.CharacterKey, visual.ActorCharacterKey))
                ?? throw new InvalidOperationException($"Actor '{visual.ActorCharacterKey}' is not in scene '{visual.SceneId}'.");

            var observers = new List<SceneListenerPerception>();
            foreach (var observer in members.Where(x => x.IsActive && !KeyEquals(x.CharacterKey, actor.CharacterKey)))
            {
                var barrier = LoadBarrier(conn, visual.SceneId, actor.CharacterKey, observer.CharacterKey);
                var distance = Distance(actor, observer);
                var visibility = ComputeVisibility(scene, observer, actor, barrier);
                var salience = Math.Clamp(visual.Salience, 0.05, 1);
                var distanceDetail = Math.Clamp(1.08 - (distance / 70.0), 0.15, 1.0);
                var score = Clamp01(visibility * distanceDetail * (0.50 + 0.50 * salience));

                // Micro body language is deliberately harder to notice than an obvious action.
                if (kind == "body_language")
                    score *= 0.82;

                // Vision should be stable, not flicker randomly on repeated UI turns.
                // Distance/lighting/clutter/barriers decide whether the cue is available.
                var seen = score >= 0.30;

                string quality;
                if (!seen)
                    quality = "none";
                else if (score >= 0.72)
                    quality = "clear";
                else if (score >= 0.48)
                    quality = "partial";
                else
                    quality = "glimpse";

                var row = new SceneListenerPerception
                {
                    ObserverCharacterKey = observer.CharacterKey,
                    DisplayName = observer.DisplayName,
                    DistanceFeet = distance,
                    Quality = quality,
                    Confidence = score,
                    PerceivedText = quality == "none" ? "" : visual.Text.Trim(),
                    BarrierLabel = barrier?.Label ?? ""
                };
                observers.Add(row);

                if (row.Perceived)
                    InsertEvidence(conn, eventKey, visual.SceneId, kind, actor.CharacterKey, row);
            }

            return new ScenePerceptionResult
            {
                EventKey = eventKey,
                EventKind = kind,
                SourceCharacterKey = actor.CharacterKey,
                Observers = observers
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ScenePerceptionEvidence>> GetEvidenceAsync(
        string observerCharacterKey,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id,EventKey,SceneId,EventKind,SourceCharacterKey,
                       ObserverCharacterKey,Quality,PerceivedText,Confidence,
                       DistanceFeet,GameTime
                FROM ScenePerceptionEvidence
                WHERE ObserverCharacterKey=$observer
                ORDER BY Id DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$observer", observerCharacterKey.Trim());
            cmd.Parameters.AddWithValue("$limit", limit);

            var rows = new List<ScenePerceptionEvidence>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new ScenePerceptionEvidence
                {
                    Id = r.GetInt64(0),
                    EventKey = r.GetString(1),
                    SceneId = r.GetString(2),
                    EventKind = r.GetString(3),
                    SourceCharacterKey = r.GetString(4),
                    ObserverCharacterKey = r.GetString(5),
                    Quality = r.GetString(6),
                    PerceivedText = r.GetString(7),
                    Confidence = r.GetDouble(8),
                    DistanceFeet = r.GetDouble(9),
                    GameTime = DateTimeOffset.TryParse(r.GetString(10), out var t) ? t : _clock.Now
                });
            }
            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    private double ComputeHearingScore(
        SceneRow scene,
        PresenceRow speaker,
        PresenceRow listener,
        BarrierRow? barrier,
        string voiceLevel,
        bool intended)
    {
        var distance = Distance(speaker, listener);
        var range = VoiceRange(voiceLevel);

        var distanceFactor = Math.Exp(-distance / range);
        var sameRoom = KeyEquals(speaker.RoomZone, listener.RoomZone);
        var sameAcoustic = KeyEquals(speaker.AcousticZone, listener.AcousticZone);
        var zoneFactor = sameRoom ? 1.0 : sameAcoustic ? 0.58 : 0.20;
        var noiseFactor = 1.0 - (Clamp01(scene.AmbientNoise) * 0.62);
        var attentionFactor = 0.55 + (Clamp01(listener.Attention) * 0.45);
        var activityFactor = ActivityHearingFactor(listener.Activity);
        var barrierFactor = 1.0 - Clamp01(barrier?.AcousticPenalty ?? 0);
        var intendedFactor = intended ? 1.12 : 1.0;

        return Clamp01(distanceFactor * zoneFactor * noiseFactor * attentionFactor * activityFactor * barrierFactor * intendedFactor);
    }

    private static double ComputeVisibility(
        SceneRow scene,
        PresenceRow observer,
        PresenceRow target,
        BarrierRow? barrier)
    {
        var distance = Distance(observer, target);
        var distanceFactor = Math.Clamp(1.10 - (distance / 125.0), 0.18, 1.0);
        var sameRoom = KeyEquals(observer.RoomZone, target.RoomZone);
        var roomFactor = sameRoom ? 1.0 : 0.32;
        var clutterFactor = 1.0 - (Clamp01(scene.VisualClutter) * 0.56);
        var attentionFactor = 0.60 + (Clamp01(observer.Attention) * 0.40);
        var facingFactor = FacingVisibilityFactor(observer, target);
        var concealmentFactor = 1.0 - (Clamp01(target.Concealment) * 0.88);
        var barrierFactor = 1.0 - Clamp01(barrier?.VisualPenalty ?? 0);
        return Clamp01(distanceFactor * roomFactor * clutterFactor * attentionFactor * facingFactor * concealmentFactor * barrierFactor);
    }

    private void InsertEvidence(
        SqliteConnection conn,
        string eventKey,
        string sceneId,
        string eventKind,
        string sourceKey,
        SceneListenerPerception row)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO ScenePerceptionEvidence
                (EventKey,SceneId,EventKind,SourceCharacterKey,ObserverCharacterKey,
                 Quality,PerceivedText,Confidence,DistanceFeet,GameTime,CreatedRealUtc)
            VALUES
                ($event,$scene,$kind,$source,$observer,$quality,$text,$confidence,$distance,$game,$real);
            """;
        cmd.Parameters.AddWithValue("$event", eventKey);
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$kind", eventKind);
        cmd.Parameters.AddWithValue("$source", sourceKey);
        cmd.Parameters.AddWithValue("$observer", row.ObserverCharacterKey);
        cmd.Parameters.AddWithValue("$quality", row.Quality);
        cmd.Parameters.AddWithValue("$text", row.PerceivedText ?? "");
        cmd.Parameters.AddWithValue("$confidence", row.Confidence);
        cmd.Parameters.AddWithValue("$distance", row.DistanceFeet);
        cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static ScenePerceivedPresence ToPerceived(
        PresenceRow row,
        double distance,
        double visibility,
        string note)
        => new()
        {
            CharacterKey = row.CharacterKey,
            NpcId = row.NpcId,
            PlayerId = row.PlayerId,
            DisplayName = row.DisplayName,
            IsPlayer = row.IsPlayer,
            DistanceFeet = distance,
            VisibilityConfidence = visibility,
            Note = note
        };

    private static string PresenceNote(double distance, double visibility, BarrierRow? barrier)
    {
        var distanceText = distance switch
        {
            <= 6 => "very close",
            <= 15 => "nearby",
            <= 30 => "across the room",
            <= 60 => "farther away",
            _ => "in the distance"
        };

        if (barrier is not null && barrier.VisualPenalty >= 0.35)
            return distanceText + " · partly obscured";
        if (visibility < 0.58)
            return distanceText + " · hard to make out";
        return distanceText;
    }

    private static double VoiceRange(string voiceLevel)
        => (voiceLevel ?? "normal").Trim().ToLowerInvariant() switch
        {
            "whisper" => 8.0,
            "quiet" => 14.0,
            "raised" => 48.0,
            "shout" => 90.0,
            _ => 28.0
        };

    private static double FacingVisibilityFactor(PresenceRow observer, PresenceRow target)
    {
        var dx = target.XFeet - observer.XFeet;
        var dy = target.YFeet - observer.YFeet;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return 1.0;

        var targetAngle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
        if (targetAngle < 0) targetAngle += 360.0;

        var delta = Math.Abs(NormalizeDegrees(targetAngle) - NormalizeDegrees(observer.FacingDegrees));
        if (delta > 180.0) delta = 360.0 - delta;

        return delta switch
        {
            <= 70 => 1.0,
            <= 120 => 0.78,
            _ => 0.46
        };
    }

    private static double ActivityHearingFactor(string activity)
    {
        var a = (activity ?? "").Trim().ToLowerInvariant();
        if (a.Contains("sleep")) return 0.05;
        if (a.Contains("headphone") || a.Contains("earbud")) return 0.14;
        if (a.Contains("driv")) return 0.62;
        if (a.Contains("busy")) return 0.70;
        if (a.Contains("work")) return 0.78;
        if (a.Contains("talk")) return 0.82;
        return 1.0;
    }

    private static string FragmentText(string text, string seed, double keepRatio)
    {
        var words = (text ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return "";
        if (words.Length <= 3 && keepRatio >= 0.60)
            return string.Join(" ", words);

        var kept = new bool[words.Length];
        var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var needed = Math.Max(1, (int)Math.Round(words.Length * keepRatio));

        // Prefer content-bearing words, but never invent or substitute words.
        var ranked = Enumerable.Range(0, words.Length)
            .OrderByDescending(i => WordWeight(words[i]))
            .ThenBy(i => seedBytes[i % seedBytes.Length])
            .Take(needed)
            .ToHashSet();

        for (var i = 0; i < words.Length; i++)
            kept[i] = ranked.Contains(i);

        var parts = new List<string>();
        var gap = false;
        for (var i = 0; i < words.Length; i++)
        {
            if (kept[i])
            {
                if (gap && parts.Count > 0)
                    parts.Add("…");
                parts.Add(words[i]);
                gap = false;
            }
            else
            {
                gap = true;
            }
        }
        if (gap && parts.Count > 0)
            parts.Add("…");

        return string.Join(" ", parts);
    }

    private static int WordWeight(string word)
    {
        var cleaned = new string(word.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length >= 7) return 4;
        if (cleaned.Length >= 5) return 3;
        if (cleaned.Length >= 3) return 2;
        return 1;
    }

    private static double StableUnit(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var value = BitConverter.ToUInt64(bytes, 0);
        return value / (double)ulong.MaxValue;
    }

    private static double Distance(PresenceRow a, PresenceRow b)
    {
        var dx = a.XFeet - b.XFeet;
        var dy = a.YFeet - b.YFeet;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private SceneRow? LoadScene(SqliteConnection conn, string sceneId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT SceneId,LocationId,DisplayName,AmbientNoise,VisualClutter,
                   DefaultRoomZone,DefaultAcousticZone
            FROM ActiveScene WHERE SceneId=$scene LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$scene", sceneId.Trim());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SceneRow
        {
            SceneId = r.GetString(0),
            LocationId = r.GetString(1),
            DisplayName = r.GetString(2),
            AmbientNoise = r.GetDouble(3),
            VisualClutter = r.GetDouble(4),
            DefaultRoomZone = r.GetString(5),
            DefaultAcousticZone = r.GetString(6)
        };
    }

    private static List<PresenceRow> LoadMembers(SqliteConnection conn, string sceneId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CharacterKey,NpcId,PlayerId,DisplayName,IsPlayer,
                   XFeet,YFeet,FacingDegrees,RoomZone,AcousticZone,
                   Attention,Activity,Concealment,IsActive
            FROM ScenePresence
            WHERE SceneId=$scene;
            """;
        cmd.Parameters.AddWithValue("$scene", sceneId.Trim());
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

    private static BarrierRow? LoadBarrier(
        SqliteConnection conn,
        string sceneId,
        string one,
        string two)
    {
        var (a, b) = CanonicalPair(one, two);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Label,AcousticPenalty,VisualPenalty
            FROM SceneBarrier
            WHERE SceneId=$scene AND CharacterAKey=$a AND CharacterBKey=$b
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$scene", sceneId.Trim());
        cmd.Parameters.AddWithValue("$a", a);
        cmd.Parameters.AddWithValue("$b", b);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new BarrierRow
        {
            Label = r.GetString(0),
            AcousticPenalty = r.GetDouble(1),
            VisualPenalty = r.GetDouble(2)
        };
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ActiveScene(
                SceneId TEXT PRIMARY KEY,
                LocationId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                AmbientNoise REAL NOT NULL DEFAULT 0.15,
                VisualClutter REAL NOT NULL DEFAULT 0.10,
                DefaultRoomZone TEXT NOT NULL DEFAULT 'main',
                DefaultAcousticZone TEXT NOT NULL DEFAULT 'main',
                UpdatedGameTime TEXT NOT NULL,
                UpdatedRealUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ScenePresence(
                SceneId TEXT NOT NULL,
                CharacterKey TEXT NOT NULL,
                NpcId INTEGER NULL,
                PlayerId TEXT NULL,
                DisplayName TEXT NOT NULL,
                IsPlayer INTEGER NOT NULL DEFAULT 0,
                XFeet REAL NOT NULL DEFAULT 0,
                YFeet REAL NOT NULL DEFAULT 0,
                FacingDegrees REAL NOT NULL DEFAULT 0,
                RoomZone TEXT NOT NULL DEFAULT 'main',
                AcousticZone TEXT NOT NULL DEFAULT 'main',
                Attention REAL NOT NULL DEFAULT 0.70,
                Activity TEXT NOT NULL DEFAULT 'idle',
                Concealment REAL NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedGameTime TEXT NOT NULL,
                UpdatedRealUtc TEXT NOT NULL,
                PRIMARY KEY(SceneId,CharacterKey)
            );

            CREATE INDEX IF NOT EXISTS IX_ScenePresence_SceneActive
                ON ScenePresence(SceneId,IsActive);

            CREATE TABLE IF NOT EXISTS SceneBarrier(
                SceneId TEXT NOT NULL,
                CharacterAKey TEXT NOT NULL,
                CharacterBKey TEXT NOT NULL,
                Label TEXT NOT NULL DEFAULT 'barrier',
                AcousticPenalty REAL NOT NULL DEFAULT 0,
                VisualPenalty REAL NOT NULL DEFAULT 0,
                UpdatedRealUtc TEXT NOT NULL,
                PRIMARY KEY(SceneId,CharacterAKey,CharacterBKey)
            );

            CREATE TABLE IF NOT EXISTS ScenePerceptionEvidence(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventKey TEXT NOT NULL,
                SceneId TEXT NOT NULL,
                EventKind TEXT NOT NULL,
                SourceCharacterKey TEXT NOT NULL,
                ObserverCharacterKey TEXT NOT NULL,
                Quality TEXT NOT NULL,
                PerceivedText TEXT NOT NULL DEFAULT '',
                Confidence REAL NOT NULL DEFAULT 0,
                DistanceFeet REAL NOT NULL DEFAULT 0,
                GameTime TEXT NOT NULL,
                CreatedRealUtc TEXT NOT NULL,
                UNIQUE(EventKey,ObserverCharacterKey,EventKind)
            );

            CREATE INDEX IF NOT EXISTS IX_ScenePerceptionEvidence_Observer
                ON ScenePerceptionEvidence(ObserverCharacterKey,Id DESC);
            CREATE INDEX IF NOT EXISTS IX_ScenePerceptionEvidence_Event
                ON ScenePerceptionEvidence(EventKey,ObserverCharacterKey);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        return conn;
    }

    private void Publish(string sceneId)
    {
        try { SceneChanged?.Invoke(sceneId); } catch { }
    }

    private static (string A, string B) CanonicalPair(string one, string two)
    {
        one = one.Trim();
        two = two.Trim();
        return string.Compare(one, two, StringComparison.OrdinalIgnoreCase) <= 0
            ? (one, two)
            : (two, one);
    }

    private static bool KeyEquals(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double NormalizeDegrees(double value)
    {
        value %= 360;
        if (value < 0) value += 360;
        return value;
    }

    private sealed class SceneRow
    {
        public string SceneId { get; set; } = "";
        public string LocationId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public double AmbientNoise { get; set; }
        public double VisualClutter { get; set; }
        public string DefaultRoomZone { get; set; } = "main";
        public string DefaultAcousticZone { get; set; } = "main";
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

    private sealed class BarrierRow
    {
        public string Label { get; set; } = "";
        public double AcousticPenalty { get; set; }
        public double VisualPenalty { get; set; }
    }
}
