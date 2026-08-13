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
    /// Example integration bridge for Brain.cs.
    ///
    /// This intentionally keeps LineBank outside DialogueEngineText:
    /// Brain owns speaker/intent/bank policy.
    /// DialogueEngineText only asks for fallback when its LLM is slow or fails.
    /// </summary>
    public static class BrainDialogueIntegration
    {
        private static readonly LineBankService LineBank = new();

        public static async Task<string> ReplyTextAsync(
            Brain brain,
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            string lineBankSpeaker = "eve2",
            int fallbackAfterMs = 3500)
        {
            string? PullFallback()
            {
                try
                {
                    if (!LineBank.DbExists)
                        return null;

                    var traits = thought.Tags
                        .Select(t => t.TraitId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(4)
                        .ToList();

                    int intensity = thought.Tags.Count == 0
                        ? 5
                        : Math.Clamp(
                            (int)Math.Round(thought.Tags.Average(t => t.Intensity)),
                            1,
                            10);

                    if (traits.Count == 0 && owner.Traits != null)
                    {
                        traits = owner.Traits.GetAll()
                            .Where(kv =>
                                TraitEngine.FastIds.Contains(kv.Key) &&
                                kv.Value >= 65)
                            .OrderByDescending(kv => kv.Value)
                            .Take(4)
                            .Select(kv => kv.Key)
                            .ToList();
                    }

                    string? intent = LineBankService.GuessIntent(playerMessage);

                    var hit = LineBank.TryPull(
                        lineBankSpeaker,
                        intent,
                        traits,
                        intensity,
                        channel: "text");

                    return hit?.Text;
                }
                catch
                {
                    return null;
                }
            }

            void StoreFresh(string fresh)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(fresh) || fresh == "...")
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

                    LineBank.StoreLiveLine(
                        lineBankSpeaker,
                        intent,
                        fresh,
                        intensity,
                        "text",
                        traitWeights.Count == 0 ? null : traitWeights);
                }
                catch
                {
                    // Bank enrichment must never break dialogue.
                }
            }

            var result = await DialogueEngineText.GenerateAsync(
                owner,
                playerMessage,
                thought,
                recentChat,
                relationshipContext,
                pullFallbackLine: PullFallback,
                storeLlmLine: StoreFresh,
                softTimeoutMs: fallbackAfterMs);

            return result.Text;
        }

        public static async Task<string> ReplyInPersonAsync(
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            string sceneContext)
        {
            var result = await DialogueEngineInPerson.GenerateAsync(
                owner,
                playerMessage,
                thought,
                recentChat,
                relationshipContext,
                sceneContext);

            return result.ToPlayerText();
        }
    }
}
