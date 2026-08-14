using ProjectEve.Characters.Base;
using ProjectEve.Characters.Cognition;
using ProjectEve.Characters.NPCs.Body;
using System;
using System.Linq;
using System.Text;

namespace ProjectEve.AI.Brain
{
    public static class DialoguePromptContext
    {
        public static string BuildCharacterContext(SimCharacter owner)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Name: {owner.Name}");
            sb.AppendLine($"Age: {owner.Age}");
            sb.AppendLine($"Gender: {owner.Gender}");
            sb.AppendLine($"Occupation: {owner.Occupation}");
            sb.AppendLine($"Location: {owner.Location}");
            sb.AppendLine($"Goal: {owner.Goal}");
            sb.AppendLine($"Need: {owner.Need}");
            sb.AppendLine($"Fear: {owner.Fear}");
            sb.AppendLine($"Want: {owner.Want}");

            if (!string.IsNullOrWhiteSpace(owner.PersonalityContext))
                sb.AppendLine("Personality context: " + OneLine(owner.PersonalityContext, 240));

            if (owner.Traits != null)
            {
                sb.AppendLine("Traits:");
                sb.AppendLine(owner.Traits.BuildLlmSummary(14));
            }

            if (owner.Cognition != null && owner.Cognition.IsGenerated)
            {
                sb.AppendLine("Cognition / education / speech:");
                sb.AppendLine(CognitionPromptContext.BuildForNpc(owner));
            }

            if (owner.Appearance != null)
            {
                sb.AppendLine("Established ordinary self-body facts:");
                sb.AppendLine(owner.Appearance.ToSelfKnowledgeFragment());

                string bodyNeed = BodyPromptContext.BuildCurrentNeed(owner.Appearance.Body);
                if (!string.IsNullOrWhiteSpace(bodyNeed))
                    sb.AppendLine(bodyNeed);
            }

            return sb.ToString();
        }

        public static string BuildHistoryMemoryContext(SimCharacter owner)
        {
            var sb = new StringBuilder();

            try
            {
                if (owner.History != null)
                {
                    var history = owner.History
                        .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Summary))
                        .OrderByDescending(h => h.Importance)
                        .Take(6)
                        .ToList();

                    if (history.Count > 0)
                    {
                        sb.AppendLine("Life history:");
                        foreach (var h in history)
                            sb.AppendLine($"- Age {h.Age}: {OneLine(h.Summary, 150)}");
                    }
                }
            }
            catch { }

            try
            {
                if (owner.MemoryDB != null)
                {
                    var memories = owner.MemoryDB.GetMemories(owner.Id, 10)
                        .OrderByDescending(m => m.Importance)
                        .ThenByDescending(m => m.Strength)
                        .Take(5)
                        .ToList();

                    if (memories.Count > 0)
                    {
                        sb.AppendLine("Relevant/high memories:");
                        foreach (var m in memories)
                            sb.AppendLine($"- {OneLine(m.Summary, 150)} [{m.Category}; imp {m.Importance}]");
                    }
                }
            }
            catch { }

            return sb.Length == 0 ? "No strong retrieved memories." : sb.ToString();
        }

        public static string OneLine(string? s, int max)
        {
            string v = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return v.Length <= max ? v : v[..max] + "…";
        }
    }
}
