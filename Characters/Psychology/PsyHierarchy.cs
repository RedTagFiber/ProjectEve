using ProjectEve.Characters.Base;
using System;
using System.Linq;

namespace ProjectEve.Characters.Traits.Core
{
    /// <summary>
    /// NPC decision scoring. Higher = more likely to choose the action.
    /// Uses Fast (in-moment) + Mid (character). Slow only for clear topic match.
    /// Does not use EmotionalProfile as a second brain.
    /// </summary>
    public class PsyHierarchy
    {
        public SimCharacter Character { get; }

        public PsyHierarchy(SimCharacter character)
        {
            Character = character;
        }

        public int GetPriority(string action)
        {
            if (string.IsNullOrWhiteSpace(action) || Character == null)
                return 0;

            string target = GetTargetFromAction(action);

            int score = 0;
            score += EvaluateFast(action);
            score += EvaluateMid(action);
            score += EvaluateSlowTopic(action);
            score += EvaluateMemory(action);
            score += EvaluateRelationships(target);
            score += EvaluateCoreNeeds(action);
            score += EvaluateMoney(action);

            return score;
        }

        // ============================================================
        // TARGET PARSER
        // ============================================================
        private string GetTargetFromAction(string action)
        {
            var words = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? "" : words[^1];
        }

        // ============================================================
        // FAST — in-moment state
        // ============================================================
        private int EvaluateFast(string action)
        {
            int score = 0;

            // Social / connection
            if (ContainsAny(action, "spend time", "talk", "visit", "text", "call", "hang out"))
            {
                score += Trait("trait.affection") / 4;
                score += Trait("trait.openness") / 4;
                score += Trait("trait.loneliness") / 5;
                score -= Trait("trait.guard") / 5;
                score -= Trait("trait.anxiety") / 6;
            }

            // Work / responsibility
            if (ContainsAny(action, "work", "job", "shift", "open shop", "close shop"))
            {
                score += Trait("trait.patience") / 5;
                score += Trait("trait.pride") / 6;
                score += Trait("trait.anxiety") / 8; // anxious people still show up sometimes
                score -= Trait("trait.desire") / 8;  // heat pulls away from shift
            }

            // Romance / affection
            if (ContainsAny(action, "kiss", "cuddle", "date", "love", "hold"))
            {
                score += Trait("trait.affection") / 3;
                score += Trait("trait.attraction") / 4;
                score += Trait("trait.desire") / 5;
                score += Trait("trait.trust") / 5;
                score -= Trait("trait.hurt") / 6;
                score -= Trait("trait.guard") / 6;
            }

            // Sexual / risky
            if (ContainsAny(action, "fuck", "sex", "sneak", "secret", "cheat", "hook up"))
            {
                score += Trait("trait.desire") / 2;
                score += Trait("trait.attraction") / 4;
                score += Trait("trait.tension") / 5;
                score -= Trait("trait.shame") / 4;
                score -= Trait("trait.fear") / 6;
                score -= Trait("trait.guilt") / 6;
            }

            // Conflict
            if (ContainsAny(action, "argue", "fight", "confront", "yell"))
            {
                score += Trait("trait.anger") / 2;
                score += Trait("trait.pride") / 5;
                score += Trait("trait.tension") / 5;
                score -= Trait("trait.patience") / 4;
                score -= Trait("trait.fear") / 6;
            }

            // Avoidance
            if (ContainsAny(action, "avoid", "leave", "ignore", "distance"))
            {
                score += Trait("trait.guard") / 3;
                score += Trait("trait.anxiety") / 4;
                score += Trait("trait.fear") / 5;
                score += Trait("trait.hurt") / 6;
                score -= Trait("trait.openness") / 5;
                score -= Trait("trait.hope") / 6;
            }

            // Repair / soft
            if (ContainsAny(action, "apologize", "sorry", "forgive", "make up"))
            {
                score += Trait("trait.guilt") / 3;
                score += Trait("trait.hope") / 4;
                score += Trait("trait.affection") / 5;
                score -= Trait("trait.pride") / 5;
                score -= Trait("trait.resentment") / 5;
            }

            // Jealousy actions
            if (ContainsAny(action, "check phone", "ask who", "accuse", "spy"))
            {
                score += Trait("trait.jealousy") / 2;
                score += Trait("trait.anxiety") / 5;
                score -= Trait("trait.trust") / 4;
            }

            return score;
        }

        // ============================================================
        // MID — stable character bias
        // ============================================================
        private int EvaluateMid(string action)
        {
            int score = 0;

            if (ContainsAny(action, "spend time", "talk", "visit", "text", "call"))
            {
                score += Trait("mid.open_book") / 5;
                score += Trait("mid.people_pleasing") / 6;
                score -= Trait("mid.avoidant") / 4;
                score -= Trait("mid.guarded") / 5;
                score += Trait("mid.anxious_attach") / 6;
            }

            if (ContainsAny(action, "work", "job", "shift"))
            {
                score += Trait("mid.dutiful") / 3;
                score += Trait("mid.ambitious") / 4;
                score += Trait("mid.perfectionist") / 6;
                score -= Trait("mid.restless") / 6;
            }

            if (ContainsAny(action, "argue", "fight", "confront", "yell"))
            {
                score += Trait("mid.confrontational") / 2;
                score += Trait("mid.blunt") / 4;
                score -= Trait("mid.conflict_avoidant") / 2;
                score -= Trait("mid.peacemaker") / 4;
                score += Trait("mid.passive_aggressive") / 6; // may engage sideways
            }

            if (ContainsAny(action, "avoid", "leave", "ignore"))
            {
                score += Trait("mid.conflict_avoidant") / 2;
                score += Trait("mid.avoidant") / 3;
                score += Trait("mid.guarded") / 4;
                score -= Trait("mid.confrontational") / 4;
            }

            if (ContainsAny(action, "kiss", "cuddle", "date", "love", "hold", "sex", "fuck"))
            {
                score += Trait("mid.loyal") / 5;
                score -= Trait("mid.avoidant") / 4;
                score += Trait("mid.anxious_attach") / 6;
            }

            if (ContainsAny(action, "apologize", "sorry", "forgive"))
            {
                score += Trait("mid.forgiving") / 3;
                score += Trait("mid.people_pleasing") / 5;
                score -= Trait("mid.grudge_holding") / 3;
                score -= Trait("mid.proud") / 5;
            }

            if (ContainsAny(action, "sneak", "secret", "cheat"))
            {
                score += Trait("mid.opportunistic") / 3;
                score -= Trait("mid.principled") / 3;
                score -= Trait("mid.loyal") / 4;
            }

            return score;
        }

        // ============================================================
        // SLOW — only when action text matches domain
        // ============================================================
        private int EvaluateSlowTopic(string action)
        {
            int score = 0;

            if (ContainsAny(action, "game", "watch the", "bengals", "browns", "buckeyes", "football", "tailgate"))
            {
                int fb = Trait("slow.sports.football");
                int cfb = Trait("slow.sports.college_football");
                if (fb >= 50) score += fb / 5;
                if (cfb >= 50) score += cfb / 5;
            }

            if (ContainsAny(action, "work", "career", "promotion", "shift"))
            {
                int wa = Trait("slow.life.work_ambition");
                if (wa >= 50) score += wa / 5;
            }

            if (ContainsAny(action, "gym", "run", "workout"))
            {
                int fit = Trait("slow.life.fitness");
                if (fit >= 50) score += fit / 5;
            }

            if (ContainsAny(action, "bar", "drink", "concert", "show"))
            {
                // light pull if restless character + any music parent high — keep simple
                score += Trait("mid.restless") / 8;
            }

            return score;
        }

        // ============================================================
        // MEMORY
        // ============================================================
        private int EvaluateMemory(string action)
        {
            int score = 0;

            try
            {
                if (Character.MemoryDB == null)
                    return 0;

                var memories = Character.MemoryDB.GetMemories(Character.Name);
                if (memories == null)
                    return 0;

                if (ContainsAny(action, "spend time", "talk", "text"))
                {
                    foreach (var mem in memories.Where(m =>
                                 m.Category is "positive" or "Emotional" or "Social"))
                        score += mem.Importance / 2;
                }

                if (ContainsAny(action, "work", "avoid"))
                {
                    foreach (var mem in memories.Where(m =>
                                 m.Category is "negative" or "Stress"))
                        score += mem.Importance / 3;
                }

                if (ContainsAny(action, "secret", "sneak"))
                {
                    foreach (var mem in memories.Where(m =>
                                 m.Category is "ooc" or "fact" or "secret"))
                        score += mem.Importance / 2;
                }
            }
            catch
            {
                return 0;
            }

            return score;
        }

        // ============================================================
        // RELATIONSHIPS
        // ============================================================
        private int EvaluateRelationships(string target)
        {
            if (Character.Relationships == null || string.IsNullOrWhiteSpace(target))
                return 0;

            var rel = Character.Relationships.FirstOrDefault(r =>
                r.TargetName != null &&
                r.TargetName.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (rel == null)
                return 0;

            return (rel.Affection / 2) + (rel.Attraction / 3) + (rel.Trust / 4);
        }

        // ============================================================
        // CORE NEEDS (goal / need / fear / want strings on character)
        // ============================================================
        private int EvaluateCoreNeeds(string action)
        {
            int score = 0;

            if (!string.IsNullOrWhiteSpace(Character.Goal) &&
                action.Contains(Character.Goal, StringComparison.OrdinalIgnoreCase))
                score += 40;

            if (!string.IsNullOrWhiteSpace(Character.Need) &&
                action.Contains(Character.Need, StringComparison.OrdinalIgnoreCase))
                score += 30;

            if (!string.IsNullOrWhiteSpace(Character.Fear) &&
                action.Contains(Character.Fear, StringComparison.OrdinalIgnoreCase))
                score -= 50;

            if (!string.IsNullOrWhiteSpace(Character.Want) &&
                action.Contains(Character.Want, StringComparison.OrdinalIgnoreCase))
                score += 20;

            return score;
        }

        // ============================================================
        // MONEY
        // ============================================================
        private int EvaluateMoney(string action)
        {
            if (Character.Money == null)
                return 0;

            int score = 0;
            int stressBias = 0;
            int fundingBias = 0;

            try
            {
                stressBias = Character.Money.StressBias();
                fundingBias = Character.Money.DesireFundingBias();
            }
            catch
            {
                return 0;
            }

            if (ContainsAny(action, "work", "job", "shift", "prepare", "plan"))
                score += Math.Max(0, stressBias);

            if (ContainsAny(action, "avoid", "rest", "alone") && stressBias > 0)
                score += stressBias / 2;

            if (ContainsAny(action, "bar", "party", "buy", "travel", "date"))
            {
                score += fundingBias;
                score -= Math.Max(0, stressBias);
            }

            if (ContainsAny(action, "sex", "fuck", "sneak", "secret", "come over", "cheat"))
            {
                score += fundingBias;
                if (stressBias > 0 && ContainsAny(action, "sex", "sneak", "secret"))
                    score += 4;
            }

            return score;
        }

        // ============================================================
        // HELPERS
        // ============================================================
        private int Trait(string id)
        {
            try
            {
                return (int)Character.GetTraitValue(id);
            }
            catch
            {
                try
                {
                    if (Character.Traits != null)
                        return (int)Character.Traits.Get(id);
                }
                catch { }

                return 50;
            }
        }

        private static bool Contains(string text, string value)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            foreach (var v in values)
                if (Contains(text, v))
                    return true;
            return false;
        }
    }
}