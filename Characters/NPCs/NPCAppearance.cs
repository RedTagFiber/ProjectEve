using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.NPCs
{
    public class NPCAppearance
    {
        
        
            public string Gender { get; set; } = "Unknown";
            public int Age { get; set; } = 0;

            public string Race { get; set; } = "Unknown";
            public string EyeColor { get; set; } = "Unknown";
            public string HairColor { get; set; } = "Unknown";
            public string HairStyle { get; set; } = "Unknown";
            public string SkinTone { get; set; } = "Unknown";
            public string BodyType { get; set; } = "Unknown";
            public string FaceShape { get; set; } = "Unknown";
            public string HeightCategory { get; set; } = "Unknown";
            public string WeightCategory { get; set; } = "Unknown";
            public string Style { get; set; } = "Unknown";
            public string UniqueFeature { get; set; } = "None";
        


        private static readonly Random rng = new();

        // ============================================================
        // MAIN GENERATOR
        // ============================================================
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
            npc.HairColor = GenerateHairColor(npc.Race, ageGroup);
            npc.HairStyle = GenerateHairStyle(gender, ageGroup);
            npc.FaceShape = GenerateFaceShape(gender);
            npc.BodyType = GenerateBodyType(gender, ageGroup);
            npc.HeightCategory = GenerateHeight(gender);
            npc.WeightCategory = GenerateWeight(gender, ageGroup);
            npc.Style = GenerateStyle(gender, ageGroup);
            npc.UniqueFeature = GenerateUniqueFeature(ageGroup);

            return npc;
        }

        // ============================================================
        // AGE GROUPS
        // ============================================================
        private static string GetAgeGroup(int age)
        {
            if (age < 13) return "Child";
            if (age < 20) return "Teen";
            if (age < 30) return "Young Adult";
            if (age < 50) return "Adult";
            if (age < 70) return "Senior";
            return "Elder";
        }

        // ============================================================
        // RACE (Weighted Small-Town USA + Pacific Islander)
        // ============================================================
        private static string GenerateRace()
        {
            var weighted = new List<(string Race, int Weight)>
            {
                ("European", 78),
                ("African", 10),
                ("Latino", 6),
                ("Asian", 3),
                ("Middle Eastern", 2),
                ("Pacific Islander", 1), // Added safely
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

        // ============================================================
        // SKIN TONE (Race-based)
        // ============================================================
        private static string GenerateSkinTone(string race)
        {
            var tones = new List<string>();

            switch (race)
            {
                case "African":
                    tones.AddRange(new[]
                    {
                        "Deep Ebony", "Dark Brown", "Warm Brown",
                        "Mahogany", "Chestnut"
                    });
                    break;

                case "Asian":
                    tones.AddRange(new[]
                    {
                        "Light Tan", "Golden Beige", "Warm Olive",
                        "Medium Tan"
                    });
                    break;

                case "European":
                    tones.AddRange(new[]
                    {
                        "Fair", "Light", "Pale", "Rosy",
                        "Porcelain", "Light Olive"
                    });
                    break;

                case "Middle Eastern":
                    tones.AddRange(new[]
                    {
                        "Olive", "Golden Tan", "Medium Brown",
                        "Warm Beige"
                    });
                    break;

                case "Latino":
                    tones.AddRange(new[]
                    {
                        "Tan", "Golden Brown", "Medium Olive",
                        "Light Brown"
                    });
                    break;

                case "Pacific Islander":
                    tones.AddRange(new[]
                    {
                        "Warm Bronze", "Golden Brown", "Deep Tan",
                        "Medium Brown"
                    });
                    break;

                case "Mixed":
                    tones.AddRange(new[]
                    {
                        "Light Brown", "Tan", "Olive",
                        "Fair", "Medium Brown"
                    });
                    break;
            }

            return tones[rng.Next(tones.Count)];
        }

        // ============================================================
        // EYE COLOR (Race + Gender + Age + Rare Variants)
        // ============================================================
        private static string GenerateEyeColor(string race, string gender, string ageGroup)
        {
            var colors = new List<string>();

            switch (race)
            {
                case "African":
                    colors.AddRange(new[]
                    {
                        "Dark Brown", "Brown", "Deep Amber",
                        "Warm Hazel", "Golden Brown", "Copper Brown"
                    });
                    break;

                case "Asian":
                    colors.AddRange(new[]
                    {
                        "Dark Brown", "Brown", "Soft Hazel",
                        "Amber", "Honey Brown"
                    });
                    break;

                case "European":
                    colors.AddRange(new[]
                    {
                        "Blue", "Light Blue", "Steel Blue", "Ice Blue",
                        "Green", "Emerald Green", "Olive Green",
                        "Hazel", "Amber", "Gray", "Silver Gray",
                        "Charcoal Gray", "Brown"
                    });
                    break;

                case "Middle Eastern":
                    colors.AddRange(new[]
                    {
                        "Brown", "Dark Brown", "Amber",
                        "Golden Hazel", "Olive Green"
                    });
                    break;

                case "Latino":
                    colors.AddRange(new[]
                    {
                        "Brown", "Dark Brown", "Hazel",
                        "Green", "Amber"
                    });
                    break;

                case "Pacific Islander":
                    colors.AddRange(new[]
                    {
                        "Brown", "Dark Brown", "Amber",
                        "Golden Brown", "Hazel"
                    });
                    break;

                case "Mixed":
                    colors.AddRange(new[]
                    {
                        "Brown", "Dark Brown", "Hazel", "Green",
                        "Blue", "Gray", "Amber", "Steel Blue",
                        "Olive Green"
                    });
                    break;
            }

            // Rare colors
            if (rng.Next(0, 100) < 3)
            {
                colors.AddRange(new[]
                {
                    "Violet",
                    ageGroup == "Child" ? "Bright Blue" : "Crystal Blue",
                    "Red (Albinism)",
                    "Pale Gray"
                });
            }

            // Patterned eyes
            if (rng.Next(0, 100) < (ageGroup == "Teen" ? 10 : 5))
            {
                colors.AddRange(new[]
                {
                    "Central Heterochromia",
                    "Ringed Hazel",
                    "Starburst Green",
                    "Speckled Amber"
                });
            }

            // Heterochromia
            if (rng.Next(0, 100) < 2)
            {
                string c1 = colors[rng.Next(colors.Count)];
                string c2 = colors[rng.Next(colors.Count)];
                return $"{c1} / {c2}";
            }

            return colors[rng.Next(colors.Count)];
        }

        // ============================================================
        // HAIR COLOR (Race + Age)
        // ============================================================
        private static string GenerateHairColor(string race, string ageGroup)
        {
            var colors = new List<string>();

            switch (race)
            {
                case "African":
                    colors.AddRange(new[]
                    {
                        "Black", "Dark Brown", "Jet Black",
                        "Soft Brown"
                    });
                    break;

                case "Asian":
                    colors.AddRange(new[]
                    {
                        "Black", "Dark Brown", "Soft Brown"
                    });
                    break;

                case "European":
                    colors.AddRange(new[]
                    {
                        "Blonde", "Dirty Blonde", "Golden Blonde",
                        "Light Brown", "Brown", "Dark Brown",
                        "Black", "Copper", "Auburn", "Red"
                    });
                    break;

                case "Middle Eastern":
                    colors.AddRange(new[]
                    {
                        "Black", "Dark Brown", "Brown",
                        "Warm Chestnut"
                    });
                    break;

                case "Latino":
                    colors.AddRange(new[]
                    {
                        "Black", "Dark Brown", "Brown",
                        "Light Brown", "Auburn"
                    });
                    break;

                case "Pacific Islander":
                    colors.AddRange(new[]
                    {
                        "Black", "Dark Brown", "Soft Brown",
                        "Warm Brown"
                    });
                    break;

                case "Mixed":
                    colors.AddRange(new[]
                    {
                        "Black", "Brown", "Dark Brown",
                        "Light Brown", "Blonde", "Auburn"
                    });
                    break;
            }

            // Age modifiers
            if (ageGroup == "Senior" || ageGroup == "Elder")
            {
                colors.Add("Gray");
                colors.Add("Silver");
                colors.Add("White");
            }

            // Teen dye colors
            if (ageGroup == "Teen" && rng.Next(0, 100) < 10)
            {
                colors.AddRange(new[]
                {
                    "Dyed Blue", "Dyed Pink", "Dyed Purple",
                    "Dyed Red", "Dyed Green"
                });
            }

            return colors[rng.Next(colors.Count)];
        }

        // ============================================================
        // HAIR STYLE (Gender + Age)
        // ============================================================
        private static string GenerateHairStyle(string gender, string ageGroup)
        {
            var styles = new List<string>();

            if (gender == "Male")
            {
                styles.AddRange(new[]
                {
                    "Buzz Cut", "Fade", "Crew Cut", "Short Messy",
                    "Short Curly", "Shoulder Length", "Braids",
                    "Dreadlocks"
                });
            }
            else
            {
                styles.AddRange(new[]
                {
                    "Long Straight", "Wavy", "Curly", "Ponytail",
                    "Bun", "Braids", "Twists", "Shoulder Length",
                    "Short Bob"
                });
            }

            // Age modifiers
            if (ageGroup == "Child")
            {
                styles.AddRange(new[]
                {
                    "Simple Short", "Simple Long", "Child Braids"
                });
            }

            if (ageGroup == "Teen")
            {
                styles.AddRange(new[]
                {
                    "Trendy Cut", "Dyed Tips", "Messy Waves"
                });
            }

            if (ageGroup == "Senior" || ageGroup == "Elder")
            {
                styles.AddRange(new[]
                {
                    "Short Natural", "Soft Waves", "Simple Bun"
                });
            }

            return styles[rng.Next(styles.Count)];
        }

        // ============================================================
        // FACE SHAPE (Gender)
        // ============================================================
        private static string GenerateFaceShape(string gender)
        {
            if (gender == "Male")
            {
                string[] shapes =
                {
                    "Square", "Oval", "Round", "Long",
                    "Diamond", "Soft Square"
                };
                return shapes[rng.Next(shapes.Length)];
            }
            else
            {
                string[] shapes =
                {
                    "Oval", "Heart", "Round", "Diamond",
                    "Soft Square", "Long"
                };
                return shapes[rng.Next(shapes.Length)];
            }
        }

        // ============================================================
        // BODY TYPE (Gender + Age)
        // ============================================================
        private static string GenerateBodyType(string gender, string ageGroup)
        {
            if (ageGroup == "Child")
                return "Childlike";

            if (ageGroup == "Teen")
                return "Developing";

            if (gender == "Male")
            {
                string[] types =
                {
                    "Slim", "Average", "Athletic", "Muscular",
                    "Stocky", "Heavy"
                };
                return types[rng.Next(types.Length)];
            }
            else
            {
                string[] types =
                {
                    "Slim", "Average", "Curvy", "Athletic",
                    "Soft", "Plus Size"
                };
                return types[rng.Next(types.Length)];
            }
        }

        // ============================================================
        // HEIGHT (Gender)
        // ============================================================
        private static string GenerateHeight(string gender)
        {
            if (gender == "Male")
            {
                string[] heights =
                {
                    "Short", "Average", "Above Average",
                    "Tall", "Very Tall"
                };
                return heights[rng.Next(heights.Length)];
            }
            else
            {
                string[] heights =
                {
                    "Short", "Below Average", "Average",
                    "Above Average", "Tall"
                };
                return heights[rng.Next(heights.Length)];
            }
        }

        // ============================================================
        // WEIGHT (Gender + Age)
        // ============================================================
        private static string GenerateWeight(string gender, string ageGroup)
        {
            if (ageGroup == "Child")
                return "Child Weight";

            if (ageGroup == "Teen")
                return "Teen Weight";

            if (gender == "Male")
            {
                string[] weights =
                {
                    "Slim", "Average", "Fit", "Athletic",
                    "Heavy", "Plus Size"
                };
                return weights[rng.Next(weights.Length)];
            }
            else
            {
                string[] weights =
                {
                    "Slim", "Average", "Curvy", "Fit",
                    "Soft", "Plus Size"
                };
                return weights[rng.Next(weights.Length)];
            }
        }

        // ============================================================
        // STYLE (Gender + Age)
        // ============================================================
        private static string GenerateStyle(string gender, string ageGroup)
        {
            var styles = new List<string>();

            if (ageGroup == "Child")
                return "Playful";

            if (ageGroup == "Teen")
            {
                styles.AddRange(new[]
                {
                    "Trendy", "Streetwear", "Sporty", "Casual"
                });
                return styles[rng.Next(styles.Count)];
            }

            if (gender == "Male")
            {
                styles.AddRange(new[]
                {
                    "Casual", "Sporty", "Streetwear",
                    "Professional", "Minimalist", "Rugged"
                });
            }
            else
            {
                styles.AddRange(new[]
                {
                    "Casual", "Chic", "Elegant", "Bohemian",
                    "Streetwear", "Professional", "Minimalist"
                });
            }

            if (ageGroup == "Senior" || ageGroup == "Elder")
                styles.Add("Comfortable");

            return styles[rng.Next(styles.Count)];
        }

        // ============================================================
        // UNIQUE FEATURES (Age)
        // ============================================================
        private static string GenerateUniqueFeature(string ageGroup)
        {
            if (ageGroup == "Child")
            {
                string[] features =
                {
                    "Freckles", "Bright Eyes", "Missing Tooth",
                    "None"
                };
                return features[rng.Next(features.Length)];
            }

            if (ageGroup == "Teen")
            {
                string[] features =
                {
                    "Acne", "Piercing", "Dyed Hair",
                    "Freckles", "None"
                };
                return features[rng.Next(features.Length)];
            }

            if (ageGroup == "Adult")
            {
                string[] features =
                {
                    "Beauty Mark", "Tattoo", "Scar",
                    "High Cheekbones", "Dimples",
                    "None"
                };
                return features[rng.Next(features.Length)];
            }

            if (ageGroup == "Senior" || ageGroup == "Elder")
            {
                string[] features =
                {
                    "Wrinkles", "Gray Hair", "Soft Eyes",
                    "Age Spots", "None"
                };
                return features[rng.Next(features.Length)];
            }

            return "None";
        }
    }
}
