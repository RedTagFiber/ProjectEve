using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectEve.Characters.NPCs
{
    public class NPCAppearance
    {
        public string Gender { get; set; } = "Unknown";
        public int Age { get; set; }

        public string Race { get; set; } = "Unknown";
        public string EyeColor { get; set; } = "Unknown";
        public string EyeStyle { get; set; } = "Unknown";
        public string HairColor { get; set; } = "Unknown";
        public string HairStyle { get; set; } = "Unknown";
        public string SkinTone { get; set; } = "Unknown";
        public string BodyType { get; set; } = "Unknown";
        public string FaceShape { get; set; } = "Unknown";
        public string HeightCategory { get; set; } = "Unknown";
        public string WeightCategory { get; set; } = "Unknown";
        public string Style { get; set; } = "Unknown";
        public string UniqueFeature { get; set; } = "None";

        public int HeightCm { get; set; }
        public int WeightKg { get; set; }
        /// <summary>none | reading | always | fashion</summary>
        public string Glasses { get; set; } = "none";
        /// <summary>Detailed scar text for prompts.</summary>
        public string ScarNotes { get; set; } = "";

        private static readonly Random rng = new();

        public static NPCAppearance GenerateRandom(string gender, int age)
        {
            var npc = new NPCAppearance
            {
                Gender = gender,
                Age = age
            };

            string ageGroup = GetAgeGroup(age);

            npc.Race = GenerateRace();
            npc.SkinTone = GenerateSkinTone(npc.Race);
            npc.EyeColor = GenerateEyeColor(npc.Race, gender, ageGroup);
            npc.EyeStyle = GenerateEyeStyle(gender);
            npc.HairColor = GenerateHairColor(npc.Race, ageGroup);
            npc.HairStyle = GenerateHairStyle(gender, ageGroup);
            npc.FaceShape = GenerateFaceShape(gender);
            npc.BodyType = GenerateBodyType(gender, ageGroup);
            npc.HeightCategory = GenerateHeight(gender);
            npc.WeightCategory = GenerateWeight(gender, ageGroup);
            npc.Style = GenerateStyle(gender, ageGroup);
            npc.UniqueFeature = GenerateUniqueFeature(ageGroup);

            npc.HeightCm = GenerateHeightCm(gender, npc.HeightCategory);
            npc.WeightKg = GenerateWeightKg(gender, npc.BodyType, npc.HeightCm);
            npc.Glasses = GenerateGlasses(ageGroup);
            npc.ScarNotes = GenerateScarNotes(age);

            if (npc.UniqueFeature == "Scar" && !string.IsNullOrWhiteSpace(npc.ScarNotes))
                npc.UniqueFeature = npc.ScarNotes;

            return npc;
        }

        private static string GetAgeGroup(int age)
        {
            if (age < 13) return "Child";
            if (age < 20) return "Teen";
            if (age < 30) return "Young Adult";
            if (age < 50) return "Adult";
            if (age < 70) return "Senior";
            return "Elder";
        }

        private static string GenerateRace()
        {
            var weighted = new List<(string Race, int Weight)>
            {
                ("European", 78),
                ("African", 10),
                ("Latino", 6),
                ("Asian", 3),
                ("Middle Eastern", 2),
                ("Pacific Islander", 1),
                ("Mixed", 5)
            };

            int total = 0;
            foreach (var item in weighted)
                total += item.Weight;

            int roll = rng.Next(0, total);
            int cumulative = 0;
            foreach (var item in weighted)
            {
                cumulative += item.Weight;
                if (roll < cumulative)
                    return item.Race;
            }
            return "European";
        }

        private static string GenerateSkinTone(string race)
        {
            var tones = new List<string>();
            switch (race)
            {
                case "African":
                    tones.AddRange(new[] { "Deep Ebony", "Dark Brown", "Warm Brown", "Mahogany", "Chestnut" });
                    break;
                case "Asian":
                    tones.AddRange(new[] { "Light Tan", "Golden Beige", "Warm Olive", "Medium Tan" });
                    break;
                case "European":
                    tones.AddRange(new[] { "Fair", "Light", "Pale", "Rosy", "Porcelain", "Light Olive" });
                    break;
                case "Middle Eastern":
                    tones.AddRange(new[] { "Olive", "Golden Tan", "Medium Brown", "Warm Beige" });
                    break;
                case "Latino":
                    tones.AddRange(new[] { "Tan", "Golden Brown", "Medium Olive", "Light Brown" });
                    break;
                case "Pacific Islander":
                    tones.AddRange(new[] { "Warm Bronze", "Golden Brown", "Deep Tan", "Medium Brown" });
                    break;
                default:
                    tones.AddRange(new[] { "Light Brown", "Tan", "Olive", "Fair", "Medium Brown" });
                    break;
            }
            return tones[rng.Next(tones.Count)];
        }

        private static string GenerateEyeColor(string race, string gender, string ageGroup)
        {
            var colors = new List<string>();
            switch (race)
            {
                case "African":
                    colors.AddRange(new[] { "Dark Brown", "Brown", "Deep Amber", "Warm Hazel", "Golden Brown", "Copper Brown" });
                    break;
                case "Asian":
                    colors.AddRange(new[] { "Dark Brown", "Brown", "Soft Hazel", "Amber", "Honey Brown" });
                    break;
                case "European":
                    colors.AddRange(new[]
                    {
                        "Blue", "Light Blue", "Steel Blue", "Ice Blue",
                        "Green", "Emerald Green", "Olive Green",
                        "Hazel", "Amber", "Gray", "Silver Gray", "Charcoal Gray", "Brown"
                    });
                    break;
                case "Middle Eastern":
                    colors.AddRange(new[] { "Brown", "Dark Brown", "Amber", "Golden Hazel", "Olive Green" });
                    break;
                case "Latino":
                    colors.AddRange(new[] { "Brown", "Dark Brown", "Hazel", "Green", "Amber" });
                    break;
                case "Pacific Islander":
                    colors.AddRange(new[] { "Brown", "Dark Brown", "Amber", "Golden Brown", "Hazel" });
                    break;
                default:
                    colors.AddRange(new[] { "Brown", "Dark Brown", "Hazel", "Green", "Blue", "Gray", "Amber" });
                    break;
            }

            if (rng.Next(0, 100) < 3)
                colors.AddRange(new[] { "Violet", ageGroup == "Child" ? "Bright Blue" : "Crystal Blue", "Pale Gray" });

            if (rng.Next(0, 100) < (ageGroup == "Teen" ? 10 : 5))
                colors.AddRange(new[] { "Central Heterochromia", "Ringed Hazel", "Starburst Green", "Speckled Amber" });

            if (rng.Next(0, 100) < 2)
            {
                string c1 = colors[rng.Next(colors.Count)];
                string c2 = colors[rng.Next(colors.Count)];
                return $"{c1} / {c2}";
            }

            return colors[rng.Next(colors.Count)];
        }

        private static string GenerateEyeStyle(string gender)
        {
            string[] styles = { "Almond", "Round", "Hooded", "Deep-set", "Upturned", "Downturned", "Monolid", "Wide-set" };
            return styles[rng.Next(styles.Length)];
        }

        private static string GenerateHairColor(string race, string ageGroup)
        {
            var colors = new List<string>();
            switch (race)
            {
                case "African":
                    colors.AddRange(new[] { "Black", "Dark Brown", "Jet Black", "Soft Brown" });
                    break;
                case "Asian":
                    colors.AddRange(new[] { "Black", "Dark Brown", "Soft Brown" });
                    break;
                case "European":
                    colors.AddRange(new[]
                    {
                        "Blonde", "Dirty Blonde", "Golden Blonde", "Light Brown", "Brown",
                        "Dark Brown", "Black", "Copper", "Auburn", "Red"
                    });
                    break;
                case "Middle Eastern":
                    colors.AddRange(new[] { "Black", "Dark Brown", "Brown", "Warm Chestnut" });
                    break;
                case "Latino":
                    colors.AddRange(new[] { "Black", "Dark Brown", "Brown", "Light Brown", "Auburn" });
                    break;
                case "Pacific Islander":
                    colors.AddRange(new[] { "Black", "Dark Brown", "Soft Brown", "Warm Brown" });
                    break;
                default:
                    colors.AddRange(new[] { "Black", "Brown", "Dark Brown", "Light Brown", "Blonde", "Auburn" });
                    break;
            }

            if (ageGroup is "Senior" or "Elder")
            {
                colors.Add("Gray");
                colors.Add("Silver");
                colors.Add("White");
            }

            if (ageGroup == "Teen" && rng.Next(0, 100) < 10)
                colors.AddRange(new[] { "Dyed Blue", "Dyed Pink", "Dyed Purple", "Dyed Red", "Dyed Green" });

            return colors[rng.Next(colors.Count)];
        }

        private static string GenerateHairStyle(string gender, string ageGroup)
        {
            var styles = new List<string>();

            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
            {
                styles.AddRange(new[]
                {
                    "Buzz Cut", "Fade", "Crew Cut", "Short Messy",
                    "Short Curly", "Shoulder Length", "Braids", "Dreadlocks"
                });
            }
            else
            {
                styles.AddRange(new[]
                {
                    "Long Straight", "Wavy", "Curly", "Ponytail",
                    "Bun", "Braids", "Twists", "Shoulder Length", "Short Bob"
                });
            }

            if (ageGroup == "Child")
                styles.AddRange(new[] { "Simple Short", "Simple Long", "Child Braids" });
            if (ageGroup == "Teen")
                styles.AddRange(new[] { "Trendy Cut", "Dyed Tips", "Messy Waves" });
            if (ageGroup is "Senior" or "Elder")
                styles.AddRange(new[] { "Short Natural", "Soft Waves", "Simple Bun" });

            return styles[rng.Next(styles.Count)];
        }

        private static string GenerateFaceShape(string gender)
        {
            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
            {
                string[] shapes = { "Square", "Oval", "Round", "Long", "Diamond", "Soft Square" };
                return shapes[rng.Next(shapes.Length)];
            }
            else
            {
                string[] shapes = { "Oval", "Heart", "Round", "Diamond", "Soft Square", "Long" };
                return shapes[rng.Next(shapes.Length)];
            }
        }

        private static string GenerateBodyType(string gender, string ageGroup)
        {
            if (ageGroup == "Child") return "Childlike";
            if (ageGroup == "Teen") return "Developing";

            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
            {
                string[] types = { "Slim", "Average", "Athletic", "Muscular", "Stocky", "Heavy" };
                return types[rng.Next(types.Length)];
            }
            else
            {
                string[] types = { "Slim", "Average", "Curvy", "Athletic", "Soft", "Plus Size" };
                return types[rng.Next(types.Length)];
            }
        }

        private static string GenerateHeight(string gender)
        {
            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
            {
                string[] heights = { "Short", "Average", "Above Average", "Tall", "Very Tall" };
                return heights[rng.Next(heights.Length)];
            }
            else
            {
                string[] heights = { "Short", "Below Average", "Average", "Above Average", "Tall" };
                return heights[rng.Next(heights.Length)];
            }
        }

        private static string GenerateWeight(string gender, string ageGroup)
        {
            if (ageGroup == "Child") return "Child Weight";
            if (ageGroup == "Teen") return "Teen Weight";

            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
            {
                string[] weights = { "Slim", "Average", "Fit", "Athletic", "Heavy", "Plus Size" };
                return weights[rng.Next(weights.Length)];
            }
            else
            {
                string[] weights = { "Slim", "Average", "Curvy", "Fit", "Soft", "Plus Size" };
                return weights[rng.Next(weights.Length)];
            }
        }

        private static string GenerateStyle(string gender, string ageGroup)
        {
            if (ageGroup == "Child") return "Playful";

            var styles = new List<string>();
            if (ageGroup == "Teen")
            {
                styles.AddRange(new[] { "Trendy", "Streetwear", "Sporty", "Casual" });
                return styles[rng.Next(styles.Count)];
            }

            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
                styles.AddRange(new[] { "Casual", "Sporty", "Streetwear", "Professional", "Minimalist", "Rugged" });
            else
                styles.AddRange(new[] { "Casual", "Chic", "Elegant", "Bohemian", "Streetwear", "Professional", "Minimalist" });

            if (ageGroup is "Senior" or "Elder")
                styles.Add("Comfortable");

            return styles[rng.Next(styles.Count)];
        }

        private static string GenerateUniqueFeature(string ageGroup)
        {
            if (ageGroup == "Child")
            {
                string[] features = { "Freckles", "Bright Eyes", "Missing Tooth", "None" };
                return features[rng.Next(features.Length)];
            }
            if (ageGroup == "Teen")
            {
                string[] features = { "Acne", "Piercing", "Dyed Hair", "Freckles", "None" };
                return features[rng.Next(features.Length)];
            }
            if (ageGroup is "Young Adult" or "Adult")
            {
                string[] features = { "Beauty Mark", "Tattoo", "Scar", "High Cheekbones", "Dimples", "None" };
                return features[rng.Next(features.Length)];
            }
            if (ageGroup is "Senior" or "Elder")
            {
                string[] features = { "Wrinkles", "Gray Hair", "Soft Eyes", "Age Spots", "None" };
                return features[rng.Next(features.Length)];
            }
            return "None";
        }

        private static int GenerateHeightCm(string gender, string heightCategory)
        {
            bool male = string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase);
            return heightCategory switch
            {
                "Short" => male ? rng.Next(160, 168) : rng.Next(150, 158),
                "Below Average" => male ? rng.Next(168, 173) : rng.Next(155, 160),
                "Average" => male ? rng.Next(173, 180) : rng.Next(160, 168),
                "Above Average" => male ? rng.Next(180, 188) : rng.Next(168, 175),
                "Tall" => male ? rng.Next(188, 196) : rng.Next(175, 183),
                "Very Tall" => male ? rng.Next(196, 205) : rng.Next(183, 190),
                _ => male ? rng.Next(170, 182) : rng.Next(158, 170)
            };
        }

        private static int GenerateWeightKg(string gender, string bodyType, int heightCm)
        {
            double bmi = bodyType switch
            {
                "Slim" or "Childlike" => 19.0,
                "Athletic" or "Muscular" or "Fit" => 23.5,
                "Curvy" or "Soft" => 25.5,
                "Stocky" or "Heavy" or "Plus Size" => 29.0,
                _ => 22.5
            };
            double h = heightCm / 100.0;
            int kg = (int)Math.Round(bmi * h * h);
            return Math.Clamp(kg + rng.Next(-3, 4), 40, 160);
        }

        private static string GenerateGlasses(string ageGroup)
        {
            int chance = ageGroup switch
            {
                "Child" => 8,
                "Teen" => 15,
                "Young Adult" => 18,
                "Adult" => 28,
                "Senior" => 45,
                "Elder" => 55,
                _ => 20
            };
            if (rng.Next(0, 100) >= chance)
                return "none";

            if (ageGroup is "Senior" or "Elder")
                return rng.Next(0, 100) < 70 ? "reading" : "always";

            string[] kinds = { "reading", "always", "fashion" };
            return kinds[rng.Next(kinds.Length)];
        }

        private static string GenerateScarNotes(int age)
        {
            int chance = Math.Min(35, 10 + Math.Max(0, (age - 18) / 4));
            if (rng.Next(0, 100) >= chance)
                return "";

            string[] scars =
            {
                "thin 2-inch cut over the left eyebrow",
                "small crescent scar on the chin",
                "faded scrape mark along the right cheekbone",
                "short white scar on the upper lip",
                "knuckle scars on the right hand",
                "surgical line low on the abdomen",
                "burn mark the size of a coin on the left forearm",
                "knee scar from a childhood fall",
                "fine scar through the left eyebrow",
                "old stitch marks on the right calf"
            };
            return scars[rng.Next(scars.Length)];
        }

        public string ToPromptFragment()
        {
            var sb = new StringBuilder();
            sb.Append($"{Age}-year-old {Gender.ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(Race) && Race != "Unknown")
                sb.Append($", {Race.ToLowerInvariant()} features");
            if (!string.IsNullOrWhiteSpace(SkinTone) && SkinTone != "Unknown")
                sb.Append($", {SkinTone.ToLowerInvariant()} skin");
            if (!string.IsNullOrWhiteSpace(HairColor) && HairColor != "Unknown")
                sb.Append($", {HairColor.ToLowerInvariant()} hair");
            if (!string.IsNullOrWhiteSpace(HairStyle) && HairStyle != "Unknown")
                sb.Append($" ({HairStyle.ToLowerInvariant()})");
            if (!string.IsNullOrWhiteSpace(EyeColor) && EyeColor != "Unknown")
                sb.Append($", {EyeColor.ToLowerInvariant()} eyes");
            if (!string.IsNullOrWhiteSpace(EyeStyle) && EyeStyle != "Unknown")
                sb.Append($" ({EyeStyle.ToLowerInvariant()})");
            if (!string.IsNullOrWhiteSpace(BodyType) && BodyType != "Unknown")
                sb.Append($", {BodyType.ToLowerInvariant()} build");
            if (HeightCm > 0)
                sb.Append($", about {HeightCm} cm");
            if (!string.IsNullOrWhiteSpace(Style) && Style != "Unknown")
                sb.Append($", {Style.ToLowerInvariant()} style");
            if (!string.Equals(Glasses, "none", StringComparison.OrdinalIgnoreCase))
                sb.Append($", wearing {Glasses} glasses");
            if (!string.IsNullOrWhiteSpace(ScarNotes))
                sb.Append($", {ScarNotes}");
            else if (!string.IsNullOrWhiteSpace(UniqueFeature) && UniqueFeature != "None")
                sb.Append($", {UniqueFeature.ToLowerInvariant()}");
            return sb.ToString();
        }
    }
}