using ProjectEve.Characters.Base;
using System.Text;
using ProjectEve.Traits;
namespace ProjectEve.Characters.Base
{
    public static class CharacterFactor
    {
        public static string BuildContext(SimCharacter npc)
        {
            if (npc == null)
                return "No character loaded.";

            var sb = new StringBuilder();

            // BASIC IDENTITY
            sb.AppendLine($"{npc.Name} is a {npc.Age}-year-old living in {npc.Location}.");
            sb.AppendLine($"Occupation: {npc.Occupation}.");
            sb.AppendLine($"Current goal: {npc.Goal}.");
            sb.AppendLine($"Need: {npc.Need}. Fear: {npc.Fear}. Want: {npc.Want}.");
            sb.AppendLine();

            // TRAITS (new system)
            sb.AppendLine("Personality Traits:");
            if (npc.Traits != null)
            {
                // Use strongest/most extreme traits for context
                var extremes = npc.Traits.GetMostExtreme(10);
                foreach (var (traitId, value, _) in extremes)
                {
                    var def = TraitRegistry.GetDefinition(traitId);
                    string name = def?.Name ?? traitId;
                    sb.AppendLine($"- {name}: {value:0}");
                }
            }
            else
            {
                sb.AppendLine("- No traits loaded.");
            }
            sb.AppendLine();

            // RELATIONSHIPS
            sb.AppendLine("Relationships:");
            if (npc.Relationships != null && npc.Relationships.Count > 0)
            {
                foreach (var rel in npc.Relationships)
                {
                    sb.AppendLine(
                        $"- {rel.TargetName}: Trust {rel.Trust}, Respect {rel.Respect}, " +
                        $"Affection {rel.Affection}, Attraction {rel.Attraction}"
                    );
                }
            }
            else
            {
                sb.AppendLine("- None loaded.");
            }
            sb.AppendLine();

            // MEMORIES
            sb.AppendLine("Key Memories:");
            try
            {
                var memories = npc.MemoryDB?.GetMemories(npc.Name);
                if (memories != null)
                {
                    foreach (var mem in memories)
                    {
                        sb.AppendLine(
                            $"- [{mem.Timestamp}] ({mem.Importance}) {mem.Summary} - {mem.Category}"
                        );
                    }
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

            // HISTORY
            sb.AppendLine("Life History:");
            if (npc.History != null && npc.History.Count > 0)
            {
                foreach (var hist in npc.History)
                {
                    sb.AppendLine(
                        $"- Age {hist.Age}: {hist.Summary} ({hist.Category})"
                    );
                }
            }
            else
            {
                sb.AppendLine("- None loaded.");
            }
            sb.AppendLine();

            // APPEARANCE
            sb.AppendLine("Appearance:");
            if (npc.Appearance != null)
            {
                sb.AppendLine(
                    $"Hair: {npc.Appearance.HairColor}, " +
                    $"Eyes: {npc.Appearance.EyeColor}, " +
                    $"Style: {npc.Appearance.Style}"
                );
            }
            else
            {
                sb.AppendLine("- No appearance loaded.");
            }

            return sb.ToString();
        }
    }
}