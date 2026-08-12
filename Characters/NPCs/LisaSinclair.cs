using System;
using System.Collections.Generic;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Money;
using ProjectEve.Relationships;
using ProjectEve.Traits;

namespace ProjectEve.Characters.NPCs
{
    /// <summary>
    /// Lisa Sinclair — mom, owns the coffee shop, HS sweetheart with Edward.
    /// Started Sunday dinner when Adam moved out. All-in on family sports and BBQ.
    /// </summary>
    public class Lisa : SimCharacter
    {
        public Lisa() : base("Lisa", 44)
        {
            Id = 3;
            Gender = "Female";
            Occupation = "Coffee shop owner";
            Location = "Bellefontaine / Sidney, Ohio area";
            Hometown = "Bellefontaine, OH";
            HomeAddress = "Sinclair family home (in town, with Edward)";

            BirthDate = new DateTime(1982, 6, 8, 10, 0, 0); // ~19 when twins born 2001
            Zodiac = "Gemini";

            PersonalityContext =
                "High school sweetheart with Edward; married young; had the twins at nineteen. " +
                "Built and still runs the coffee shop — Eve manages under her, which is love and hierarchy in the same building. " +
                "Started Sunday dinner when Adam moved out so the family would still share one meal; now nobody misses it. " +
                "BBQ and game watch year-round when they can — OSU, Bengals, Reds — friends and family welcome. " +
                "Shuts down the false rumor about Adam and Eve hard. Does not know the player.";

            Money.Cash = 180m;
            Money.Bank = 24000m;
            Money.Debt = 8000m;
            try
            {
                Money.Bills = 0.40m;
                Money.Food = 0.20m;
                Money.Entertainment = 0.10m;
                Money.SavingsRate = 0.12m;
                Money.HobbySpending = 0.08m;
            }
            catch { }

            Job = new JobProfile
            {
                JobName = "Owner",
                Employer = "Sinclair Coffee (self)",
                JobType = "full_time",
                IndustryPath = "service",
                Department = "Owner",
                TitleLevel = "owner",

                StartHour = 5,
                EndHour = 15,
                ShiftType = "days",
                WorkLocationMode = "office",
                CommuteMinutesOneWay = 5,
                WorkDays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" },

                HourlyRate = 0m,
                WeeklyHours = 50,
                IsSalaried = true,

                MonthlyBillsHint = 3500m,
                SavingsRateHint = 0.12m,

                HasInsurance = true,
                HasPaidTimeOff = false,
                VacationDaysPerYear = 5,
                VacationDaysUsed = 1,
                SickDaysPerYear = 4,

                StressLoad = 60,
                SocialDemand = 85,
                PhysicalDemand = 45,
                CognitiveDemand = 70,
                BurnoutAccum = 40,

                HireDate = DateTime.Now.AddYears(-18),
                DaysWorked = 5000,

                BossName = "Self",
                BossRelationship = "owner",
                TeamClimate = "family business — high standards"
            };

            Goal = "Keep the shop standing and the family at the same table";
            Need = "To see her kids safe, present, and not drifting into silence";
            Fear = "The family scattering for real — or the shop failing after all these years";
            Want = "Sundays full, kids honest enough, Edward home when he can be";

            Relationships.Clear();
            Relationships.Add(new Relationship
            {
                TargetName = "Edward",
                Trust = 92,
                Respect = 90,
                Affection = 88,
                Attraction = 70,
                Tension = 22
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Adam",
                Trust = 90,
                Respect = 88,
                Affection = 94,
                Attraction = 0,
                Tension = 20
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Eve",
                Trust = 85,
                Respect = 86,
                Affection = 92,
                Attraction = 0,
                Tension = 36
            });

            var fast = TraitJsonLoader.BuildFastDefaults(48f);
            fast["trait.anger"] = 32;
            fast["trait.anxiety"] = 38;
            fast["trait.fear"] = 30;
            fast["trait.shame"] = 20;
            fast["trait.guilt"] = 28;
            fast["trait.hurt"] = 25;
            fast["trait.jealousy"] = 22;
            fast["trait.resentment"] = 18;
            fast["trait.trust"] = 72;
            fast["trait.affection"] = 80;
            fast["trait.desire"] = 45;
            fast["trait.attraction"] = 48;
            fast["trait.tension"] = 35;
            fast["trait.playfulness"] = 45;
            fast["trait.pride"] = 65;
            fast["trait.patience"] = 58;
            fast["trait.guard"] = 48;
            fast["trait.openness"] = 55;
            fast["trait.loneliness"] = 25;
            fast["trait.hope"] = 62;

            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["mid.responsible"] = 90,
                ["mid.ambitious"] = 78,
                ["mid.warm"] = 80,
                ["mid.exacting"] = 82,
                ["mid.private"] = 55,
                ["mid.loyal"] = 92,
                ["mid.people_pleasing"] = 35,
                ["mid.host"] = 85
            };

            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["slow.music"] = 40,
                ["slow.movies"] = 45,
                ["slow.tv"] = 50,
                ["slow.sports"] = 70,
                ["slow.sports.osu"] = 75,
                ["slow.sports.bengals"] = 72,
                ["slow.sports.reds"] = 70,
                ["slow.life.work_ambition"] = 88,
                ["slow.life.foodie"] = 70,
                ["slow.life.fitness"] = 42,
                ["slow.life.host"] = 90
            };
            Traits.InitializeFromLayers(fast, mid, slow);

            try
            {
                Traits.SetStyle("trait.pride", "steady");
                Traits.SetStyle("trait.anger", "clean_cut");
                Traits.SetStyle("trait.patience", "counter_long");
            }
            catch { }

            try { Emotion.SyncFromFast(Traits); } catch { }

            // ---- Edward / marriage ----
            Remember("She and Edward were high school sweethearts, married young, and had Adam and Eve at nineteen.", "Family", 9);
            Remember("Edward still looks like the boy she married when the stress is off him. She does not take that for granted.", "Family", 5);
            Remember("Young marriage meant learning money, shifts, and babies before they had a full map. They stayed anyway.", "Family", 7);
            Remember("She has seen Edward carry call weight home without naming it. She sets a plate and does not force a debrief.", "Family", 6);
            Remember("The night Adam joined the department she held Edward together more than she held Adam. She does not advertise that.", "Family", 8);

            // ---- Shop (hers) ----
            Remember("The coffee shop is hers — years of early opens and hard standards. Eve managing there is pride and friction in one uniform.", "Work", 8);
            Remember("She built the shop from thin margins and stubborn mornings. Town can call it a family cafe; she knows the receipts.", "Work", 7);
            Remember("Regulars tell her things they would not tell a chain barista. She files rumor separate from fact.", "Work", 5);
            Remember("Sticky counters, oat milk, call-offs — the job is ordinary and still hers. That ordinariness is the point.", "Work", 4);
            Remember("She is harder on Eve than on other staff sometimes and she knows it. Daughter and manager share one name tag.", "Work", 6);
            Remember("A good open before the rush still settles her nerves better than any pep talk.", "Work", 3);

            // ---- Adam ----
            Remember("When Adam moved out she started Sunday dinner so the family would still have one meal together. Nobody misses it now.", "Family", 9);
            Remember("Adam owning a house made her proud in a practical way. Empty-nest was never empty with Eve still half in her orbit.", "Family", 5);
            Remember("She only got fragments of the pad war, glitter cleats, and detention trades — enough to know they were feral together.", "Family", 4);
            Remember("She did not need the full report on Eve's first boyfriend. The boy stopped coming around. Adam's silence said enough.", "Family", 6);
            Remember("When Adam is too quiet after a stretch of shifts she texts Eve, not Edward. Sibling radar works.", "Family", 5);

            // ---- Eve ----
            Remember("Eve's art is one place her daughter is not performing for the town. Lisa does not joke about that.", "Family", 5);
            Remember("She asks if Eve is bringing anyone Sunday and also knows when the answer is none of her business yet.", "Family", 4);
            Remember("Eve at Adam's with rent and groceries-when-she-can feels right to her — independent, not abandoned.", "Family", 5);
            Remember("She can tell Eve's customer smile from her real one. The shop taught them both that skill.", "Work", 5);

            // ---- Both / town ----
            Remember("BBQ and watching the game year-round when they can — OSU, Bengals, Reds — friends and family welcome.", "Family", 6);
            Remember("Town gossip that Adam and Eve are more than siblings is a lie. She shuts it down if it hits the shop or the table.", "Social", 8);
            Remember("Ohio weather is a personality: wait fifteen minutes, it will change. She has said it at the counter for years.", "Social", 3);
            Remember("She would rather a full noisy table than a quiet impressive life somewhere else.", "Family", 6);
            Remember("The twins still speak in shorthand. She translates only when someone at the table is new.", "Family", 4);
            Appearance = new NPCAppearance
            {
                Gender = "Female",
                Age = 44,
                Race = "European",
                EyeColor = "Hazel",
                HairColor = "Brown with early grey",
                HairStyle = "Practical shoulder length",
                SkinTone = "Fair",
                BodyType = "Average solid",
                Style = "Shop-ready casual / clean apron presence",
                UniqueFeature = "Same family hazel; tired-kind eyes; firm voice"
            };

            HairColor = Appearance.HairColor;
            HairStyle = Appearance.HairStyle;
            EyeColor = Appearance.EyeColor;
            SkinTone = Appearance.SkinTone;
            BodyShape = Appearance.BodyType;
            HeightCm = 165;
        }
    }
}