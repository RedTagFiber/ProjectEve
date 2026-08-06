using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Narrative.Descriptions
{
    /// <summary>
    /// Single description pack with wide variety so scenes don't repeat.
    /// Class name is SceneText on purpose so it does not clash with the namespace name.
    /// </summary>
    public static class SceneText
    {
        private static readonly Random Rng = new();

        // =====================================================
        // WEATHER
        // =====================================================
        public static string Weather(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["rain"] = new[]
                {
                    "Rain taps the glass in uneven rhythms.",
                    "A steady rain darkens the street and softens tire noise.",
                    "Cold rain needles the sidewalk and clings to coats.",
                    "Rain comes sideways for a minute, then settles.",
                    "Wet streaks run down the windows in thin lines."
                },
                ["rainy"] = null!,
                ["storm"] = new[]
                {
                    "Thunder sits far off while the rain gets heavier.",
                    "The storm makes the lights feel smaller.",
                    "Wind and rain hit together in short bursts."
                },
                ["snow"] = new[]
                {
                    "Snow drifts quiet and sticks to every edge.",
                    "Fresh snow mutes the town into softer sound.",
                    "Dry cold snow scrapes under boots.",
                    "A thin snow dusts cars and railings."
                },
                ["sunny"] = new[]
                {
                    "Hard sunlight bleaches the street and makes shade feel good.",
                    "Bright sun sits on skin and metal the same way.",
                    "Clear sun throws sharp shadows under tables and signs.",
                    "Warm light climbs the walls and stays there."
                },
                ["overcast"] = new[]
                {
                    "A flat gray sky flattens color out of everything.",
                    "Cloud cover makes noon look like late afternoon.",
                    "No direct sun, just even dull light."
                },
                ["misty"] = new[]
                {
                    "Mist softens distance until the far end of the street fades.",
                    "A thin fog hangs low and wet.",
                    "Moisture beads on railings and hair."
                },
                ["windy"] = new[]
                {
                    "Wind keeps shoving at jackets and loose hair.",
                    "A restless wind carries paper and cold in equal measure.",
                    "Gusts slap the door each time it opens."
                },
                ["humid"] = new[]
                {
                    "Humidity sticks clothes to skin.",
                    "The air feels thick enough to chew.",
                    "Sweat shows faster than it should."
                },
                ["cold"] = new[]
                {
                    "Cold bites fingers and makes breath visible.",
                    "The kind of cold that wakes people up mean.",
                    "Chill seeps in through seams and gaps."
                },
                ["hot"] = new[]
                {
                    "Heat sits heavy and slows every movement.",
                    "Hot air rises off asphalt in weak waves.",
                    "Everything feels slightly too close in the heat."
                },
                ["night"] = new[]
                {
                    "Night air cools the edges of the day.",
                    "The dark makes ordinary sounds carry farther.",
                    "Streetlights do half the work visibility needs."
                }
            }, fallback: new[]
            {
                "The weather is ordinary and easy to ignore.",
                "Nothing dramatic in the sky, just a usable day.",
                "The air feels normal for this part of Ohio."
            });
        }

        // =====================================================
        // SMELL
        // =====================================================
        public static string Smell(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["coffee"] = new[]
                {
                    "Fresh espresso cuts through the room first.",
                    "Coffee oils and steamed milk hang warm in the air.",
                    "Burnt-sweet coffee residue lives in the walls by now.",
                    "A new pot opens and the whole counter changes smell.",
                    "Bitter coffee and sugar packets share the same breath of air."
                },
                ["espresso"] = null!,
                ["pastry"] = new[]
                {
                    "Butter and sugar rise off the pastry case.",
                    "Warm dough and cinnamon fight the coffee for attention.",
                    "Cooling glaze makes the air smell sweeter near the counter."
                },
                ["vanilla"] = new[]
                {
                    "Vanilla sits soft under the stronger smells.",
                    "A clean vanilla note stays close to skin and sleeves."
                },
                ["rain"] = new[]
                {
                    "Wet pavement smell rides in on coats.",
                    "Rain brings mineral damp and cold fabric.",
                    "The room picks up outdoor wet every time the door opens."
                },
                ["old wood"] = new[]
                {
                    "Old wood and dust season the corners.",
                    "Dry timber smell lives in the baseboards."
                },
                ["lotion"] = new[]
                {
                    "Lotion stays close, clean and personal.",
                    "A skin-warm lotion note shows only at short range."
                },
                ["shampoo"] = new[]
                {
                    "Shampoo still sits in damp hair.",
                    "Clean hair products cut through heavier room smells."
                },
                ["beer"] = new[]
                {
                    "Beer and citrus cleaner share the same air.",
                    "Bar smell: hops, old syrup, cold glass.",
                    "A yeasty beer note hangs near the taps."
                },
                ["bar"] = null!,
                ["smoke"] = new[]
                {
                    "A thin smoke ghost clings to jackets.",
                    "Old smoke lives in fabric more than air."
                },
                ["sex"] = new[]
                {
                    "Warm skin and used sheets still mark the air.",
                    "The room smells like bodies were here and didn't hide it.",
                    "Sweat, skin, and laundry waiting all sit together."
                },
                ["skin"] = null!,
                ["sweat"] = new[]
                {
                    "Clean sweat, sharp and human.",
                    "A worked-up body smell stays after movement."
                },
                ["food"] = new[]
                {
                    "Grease and salt ride the air from somewhere nearby.",
                    "Cooked food smell makes the room feel occupied."
                },
                ["pizza"] = new[]
                {
                    "Yeast, cheese, and box cardboard arrive together.",
                    "Pizza oil hangs longer than it should."
                },
                ["bleach"] = new[]
                {
                    "Bleach tries to win against everything else.",
                    "Cleaning chemicals leave a hard bright edge in the nose."
                },
                ["perfume"] = new[]
                {
                    "Perfume shows in a narrow trail, then fades.",
                    "A deliberate scent sits on pulse points."
                },
                ["cologne"] = new[]
                {
                    "Cologne is stronger up close than across the room.",
                    "A short cologne note arrives and leaves."
                },
                ["car"] = new[]
                {
                    "Car interior: vinyl, dust, and outside air mixed wrong.",
                    "Heater-dust smell from a vehicle still hangs on clothes."
                },
                ["night air"] = new[]
                {
                    "Cool night air smells emptier and cleaner.",
                    "Outside night brings less sweetness, more metal and wet."
                },
                ["garbage"] = new[]
                {
                    "A brief bad note from the alley side of the building.",
                    "Trash smell appears and disappears with the wind."
                }
            }, fallback: new[]
            {
                "The air smells familiar, nothing sharp.",
                "No one smell owns the room.",
                "A mixed ordinary indoor scent sits in the background."
            });
        }

        // =====================================================
        // LIGHTING
        // =====================================================
        public static string Lighting(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["bright"] = new[]
                {
                    "Bright light leaves little privacy in faces.",
                    "Overhead brightness flattens shadows."
                },
                ["warm"] = new[]
                {
                    "Warm light pools on tables and cheekbones.",
                    "Amber light makes skin look closer."
                },
                ["soft"] = new[]
                {
                    "Soft light rounds edges and hides flaws.",
                    "Nothing is harsh; detail comes in slowly."
                },
                ["dim"] = new[]
                {
                    "Dim light pushes the corners away.",
                    "You see outlines first, detail second."
                },
                ["dark"] = new[]
                {
                    "Most of the room is dark on purpose.",
                    "Only the nearest surfaces hold light."
                },
                ["neon"] = new[]
                {
                    "Neon paints color across glass and skin.",
                    "Pink-blue light makes eyes look wetter."
                },
                ["morning"] = new[]
                {
                    "Morning light is honest and a little cruel.",
                    "Early sun finds dust in the air."
                },
                ["evening"] = new[]
                {
                    "Evening light turns thick and gold.",
                    "Late light stretches shadows long."
                },
                ["night"] = new[]
                {
                    "Night lighting is practical, not pretty.",
                    "Lamps do isolated work in a dark room."
                }
            }, fallback: new[]
            {
                "The lighting is plain and usable.",
                "Nothing dramatic about the light, just enough to see."
            });
        }

        // =====================================================
        // CROWD
        // =====================================================
        public static string Crowd(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["empty"] = new[]
                {
                    "Empty enough that footsteps matter.",
                    "No audience. Just the room."
                },
                ["quiet"] = new[]
                {
                    "A few people, low voices, long gaps.",
                    "Quiet enough to overhear without trying."
                },
                ["half-full"] = new[]
                {
                    "Occupied without pressure.",
                    "Enough bodies to feel public, not crowded."
                },
                ["busy"] = new[]
                {
                    "Constant movement between door and counter.",
                    "Talk overlaps; nobody gets a clean silence."
                },
                ["packed"] = new[]
                {
                    "Shoulders and elbows own the space.",
                    "Personal space becomes theoretical."
                },
                ["lively"] = new[]
                {
                    "Laughs land easy and often.",
                    "The room has energy with nowhere to put it."
                }
            }, fallback: new[]
            {
                "A normal number of people occupy the space.",
                "Neither empty nor overwhelming."
            });
        }

        // =====================================================
        // BUILDING / PLACE
        // =====================================================
        public static string Building(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["coffee"] = new[]
                {
                    "Counter wear and steam paths mark a working coffee shop.",
                    "The shop feels used in the right places.",
                    "Pastry case light and machine noise define the room."
                },
                ["cafe"] = null!,
                ["coffee shop"] = null!,
                ["apartment"] = new[]
                {
                    "Small rooms, personal mess, lived-in corners.",
                    "An apartment that shows habits more than design."
                },
                ["house"] = new[]
                {
                    "Ordinary house quiet, private and closed to the street.",
                    "Rooms that know daily routines."
                },
                ["bar"] = new[]
                {
                    "Sticky-clean surfaces and old neon honesty.",
                    "A bar that doesn't pretend to be new."
                },
                ["modern"] = new[] { "Hard surfaces, clean lines, little softness." },
                ["rustic"] = new[] { "Wood and older finishes make the room feel heavier." },
                ["cozy"] = new[] { "Close walls, warm corners, nowhere to get lost." }
            }, fallback: new[]
            {
                "The place is ordinary and easy to place.",
                "Nothing landmark about the building, just local."
            });
        }

        // =====================================================
        // CLOTHING
        // =====================================================
        public static string Clothing(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["apron"] = new[]
                {
                    "Apron on, shift written into the clothes.",
                    "Work layers and an apron that has seen steam."
                },
                ["work"] = null!,
                ["hoodie"] = new[]
                {
                    "A hoodie softens her outline.",
                    "Hoodie comfort, sleeves half-useful."
                },
                ["casual"] = new[]
                {
                    "Casual clothes, no performance in them.",
                    "Easy clothes chosen for movement."
                },
                ["sundress"] = new[]
                {
                    "A sundress leaves more skin in the air.",
                    "Light dress fabric moves when she does."
                },
                ["dress"] = null!,
                ["bar"] = new[]
                {
                    "A bar outfit meant to be noticed without asking.",
                    "Clothes chosen for night eyes."
                },
                ["slut"] = new[]
                {
                    "Deliberately revealing, no confusion about intent.",
                    "An outfit that skips subtlety on purpose."
                },
                ["club"] = null!,
                ["revealing"] = null!,
                ["cozy"] = new[] { "Soft layers held close to the body." },
                ["formal"] = new[] { "Neater clothes, put-together on purpose." },
                ["nothing"] = new[] { "No clothes. Skin and temperature only." },
                ["nude"] = null!,
                ["shirt only"] = new[] { "Just a shirt, barely doing coverage work." }
            }, fallback: new[]
            {
                "Clothes fit the moment without explanation.",
                "Nothing costume-like, just what she has on."
            });
        }

        // =====================================================
        // HAIR
        // =====================================================
        public static string Hair(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["loose"] = new[]
                {
                    "Hair falls loose around her face.",
                    "Loose hair keeps moving after she stops."
                },
                ["down"] = null!,
                ["tied"] = new[]
                {
                    "Hair pulled back, practical, a few strands free.",
                    "Tied hair shows more of her face than she sometimes wants."
                },
                ["up"] = null!,
                ["messy"] = new[]
                {
                    "Messy hair from weather, work, or hands.",
                    "Unfixed hair that tells on the last hour."
                },
                ["styled"] = new[] { "Hair done with intention." },
                ["wet"] = new[] { "Damp hair darker at the ends." },
                ["long"] = new[] { "Long hair announces every turn." },
                ["short"] = new[] { "Short hair keeps the face open." }
            }, fallback: new[]
            {
                "Hair sits natural.",
                "Nothing forced about her hair right now."
            });
        }

        // =====================================================
        // EXPRESSION
        // =====================================================
        public static string Expression(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["smile"] = new[] { "A real smile breaks through.", "Smile first, eyes second." },
                ["happy"] = null!,
                ["warm"] = new[] { "Warmth softens her whole face." },
                ["neutral"] = new[] { "Even face, little give.", "Neutral in a way that still watches." },
                ["calm"] = null!,
                ["tired"] = new[] { "Tiredness sits under the eyes.", "A worn look she doesn't fully hide." },
                ["sad"] = new[] { "Sadness shows without theater." },
                ["lonely"] = new[] { "Something withdrawn sits in her look." },
                ["anxious"] = new[] { "Tension shows in mouth and shoulders." },
                ["nervous"] = null!,
                ["embarrassed"] = new[] { "She looks away a fraction too late." },
                ["ashamed"] = null!,
                ["angry"] = new[] { "Anger sharpens everything." },
                ["irritated"] = new[] { "Irritation tightens her mouth." },
                ["jealous"] = new[] { "Jealousy hardens the eyes." },
                ["tempted"] = new[] { "Want and restraint share the same face." },
                ["horny"] = new[] { "Desire is open and unhidden.", "No confusion about what she wants." },
                ["playful"] = new[] { "A mischievous edge sits on her mouth." },
                ["smug"] = new[] { "She looks like she got away with something." },
                ["spiteful"] = new[] { "Cold on purpose." },
                ["predatory"] = new[] { "She watches like the decision is already made." },
                ["guilty"] = new[] { "Guilt pulls her expression inward." },
                ["focused"] = new[] { "Attention locks and stays." },
                ["numb"] = new[] { "Almost nothing shows." }
            }, fallback: new[]
            {
                "Her expression holds between meanings.",
                "Hard to label, easy to feel."
            });
        }

        // =====================================================
        // POSTURE
        // =====================================================
        public static string Posture(string key)
        {
            return Pick(Norm(key), new Dictionary<string, string[]>
            {
                ["standing"] = new[] { "She stands ready, weight even." },
                ["leaning"] = new[] { "She leans in like distance was the problem." },
                ["sitting"] = new[] { "She sits angled toward the other person." },
                ["crossed arms"] = new[] { "Arms folded, closed or self-contained." },
                ["open"] = new[] { "Open posture, unguarded shoulders." },
                ["close"] = new[] { "Inside personal space on purpose." },
                ["restless"] = new[] { "She shifts and never fully settles." },
                ["tired"] = new[] { "Posture drops with fatigue." }
            }, fallback: new[]
            {
                "Her posture matches the room.",
                "Body language stays simple and readable."
            });
        }

        // =====================================================
        // COMPOSITES
        // =====================================================
        public static string Appearance(string clothing, string hair, string expression)
            => $"{Clothing(clothing)} {Hair(hair)} {Expression(expression)}".Trim();

        public static string Scene(string building, string lighting, string smell, string crowd, string weather = "")
        {
            var parts = new List<string>
            {
                Building(building),
                Lighting(lighting),
                Smell(smell),
                Crowd(crowd)
            };
            if (!string.IsNullOrWhiteSpace(weather))
                parts.Add(Weather(weather));
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private static string Norm(string? key) => (key ?? "").Trim().ToLowerInvariant();

        private static string Pick(string key, Dictionary<string, string[]> map, string[] fallback)
        {
            map["rainy"] = map.ContainsKey("rain") ? map["rain"] : fallback;
            map["espresso"] = map.ContainsKey("coffee") ? map["coffee"] : fallback;
            map["cafe"] = map.ContainsKey("coffee") ? map["coffee"] : fallback;
            map["coffee shop"] = map.ContainsKey("coffee") ? map["coffee"] : fallback;
            map["bar"] = map.ContainsKey("beer") ? map["beer"] : (map.ContainsKey("bar") ? map["bar"] : fallback);
            map["skin"] = map.ContainsKey("sex") ? map["sex"] : fallback;
            map["work"] = map.ContainsKey("apron") ? map["apron"] : fallback;
            map["dress"] = map.ContainsKey("sundress") ? map["sundress"] : fallback;
            map["club"] = map.ContainsKey("slut") ? map["slut"] : fallback;
            map["revealing"] = map.ContainsKey("slut") ? map["slut"] : fallback;
            map["down"] = map.ContainsKey("loose") ? map["loose"] : fallback;
            map["up"] = map.ContainsKey("tied") ? map["tied"] : fallback;
            map["happy"] = map.ContainsKey("smile") ? map["smile"] : fallback;
            map["calm"] = map.ContainsKey("neutral") ? map["neutral"] : fallback;
            map["nervous"] = map.ContainsKey("anxious") ? map["anxious"] : fallback;
            map["ashamed"] = map.ContainsKey("embarrassed") ? map["embarrassed"] : fallback;
            map["nude"] = map.ContainsKey("nothing") ? map["nothing"] : fallback;

            if (map.TryGetValue(key, out var arr) && arr != null && arr.Length > 0)
                return arr[Rng.Next(arr.Length)];

            return fallback[Rng.Next(fallback.Length)];
        }
    }
}