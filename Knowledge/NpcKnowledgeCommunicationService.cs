using ProjectEve.Core.Knowledge;
using ProjectEve.Core.Scene;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Knowledge;

/// <summary>
/// Telephone-game bridge:
/// source NPC speaks what IT knows -> scene hearing decides who actually hears ->
/// each hearing NPC receives a NEW reported claim containing only what that NPC heard.
/// </summary>
public sealed class NpcKnowledgeCommunicationService : INpcKnowledgeCommunicationService
{
    private readonly INpcKnowledgeService _knowledge;
    private readonly IScenePerceptionService _scene;

    public NpcKnowledgeCommunicationService(
        INpcKnowledgeService knowledge,
        IScenePerceptionService scene)
    {
        _knowledge = knowledge;
        _scene = scene;
    }

    public async Task<NpcKnowledgeSpeechResult> SpeakKnownClaimAsync(
        NpcKnowledgeSpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.FromNpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.FromNpcId));
        if (request.SourceClaimId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.SourceClaimId));
        if (string.IsNullOrWhiteSpace(request.SceneId))
            throw new ArgumentException("SceneId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SpokenText))
            throw new ArgumentException("SpokenText is required.", nameof(request));

        var intended = (request.IntendedNpcIds ?? Array.Empty<int>())
            .Where(x => x > 0 && x != request.FromNpcId)
            .Distinct()
            .Select(x => "npc:" + x.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        var sceneResult = await _scene.ResolveSpeechAsync(
            new SceneSpeechEvent
            {
                SceneId = request.SceneId.Trim(),
                SpeakerCharacterKey = "npc:" + request.FromNpcId.ToString(CultureInfo.InvariantCulture),
                Text = request.SpokenText.Trim(),
                VoiceLevel = string.IsNullOrWhiteSpace(request.VoiceLevel) ? "normal" : request.VoiceLevel.Trim(),
                IntendedListenerKeys = intended,
                EventKey = "knowledge-speech:" + request.SourceClaimId.ToString(CultureInfo.InvariantCulture) + ":" + Guid.NewGuid().ToString("N")
            },
            cancellationToken);

        var recipients = new List<NpcKnowledgeSpeechRecipient>();

        foreach (var observer in sceneResult.Observers)
        {
            if (!observer.Perceived ||
                !TryParseNpcKey(observer.ObserverCharacterKey, out var toNpcId) ||
                toNpcId == request.FromNpcId)
            {
                continue;
            }

            var transfer = await _knowledge.TransmitAsync(
                new NpcKnowledgeTransmissionRequest
                {
                    FromNpcId = request.FromNpcId,
                    ToNpcId = toNpcId,
                    SourceClaimId = request.SourceClaimId,
                    PlayerId = request.PlayerId ?? "",
                    ReportedText = observer.PerceivedText,
                    RecipientConfidenceOverride = Math.Clamp(
                        (int)Math.Round(observer.Confidence * 100.0) - 8,
                        10,
                        88),
                    Channel = string.IsNullOrWhiteSpace(request.Channel) ? "in_person" : request.Channel.Trim(),
                    SceneId = request.SceneId.Trim()
                },
                cancellationToken);

            recipients.Add(new NpcKnowledgeSpeechRecipient
            {
                NpcId = toNpcId,
                PerceptionQuality = observer.Quality,
                PerceivedText = observer.PerceivedText,
                KnowledgeTransferred = transfer.Transmitted,
                RecipientClaimId = transfer.RecipientClaim?.Id ?? 0,
                TransmissionId = transfer.TransmissionId
            });
        }

        return new NpcKnowledgeSpeechResult
        {
            SceneEventKey = sceneResult.EventKey,
            HeardByNpcCount = recipients.Count,
            Recipients = recipients
        };
    }

    private static bool TryParseNpcKey(string key, out int npcId)
    {
        npcId = 0;
        if (string.IsNullOrWhiteSpace(key) ||
            !key.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(
            key[4..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out npcId) && npcId > 0;
    }
}
