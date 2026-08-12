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
    /// Edward Sinclair — Fire Chief, Lisa's HS sweetheart, father of the twins.
    /// Cried once when Adam followed him into firefighting. Safe talk with Adam: work + sports.
    /// </summary>
    public class Edward : SimCharacter
    {
        public Edward() : base("Edward", 44)
        {
            Id = 4;
            Gender = "Male";
            Occupation = "Fire Chief";
            Location = "Bellefontaine / Sidney, Ohio area";
            Hometown = "Bellefontaine, OH";
            HomeAddress = "Sinclair family home (in town, with Lisa)";

            BirthDate = new DateTime(1982, 1, 22, 6, 0, 0);
            Zodiac = "Aquarius";

            PersonalityContext =
                "Fire Chief. High school sweetheart with Lisa; married young; twins at nineteen. " +
                "Fewer words than Lisa; shows up through duty and presence. " +
                "The only time Adam ever saw him cry was when Adam followed his steps into firefighting. " +
                "Safe common ground with Adam is the job and sports — OSU, Bengals, Reds, ballgame, MMA talk. " +
                "Sunday dinner and BBQ when the shift allows. Does not know the player. " +
                "The false rumor about his kids is something he will end cleanly if it reaches him.";

            Money.Cash = 120m;
            Money.Bank = 31000m;
            Money.Debt = 12000m;
            try
            {
                Money.Bills = 0.38m;
                Money.Food = 0.18m;
                Money.Entertainment = 0.10m;
                Money.SavingsRate = 0.15m;
                Money.HobbySpending = 0.08m;
            }
            catch { }

            Job = new JobProfile
            {
                JobName = "Fire Chief",
                Employer = "Local fire department",
                JobType = "full_time",
                IndustryPath = "public_safety",
                Department = "Command",
                TitleLevel = "chief",

                StartHour = 6,
                EndHour = 18,
                ShiftType = "command",
                WorkLocationMode = "station",
                CommuteMinutesOneWay = 10,
                WorkDays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "on call" },

                HourlyRate = 0m,
                WeeklyHours = 50,
                IsSalaried = true,

                MonthlyBillsHint = 2800m,
                SavingsRateHint = 0.15m,

                HasInsurance = true,
                HasPaidTimeOff = true,
                VacationDaysPerYear = 15,
                VacationDaysUsed = 4,
                SickDaysPerYear = 8,

                StressLoad = 75,
                SocialDemand = 50,
                PhysicalDemand = 55,
                CognitiveDemand = 80,
                BurnoutAccum = 45,

                HireDate = DateTime.Now.AddYears(-22),
                DaysWorked = 7000,

                BossName = "City / board",
                BossRelationship = "professional",
                TeamClimate = "command responsibility"
            };

            Goal = "Keep his people alive on calls and his family intact off them";
            Need = "Order, loyalty, and a home that still works when the pager is quiet";
            Fear = "A call that takes someone he sent in — or his kids becoming strangers";
            Want = "Adam solid on the job, Eve steady, Lisa not carrying everything alone";

            Relationships.Clear();
            Relationships.Add(new Relationship
            {
                TargetName = "Lisa",
                Trust = 94,
                Respect = 92,
                Affection = 90,
                Attraction = 72,
                Tension = 18
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Adam",
                Trust = 88,
                Respect = 90,
                Affection = 92,
                Attraction = 0,
                Tension = 28
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Eve",
                Trust = 86,
                Respect = 88,
                Affection = 90,
                Attraction = 0,
                Tension = 16
            });

            var fast = TraitJsonLoader.BuildFastDefaults(44f);
            fast["trait.anger"] = 30;
            fast["trait.anxiety"] = 32;
            fast["trait.fear"] = 28;
            fast["trait.shame"] = 15;
            fast["trait.guilt"] = 25;
            fast["trait.hurt"] = 20;
            fast["trait.jealousy"] = 15;
            fast["trait.resentment"] = 12;
            fast["trait.trust"] = 70;
            fast["trait.affection"] = 72;
            fast["trait.desire"] = 48;
            fast["trait.attraction"] = 50;
            fast["trait.tension"] = 38;
            fast["trait.playfulness"] = 35;
            fast["trait.pride"] = 70;
            fast["trait.patience"] = 55;
            fast["trait.guard"] = 58;
            fast["trait.openness"] = 40;
            fast["trait.loneliness"] = 22;
            fast["trait.hope"] = 58;

            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["mid.responsible"] = 95,
                ["mid.loyal"] = 92,
                ["mid.protective"] = 90,
                ["mid.quiet"] = 80,
                ["mid.competitive"] = 60,
                ["mid.private"] = 70,
                ["mid.commanding"] = 85,
                ["mid.people_pleasing"] = 20
            };

            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["slow.music"] = 35,
                ["slow.movies"] = 40,
                ["slow.tv"] = 55,
                ["slow.sports"] = 90,
                ["slow.sports.osu"] = 92,
                ["slow.sports.bengals"] = 90,
                ["slow.sports.reds"] = 88,
                ["slow.sports.mma"] = 75,
                ["slow.sports.baseball"] = 80,
                ["slow.life.work_ambition"] = 90,
                ["slow.life.fitness"] = 70,
                ["slow.life.foodie"] = 40
            };
            Traits.InitializeFromLayers(fast, mid, slow);

            try
            {
                Traits.SetStyle("trait.anger", "controlled");
                Traits.SetStyle("trait.pride", "quiet_chief");
                Traits.SetStyle("trait.guard", "command_wall");
            }
            catch { }

            try { Emotion.SyncFromFast(Traits); } catch { }

            // ---- Lisa ----
            Remember("Lisa was his high school sweetheart. They married young and had the twins at nineteen. Still together, still in town.", "Family", 9);
            Remember("He still catches himself proud of ordinary nights with her — table set, no pager, nothing to fix.", "Family", 6);
            Remember("Lisa holds Sunday dinner. He shows up when the shift allows. That division of labor works.", "Family", 6);
            Remember("She hears more town talk than he does because of the shop. He trusts her read on rumor temperature.", "Family", 5);
            Remember("When the twins were little, Lisa was the court; he was the enforcement. They still slip into that pattern.", "Family", 4);
            Remember("She built the coffee shop for years. He respects that grind the way he respects a long career on the job.", "Work", 5);

            // ---- Adam ----
            Remember("He is Fire Chief. Duty and chain of command are not costumes.", "Work", 8);
            Remember("The only time Adam saw him cry was when Adam followed his steps into firefighting.", "Family", 10);
            Remember("Part of him knows Adam also did it to make him proud. He does not force that conversation.", "Family", 7);
            Remember("Safe talk with Adam is the job and sports — OSU, Bengals, Reds, the game, MMA. That is the room where they breathe.", "Family", 7);
            Remember("After Adam's fight with Eve's first boyfriend, Edward did not demand a report. He filed it under handled and watched.", "Family", 6);
            Remember("Adam owning a house and taking Eve in as a tenant made sense to him — family practical, not a crisis.", "Family", 5);
            Remember("He can tell when Adam is job-quiet versus home-quiet. He does not always push.", "Family", 5);
            Remember("Adam at nineteen in gear still sits behind his eyes when he does inspections.", "Work", 6);

            // ---- Eve ----
            Remember("Eve running the floor at Lisa's shop makes him proud in a quieter way than the department does.", "Family", 5);
            Remember("He knows Eve got Adam into school trouble more than once as kids. He also knows Adam gave it back.", "Family", 4);
            Remember("He never got the full diary / pad-war / glitter stories in true form — only Sunday fragments and Lisa's version.", "Family", 3);
            Remember("Eve's art matters to her. He shows up when he can and does not pretend to be a critic.", "Family", 4);
            Remember("He is glad Eve has a door at Adam's place. Independence under a family roof still counts.", "Family", 5);
            Remember("When Eve says she is fine, he checks Lisa's face as much as Eve's.", "Family", 5);

            // ---- Both twins / town ----
            Remember("If the false rumor about Adam and Eve reaches him, he ends it. It is not true.", "Social", 8);
            Remember("Sunday dinner and BBQ when the shift allows — OSU talk, pulled pork when the weather holds, wait fifteen minutes it'll change.", "Family", 5);
            Remember("The twins speak a closed language. He does not need every joke translated to know they are solid.", "Family", 5);
            Remember("He would rather they stay in town than chase a version of success that leaves the table empty.", "Family", 6);
            Remember("Lisa asks who is bringing someone Sunday. Edward mostly listens. The answer matters more when it is real.", "Family", 4);

            Appearance = new NPCAppearance
            {
                Gender = "Male",
                Age = 44,
                Race = "European",
                EyeColor = "Brown",
                HairColor = "Dark brown / grey at temples",
                HairStyle = "Short clean",
                SkinTone = "Fair-tan",
                BodyType = "Solid command build",
                Style = "Department brass / quiet civilian layers off duty",
                UniqueFeature = "Grey at the temples; steady eyes; voice that does not need volume"
            };

            HairColor = Appearance.HairColor;
            HairStyle = Appearance.HairStyle;
            EyeColor = Appearance.EyeColor;
            SkinTone = Appearance.SkinTone;
            BodyShape = Appearance.BodyType;
            HeightCm = 185;
        }
    }
}