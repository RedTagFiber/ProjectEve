using System;

namespace ProjectEve.Characters.Traits.Special
{
    /// <summary>
    /// Mid-layer identity trait definition (WHO they are).
    /// Live value is NpcTraits["mid.…"] — this class is catalog + flavor, not a second number bag.
    /// </summary>
    public class SpecialTrait
    {
        /// <summary>Canonical id: mid.loyal, mid.guarded, mid.workaholic</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name: Loyal, Guarded, Workaholic</summary>
        public string Name { get; set; } = "";

        /// <summary>Identity | Emotional | Social | Cognitive | Behavioral | Trauma</summary>
        public string Category { get; set; } = "Identity";

        public string Description { get; set; } = "";

        /// <summary>Suggested prior intensity 0–100 when rolled onto an NPC.</summary>
        public int DefaultIntensity { get; set; } = 55;

        /// <summary>
        /// Soft relationship / comfort modifiers when intensity is high.
        /// Applied as: bonus * (midValue / 100).
        /// </summary>
        public int ComfortBonus { get; set; } = 0;
        public int TrustBonus { get; set; } = 0;
        public int AffectionBonus { get; set; } = 0;
        public int AttractionBonus { get; set; } = 0;

        public SpecialTrait() { }

        public SpecialTrait(
            string idOrName,
            string category = "Identity",
            string description = "",
            int comfortBonus = 0,
            int trustBonus = 0,
            int affectionBonus = 0,
            int attractionBonus = 0)
        {
            // Accept "Loyal" or "mid.loyal"
            if (idOrName.StartsWith("mid.", StringComparison.OrdinalIgnoreCase))
            {
                Id = idOrName.ToLowerInvariant();
                Name = Humanize(Id);
            }
            else
            {
                Name = idOrName;
                Id = "mid." + Sanitize(idOrName);
            }

            Category = category;
            Description = description;
            ComfortBonus = comfortBonus;
            TrustBonus = trustBonus;
            AffectionBonus = affectionBonus;
            AttractionBonus = attractionBonus;
        }

        /// <summary>Scaled bonus at current Mid intensity (0–100).</summary>
        public int ScaledBonus(int baseBonus, float midValue0to100)
            => (int)Math.Round(baseBonus * (Math.Clamp(midValue0to100, 0f, 100f) / 100f));

        private static string Sanitize(string name)
        {
            var s = name.Trim().ToLowerInvariant().Replace(' ', '_');
            foreach (var ch in s.ToCharArray())
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                    s = s.Replace(ch.ToString(), "");
            }
            return s;
        }

        private static string Humanize(string id)
        {
            // mid.people_pleasing → People Pleasing
            var raw = id.StartsWith("mid.", StringComparison.OrdinalIgnoreCase)
                ? id.Substring(4)
                : id;
            raw = raw.Replace('_', ' ');
            if (raw.Length == 0) return id;
            return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
        }
    }
}