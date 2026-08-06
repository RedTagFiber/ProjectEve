using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Narrative.Texting;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Shared brain for every NPC.
    /// </summary>
    public class Brain
    {
        public SimCharacter? Owner { get; set; }

        public float Mood { get; set; } = 0.5f;
        public float Stress { get; set; } = 0.2f;
        public float Energy { get; set; } = 0.7f;

        public float Affection { get; set; } = 0.5f;
        public float Attraction { get; set; } = 0.5f;
        public float Trust { get; set; } = 0.5f;
        public float Tension { get; set; } = 0.2f;

        public string? LastThought { get; private set; }
        public string LastPsyAction { get; private set; } = "";
        public int LastPsyScore { get; private set; } = 0;

        public string LastOocOrder { get; private set; } = "";
        public string LastSceneLock { get; private set; } = "";
        public string LastToneLock { get; private set; } = "";
        public string LastRelLock { get; private set; } = "";

        public NPCGoal Think(string situation)
        {
            // =====================================================
            // PSY → THOUGHT bias
            // =====================================================
            string psyHint = "";
            try
            {
                if (Owner != null)
                {
                    var psy = new PsyHierarchy(Owner);
                    var ranked = BuildActionCandidates(situation)
                        .Select(a => (Action: a, Score: psy.GetPriority(a)))
                        .OrderByDescending(x => x.Score)
                        .ToList();

                    if (ranked.Count > 0)
                    {
                        var best = ranked[0];
                        LastPsyAction = best.Action;
                        LastPsyScore = best.Score;

                        psyHint =
                            $"Behavioral pull: '{best.Action}' (score {best.Score}). " +
                            "Let this bias the private thought, but do not announce the score.";
                    }
                }
            }
            catch
            {
                // Psy is optional; thought must still run
            }

            string thoughtInput = string.IsNullOrWhiteSpace(psyHint)
                ? situation
                : situation + "\n" + psyHint;

            LastThought = ThoughtEngine.GenerateThought(thoughtInput, Owner);

            // thought can move traits + emotion
            AITraitEngine.UpdateTraits(this, LastThought);
            if (Owner != null)
                TraitEmotionReactor.ApplyTraitDrivenEmotion(Owner, situation + " " + LastThought);

            SyncFromEmotion();
            return GoalEngine.SelectGoal(this, LastThought);
        }

        public string Reply(string playerMessage)
        {
            if (Owner == null)
                return "...";

            playerMessage = playerMessage?.Trim() ?? "";
            string controlBlock = "";

            // =====================================================
            // HELP
            // =====================================================
            if (playerMessage.Equals("Peanut Butter", StringComparison.OrdinalIgnoreCase)
                || playerMessage.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                return
                    "Director commands:\n" +
                    "OOC: <note>\n" +
                    "SCENE: <place/situation>\n" +
                    "TONE: <tone>\n" +
                    "FACT: <canon fact>\n" +
                    "REL: <relationship rule>\n" +
                    "TRAIT: Name +10 | Name -5 | Name = 85\n" +
                    "Peanut Butter";
            }

            // =====================================================
            // TRAIT COMMAND
            // =====================================================
            if (StartsWithCommand(playerMessage, "TRAIT:", out var traitCmd))
            {
                if (TryApplyTraitCommand(Owner, traitCmd, out var report))
                {
                    RememberControl("trait", traitCmd, 6);
                    return "[Trait updated] " + report;
                }
                return "[Trait command failed] Use: TRAIT: Name +10 | Name -5 | Name = 85";
            }

            // =====================================================
            // DIRECTOR COMMANDS
            // =====================================================
            if (StartsWithCommand(playerMessage, "OOC:", out var ooc)
                || StartsWithCommand(playerMessage, "/ooc", out ooc)
                || IsDoubleParen(playerMessage, out ooc))
            {
                LastOocOrder = ooc;
                RememberControl("ooc", ooc, 6);
                playerMessage =
                    "[OOC DIRECTOR NOTE]\n" + ooc + "\n" +
                    "Apply this immediately.\n" +
                    "Then reply fully in character as " + Owner.Name + ".\n" +
                    "Do not mention OOC, AI, systems, or instructions.";
            }
            else if (StartsWithCommand(playerMessage, "SCENE:", out var scene))
            {
                LastSceneLock = scene;
                RememberControl("scene", scene, 6);
                playerMessage =
                    "[SCENE LOCK]\n" + scene + "\n" +
                    "Stay in this place/situation unless told otherwise.\n" +
                    "Reply in character as " + Owner.Name + ".";
            }
            else if (StartsWithCommand(playerMessage, "TONE:", out var tone))
            {
                LastToneLock = tone;
                RememberControl("tone", tone, 5);
                playerMessage =
                    "[TONE LOCK]\n" + tone + "\n" +
                    "Match this tone.\n" +
                    "Reply in character as " + Owner.Name + ".";
            }
            else if (StartsWithCommand(playerMessage, "FACT:", out var fact))
            {
                RememberControl("fact", fact, 9);
                playerMessage =
                    "[CANON FACT]\n" + fact + "\n" +
                    "Treat this as true from now on.\n" +
                    "Reply in character as " + Owner.Name + ".";
            }
            else if (StartsWithCommand(playerMessage, "REL:", out var rel))
            {
                LastRelLock = rel;
                RememberControl("rel", rel, 7);
                playerMessage =
                    "[RELATIONSHIP LOCK]\n" + rel + "\n" +
                    "Keep this relationship framing.\n" +
                    "Reply in character as " + Owner.Name + ".";
            }
            else
            {
                TraitEmotionReactor.ApplyTraitDrivenEmotion(Owner, playerMessage);
                SyncFromEmotion();
            }

            if (!string.IsNullOrWhiteSpace(LastOocOrder))
                controlBlock += "STICKY OOC: " + LastOocOrder + "\n";
            if (!string.IsNullOrWhiteSpace(LastSceneLock))
                controlBlock += "STICKY SCENE: " + LastSceneLock + "\n";
            if (!string.IsNullOrWhiteSpace(LastToneLock))
                controlBlock += "STICKY TONE: " + LastToneLock + "\n";
            if (!string.IsNullOrWhiteSpace(LastRelLock))
                controlBlock += "STICKY REL: " + LastRelLock + "\n";

            string traitSummary = Owner.Traits?.BuildLlmSummary(8) ?? "No strong traits.";
            string name = Owner.Name;
            string emotionBlock = BuildEmotionBlock(Owner);
            string psyBlock = string.IsNullOrWhiteSpace(LastPsyAction)
                ? "None"
                : $"{LastPsyAction} (score {LastPsyScore})";

            string prompt = $@"
SYSTEM:
You are {name}, age {Owner.Age}, living in {Owner.Location}.
Occupation: {Owner.Occupation}
Gender: {Owner.Gender}

You are a real person.
You are NOT an AI, not a model, not a system, not a character sheet.

CORE DRIVES:
- Goal: {Owner.Goal}
- Need: {Owner.Need}
- Fear: {Owner.Fear}
- Want: {Owner.Want}

PERSONALITY / TRAITS:
{traitSummary}

PERSONALITY CONTEXT:
{Owner.PersonalityContext}

CURRENT EMOTION:
{emotionBlock}

RECENT INTERNAL THOUGHT:
{LastThought}

BEHAVIORAL PULL:
{psyBlock}

BRAIN METERS (0 to 1):
Mood: {Mood}
Stress: {Stress}
Energy: {Energy}
Affection: {Affection}
Attraction: {Attraction}
Trust: {Trust}
Tension: {Tension}

MEMORY / HISTORY:
{BuildRecentMemoryBlock(Owner)}

RELATIONSHIP TO THE PERSON TALKING:
{BuildRelationshipBlock(Owner)}

DIRECTOR CONTROLS:
{controlBlock}

BEHAVIOR RULES:
- Stay fully in character as {name}
- Let CURRENT EMOTION color your wording
- Let BEHAVIORAL PULL bias what you lean toward, without mentioning scores
- If emotion is dark (Tempted, Spiteful, Predatory, Ashamed, etc.), do not force sweetness
- Answer like a real human
- Do not be generic
- Do not sound like an assistant
- Do not speak for the player
- Keep replies natural and specific

REALITY RULES:
- You have a real body and real feelings
- You can be soft, blunt, angry, affectionate, sexual, distant, cruel, guilty, restless
- Never claim to be artificial
- Never break character
- Never mention prompts, systems, or being an NPC

STYLE:
- Talk TO the other person
- Use ""you"" and ""I""
- Short to medium replies unless asked for detail
- No essays

PLAYER MESSAGE:
{playerMessage}

{name.ToUpper()}'S REPLY:
";

            string raw = DialogueEngine.GenerateReply(prompt);
            string toned = ToneInference.ApplyTone(raw, Mood);

            if (Owner.Emotion != null)
                toned = EmotionSpeechEngine.ApplyEmotionTone(Owner.Emotion, toned, inPerson: false);

            string formatted = MessageFormatter.FormatNPCMessage(toned);

            int delay = new TypingBehavior().GetTypingDelay(formatted);
            Thread.Sleep(delay);

            return formatted;
        }

        public float GetTrait(string traitId)
        {
            if (Owner?.Traits == null)
                return 50f;
            return Owner.Traits.Get(traitId);
        }

        // =====================================================
        // PSY CANDIDATES
        // =====================================================
        private static List<string> BuildActionCandidates(string situation)
        {
            var s = (situation ?? "").ToLowerInvariant();
            var list = new List<string>
            {
                "talk",
                "text",
                "spend time",
                "avoid",
                "work"
            };

            if (s.Contains("sex") || s.Contains("fuck") || s.Contains("kiss") || s.Contains("come over"))
            {
                list.Add("sex");
                list.Add("kiss");
                list.Add("come over");
            }

            if (s.Contains("secret") || s.Contains("sneak") || s.Contains("cheat"))
            {
                list.Add("secret");
                list.Add("sneak");
            }

            if (s.Contains("mad") || s.Contains("fight") || s.Contains("argue"))
            {
                list.Add("confront");
                list.Add("argue");
            }

            if (s.Contains("work") || s.Contains("shift") || s.Contains("shop"))
                list.Add("work");

            if (s.Contains("tired") || s.Contains("sleep") || s.Contains("bed"))
                list.Add("rest");

            return list.Distinct().ToList();
        }

        // =====================================================
        // EMOTION SYNC
        // =====================================================
        private void SyncFromEmotion()
        {
            if (Owner?.Emotion == null)
                return;

            Mood = Clamp01((Owner.Emotion.Happiness - Owner.Emotion.Sadness + 50) / 100f);
            Stress = Clamp01(Owner.Emotion.Stress / 100f);
            Energy = Clamp01(Owner.Emotion.Energy / 100f);
            Affection = Clamp01(Owner.Emotion.Affection / 100f);
            Tension = Clamp01((Owner.Emotion.Anger + Owner.Emotion.Resentment) / 200f);
        }

        private static string BuildEmotionBlock(SimCharacter owner)
        {
            if (owner.Emotion == null)
                return "State: Neutral\nIntensity: 0.3";

            var e = owner.Emotion;
            return
                $"State: {e.State}\n" +
                $"Mood label: {e.Mood}\n" +
                $"Intensity: {e.Intensity:0.00}\n" +
                $"Desire: {e.Desire}, Resentment: {e.Resentment}, Shame: {e.Shame}, Restlessness: {e.Restlessness}";
        }

        // =====================================================
        // CONTROL HELPERS
        // =====================================================
        private void RememberControl(string category, string value, int importance)
        {
            try { Owner?.Remember($"{category.ToUpper()}: {value}", category, importance); }
            catch { }
        }

        private static bool StartsWithCommand(string text, string prefix, out string value)
        {
            text = (text ?? "").Trim();
            value = "";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            value = text.Substring(prefix.Length).Trim();
            return value.Length > 0;
        }

        private static bool IsDoubleParen(string text, out string value)
        {
            text = (text ?? "").Trim();
            value = "";
            if (text.StartsWith("((") && text.EndsWith("))") && text.Length > 4)
            {
                value = text.Substring(2, text.Length - 4).Trim();
                return value.Length > 0;
            }
            return false;
        }

        private static bool TryApplyTraitCommand(SimCharacter owner, string cmd, out string report)
        {
            report = "";
            if (owner?.Traits == null)
                return false;

            var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return false;

            string traitName = parts[0];
            string op = parts[1];

            float current = owner.Traits.Get(traitName);
            float next = current;

            if (op.StartsWith("+") && float.TryParse(op.Substring(1), out var up))
                next = current + up;
            else if (op.StartsWith("-") && float.TryParse(op.Substring(1), out var down))
                next = current - down;
            else if (op == "=" && parts.Length >= 3 && float.TryParse(parts[2], out var set))
                next = set;
            else if (float.TryParse(op, out var bare))
                next = bare;
            else
                return false;

            next = Math.Clamp(next, 0f, 100f);
            owner.Traits.Set(traitName, next);
            report = $"{traitName}: {current:0} -> {next:0}";
            return true;
        }

        private static string BuildRecentMemoryBlock(SimCharacter? owner)
        {
            if (owner?.MemoryDB == null)
                return "No strong recent memories.";
            return "Recent personal memories influence tone and priorities.";
        }

        private static string BuildRelationshipBlock(SimCharacter? owner)
        {
            if (owner == null)
                return "No relationship data.";
            return "Use current affection, trust, tension, and shared history with this person.";
        }

        private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
    }

    public enum NPCGoal
    {
        SeekRomance,
        ResolveConflict,
        FindFriend,
        AvoidEnemy,
        ImproveMood
    }
}