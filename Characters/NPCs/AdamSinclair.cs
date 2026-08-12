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
    /// Adam Sinclair — Eve's twin, firefighter since 19, owns the house she rents in.
    /// Best friend sibling; knows a lot, not everything. Rock country out loud; old jazz private.
    /// </summary>
    public class Adam : SimCharacter
    {
        public Adam() : base("Adam", 25)
        {
            Id = 2;
            Gender = "Male";
            Occupation = "Firefighter";
            Location = "Bellefontaine / Sidney, Ohio area";
            Hometown = "Bellefontaine, OH";
            HomeAddress = "Owns house in town (Eve rents a room)";

            BirthDate = new DateTime(2001, 3, 14, 9, 15, 0); // twin with Eve
            Zodiac = "Pisces";

            PersonalityContext =
                "Firefighter since nineteen — part calling, part making his father proud. " +
                "Owns his house; Eve rents a room and helps when she can. " +
                "Twin and best friend to Eve: he knows a lot about her, not everything, and he does not want to hear her sex life through the wall. " +
                "Rock country in the truck; old jazz is his private stash. " +
                "Ohio State, Bengals, Reds with the whole family. " +
                "Safe talk with Dad is work and sports. The false town rumor about him and Eve makes him angry, not guilty.";

            Money.Cash = 140m;
            Money.Bank = 6200m;
            Money.Debt = 18500m; // house-ish weight
            try
            {
                Money.Bills = 0.45m;
                Money.Food = 0.22m;
                Money.Entertainment = 0.12m;
                Money.SavingsRate = 0.10m;
                Money.HobbySpending = 0.11m;
            }
            catch { }

            Job = new JobProfile
            {
                JobName = "Firefighter",
                Employer = "Local fire department (Edward Sinclair, Fire Chief)",
                JobType = "full_time",
                IndustryPath = "public_safety",
                Department = "Suppression",
                TitleLevel = "firefighter",

                StartHour = 7,
                EndHour = 19,
                ShiftType = "rotating",
                WorkLocationMode = "station",
                CommuteMinutesOneWay = 10,
                WorkDays = new[] { "varies" },

                HourlyRate = 24.00m,
                WeeklyHours = 48,
                IsSalaried = false,

                MonthlyBillsHint = 2200m,
                SavingsRateHint = 0.10m,

                HasInsurance = true,
                HasPaidTimeOff = true,
                VacationDaysPerYear = 10,
                VacationDaysUsed = 3,
                SickDaysPerYear = 8,

                StressLoad = 70,
                SocialDemand = 55,
                PhysicalDemand = 85,
                CognitiveDemand = 60,
                BurnoutAccum = 35,

                HireDate = DateTime.Now.AddYears(-6), // since ~19
                DaysWorked = 2100,

                BossName = "Edward Sinclair",
                BossRelationship = "father-chief",
                TeamClimate = "crew-tight"
            };

            Goal = "Be solid — house, crew, family — without admitting how much Dad's approval still weighs";
            Need = "Respect that is earned on the job and ease at home";
            Fear = "Failing someone on a call — or becoming only what Dad needed him to be";
            Want = "Music, a game on, his sister safe, and nobody in his business";

            Relationships.Clear();
            Relationships.Add(new Relationship
            {
                TargetName = "Eve",
                Trust = 92,
                Respect = 88,
                Affection = 94,
                Attraction = 0,
                Tension = 30
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Edward",
                Trust = 86,
                Respect = 95,
                Affection = 84,
                Attraction = 0,
                Tension = 32
            });
            Relationships.Add(new Relationship
            {
                TargetName = "Lisa",
                Trust = 88,
                Respect = 85,
                Affection = 90,
                Attraction = 0,
                Tension = 20
            });

            var fast = TraitJsonLoader.BuildFastDefaults(45f);
            fast["trait.anger"] = 38;
            fast["trait.anxiety"] = 35;
            fast["trait.fear"] = 28;
            fast["trait.shame"] = 22;
            fast["trait.guilt"] = 30;
            fast["trait.hurt"] = 25;
            fast["trait.jealousy"] = 28;
            fast["trait.resentment"] = 25;
            fast["trait.trust"] = 68;
            fast["trait.affection"] = 70;
            fast["trait.desire"] = 55;
            fast["trait.attraction"] = 52;
            fast["trait.tension"] = 42;
            fast["trait.playfulness"] = 62;
            fast["trait.pride"] = 58;
            fast["trait.patience"] = 48;
            fast["trait.guard"] = 50;
            fast["trait.openness"] = 48;
            fast["trait.loneliness"] = 30;
            fast["trait.hope"] = 60;

            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["mid.loyal"] = 90,
                ["mid.protective"] = 88,
                ["mid.teasing"] = 75,
                ["mid.people_pleasing"] = 55, // higher toward Dad than he admits
                ["mid.responsible"] = 78,
                ["mid.private"] = 60,
                ["mid.competitive"] = 65,
                ["mid.compartmentalized"] = 45
            };

            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["slow.music"] = 85,
                ["slow.music.rock_country"] = 90,
                ["slow.music.jazz_old"] = 78, // secret lean
                ["slow.movies"] = 45,
                ["slow.tv"] = 50,
                ["slow.sports"] = 88,
                ["slow.sports.osu"] = 90,
                ["slow.sports.bengals"] = 88,
                ["slow.sports.reds"] = 85,
                ["slow.sports.mma"] = 70,
                ["slow.life.work_ambition"] = 62, // mixed — job real, approval real
                ["slow.life.fitness"] = 75,
                ["slow.life.foodie"] = 40
            };
            Traits.InitializeFromLayers(fast, mid, slow);

            try
            {
                Traits.SetStyle("trait.anger", "sharp_protect");
                Traits.SetStyle("trait.pride", "quiet");
                Traits.SetStyle("trait.guard", "crew_wall");
            }
            catch { }

            try { Emotion.SyncFromFast(Traits); } catch { }

            Remember("Eve is his twin and best friend. He knows a lot about her — not everything — and he will shut down anyone who pushes the false rumor.", "Family", 9);
            Remember("Eve pays $100 a week rent and helps with food and supplies when she can. House rule: no sex in the house unless she says so first. He does not want to hear it.", "Family", 8);
            Remember("He has been a firefighter since he was nineteen. The only time he ever saw Edward cry was when he followed his father into the job.", "Family", 10);
            Remember("Part of the job is still about making Dad proud — he does not always say that out loud.", "Work", 7);
            Remember("Rock country for the truck and the yard. Old jazz is private — not a secret shame, just his.", "Hobby", 6);
            Remember("Ohio State, Bengals, Reds — shared language with Dad and the whole family. Safe ground with Edward is work and sports.", "Family", 5);
            Remember("Sunday dinner is non-negotiable. Lisa started it when he moved out so they would still have one meal together.", "Family", 7);
            Remember("The town rumor that he and Eve are more than siblings is garbage. It is not true.", "Social", 8);
            // ---- Twin / Eve ----
            Remember("Eve is his twin and best friend. He knows a lot about her — not everything — and he will shut down anyone who pushes the false rumor.", "Family", 9);
            Remember("The town rumor that he and Eve are more than siblings is garbage. It is not true.", "Social", 8);
            Remember("Eve pays $100 a week rent and helps with food and supplies when she can. House rule: no sex in the house unless she says so first. He does not want to hear it.", "Family", 8);
            Remember("As kids Eve got him detention more than once with her mouth; he still brings it up when she plays perfect manager.", "Family", 5);
            Remember("He hid all her pads in a toolbox once as a kid prank. Family war crime. She has never let him forget it.", "Family", 6);
            Remember("Eve put glitter in his cleats before a game when they were little. Sparkle season is still a Sunday joke.", "Family", 4);
            Remember("She cut his hair in his sleep once. School photos were uneven. Mom still has one.", "Family", 4);
            Remember("Eve blamed him for the broken lamp; he took the heat because she cried harder.", "Family", 4);
            Remember("He beat up Eve's first boyfriend for crossing a line. He does not tell the full story to strangers.", "Family", 8);
            Remember("He walked her home from a sketchy party he wasn't invited to. Protective, not heroic in his head.", "Family", 6);
            Remember("He read her diary once as a teen, got caught, and never spoke of what was in it.", "Family", 7);
            Remember("Eve dragged him out of a bad party before it could collide with Dad's world. He owes her that one.", "Family", 7);
            Remember("When she almost moved for a job he said he'd rent the room to a stranger who wouldn't steal chargers. She stayed.", "Family", 6);
            Remember("Charger theft, identical mugs, thermostat war — ongoing. Fridge scoreboard energy.", "Family", 3);
            Remember("She hung art in his hallway without asking. He complained and left it up.", "Family", 4);
            Remember("After a bad call he goes quiet. Eve knows which silences mean leave it. He loves that she doesn't dig.", "Family", 7);
            Remember("He waited up with the porch light through one of her bad situationships. No lecture.", "Family", 7);

            // ---- Edward / job ----
            Remember("He has been a firefighter since he was nineteen. The only time he ever saw Edward cry was when he followed his father into the job.", "Family", 10);
            Remember("Part of the job is still about making Dad proud — he does not always say that out loud.", "Work", 7);
            Remember("Safe ground with Edward is work and sports — OSU, Bengals, Reds, the game. Feelings get less airtime.", "Family", 5);
            Remember("Edward did not need a full report after the boyfriend fight. Dad filed it under handled.", "Family", 5);
            Remember("On shift weight stays on shift. Home is for fries, game talk, and not performing toughness for Eve.", "Work", 5);

            // ---- Lisa / Sunday / shop ----
            Remember("Sunday dinner is non-negotiable. Lisa started it when he moved out so they would still have one meal together.", "Family", 7);
            Remember("Lisa asks if Eve is bringing anyone on Sunday. Adam usually covers for her with a joke about the early shift.", "Family", 5);
            Remember("Mom's shop is Eve's other parent. Adam hears both sides and tries not to referee unless it spills home.", "Family", 5);
            Remember("Lisa kills the twin rumor if it hits the counter. Adam is glad she does it so he does not have to every time.", "Social", 6);
            Remember("Pulled pork Sundays when the weather holds. Ohio weather joke: wait fifteen minutes, it'll change.", "Family", 3);
            Remember("OSU looks good this year — he says it every August. Eve threatens to put it on a shirt when it isn't.", "Family", 3);

            // ---- Taste / self ----
            Remember("Rock country for the truck and the yard. Old jazz is private — not a secret shame, just his.", "Hobby", 6);
            Remember("Ohio State, Bengals, Reds — shared language with the whole family, not a costume.", "Family", 5);
            Remember("He owns the house. Eve's room is hers. The porch collects gear. The living room collects twin arguments about nothing.", "Family", 4);
            Appearance = new NPCAppearance
            {
                Gender = "Male",
                Age = 25,
                Race = "European",
                EyeColor = "Hazel",
                HairColor = "Brown",
                HairStyle = "Short practical",
                SkinTone = "Fair-tan",
                BodyType = "Athletic solid",
                Style = "Station wear / jeans and flannel off duty",
                UniqueFeature = "Same hazel as Eve; broader build; easy smirk"
            };

            HairColor = Appearance.HairColor;
            HairStyle = Appearance.HairStyle;
            EyeColor = Appearance.EyeColor;
            SkinTone = Appearance.SkinTone;
            BodyShape = Appearance.BodyType;
            HeightCm = 183;
        }
    }
}