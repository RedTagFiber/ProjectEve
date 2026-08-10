using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ProjectEve.Traits
{
    /// <summary>
    /// Fast trait movement from Thought tags (preferred) or keyword fallback.
    /// Mid rare/small; Slow never here.
    /// </summary>
    public static class TraitEngine
    {
        public static readonly string[] FastIds =
        {
            "trait.anger", "trait.anxiety", "trait.fear", "trait.shame", "trait.guilt",
            "trait.hurt", "trait.jealousy", "trait.resentment", "trait.trust", "trait.affection",
            "trait.desire", "trait.attraction", "trait.tension", "trait.playfulness", "trait.pride",
            "trait.patience", "trait.guard", "trait.openness", "trait.loneliness", "trait.hope"
        };

        // ============================================================
        // APPLY SINGLE / BATCH
        // ============================================================
        public static void ApplyTag(
            SimCharacter npc,
            string traitId,
            float baseDelta,
            int intensity = 5)
        {
            if (npc?.Traits == null || string.IsNullOrWhiteSpace(traitId))
                return;
            if (traitId.StartsWith("slow.", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(traitId, "none", StringComparison.OrdinalIgnoreCase))
                return;

            float scale = Math.Clamp(intensity, 1, 10) / 5f;
            float delta = baseDelta * scale;

            if (traitId.StartsWith("mid.", StringComparison.OrdinalIgnoreCase))
                delta *= 0.35f;

            float current = npc.Traits.Get(traitId);
            if (delta < 0 && current >= 70f) delta *= 1.25f;
            if (delta > 0 && current <= 30f) delta *= 1.15f;

            npc.Traits.Adjust(traitId, delta);
        }

        public static void ApplyTags(
            SimCharacter npc,
            IEnumerable<(string TraitId, float Delta, int Intensity)> tags)
        {
            if (npc?.Traits == null || tags == null) return;
            foreach (var t in tags)
                ApplyTag(npc, t.TraitId, t.Delta, t.Intensity);
        }

        // ============================================================
        // PARSE THOUGHT TAGS
        // Format: TAGS: trait.affection+3@6; trait.anger-2@5
        // or:     TAGS: none
        // ============================================================
        public static List<(string TraitId, float Delta, int Intensity)> ParseThoughtTags(string? thought)
        {
            var list = new List<(string, float, int)>();
            if (string.IsNullOrWhiteSpace(thought)) return list;

            int idx = thought.IndexOf("TAGS:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return list;

            string line = thought[(idx + 5)..].Trim();
            int nl = line.IndexOfAny(new[] { '\r', '\n' });
            if (nl >= 0) line = line[..nl].Trim();

            if (line.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                line.Length == 0)
                return list;

            foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var m = Regex.Match(part,
                    @"^(?<id>[\w\.]+)(?<sign>[+-])(?<d>\d+(\.\d+)?)@(?<i>\d+)$",
                    RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                float d = float.Parse(m.Groups["d"].Value);
                if (m.Groups["sign"].Value == "-") d = -d;
                int i = Math.Clamp(int.Parse(m.Groups["i"].Value), 1, 10);
                list.Add((m.Groups["id"].Value, d, i));
            }
            return list;
        }

        /// <summary>
        /// Preferred path: Thought tags if present, else keyword fallback.
        /// </summary>
        public static void UpdateTraitsAfterChat(
            SimCharacter npc,
            string userMessage,
            string? thoughtText = null,
            IEnumerable<(string TraitId, float Delta, int Intensity)>? thoughtTags = null)
        {
            if (npc == null) return;

            var tags = thoughtTags != null
                ? new List<(string, float, int)>(thoughtTags)
                : ParseThoughtTags(thoughtText);

            if (tags.Count > 0)
            {
                ApplyTags(npc, tags);
                return;
            }

            ApplyContextHints(npc, userMessage);
        }

        // Overload matching older call sites
        public static void UpdateTraitsAfterChat(
            SimCharacter npc,
            string userMessage,
            IEnumerable<(string TraitId, float Delta, int Intensity)>? thoughtTags)
        {
            UpdateTraitsAfterChat(npc, userMessage, null, thoughtTags);
        }

        // ============================================================
        // KEYWORD FALLBACK — broad coverage for Fast 20
        // ============================================================
        public static void ApplyContextHints(SimCharacter npc, string context)
        {
            if (npc?.Traits == null || string.IsNullOrWhiteSpace(context))
                return;

            string c = context.ToLowerInvariant();

            // ---- Injected test tags from Program ----
            if (c.Contains("(stress)"))
            {
                ApplyTag(npc, "trait.anger", +6f, 8);
                ApplyTag(npc, "trait.hurt", +4f, 7);
                ApplyTag(npc, "trait.trust", -3f, 6);
                ApplyTag(npc, "trait.affection", -2f, 5);
                ApplyTag(npc, "trait.tension", +3f, 6);
                return;
            }
            if (c.Contains("(comfort)"))
            {
                ApplyTag(npc, "trait.affection", +5f, 7);
                ApplyTag(npc, "trait.trust", +4f, 6);
                ApplyTag(npc, "trait.anger", -4f, 6);
                ApplyTag(npc, "trait.hurt", -3f, 5);
                ApplyTag(npc, "trait.hope", +3f, 5);
                ApplyTag(npc, "trait.anxiety", -2f, 4);
                return;
            }

            // ---- ANGER ----
            if (ContainsAny(c,
                "hate you", "fuck you", "pissed", "angry", "shut up", "idiot", "stupid",
                "disrespect", "yell", "scream", "go to hell", "don't talk to me", "fed up"))
            {
                ApplyTag(npc, "trait.anger", +5f, 7);
                ApplyTag(npc, "trait.patience", -3f, 6);
                ApplyTag(npc, "trait.tension", +3f, 6);
            }

            // ---- HURT ----
            if (ContainsAny(c,
                "hurt me", "you hurt", "broke my", "how could you", "after everything",
                "don't care about me", "threw me away", "abandoned"))
            {
                ApplyTag(npc, "trait.hurt", +5f, 7);
                ApplyTag(npc, "trait.trust", -2f, 5);
                ApplyTag(npc, "trait.affection", -2f, 5);
            }

            // ---- ANXIETY / FEAR ----
            if (ContainsAny(c,
                "worried", "anxious", "nervous", "panic", "what if", "overthinking",
                "afraid", "scared", "terrified", "danger", "something bad"))
            {
                ApplyTag(npc, "trait.anxiety", +4f, 6);
                ApplyTag(npc, "trait.fear", +3f, 5);
            }
            if (ContainsAny(c, "leave me", "don't go", "are you leaving", "walk away"))
            {
                ApplyTag(npc, "trait.fear", +4f, 7);
                ApplyTag(npc, "trait.anxiety", +3f, 6);
                ApplyTag(npc, "trait.loneliness", +2f, 5);
            }

            // ---- SHAME / GUILT ----
            if (ContainsAny(c,
                "embarrassed", "ashamed", "humiliated", "caught", "shouldn't have",
                "look at me", "everyone saw"))
            {
                ApplyTag(npc, "trait.shame", +4f, 6);
                ApplyTag(npc, "trait.guard", +2f, 5);
            }
            if (ContainsAny(c,
                "my fault", "i messed up", "i regret", "i shouldn't", "forgive me", "i was wrong"))
            {
                ApplyTag(npc, "trait.guilt", +4f, 6);
                ApplyTag(npc, "trait.shame", +2f, 4);
            }

            // ---- JEALOUSY / RESENTMENT ----
            if (ContainsAny(c,
                "other guy", "other girl", "with someone", "who is she", "who is he",
                "texting him", "texting her", "ex ", "your ex", "flirting"))
            {
                ApplyTag(npc, "trait.jealousy", +5f, 7);
                ApplyTag(npc, "trait.trust", -3f, 6);
                ApplyTag(npc, "trait.resentment", +2f, 5);
                ApplyTag(npc, "trait.tension", +2f, 5);
            }
            if (ContainsAny(c, "always do this", "never listen", "unfair", "you always", "you never"))
            {
                ApplyTag(npc, "trait.resentment", +3f, 6);
                ApplyTag(npc, "trait.anger", +2f, 5);
            }

            // ---- AFFECTION / TRUST / HOPE (compliments + love) ----
            if (ContainsAny(c,
                "i love you", "love you", "love ", "my love", "in love",
                "miss you", "care about you", "mean a lot", "proud of you",
                "wonderful", "beautiful", "gorgeous", "pretty", "handsome",
                "you look", "look good", "look wonderful", "stunning", "adorable",
                "into you", "only grow", "growing on me", "appreciate you",
                "thank you", "glad you're", "lucky to"))
            {
                ApplyTag(npc, "trait.affection", +4f, 6);
                ApplyTag(npc, "trait.trust", +2f, 5);
                ApplyTag(npc, "trait.hope", +2f, 5);
                ApplyTag(npc, "trait.desire", +1.5f, 4);
                ApplyTag(npc, "trait.anxiety", -1.5f, 4);
                ApplyTag(npc, "trait.hurt", -1f, 3);
            }
            if (ContainsAny(c, "sorry", "forgive", "come here", "hold me", "i'm here"))
            {
                ApplyTag(npc, "trait.affection", +3f, 6);
                ApplyTag(npc, "trait.anger", -3f, 5);
                ApplyTag(npc, "trait.hurt", -2f, 5);
                ApplyTag(npc, "trait.trust", +2f, 5);
            }

            // ---- DESIRE / ATTRACTION / TENSION ----
            if (ContainsAny(c,
                "want you", "need you", "kiss me", "kiss ", "come over", "in bed",
                "horny", "turned on", "touch me", "make out", "sexy", "hot ",
                "tonight", "stay the night", "take off"))
            {
                ApplyTag(npc, "trait.desire", +4f, 7);
                ApplyTag(npc, "trait.attraction", +3f, 6);
                ApplyTag(npc, "trait.tension", +3f, 6);
                ApplyTag(npc, "trait.affection", +1f, 3);
            }

            // ---- PLAYFULNESS ----
            if (ContainsAny(c,
                "haha", "lol", "lmao", "kidding", "teasing", "joke", "joking",
                "goofy", "silly", "play with"))
            {
                ApplyTag(npc, "trait.playfulness", +3f, 5);
                ApplyTag(npc, "trait.tension", -1.5f, 4);
                ApplyTag(npc, "trait.anger", -1f, 3);
            }

            // ---- PRIDE ----
            if (ContainsAny(c,
                "i'm right", "told you so", "better than", "i won", "nailed it",
                "impressive", "crush it", "boss"))
            {
                ApplyTag(npc, "trait.pride", +3f, 5);
            }
            if (ContainsAny(c, "you were right", "i was wrong", "you win"))
            {
                ApplyTag(npc, "trait.pride", -2f, 4);
                ApplyTag(npc, "trait.openness", +2f, 4);
            }

            // ---- GUARD / OPENNESS ----
            if (ContainsAny(c,
                "none of your business", "don't want to talk", "leave it", "drop it",
                "i'm fine", "whatever", "don't ask"))
            {
                ApplyTag(npc, "trait.guard", +4f, 6);
                ApplyTag(npc, "trait.openness", -3f, 5);
            }
            if (ContainsAny(c,
                "i'll tell you", "honestly", "can i tell you", "between us",
                "secret", "i trust you with", "open up"))
            {
                ApplyTag(npc, "trait.openness", +4f, 6);
                ApplyTag(npc, "trait.guard", -2f, 5);
                ApplyTag(npc, "trait.trust", +2f, 5);
            }

            // ---- LONELINESS ----
            if (ContainsAny(c,
                "alone", "lonely", "ignored", "left me", "didn't text", "where are you",
                "nobody", "by myself", "no one cares"))
            {
                ApplyTag(npc, "trait.loneliness", +4f, 6);
                ApplyTag(npc, "trait.hurt", +2f, 5);
                ApplyTag(npc, "trait.hope", -1f, 3);
            }

            // ---- HOPE ----
            if (ContainsAny(c,
                "we'll figure", "it will work", "together", "future", "someday",
                "i believe", "we can", "promise me"))
            {
                ApplyTag(npc, "trait.hope", +3f, 5);
                ApplyTag(npc, "trait.anxiety", -1f, 3);
            }

            // ---- PATIENCE ----
            if (ContainsAny(c, "hurry", "now", "already", "how long", "waiting forever"))
            {
                ApplyTag(npc, "trait.patience", -3f, 5);
                ApplyTag(npc, "trait.anger", +1f, 3);
            }
            if (ContainsAny(c, "take your time", "no rush", "whenever you're ready"))
            {
                ApplyTag(npc, "trait.patience", +2f, 4);
                ApplyTag(npc, "trait.tension", -1f, 3);
            }
        }

        // ============================================================
        // DAY DRIFT / RELATIONSHIP
        // ============================================================
        public static void ApplyDailyDrift(SimCharacter npc)
        {
            if (npc?.Traits == null) return;
            npc.Traits.DriftTowardSetPoints(FastIds, step: 1.5f);
        }

        public static void ApplyRelationshipInfluence(SimCharacter npc)
        {
            if (npc?.Traits == null || npc.Relationships == null) return;

            foreach (var rel in npc.Relationships)
            {
                int affection = GetRelInt(rel, "Affection");
                int trust = GetRelInt(rel, "Trust");
                int attraction = GetRelInt(rel, "Attraction");

                if (affection > 70) ApplyTag(npc, "trait.affection", +0.5f, 3);
                if (trust < 30)
                {
                    ApplyTag(npc, "trait.anxiety", +0.5f, 3);
                    ApplyTag(npc, "trait.guard", +0.5f, 3);
                }
                if (attraction > 70) ApplyTag(npc, "trait.attraction", +0.4f, 3);
            }
        }

        public static void UpdateTraitsDay(SimCharacter npc)
        {
            if (npc == null) return;
            ApplyDailyDrift(npc);
            ApplyRelationshipInfluence(npc);
        }

        public static void IncreaseTrait(SimCharacter npc, string traitId, float amount)
            => ApplyTag(npc, traitId, amount, 5);

        public static void DecreaseTrait(SimCharacter npc, string traitId, float amount)
            => ApplyTag(npc, traitId, -amount, 5);

        private static bool ContainsAny(string text, params string[] values)
        {
            foreach (var v in values)
                if (text.Contains(v, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static int GetRelInt(object rel, string prop)
        {
            try
            {
                var p = rel.GetType().GetProperty(prop);
                if (p == null) return 50;
                var val = p.GetValue(rel);
                if (val is int i) return i;
                if (val is float f) return (int)f;
                if (val is double d) return (int)d;
            }
            catch { }
            return 50;
        }
    }
}