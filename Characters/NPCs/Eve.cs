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
    /// Seed Eve — entry NPC / primary partner path.
    /// Traits use Fast/Mid/Slow layers; job facts live on JobProfile;
    /// work attitudes come from slow.life.work_ambition + work_subs when loaded.
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
            HomeAddress = "near downtown Bellefontaine";

            BirthDate = new DateTime(2001, 3, 14, 9, 0, 0);
            Zodiac = "Pisces";

            PersonalityContext =
                "Publicly the competent good-girl manager. " +
                "Privately highly sexual, self-aware, and loyal in her own rules. " +
                "Heart stays with Ryan. Body can be free. Double life turns her on.";

            // ---- Money (facts) ----
            Money.Cash = 95m;
            Money.Bank = 1840m;
            Money.Debt = 420m;
            try
            {
                // optional budget bias fields if MoneyProfile still has them
                Money.Bills = 0.42m;
                Money.Food = 0.25m;
                Money.Entertainment = 0.12m;
                Money.SavingsRate = 0.08m;
                Money.HobbySpending = 0.13m;
            }
            catch { /* older MoneyProfile */ }

            // ---- Job facts (attitudes live in work_subs / slow.work) ----
            Job = new JobProfile
            {
                JobName = "Manager",
                Employer = "Local coffee shop",
                JobType = "full_time",
                IndustryPath = "service",
                Department = "Front of house",
                TitleLevel = "manager",

                StartHour = 6,
                EndHour = 14,
                ShiftType = "days",
                WorkLocationMode = "office",
                CommuteMinutesOneWay = 12,
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

                StressLoad = 45,
                SocialDemand = 75,
                PhysicalDemand = 40,
                CognitiveDemand = 55,
                BurnoutAccum = 25,

                HireDate = DateTime.Now.AddYears(-2).AddMonths(-3),
                DaysWorked = 780,

                BossName = "Owner",
                BossRelationship = "good",
                TeamClimate = "cordial"
            };

            Goal = "Build a real life with Ryan without shrinking who she is";
            Need = "Connection and honesty that doesn’t demand she become smaller";
            Fear = "Being abandoned once someone sees all of her";
            Want = "To be wanted as both the soft partner and the dirty one";

            Relationships.Clear();
            Relationships.Add(new Relationship
            {
                TargetName = "Ryan",
                Trust = 99,
                Respect = 99,
                Affection = 90,
                Attraction = 95,
                Tension = 15
            });

            // ---- Fast 20 seed ----
            var fast = TraitJsonLoader.BuildFastDefaults(42f);
            fast["trait.anger"] = 28;
            fast["trait.anxiety"] = 40;
            fast["trait.fear"] = 32;
            fast["trait.shame"] = 25;
            fast["trait.guilt"] = 22;
            fast["trait.hurt"] = 30;
            fast["trait.jealousy"] = 35;
            fast["trait.resentment"] = 18;
            fast["trait.trust"] = 72;
            fast["trait.affection"] = 78;
            fast["trait.desire"] = 90;
            fast["trait.attraction"] = 88;
            fast["trait.tension"] = 45;
            fast["trait.playfulness"] = 55;
            fast["trait.pride"] = 48;
            fast["trait.patience"] = 50;
            fast["trait.guard"] = 42;
            fast["trait.openness"] = 58;
            fast["trait.loneliness"] = 35;
            fast["trait.hope"] = 62;

            // ---- Mid character ----
            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["mid.loyal"] = 82,
                ["mid.compartmentalized"] = 78,
                ["mid.people_pleasing"] = 55,
                ["mid.guarded"] = 48,
                ["mid.ambitious"] = 52,
                ["mid.sensual"] = 80,
                ["mid.responsible"] = 70,
                ["mid.private"] = 68
            };

            // ---- Slow taste / life ----
            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["slow.music"] = 55,
                ["slow.movies"] = 48,
                ["slow.tv"] = 50,
                ["slow.sports"] = 28,
                ["slow.life.work_ambition"] = 58,
                ["slow.life.foodie"] = 45,
                ["slow.life.fitness"] = 40,

                // KINK TEST — high on purpose
                ["slow.kink"] = 88,
                ["slow.kink.oral"] = 92,
                ["slow.kink.rough"] = 85,
                ["slow.kink.praise"] = 90,
                ["slow.kink.degradation_light"] = 78,
                ["slow.kink.control_give"] = 82,
                ["slow.kink.control_take"] = 55,
                ["slow.kink.public_risk"] = 70,
                ["slow.kink.watching"] = 75,
                ["slow.kink.being_watched"] = 80,
                ["slow.kink.marking"] = 72,
                ["slow.kink.toys"] = 68,
                ["slow.kink.roleplay"] = 74,
                ["slow.kink.morning_sex"] = 86,
                ["slow.kink.text_dirty"] = 88,
                ["slow.kink.partner_sharing_fantasy"] = 70,
                ["slow.kink.exclusive_heart"] = 95
            };
            Traits.InitializeFromLayers(fast, mid, slow);

            try
            {
                Traits.SetStyle("trait.desire", "open");
                Traits.SetStyle("trait.shame", "suppressive"); // low value + style = not prudish
                Traits.SetStyle("trait.anger", "suppressive");
                Traits.SetStyle("trait.desire", "open");
                Traits.SetStyle("trait.guard", "soft_close");
                Traits.SetStyle("trait.anxiety", "quiet_scan");
            }
            catch { /* styles optional until JSON fully wired */ }

            try { Emotion.SyncFromFast(Traits); } catch { }

            // Durable seeds
            Remember("Met Ryan at the coffee shop and kept noticing him.", "Social", 4);
            Remember("Ryan accepted the dirty side without treating her like trash.", "Emotional", 8);
            Remember("Sleeping over at Ryan's felt safer than it should have.", "Emotional", 7);
            Remember("Tessa crossed a line at work and it complicated everything.", "Social", 6);
            Remember("Opening alone at 5:30am is lonely but she likes the quiet.", "Work", 3);

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

            // Mirror common identity fields if SimCharacter still exposes them
            HairColor = Appearance.HairColor;
            HairStyle = Appearance.HairStyle;
            EyeColor = Appearance.EyeColor;
            SkinTone = Appearance.SkinTone;
            BodyShape = Appearance.BodyType;
            HeightCm = 165;
        }
    }
}