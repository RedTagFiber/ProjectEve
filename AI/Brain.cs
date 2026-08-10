using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Characters.Traits.Core;
using ProjectEve.Narrative.Texting;
using ProjectEve.Traits;
using ProjectEve.Traits.Matrix;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Shared brain for every NPC.
    /// Flow: Think (Thought + TAGS → TraitEngine once) → Reply (spoken words only).
    /// </summary>
    public class Brain
    {
        public SimCharacter? Owner { get; set; }

        // Soft meters 0–1 mirrored from Fast (not a second emotion system)
        public float Mood { get; set; } = 0.5f;
        public float Stress { get; set; } = 0.2f;
        public float Energy { get; set; } = 0.7f;
        public float Affection { get; set; } = 0.5f;
        public float Attraction { get; set; } = 0.5f;
        public float Trust { get; set; } = 0.5f;
        public float Tension { get; set; } = 0.2f;

        public string? LastThought { get; private set; }
        public string LastPsyAction { get; private set; } = "";
        public int LastPsyScore { get; private set; }
        public string LastOocOrder { get; private set; } = "";
        public string LastSceneLock { get; private set; } = "";
        public string LastToneLock { get; private set; } = "";
        public string LastRelLock { get; private set; } = "";

        // =====================================================
        // THINK — Thought first, then ONE trait pass from TAGS
        // =====================================================
        public NPCGoal Think(string situation)
        {
            situation ??= "";

            string psyHint = BuildPsyHint(situation);
            string aboutBlock = BuildRelationshipBlock(Owner);

            string thoughtInput = situation;
            if (!string.IsNullOrWhiteSpace(psyHint))
                thoughtInput += "\n" + psyHint;
            if (!string.IsNullOrWhiteSpace(aboutBlock))
                thoughtInput += "\n" + aboutBlock;

            LastThought = ThoughtEngine.GenerateThought(thoughtInput, Owner);

            if (Owner != null)
            {
                try
                {
                    // Prefer TAGS in LastThought; keyword fallback inside TraitEngine
                    TraitEngine.UpdateTraitsAfterChat(Owner, situation, LastThought);
                }
                catch
                {
                    try { TraitEngine.UpdateTraitsAfterChat(Owner, situation); }
                    catch { }
                }

                // Do not call AITraitEngine here — avoids double movement
                SyncMetersFromFast();
            }

            return GoalEngine.SelectGoal(this, LastThought);
        }

        // =====================================================
        // REPLY — spoken words only (traits already moved in Think)
        // =====================================================
        public string Reply(string playerMessage)
        {
            if (Owner == null)
                return "...";

            playerMessage = playerMessage?.Trim() ?? "";

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
                    "TRAIT: trait.anger +10 | trait.trust -5 | trait.desire = 85\n" +
                    "Peanut Butter";
            }

            if (StartsWithCommand(playerMessage, "TRAIT:", out var traitCmd))
            {
                if (TryApplyTraitCommand(Owner, traitCmd, out var report))
                {
                    RememberControl("trait", traitCmd, 6);
                    SyncMetersFromFast();
                    return "[Trait updated] " + report;
                }
                return "[Trait command failed] Use: TRAIT: trait.anger +10 | trait.trust -5 | trait.desire = 85";
            }

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
            else if (string.IsNullOrWhiteSpace(LastThought))
            {
                // Reply without Think (shouldn't happen) — apply once
                try { TraitEngine.UpdateTraitsAfterChat(Owner, playerMessage); }
                catch { }
                SyncMetersFromFast();
            }

            string controlBlock = "";
            if (!string.IsNullOrWhiteSpace(LastOocOrder))
                controlBlock += "STICKY OOC: " + LastOocOrder + "\n";
            if (!string.IsNullOrWhiteSpace(LastSceneLock))
                controlBlock += "STICKY SCENE: " + LastSceneLock + "\n";
            if (!string.IsNullOrWhiteSpace(LastToneLock))
                controlBlock += "STICKY TONE: " + LastToneLock + "\n";
            if (!string.IsNullOrWhiteSpace(LastRelLock))
                controlBlock += "STICKY REL: " + LastRelLock + "\n";

            string traitSummary = Owner.Traits?.BuildLlmSummary(10) ?? "No strong traits.";
            string name = Owner.Name;
            string emotionBlock = BuildFastEmotionBlock(Owner);
            string psyBlock = string.IsNullOrWhiteSpace(LastPsyAction)
                ? "None"
                : $"{LastPsyAction} (score {LastPsyScore})";
            string aboutBlock = BuildRelationshipBlock(Owner);

            // Strip TAGS from thought before showing Dialogue (speech only)
            string thoughtForDialogue = StripTagsLine(LastThought);

            string prompt = $@"
SYSTEM:
You are {name}, age {Owner.Age}, living in {Owner.Location}.
Occupation: {Owner.Occupation}
Gender: {Owner.Gender}
You are a real person — not an AI, model, or system.

CORE DRIVES:
- Goal: {Owner.Goal}
- Need: {Owner.Need}
- Fear: {Owner.Fear}
- Want: {Owner.Want}

PERSONALITY / TRAITS (0-100):
{traitSummary}

PERSONALITY CONTEXT:
{Owner.PersonalityContext}

CURRENT EMOTIONAL READ (Fast traits):
{emotionBlock}

RELATIONSHIP:
{aboutBlock}

RECENT INTERNAL THOUGHT (private — do not quote as speech):
{thoughtForDialogue}

BEHAVIORAL PULL:
{psyBlock}

BRAIN METERS (0-1, from Fast):
Mood: {Mood:0.00} Stress: {Stress:0.00} Energy: {Energy:0.00}
Affection: {Affection:0.00} Attraction: {Attraction:0.00}
Trust: {Trust:0.00} Tension: {Tension:0.00}

MEMORY:
{BuildRecentMemoryBlock(Owner)}

DIRECTOR CONTROLS:
{controlBlock}

RULES:
- Stay fully in character as {name}
- Let emotional read and relationship band color wording
- Output SPOKEN WORDS ONLY — no *actions*, no narration, no stage directions, no TAGS
- Do not speak for the player
- Never claim to be artificial or mention prompts/systems
- Short to medium unless the moment needs more

PLAYER MESSAGE:
{playerMessage}

{name.ToUpper()}'S REPLY (spoken words only):
";

            string raw = DialogueEngine.GenerateReply(prompt);
            string toned = ToneInference.ApplyTone(raw, Mood);

            try
            {
                toned = EmotionSpeechEngine.ApplyEmotionTone(Owner, toned, inPerson: false);
            }
            catch
            {
                try
                {
                    if (Owner.Emotion != null)
                        toned = EmotionSpeechEngine.ApplyEmotionTone(Owner.Emotion, toned, inPerson: false);
                }
                catch { }
            }

            string formatted = MessageFormatter.FormatNPCMessage(toned);
            try
            {
                int delay = new TypingBehavior().GetTypingDelay(formatted);
                Thread.Sleep(Math.Clamp(delay, 0, 2500));
            }
            catch { }

            return formatted;
        }

        public float GetTrait(string traitId)
        {
            if (Owner?.Traits == null) return 50f;
            return Owner.Traits.Get(traitId);
        }

        // =====================================================
        // FAST → METERS
        // =====================================================
        private void SyncMetersFromFast()
        {
            if (Owner?.Traits == null) return;

            float T(string id) => Owner.Traits.Get(id);

            float up = (T("trait.hope") + T("trait.affection") + T("trait.playfulness")) / 3f;
            float down = (T("trait.hurt") + T("trait.loneliness") + T("trait.shame")) / 3f;

            Mood = Clamp01((up - down + 50f) / 100f);
            Stress = Clamp01((T("trait.anxiety") + T("trait.fear") + T("trait.tension")) / 300f);
            Affection = Clamp01(T("trait.affection") / 100f);
            Attraction = Clamp01(T("trait.attraction") / 100f);
            Trust = Clamp01(T("trait.trust") / 100f);
            Tension = Clamp01((T("trait.anger") + T("trait.tension") + T("trait.resentment")) / 300f);
        }

        private static string BuildFastEmotionBlock(SimCharacter owner)
        {
            if (owner.Traits == null) return "No trait bag.";

            float T(string id) => owner.Traits.Get(id);

            var pairs = new (string Label, float V)[]
            {
                ("Anger", T("trait.anger")),
                ("Anxiety", T("trait.anxiety")),
                ("Fear", T("trait.fear")),
                ("Shame", T("trait.shame")),
                ("Guilt", T("trait.guilt")),
                ("Hurt", T("trait.hurt")),
                ("Jealousy", T("trait.jealousy")),
                ("Resentment", T("trait.resentment")),
                ("Desire", T("trait.desire")),
                ("Affection", T("trait.affection")),
                ("Guard", T("trait.guard")),
                ("Loneliness", T("trait.loneliness")),
                ("Hope", T("trait.hope")),
                ("Playfulness", T("trait.playfulness")),
                ("Tension", T("trait.tension")),
            };

            var top = pairs.OrderByDescending(p => p.V).First();
            string band = top.V >= 85 ? "extreme" :
                          top.V >= 70 ? "high" :
                          top.V >= 50 ? "mid" :
                          top.V >= 30 ? "low" : "off";

            return
                $"Dominant: {top.Label} ({top.V:0}, {band})\n" +
                $"Anger {T("trait.anger"):0} | Anxiety {T("trait.anxiety"):0} | Trust {T("trait.trust"):0} | " +
                $"Affection {T("trait.affection"):0} | Desire {T("trait.desire"):0} | Guard {T("trait.guard"):0}";
        }

        // =====================================================
        // RELATIONSHIP / ABOUT (interim until edge DB)
        // =====================================================
        private static string BuildRelationshipBlock(SimCharacter? owner)
        {
            if (owner?.Traits == null)
                return "ABOUT the person talking: unknown.";

            try
            {
                float trust = owner.Traits.Get("trait.trust");
                float aff = owner.Traits.Get("trait.affection");
                float des = owner.Traits.Get("trait.desire");
                float ang = owner.Traits.Get("trait.anger");
                float like = Math.Clamp(0.45f * trust + 0.45f * aff + 0.1f * des - 0.15f * ang, 0f, 100f);

                string bandName = "neutral";
                if (RelationshipMatrixLoader.Loaded)
                    bandName = RelationshipMatrixLoader.GetBand(like).Name;
                else if (like <= 20) bandName = "hostile";
                else if (like <= 40) bandName = "cold";
                else if (like <= 60) bandName = "neutral";
                else if (like <= 80) bandName = "friend";
                else bandName = "close";

                return
                    $"ABOUT the person talking (Ryan):\n" +
                    $"- LikeScore ~{like:0} ({bandName}) [interim from Fast until relationship edge DB]\n" +
                    $"- Trust {trust:0} Affection {aff:0} Desire {des:0} Anger {ang:0}\n" +
                    $"- Even hello / small talk should respect this band.";
            }
            catch
            {
                return "ABOUT the person talking: use trust/affection from traits.";
            }
        }

        // =====================================================
        // PSY
        // =====================================================
        private string BuildPsyHint(string situation)
        {
            try
            {
                if (Owner == null) return "";

                var psy = new PsyHierarchy(Owner);
                var ranked = BuildActionCandidates(situation)
                    .Select(a => (Action: a, Score: psy.GetPriority(a)))
                    .OrderByDescending(x => x.Score)
                    .ToList();

                if (ranked.Count == 0) return "";

                var best = ranked[0];
                LastPsyAction = best.Action;
                LastPsyScore = best.Score;
                return
                    $"Behavioral pull: '{best.Action}' (score {best.Score}). " +
                    "Bias private thought; do not announce the score.";
            }
            catch
            {
                return "";
            }
        }

        private static List<string> BuildActionCandidates(string situation)
        {
            var s = (situation ?? "").ToLowerInvariant();
            var list = new List<string> { "talk", "text", "spend time", "avoid", "work" };

            if (ContainsAny(s, "sex", "fuck", "kiss", "come over"))
            {
                list.Add("sex");
                list.Add("kiss");
                list.Add("come over");
            }
            if (ContainsAny(s, "secret", "sneak", "cheat"))
            {
                list.Add("secret");
                list.Add("sneak");
            }
            if (ContainsAny(s, "mad", "fight", "argue", "hate"))
            {
                list.Add("confront");
                list.Add("argue");
            }
            if (ContainsAny(s, "work", "shift", "shop"))
                list.Add("work");
            if (ContainsAny(s, "tired", "sleep", "bed"))
                list.Add("rest");
            if (ContainsAny(s, "sorry", "forgive"))
                list.Add("apologize");

            return list.Distinct().ToList();
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private void RememberControl(string category, string value, int importance)
        {
            try { Owner?.Remember($"{category.ToUpper()}: {value}", category, importance); }
            catch { }
        }

        private static string StripTagsLine(string? thought)
        {
            if (string.IsNullOrWhiteSpace(thought)) return "(none)";
            var lines = thought.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = lines.Where(l => !l.TrimStart().StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase));
            string s = string.Join(" ", kept).Trim();
            return string.IsNullOrWhiteSpace(s) ? "(none)" : s;
        }

        private static bool StartsWithCommand(string text, string prefix, out string value)
        {
            text = (text ?? "").Trim();
            value = "";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            value = text[prefix.Length..].Trim();
            return value.Length > 0;
        }

        private static bool IsDoubleParen(string text, out string value)
        {
            text = (text ?? "").Trim();
            value = "";
            if (text.StartsWith("((") && text.EndsWith("))") && text.Length > 4)
            {
                value = text[2..^2].Trim();
                return value.Length > 0;
            }
            return false;
        }

        private static bool TryApplyTraitCommand(SimCharacter owner, string cmd, out string report)
        {
            report = "";
            if (owner?.Traits == null) return false;

            var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            string traitId = parts[0];
            string op = parts[1];
            float current = owner.Traits.Get(traitId);
            float next = current;

            if (op.StartsWith("+") && float.TryParse(op[1..], out var up))
                next = current + up;
            else if (op.StartsWith("-") && float.TryParse(op[1..], out var down))
                next = current - down;
            else if (op == "=" && parts.Length >= 3 && float.TryParse(parts[2], out var set))
                next = set;
            else if (float.TryParse(op, out var bare))
                next = bare;
            else
                return false;

            next = Math.Clamp(next, 0f, 100f);
            owner.Traits.Set(traitId, next);
            report = $"{traitId}: {current:0} -> {next:0}";
            return true;
        }

        private static string BuildRecentMemoryBlock(SimCharacter? owner)
        {
            if (owner?.MemoryDB == null)
                return "No strong recent memories.";
            return "Recent personal memories influence tone and priorities.";
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (var w in words)
                if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
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