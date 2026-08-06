using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using System;
using System.Linq;

/// <summary>
/// NPC decision scoring.
/// Higher score = more likely to choose the action.
/// Uses TraitRegistry ids + EmotionalProfile + relationships + money pressure.
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
        score += EvaluateTraits(action);
        score += EvaluateEmotion(action);
        score += EvaluateMemory(action);
        score += EvaluateRelationships(target);
        score += EvaluateImpulsiveness(action);
        score += EvaluateAnxiety(action);
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
    // TRAITS (TraitRegistry ids only)
    // ============================================================
    private int EvaluateTraits(string action)
    {
        int score = 0;

        // Social / connection
        if (ContainsAny(action, "spend time", "talk", "visit", "text", "call", "hang out"))
        {
            score += Trait("trait.empathy") / 3;
            score += Trait("trait.extroversion") / 3;
            score += Trait("trait.sensitivity") / 4;
            score += Trait("trait.aftercareNeed") / 5;
        }

        // Work / responsibility
        if (ContainsAny(action, "work", "job", "shift", "open shop", "close shop"))
        {
            score += Trait("trait.focus") / 2;
            score += Trait("trait.confidence") / 4;
            score += Trait("trait.anxiety") / 5;
        }

        // Romance / affection
        if (ContainsAny(action, "kiss", "cuddle", "date", "love", "hold"))
        {
            score += Trait("trait.empathy") / 3;
            score += Trait("trait.praiseKink") / 5;
            score += Trait("trait.aftercareNeed") / 4;
            score += Trait("trait.possessiveDesire") / 5;
        }

        // Sexual / risky
        if (ContainsAny(action, "fuck", "sex", "sneak", "secret", "cheat", "hook up"))
        {
            score += Trait("trait.sexualConfidence") / 2;
            score += Trait("trait.sexualCuriosity") / 3;
            score += Trait("trait.secrecyKink") / 3;
            score += Trait("trait.doubleLifeComfort") / 4;
            score += Trait("trait.publicRisk") / 5;
            score += Trait("trait.roughnessPreference") / 5;
            score -= Trait("trait.sexualShame") / 4;
        }

        // Conflict
        if (ContainsAny(action, "argue", "fight", "confront", "yell"))
        {
            score += Trait("trait.anger") / 2;
            score += Trait("trait.dominance") / 4;
            score += Trait("trait.impulsiveness") / 4;
            score -= Trait("trait.stoicism") / 4;
        }

        // Avoidance
        if (ContainsAny(action, "avoid", "leave", "ignore", "distance"))
        {
            score += Trait("trait.anxiety") / 3;
            score += Trait("trait.introversion") / 4;
            score += Trait("trait.insecurity") / 4;
            score -= Trait("trait.extroversion") / 5;
        }

        return score;
    }

    // ============================================================
    // EMOTION
    // ============================================================
    private int EvaluateEmotion(string action)
    {
        int score = 0;

        float affection = Character.Brain?.Affection ?? 0.5f;
        float stress = Character.Brain?.Stress ?? 0.2f;
        float mood = Character.Brain?.Mood ?? 0.5f;
        var state = Character.Emotion?.State ?? EmotionState.Neutral;

        if (affection > 0.6f &&
            ContainsAny(action, "spend time", "talk", "text", "hold"))
        {
            score += (int)(affection * 50);
        }

        if (stress > 0.5f)
        {
            if (ContainsAny(action, "party", "crowd"))
                score -= (int)(stress * 30);

            if (ContainsAny(action, "work", "alone", "rest"))
                score += (int)(stress * 20);
        }

        score += state switch
        {
            EmotionState.Horny when ContainsAny(action, "sex", "fuck", "kiss", "come over") => 25,
            EmotionState.Tempted when ContainsAny(action, "secret", "sneak", "cheat", "risk") => 22,
            EmotionState.Reckless when ContainsAny(action, "sneak", "fuck", "confront", "leave") => 18,
            EmotionState.Affectionate when ContainsAny(action, "hold", "talk", "cuddle", "text") => 18,
            EmotionState.InLove when ContainsAny(action, "stay", "love", "hold", "talk") => 20,
            EmotionState.Lonely when ContainsAny(action, "text", "visit", "spend time", "call") => 16,
            EmotionState.Angry when ContainsAny(action, "confront", "argue", "fight") => 20,
            EmotionState.Spiteful when ContainsAny(action, "ignore", "secret", "hurt") => 15,
            EmotionState.Anxious when ContainsAny(action, "party", "crowd", "confront") => -15,
            EmotionState.Ashamed when ContainsAny(action, "sex", "secret", "confess") => -8,
            EmotionState.Tired when ContainsAny(action, "party", "work", "sex") => -10,
            EmotionState.Restless when ContainsAny(action, "go out", "sneak", "text", "fuck") => 12,
            EmotionState.Predatory when ContainsAny(action, "sex", "fuck", "come over") => 20,
            _ => 0
        };

        if (mood > 0.65f && Contains(action, "sex")) score += 8;
        if (mood < 0.3f && Contains(action, "sex")) score -= 8;

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
    // IMPULSIVENESS
    // ============================================================
    private int EvaluateImpulsiveness(string action)
    {
        int impulsiveness = Trait("trait.impulsiveness");

        if (ContainsAny(action, "spend time", "talk", "visit", "sex", "fuck", "sneak", "text", "confront"))
            return impulsiveness / 2;

        return 0;
    }

    // ============================================================
    // ANXIETY
    // ============================================================
    private int EvaluateAnxiety(string action)
    {
        int anxiety = Trait("trait.anxiety");
        int score = 0;

        if (ContainsAny(action, "work", "prepare", "avoid", "plan", "rest"))
            score += anxiety / 2;

        if (ContainsAny(action, "spend time", "talk", "visit", "sex", "party", "confront"))
            score -= anxiety / 3;

        return score;
    }

    // ============================================================
    // CORE NEEDS
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
    // MONEY PRESSURE
    // Broke/tight => push work, avoid spendy risk
    // Stable/comfortable => fund impulse, secrecy logistics, desire
    // Desire can still spike when broke (free trouble stays tempting)
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

        // Work / practical survival
        if (ContainsAny(action, "work", "job", "shift", "prepare", "plan"))
            score += Math.Max(0, stressBias);

        // Avoidance when money hurts
        if (ContainsAny(action, "avoid", "rest", "alone") && stressBias > 0)
            score += stressBias / 2;

        // Spendy / public social
        if (ContainsAny(action, "bar", "party", "buy", "travel", "date"))
        {
            score += fundingBias;
            score -= Math.Max(0, stressBias);
        }

        // Desire / secrecy
        if (ContainsAny(action, "sex", "fuck", "sneak", "secret", "come over", "cheat"))
        {
            score += fundingBias;

            // broke still tempts — want doesn't vanish
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
        try { return (int)Character.GetTraitValue(id); }
        catch { return 50; }
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