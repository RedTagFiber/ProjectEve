using ProjectEve.Characters.Base;
using ProjectEve.Narrative.Texting;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Bridge between Brain, LineBank, and the split dialogue engines.
    ///
    /// TEXT FLOW:
    /// ThoughtPacket -> LineBank candidate seed -> Qwen final reply -> grow LineBank.
    ///
    /// A LineBank hit is NOT sent directly to the player.
    /// It is only a candidate for Qwen to adapt/ignore.
    /// Direct bank output is reserved for a genuine dialogue-model failure.
    /// </summary>
    public static class BrainDialogueIntegration
    {
        private static readonly LineBankService LineBank = new();

        public static async Task<TextDialogueResult> ReplyTextAsync(
            Brain brain,
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            string lineBankSpeaker = "eve2")
        {
            LineHit? seedHit = PullSeed(
                brain,
                owner,
                playerMessage,
                thought,
                lineBankSpeaker);

            string? seed = seedHit?.Text;

            if (seedHit != null)
                brain.RecordLineBankSeed(seedHit);
            else
                brain.RecordNoLineBankSeed();

            void StoreFinal(string finalText)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(finalText) ||
                        finalText == "..." ||
                        finalText.StartsWith('('))
                        return;

                    string intent =
                        LineBankService.GuessIntent(playerMessage) ??
                        "intent.meta.nothing";

                    var traitWeights = thought.Tags
                        .Select(t => t.TraitId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(t => t, _ => 1.0);

                    int intensity = thought.Tags.Count == 0
                        ? 5
                        : Math.Clamp(
                            (int)Math.Round(thought.Tags.Average(t => t.Intensity)),
                            1,
                            10);

                    // Keep useful multi-bubble text together when appropriate.
                    var parts = finalText
                        .Split(new[] { '\r', '\n' },
                            StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToList();

                    if (parts.Count is >= 2 and <= 5 &&
                        parts.All(x => x.Length < 120))
                    {
                        LineBank.StoreLiveCombo(
                            lineBankSpeaker,
                            intent,
                            parts,
                            "text");
                        return;
                    }

                    LineBank.StoreLiveLine(
                        lineBankSpeaker,
                        intent,
                        finalText,
                        style: null,
                        intensity: intensity,
                        channel: "text",
                        traitWeights:
                            traitWeights.Count == 0
                                ? null
                                : traitWeights);
                }
                catch
                {
                    // Bank growth must never break dialogue.
                }
            }

            return await DialogueEngineText.GenerateAsync(
                owner,
                playerMessage,
                thought,
                recentChat,
                relationshipContext,
                lineBankSeed: seed,
                previousNpcReply: brain.LastNpcReply,
                storeFinalLine: StoreFinal);
        }

        public static async Task<InPersonDialogueResult> ReplyInPersonAsync(
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            string sceneContext)
        {
            return await DialogueEngineInPerson.GenerateAsync(
                owner,
                playerMessage,
                thought,
                recentChat,
                relationshipContext,
                sceneContext);
        }

        private static LineHit? PullSeed(
            Brain brain,
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string lineBankSpeaker)
        {
            try
            {
                if (!LineBank.DbExists)
                    return null;

                // Cooldown rule #2:
                // after two LineBank-assisted replies in a row, force one AI_NEW turn.
                if (!brain.CanUseLineBankSeed)
                    return null;

                // Strong-match gate:
                // no recognized message intent = no seed.
                // This prevents generic trait-based lines from hijacking ordinary chat.
                string? intent = LineBankService.GuessIntent(playerMessage);
                if (string.IsNullOrWhiteSpace(intent))
                    return null;

                var traits = thought.Tags
                    .Select(t => t.TraitId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();

                int intensity = thought.Tags.Count == 0
                    ? 5
                    : Math.Clamp(
                        (int)Math.Round(
                            thought.Tags.Average(t => t.Intensity)),
                        1,
                        10);

                var hit = LineBank.TryPull(
                    lineBankSpeaker,
                    intent,
                    traits,
                    intensity,
                    channel: "text",
                    excludeRowId: brain.LastLineBankSeedRowId);

                if (hit == null)
                    return null;

                // TryPull may fall back to a trait-derived intent if the requested
                // intent had no row. For seed mode, that is too weak a match.
                string expectedIntent = intent.StartsWith("intent.", StringComparison.OrdinalIgnoreCase)
                    ? intent
                    : "intent." + intent;

                if (!hit.IntentId.Equals(
                        expectedIntent,
                        StringComparison.OrdinalIgnoreCase))
                    return null;

                // Cooldown rule #1:
                // never use the same wording on back-to-back LineBank turns,
                // even if duplicate rows exist in the DB.
                if (brain.IsSameAsLastSeed(hit.Text))
                    return null;

                return hit;
            }
            catch
            {
                return null;
            }
        }
    }
}
