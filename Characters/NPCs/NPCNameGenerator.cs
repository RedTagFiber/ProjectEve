using ProjectEve.Traits;
using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.NPCs
{
    public static class NPCNameGenerator
    {
        private static readonly Random rng = new();

        // ============================================================
        // MAIN ENTRY — FULL NAME WITH TRAITS
        // ============================================================
        public static string GenerateFullName(string gender, string race, NpcTraits? traits = null)
        {
            string first = GenerateFirstName(gender);
            string middle = GenerateMiddleName(gender);
            string last = GenerateLastName(race);
            string nickname = GenerateNickname(gender, traits);

            if (rng.Next(0, 100) < 20)
                return $"{first} \"{nickname}\" {last}";

            return $"{first} {middle} {last}";
        }

        // ============================================================
        // FIRST NAME
        // ============================================================
        private static string GenerateFirstName(string gender)
        {
            string[] male =
            {
                "James","John","Michael","Robert","David","Daniel","Matthew","Andrew","Joseph","Logan",
                "Ethan","Noah","Liam","Caleb","Wyatt","Hunter","Austin","Luke","Mason","Jacob",
                "Benjamin","Samuel","Gabriel","Elijah","Nathan","Dylan","Carter","Jordan","Tyler","Brandon",
                "Zachary","Jason","Aaron","Christian","Isaac","Evan","Cole","Jesse","Shawn","Trevor",
                "Blake","Grant","Garrett","Chase","Tanner","Travis","Kyle","Ryan","Shane","Derek",
                "Brett","Cody","Spencer","Mitchell","Connor","Gavin","Sawyer","Jaxon","Easton","Brody",
                "Colton","Brayden","Parker","Grayson","Hudson","Nolan","Beau","Wesley","Dean","Clayton",
                "Warren","Marshall","Wayne","Cliff","Riley","Toby","Reid","Miles","Corbin","Kaden",
                "Xavier","Malachi","Preston","Silas","Jude","Rowan","Emmett","Holden","Landon","Elliot",
                "Finn","Calvin","Tate","Wade","Russell","Harvey","Franklin","Leon","Dawson","Casey",
                "Elias","Briggs","Bo","Jasper","Maverick"
            };

            string[] female =
            {
                "Emily","Sarah","Jessica","Hannah","Ashley","Olivia","Sophia","Ava","Isabella","Grace",
                "Chloe","Madison","Abigail","Natalie","Ella","Lily","Hailey","Brooklyn","Savannah","Zoe",
                "Emma","Mia","Amelia","Harper","Evelyn","Aria","Scarlett","Victoria","Penelope","Layla",
                "Nora","Aubrey","Addison","Stella","Paisley","Skylar","Lucy","Anna","Caroline","Kennedy",
                "Allison","Aaliyah","Claire","Sadie","Piper","Ruby","Alice","Jade","Faith","Autumn",
                "Bailey","Reagan","Quinn","Delilah","Mackenzie","Kylie","Lydia","Morgan","Jocelyn","Valerie",
                "Brianna","Madeline","Sienna","Tessa","Riley","Hope","Luna","Emery","Hadley","Marley",
                "Cassidy","Josie","Callie","Daisy","Georgia","Phoebe","Molly","Lacey","Arianna","Camila",
                "Eliza","Willa","June","Hazel","Penny","Rosie","Mabel","Violet","Cora","Lana",
                "Talia","Brynn","Charlee","Ember","Holly","Kendall","Ruth","Nina","Tatum","Joy",
                "Lennon","Aubrielle","Briar","Magnolia"
            };

            return gender == "Male"
                ? male[rng.Next(male.Length)]
                : female[rng.Next(female.Length)];
        }

        // ============================================================
        // MIDDLE NAME
        // ============================================================
        private static string GenerateMiddleName(string gender)
        {
            string[] male =
            {
                "Allen","Lee","Thomas","Ray","Scott","Patrick","Cole","James","Michael","Dean",
                "Alexander","Anthony","Brian","Charles","Christopher","Daniel","Edward","Eric","Frank","George",
                "Henry","Isaac","Jack","Jacob","Jerome","Joel","John","Joseph","Kenneth","Kyle",
                "Lawrence","Louis","Mark","Martin","Nathan","Nicholas","Owen","Paul","Peter","Richard",
                "Robert","Samuel","Stephen","Timothy","Victor","Walter","Wayne","Wesley","Alan","Bruce",
                "Caleb","Dale","Elliot","Graham","Harold","Jeffrey","Leon","Miles","Neil","Oscar",
                "Phillip","Riley","Shawn","Travis","Warren"
            };

            string[] female =
            {
                "Marie","Ann","Lynn","Grace","Rose","Nicole","Elaine","Faith","Jane","Renee",
                "Alice","Amber","Beth","Camille","Dawn","Denise","Diana","Ellen","Faye","Gail",
                "Helen","Hope","Irene","Jean","Joy","June","Kate","Kay","Leigh","Louise",
                "May","Michelle","Naomi","Olivia","Paige","Pearl","Ruth","Sage","Sharon","Sue",
                "Theresa","Valerie","Violet","Wendy","Yvonne","Adele","Brielle","Celeste","Daphne","Estelle",
                "Frances","Harper","Isabel","Joan","Lydia","Mabel","Nadine","Opal","Quinn","Selene"
            };

            return gender == "Male"
                ? male[rng.Next(male.Length)]
                : female[rng.Next(female.Length)];
        }

        // ============================================================
        // LAST NAME
        // ============================================================
        private static string GenerateLastName(string race)
        {
            List<string> names = new();

            switch (race)
            {
                case "European":
                    names.AddRange(new[]
                    {
                        "Smith","Johnson","Miller","Davis","Brown","Wilson","Taylor","Anderson","Clark","Walker",
                        "Thompson","Moore","Hall","Allen","Young","King","Wright","Hill","Green","Adams",
                        "Baker","Nelson","Carter","Mitchell","Roberts","Turner","Phillips","Campbell","Parker","Evans",
                        "Edwards","Collins","Stewart","Morris","Rogers","Reed","Cook","Morgan","Bell","Murphy",
                        "Bailey","Cooper","Richardson","Cox","Howard"
                    });
                    break;

                case "African":
                    names.AddRange(new[]
                    {
                        "Jackson","Harris","Robinson","Thompson","Lewis","Young","Allen","King","Scott","Green",
                        "Walker","Wright","Hill","Mitchell","Taylor","Brown","Davis","Clark","Moore","Hall",
                        "Anderson","Thomas","White","Brooks","Edwards","Parker","Evans","Collins","Stewart","Morris"
                    });
                    break;

                case "Latino":
                    names.AddRange(new[]
                    {
                        "Garcia","Martinez","Rodriguez","Lopez","Hernandez","Sanchez","Ramirez","Torres",
                        "Gonzalez","Perez","Castro","Vargas","Morales","Reyes","Cruz","Flores","Diaz","Ramos",
                        "Navarro","Soto","Mendoza","Silva","Delgado"
                    });
                    break;

                case "Asian":
                    names.AddRange(new[]
                    {
                        "Kim","Lee","Park","Chen","Wong","Tanaka","Sato","Yamamoto",
                        "Choi","Lim","Han","Zhang","Li","Khan","Singh","Patel","Gupta","Sharma",
                        "Ito","Suzuki","Kato","Nakamura","Fujita"
                    });
                    break;

                case "Middle Eastern":
                    names.AddRange(new[]
                    {
                        "Haddad","Karim","Nassar","Saleh","Farouk","Aziz","Rahman",
                        "Khalil","Yasin","Barakat","Saad","Hakim","Zara","Hussein","Ismail","Tariq","Basir"
                    });
                    break;

                case "Pacific Islander":
                    names.AddRange(new[]
                    {
                        "Kaimana","Kealoha","Manu","Loto","Fale","Tupu","Matai",
                        "Alofa","Siva","Peni","Tama","Kele","Malu","Sione","Leka","Nalu","Hana"
                    });
                    break;

                case "Mixed":
                default:
                    names.AddRange(new[]
                    {
                        "Reed","Gray","Brooks","Cole","Diaz","Nguyen","Patel","Santos",
                        "Rivera","King","Bennett","Hayes","Price","Foster","Grant","Stone","West","Lane",
                        "Ford","Hart","Cross","Shaw","Wells"
                    });
                    break;
            }

            if (names.Count > 1 && rng.Next(0, 100) < 3)
            {
                string n1 = names[rng.Next(names.Count)];
                string n2 = names[rng.Next(names.Count)];
                return $"{n1}-{n2}";
            }

            return names[rng.Next(names.Count)];
        }

        // ============================================================
        // NORMAL NICKNAME
        // ============================================================
        public static string GenerateNickname(string gender, NpcTraits? traits = null)
        {
            var brave = new[] { "Ace", "Chief", "Ranger", "Hawk", "Maverick", "Blaze", "Iron", "Titan", "Valor", "Grit" };
            var aggressive = new[] { "Bull", "Gator", "Spike", "Rex", "Crusher", "Knuckles", "Bruiser", "Fang", "Viper", "Tank" };
            var kind = new[] { "Sunny", "Honey", "Angel", "Peaches", "Dove", "Blossom", "Smiley", "Sweetie", "Breeze", "Hope" };
            var shy = new[] { "Whisper", "Mouse", "Shadow", "Quiet", "Softie", "Flicker", "Shade", "Moth", "Pebble", "Tiny" };
            var funny = new[] { "Goofy", "Joker", "Bubbles", "Giggles", "Snickers", "Wiggles", "Zippy", "Scooter", "Pickles", "Noodles" };
            var smart = new[] { "Doc", "Professor", "Brain", "Sage", "Logic", "Wizard", "Cipher", "Thinker", "Scholar", "Byte" };
            var athletic = new[] { "Dash", "Runner", "Rocket", "Flex", "Turbo", "Striker", "Sprint", "Hammer", "Powerhouse", "Bolt" };
            var troublemaker = new[] { "Slick", "Rowdy", "Rascal", "Bandit", "Wildcard", "Chaos", "Spook", "Trickster", "Jinx", "Rebel" };
            var calm = new[] { "Chill", "Zen", "Still", "Cloud", "Drift", "River", "Calm", "Mellow", "Shade", "Peace" };

            if (traits != null)
            {
                if (traits.Get("trait.confidence") >= 70 || traits.Get("trait.challengeSeeking") >= 70)
                    return brave[rng.Next(brave.Length)];
                if (traits.Get("trait.anger") >= 70 || traits.Get("trait.confrontationTendency") >= 70)
                    return aggressive[rng.Next(aggressive.Length)];
                if (traits.Get("trait.empathy") >= 70 || traits.Get("trait.selfCompassion") >= 70)
                    return kind[rng.Next(kind.Length)];
                if (traits.Get("trait.introversion") >= 70 || traits.Get("trait.socialAnxiety") >= 70)
                    return shy[rng.Next(shy.Length)];
                if (traits.Get("trait.humor") >= 70 || traits.Get("trait.sarcasm") >= 70)
                    return funny[rng.Next(funny.Length)];
                if (traits.Get("trait.logic") >= 70 || traits.Get("trait.creativity") >= 70)
                    return smart[rng.Next(smart.Length)];
                if (traits.Get("trait.physicalStrength") >= 70 || traits.Get("trait.exerciseHabit") >= 70)
                    return athletic[rng.Next(athletic.Length)];
                if (traits.Get("trait.rebellion") >= 70 || traits.Get("trait.impulsiveness") >= 70)
                    return troublemaker[rng.Next(troublemaker.Length)];
                if (traits.Get("trait.stoicism") >= 70 || traits.Get("trait.moodStability") >= 70)
                    return calm[rng.Next(calm.Length)];
            }

            string[] maleFallback = { "Buddy", "Duke", "Rocky", "Moose", "Bear", "Lucky", "Rusty", "Boomer", "Rowdy", "Wally" };
            string[] femaleFallback = { "Lulu", "Kitty", "Roxy", "Dolly", "Sunny", "Star", "Cherry", "Goldie", "Pixie", "Bambi" };

            return gender == "Male"
                ? maleFallback[rng.Next(maleFallback.Length)]
                : femaleFallback[rng.Next(femaleFallback.Length)];
        }

        // ============================================================
        // DIRTY / SEXUAL NAMES  (large pool for mood / arousal system)
        // ============================================================
        public static string GenerateDirtyName(string gender, NpcTraits? traits = null)
        {
            string[] femaleDirty =
            {
                "slut","whore","good girl","dirty girl","needy slut","cock whore","fucktoy","cumslut",
                "pet","kitten","doll","toy","princess","baby girl","little slut","filthy girl",
                "open slut","easy slut","hungry slut","wet slut","used slut","owned slut","pathetic slut",
                "mouth slut","throat slut","bed slut","night slut","secret slut","cheap slut","pretty slut",
                "needy whore","loyal whore","personal whore","house whore","corner whore","pretty whore",
                "fuckdoll","cumdump","seed slut","breeding slut","freeuse slut","public slut","quiet slut",
                "moaning slut","begging slut","ruined slut","broken slut","marked slut","claimed slut",
                "daddy's girl","good little slut","filthy little thing","needy little hole","pretty little toy",
                "sweet slut","soft slut","eager slut","shameless slut","cock-drunk slut","used-up slut",
                "wet little whore","loyal little pet","dirty little secret","favorite slut","personal toy"
            };

            string[] maleDirty =
            {
                "good boy","dirty boy","needy boy","cock slut","fucktoy","pet","doll","toy",
                "slut","whore","eager slut","mouth slut","throat slut","owned slut","used slut",
                "pathetic slut","loyal slut","personal slut","bedroom slut","secret slut","filthy boy",
                "desperate slut","ruined slut","broken slut","marked slut","claimed slut","fuckdoll",
                "cumdump","breeding slut","freeuse slut","public slut","quiet slut","moaning slut",
                "begging slut","daddy's boy","good little slut","filthy little thing","pretty little toy",
                "sweet slut","soft slut","eager slut","shameless slut","cock-hungry slut","used-up slut",
                "loyal little pet","dirty little secret","favorite slut","personal toy","needy hole"
            };

            string[] sharedDirty =
            {
                "slut","whore","fucktoy","cumslut","pet","toy","doll","needy slut","owned slut",
                "used slut","good little slut","filthy thing","personal whore","secret slut","freeuse slut",
                "begging slut","ruined slut","claimed slut","marked slut","cock-drunk","seeded slut"
            };

            // Bias by traits if present
            if (traits != null)
            {
                if (traits.Get("trait.degradationDesire") >= 70 || traits.Get("trait.objectificationDesire") >= 70)
                {
                    var harsh = new[]
                    {
                        "slut","whore","cumslut","fucktoy","cumdump","used slut","pathetic slut",
                        "freeuse slut","ruined slut","seed slut","cock whore","dirty little hole"
                    };
                    return harsh[rng.Next(harsh.Length)];
                }

                if (traits.Get("trait.praiseKink") >= 70 || traits.Get("trait.submission") >= 70)
                {
                    var soft = new[]
                    {
                        "good girl","good boy","pet","kitten","princess","baby","doll","good little slut",
                        "loyal pet","sweet slut","pretty toy","favorite"
                    };
                    return soft[rng.Next(soft.Length)];
                }
            }

            if (gender == "Male")
                return maleDirty[rng.Next(maleDirty.Length)];
            if (gender == "Female")
                return femaleDirty[rng.Next(femaleDirty.Length)];

            return sharedDirty[rng.Next(sharedDirty.Length)];
        }

        // ============================================================
        // DARK NAMES  (power, coldness, danger, control)
        // ============================================================
        public static string GenerateDarkName(string gender, NpcTraits? traits = null)
        {
            string[] darkNames =
            {
                "Ghost","Venom","Razor","Hollow","Shade","Sable","Ash","Noir","Vex","Ruin",
                "Crow","Thorn","Wraith","Spite","Cold","Null","Hush","Bleak","Widow","Viper",
                "Sorrow","Grave","Static","Echo","Hex","Omen","Raven","Dusk","Frost","Steel",
                "Spite","Bane","Cinder","Marrow","Scar","Nails","Hook","Quiet","Still","Blank",
                "Sever","Knot","Chain","Lock","Brand","Mark","Claim","Own","Keep","Cage",
                "Doll","Pet","Thing","Object","Property","Hole","Mouth","Body","Soft","Open",
                "Used","Ruined","Broken","Marked","Owned","Claimed","Kept","Leashed","Bound","Taken"
            };

            string[] coldIntimate =
            {
                "mine","property","thing","object","pet","doll","owned","claimed","kept","used",
                "ruined","marked","open","soft","quiet","still","bound","leashed","taken","held"
            };

            if (traits != null)
            {
                if (traits.Get("trait.possessiveness") >= 70 || traits.Get("trait.ownershipDesire") >= 70)
                    return coldIntimate[rng.Next(coldIntimate.Length)];

                if (traits.Get("trait.callousness") >= 70 || traits.Get("trait.cruelty") >= 70)
                {
                    var cruel = new[] { "thing", "object", "hole", "used", "ruined", "broken", "property", "doll" };
                    return cruel[rng.Next(cruel.Length)];
                }
            }

            return darkNames[rng.Next(darkNames.Length)];
        }

        // ============================================================
        // REACTION HELPER — for SQLite / mood / arousal system
        // Returns a score from -10 to +10
        // Positive = they liked being called that
        // Negative = they disliked it
        // ============================================================
        public static int GetNameReactionScore(string usedName, NpcTraits traits)
        {
            if (traits == null || string.IsNullOrWhiteSpace(usedName))
                return 0;

            string name = usedName.Trim().ToLowerInvariant();
            int score = 0;

            // Dirty-word detection
            bool isDirty =
                name.Contains("slut") || name.Contains("whore") || name.Contains("fucktoy") ||
                name.Contains("cum") || name.Contains("toy") || name.Contains("pet") ||
                name.Contains("doll") || name.Contains("hole") || name.Contains("dump") ||
                name.Contains("freeuse") || name.Contains("seed") || name.Contains("used");

            bool isSoftDirty =
                name.Contains("good girl") || name.Contains("good boy") || name.Contains("princess") ||
                name.Contains("baby") || name.Contains("kitten") || name.Contains("angel");

            bool isDark =
                name.Contains("mine") || name.Contains("property") || name.Contains("object") ||
                name.Contains("owned") || name.Contains("claimed") || name.Contains("ruined") ||
                name.Contains("broken") || name.Contains("thing");

            float degradation = traits.Get("trait.degradationDesire");
            float objectification = traits.Get("trait.objectificationDesire");
            float praise = traits.Get("trait.praiseKink");
            float submission = traits.Get("trait.submission");
            float ownership = traits.Get("trait.ownershipDesire");
            float shame = traits.Get("trait.sexualShame");
            float libido = traits.Get("trait.libido");

            if (isDirty)
            {
                if (degradation >= 60) score += 4;
                if (objectification >= 60) score += 3;
                if (submission >= 60) score += 2;
                if (shame >= 60) score -= 4; // conflicted / disliked
                if (degradation <= 30) score -= 3;
            }

            if (isSoftDirty)
            {
                if (praise >= 60) score += 4;
                if (submission >= 50) score += 2;
                if (degradation >= 80) score -= 1; // may want harsher
            }

            if (isDark)
            {
                if (ownership >= 60) score += 4;
                if (objectification >= 60) score += 3;
                if (degradation >= 60) score += 2;
                if (ownership <= 30 && objectification <= 30) score -= 3;
            }

            // High libido slightly boosts positive reaction to sexual naming
            if (libido >= 70 && score > 0)
                score += 1;

            return Math.Clamp(score, -10, 10);
        }

        // ============================================================
        // APPLY REACTION TO TRAITS / MOOD
        // Call this when someone uses a dirty or dark name on an NPC
        // ============================================================
        public static void ApplyNameReaction(NpcTraits traits, string usedName)
        {
            if (traits == null || string.IsNullOrWhiteSpace(usedName))
                return;

            int score = GetNameReactionScore(usedName, traits);

            if (score >= 3)
            {
                // Liked it — get more aroused / open
                traits.Adjust("trait.libido", +2);
                traits.Adjust("trait.sexualConfidence", +1);
                traits.Adjust("trait.degradationDesire", +1);
            }
            else if (score <= -3)
            {
                // Disliked it — pull back
                traits.Adjust("trait.libido", -2);
                traits.Adjust("trait.sexualShame", +2);
                traits.Adjust("trait.anxiety", +1);
            }
            else if (score > 0)
            {
                traits.Adjust("trait.libido", +1);
            }
            else if (score < 0)
            {
                traits.Adjust("trait.sexualShame", +1);
            }
        }
    }
}