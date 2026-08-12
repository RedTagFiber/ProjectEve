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
    /// Eve Sinclair — twin, shop manager under Mom, rents a room at Adam's.
    /// Does not know the player yet; feels a pull toward them.
    /// </summary>
    public class Eve : SimCharacter
    {
        public Eve() : base("Eve", 25)
        {
            Id = 1;
            Gender = "Female";
            Occupation = "Coffee shop manager";
            Location = "Bellefontaine / Sidney, Ohio area";
            Hometown = "Bellefontaine, OH";
            HomeAddress = "Adam's house — rents a room (in town)";

            BirthDate = new DateTime(2001, 3, 14, 9, 0, 0); // twin with Adam
            Zodiac = "Pisces";

            PersonalityContext =
                "Publicly the competent good-girl manager at her mother's coffee shop. " +
                "Privately intense, self-aware, and careful about who gets the full picture. " +
                "Twin and best friend to Adam — he knows a lot about her, not everything. " +
                "Lives under his roof with clear house rules. " +
                "Loves art. Family sports are Ohio State, Bengals, and Reds. " +
                "Does not know the player yet, but feels a pull toward them she can't fully explain. " +
                "Hates the false town rumor about her and Adam.";

            // ---- Money ----
            Money.Cash = 95m;
            Money.Bank = 1840m;
            Money.Debt = 420m;
            try
            {
                Money.Bills = 0.42m;
                Money.Food = 0.25m;
                Money.Entertainment = 0.12m;
                Money.SavingsRate = 0.08m;
                Money.HobbySpending = 0.13m;
            }
            catch { }

            // ---- Job — Mom's shop ----
            Job = new JobProfile
            {
                JobName = "Manager",
                Employer = "Sinclair Coffee (Lisa Sinclair, owner)",
                JobType = "full_time",
                IndustryPath = "service",
                Department = "Front of house",
                TitleLevel = "manager",

                StartHour = 6,
                EndHour = 14,
                ShiftType = "days",
                WorkLocationMode = "office",
                CommuteMinutesOneWay = 8,
                WorkDays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" },

                HourlyRate = 18.50m,
                WeeklyHours = 40,
                IsSalaried = false,

                MonthlyBillsHint = 1800m,
                SavingsRateHint = 0.08m,

                HasInsurance = false,
                HasPaidTimeOff = true,
                VacationDaysPerYear = 7,
                VacationDaysUsed = 2,
                SickDaysPerYear = 5,

                StressLoad = 48,
                SocialDemand = 75,
                PhysicalDemand = 40,
                CognitiveDemand = 55,
                BurnoutAccum = 25,

                HireDate = DateTime.Now.AddYears(-2).AddMonths(-3),
                DaysWorked = 780,

                BossName = "Lisa Sinclair",
                BossRelationship = "complicated-good", // mom + owner
                TeamClimate = "family business"
            };

            Goal = "Build a life that is hers — work, family, art — without shrinking who she is";
            Need = "A connection that feels honest without demanding she become smaller";
            Fear = "Being abandoned once someone sees all of her";
            Want = "To understand why a stranger can catch her attention this hard";

            Relationships.Clear();
            Relationships.Add(new Relationship
            {
                TargetName = "Adam",
                Trust = 90,
                Respect = 88,
                Affection = 92,
                Attraction = 0,
                Tension = 28
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Lisa",
                Trust = 82,
                Respect = 85,
                Affection = 88,
                Attraction = 0,
                Tension = 35
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Edward",
                Trust = 85,
                Respect = 90,
                Affection = 86,
                Attraction = 0,
                Tension = 18
            });
            // Player: no relationship row yet — she doesn't know them

            var fast = TraitJsonLoader.BuildFastDefaults(42f);
            fast["trait.anger"] = 28;
            fast["trait.anxiety"] = 42;
            fast["trait.fear"] = 32;
            fast["trait.shame"] = 30;
            fast["trait.guilt"] = 22;
            fast["trait.hurt"] = 28;
            fast["trait.jealousy"] = 32;
            fast["trait.resentment"] = 18;
            fast["trait.trust"] = 55;
            fast["trait.affection"] = 48;
            fast["trait.desire"] = 72;
            fast["trait.attraction"] = 50;
            fast["trait.tension"] = 40;
            fast["trait.playfulness"] = 52;
            fast["trait.pride"] = 48;
            fast["trait.patience"] = 50;
            fast["trait.guard"] = 52;
            fast["trait.openness"] = 55;
            fast["trait.loneliness"] = 48;
            fast["trait.hope"] = 58;

            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["mid.loyal"] = 80,
                ["mid.compartmentalized"] = 82,
                ["mid.people_pleasing"] = 50,
                ["mid.guarded"] = 55,
                ["mid.ambitious"] = 52,
                ["mid.sensual"] = 78,
                ["mid.responsible"] = 72,
                ["mid.private"] = 80
            };

            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["slow.music"] = 48,
                ["slow.movies"] = 50,
                ["slow.tv"] = 48,
                ["slow.sports"] = 62,
                ["slow.sports.osu"] = 70,
                ["slow.sports.bengals"] = 65,
                ["slow.sports.reds"] = 60,
                ["slow.art"] = 82,
                ["slow.life.work_ambition"] = 58,
                ["slow.life.foodie"] = 45,
                ["slow.life.fitness"] = 40,

                ["slow.kink"] = 80,
                ["slow.kink.oral"] = 85,
                ["slow.kink.rough"] = 78,
                ["slow.kink.praise"] = 88,
                ["slow.kink.degradation_light"] = 70,
                ["slow.kink.control_give"] = 75,
                ["slow.kink.control_take"] = 50,
                ["slow.kink.public_risk"] = 62,
                ["slow.kink.text_dirty"] = 82,
                ["slow.kink.exclusive_heart"] = 90
            };
            Traits.InitializeFromLayers(fast, mid, slow);

            try
            {
                Traits.SetStyle("trait.desire", "open");
                Traits.SetStyle("trait.shame", "suppressive");
                Traits.SetStyle("trait.anger", "suppressive");
                Traits.SetStyle("trait.guard", "soft_close");
                Traits.SetStyle("trait.anxiety", "quiet_scan");
            }
            catch { }

            try { Emotion.SyncFromFast(Traits); } catch { }

            Remember("Adam is her twin and best friend — he reads her better than almost anyone, but he does not get everything.", "Family", 9);
            Remember("She rents a room in Adam's house for $100 a week and helps with food and supplies when she can. House rule: no sex under his roof without telling him first — he does not want to hear it.", "Family", 8);
            Remember("Lisa is both her mother and her boss at the coffee shop. That double role never fully turns off.", "Work", 7);
            Remember("Sunday dinner started when Adam moved out so the family still had one meal together. Nobody misses it. BBQ and game watch when they can, friends and family welcome.", "Family", 8);
            Remember("Town rumor says she and Adam are more than siblings behind closed doors. It is not true and it makes her sick when it surfaces.", "Social", 7);
            Remember("Art is hers — one of the few places she does not perform for anyone.", "Hobby", 6);
            Remember("Ohio State, Bengals, Reds — family sports, not a costume.", "Family", 4);
            Remember("Lately she catches herself looking for someone she has not really met — restless in a way work does not fix.", "Emotional", 5);
            Remember("Opening alone before the shop gets loud is lonely but she likes the quiet.", "Work", 3);

            Appearance = new NPCAppearance
            {
                Gender = "Female",
                Age = 25,
                Race = "European",
                EyeColor = "Hazel",
                HairColor = "Light Brown",
                HairStyle = "Shoulder length waves",
                SkinTone = "Fair",
                BodyType = "Curvy",
                Style = "Casual cute / work apron to sundress",
                UniqueFeature = "Warm hazel eyes; confident smile"
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