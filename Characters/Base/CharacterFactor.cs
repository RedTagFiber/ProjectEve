using ProjectEve.Characters.Base;
using ProjectEve.Traits;
using System.Text;

namespace ProjectEve.Characters.Base
{
    /// <summary>
    /// Builds a text context block for prompts from a live SimCharacter.
    /// </summary>
    public static class CharacterFactor
    {
        public static string BuildContext(SimCharacter npc)
        {
            if (npc == null)
                return "No character loaded.";

            var sb = new StringBuilder();

            sb.AppendLine($"{npc.Name} is a {npc.Age}-year-old living in {npc.Location}.");
            try
            {
                if (!string.IsNullOrWhiteSpace(npc.HomeAddress))
                    sb.AppendLine($"Home: {npc.HomeAddress}.");
            }
            catch { }

            sb.AppendLine($"Occupation: {npc.Occupation}.");

            try
            {
                if (npc.Job != null && !string.IsNullOrWhiteSpace(npc.Job.JobName))
                    sb.AppendLine($"Job: {npc.Job.SummaryLine()}.");
            }
            catch
            {
                try
                {
                    if (npc.Job != null && !string.IsNullOrWhiteSpace(npc.Job.JobName))
                        sb.AppendLine($"Job: {npc.Job.JobName} @ {npc.Job.Employer}.");
                }
                catch { }
            }

            try
            {
                if (npc.Money != null)
                    sb.AppendLine($"Money: cash {npc.Money.Cash:0} / bank {npc.Money.Bank:0} / debt {npc.Money.Debt:0}.");
            }
            catch { }

            sb.AppendLine($"Current goal: {npc.Goal}.");
            sb.AppendLine($"Need: {npc.Need}. Fear: {npc.Fear}. Want: {npc.Want}.");
            sb.AppendLine();

            sb.AppendLine("Traits (strongest first):");
            if (npc.Traits != null && npc.Traits.GetAll().Count > 0)
            {
                var extremes = npc.Traits.GetMostExtreme(12);
                foreach (var (traitId, value, _) in extremes)
                {
                    string name = traitId;
                    try
                    {
                        var def = TraitRegistry.GetDefinition(traitId);
                        if (def != null && !string.IsNullOrWhiteSpace(def.Name))
                            name = def.Name;
                    }
                    catch { }

                    string style = "";
                    try
                    {
                        var s = npc.Traits.GetStyle(traitId);
                        if (!string.IsNullOrWhiteSpace(s))
                            style = $" [{s}]";
                    }
                    catch { }

                    sb.AppendLine($"- {name} ({traitId}): {value:0}{style}");
                }

                // surface high kink parent if present
                try
                {
                    float kink = npc.Traits.Get("slow.kink");
                    if (kink >= 50)
                        sb.AppendLine($"- Adult/kink openness (slow.kink): {kink:0}");
                }
                catch { }
            }
            else
            {
                sb.AppendLine("- No traits loaded.");
            }
            sb.AppendLine();

            sb.AppendLine("Relationships:");
            if (npc.Relationships != null && npc.Relationships.Count > 0)
            {
                foreach (var rel in npc.Relationships)
                {
                    string tension = "";
                    try { tension = $", Tension {rel.Tension}"; } catch { }
                    sb.AppendLine(
                        $"- {rel.TargetName}: Trust {rel.Trust}, Respect {rel.Respect}, " +
                        $"Affection {rel.Affection}, Attraction {rel.Attraction}{tension}");
                }
            }
            else
            {
                sb.AppendLine("- None loaded.");
            }
            sb.AppendLine();

            sb.AppendLine("Key Memories:");
            try
            {
                var memories = npc.MemoryDB?.GetMemories(npc.Name);
                if (memories != null)
                {
                    int n = 0;
                    foreach (var mem in memories)
                    {
                        sb.AppendLine($"- ({mem.Importance}) {mem.Summary} [{mem.Category}]");
                        if (++n >= 8) break;
                    }
                    if (n == 0) sb.AppendLine("- None loaded.");
                }
                else
                {
                    sb.AppendLine("- None loaded.");
                }
            }
            catch
            {
                sb.AppendLine("- Memory system not ready.");
            }
            sb.AppendLine();

            sb.AppendLine("Life History:");
            if (npc.History != null && npc.History.Count > 0)
            {
                int n = 0;
                foreach (var hist in npc.History)
                {
                    sb.AppendLine($"- Age {hist.Age}: {hist.Summary} ({hist.Category})");
                    if (++n >= 8) break;
                }
            }
            else
            {
                sb.AppendLine("- None loaded.");
            }
            sb.AppendLine();

            sb.AppendLine("Appearance:");
            if (npc.Appearance != null)
            {
                sb.AppendLine(
                    $"Hair: {npc.Appearance.HairColor} / {npc.Appearance.HairStyle}, " +
                    $"Eyes: {npc.Appearance.EyeColor}, " +
                    $"Body: {npc.Appearance.BodyType}, " +
                    $"Style: {npc.Appearance.Style}");
                if (!string.IsNullOrWhiteSpace(npc.Appearance.UniqueFeature))
                    sb.AppendLine($"Feature: {npc.Appearance.UniqueFeature}");
            }
            else
            {
                try
                {
                    sb.AppendLine(
                        $"Hair: {npc.HairColor} / {npc.HairStyle}, Eyes: {npc.EyeColor}, Body: {npc.BodyShape}");
                }
                catch
                {
                    sb.AppendLine("- No appearance loaded.");
                }
            }

            if (!string.IsNullOrWhiteSpace(npc.PersonalityContext))
            {
                sb.AppendLine();
                sb.AppendLine("Personality context:");
                sb.AppendLine(npc.PersonalityContext);
            }

            return sb.ToString();
        }
    }
}