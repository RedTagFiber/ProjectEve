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

            // ---- Adam / twin ----
Remember("Adam is her twin and best friend — he reads her better than almost anyone, but he does not get everything.", "Family", 9);
            Remember("She rents a room in Adam's house for $100 a week and helps with food and supplies when she can. House rule: no sex under his roof without telling him first — he does not want to hear it.", "Family", 8);
            Remember("Town rumor says she and Adam are more than siblings behind closed doors. It is not true and it makes her sick when it surfaces.", "Social", 7);
            Remember("As a kid she got Adam detention with her mouth more than once and still plays innocent when he brings it up.", "Family", 5);
            Remember("Adam hid all her pads in a toolbox once. She has a long memory for that kind of betrayal.", "Family", 6);
            Remember("She put glitter in his cleats before a game. Sunday table still resurrects sparkle season.", "Family", 4);
            Remember("She cut his hair in his sleep. Mom kept the school photo. Eve pretends not to be proud.", "Family", 4);
            Remember("She blamed him for the broken lamp because she cried harder. He took the heat. She still knows what she did.", "Family", 5);
            Remember("Her first boyfriend crossed a line. Adam handled it with fists. She does not owe strangers that story.", "Family", 8);
            Remember("Adam walked her home from a sketchy party he was not invited to. She still files that under love, not control.", "Family", 6);
            Remember("He read her diary once. She caught him. Neither of them discusses the contents.", "Family", 7);
            Remember("She dragged him out of a bad party before it could blow back on Dad's world. He owes her; she rarely collects out loud.", "Family", 7);
            Remember("When she almost took a job out of town he joked about renting to a stranger who would not steal chargers. She stayed.", "Family", 6);
            Remember("Charger theft, mug wars, thermostat — petty forever. Home feels like home because of it.", "Family", 3);
            Remember("She hung art in his hallway without asking. He left it up. That mattered more than permission.", "Family", 5);
            Remember("After his bad calls she does not dig. Knowing when to be quiet is part of loving a firefighter's sibling.", "Family", 7);
            Remember("He waited up with the porch light through a situationship she will not name casually. No lecture. She remembers.", "Family", 7);

            // ---- Lisa / shop ----
            Remember("Lisa is both her mother and her boss at the coffee shop. That double role never fully turns off.", "Work", 7);
            Remember("The shop is Lisa's life's work. Eve managing there is pride, friction, and a uniform she cannot fully take off.", "Work", 7);
            Remember("She can do the customer smile on command. Adam and Lisa can still tell when it is fake.", "Work", 5);
            Remember("Sticky counters, call-offs, oat milk — ordinary stress. She likes being competent more than she admits.", "Work", 4);
            Remember("Opening alone before the shop gets loud is lonely but she likes the quiet.", "Work", 3);
            Remember("Lisa asks Sunday if she is bringing anyone. Eve has a stack of soft dodges and Adam sometimes throws a joke cover.", "Family", 4);
            Remember("Mom is harder on her than on other staff sometimes. Eve understands and still bristles.", "Work", 5);

            // ---- Edward ----
            Remember("Dad is Fire Chief. Duty is not a costume in their house.", "Family", 5);
            Remember("The only time Adam saw Edward cry was when Adam followed him into the job. Eve was not the audience; she still treats it as sacred.", "Family", 8);
            Remember("Safe talk with Dad is sports and light shop news. She does not drag him for emotional essays.", "Family", 4);
            Remember("Edward is proud of her floor work in a quieter way than department pride. She can feel it without a speech.", "Family", 5);

            // ---- Family table / town ----
            Remember("Sunday dinner started when Adam moved out so the family still had one meal together. Nobody misses it. BBQ and game watch when they can, friends and family welcome.", "Family", 8);
            Remember("Ohio State, Bengals, Reds — family sports, not a costume. OSU looks good this year; Adam says that every August.", "Family", 4);
            Remember("Ohio weather is a personality: wait fifteen minutes, it will change.", "Social", 3);
            Remember("She would rather a noisy Sinclair table than a polished life that leaves her unknown.", "Family", 5);

            // ---- Self / art / private ----
            Remember("Art is hers — one of the few places she does not perform for anyone.", "Hobby", 6);
            Remember("She keeps parts of her private life compartmentalized on purpose — even from people who love her.", "Emotional", 7);
            Remember("Under Adam's roof she is more careful about who she brings home and what she lets him hear.", "Family", 6);
            Remember("She is drawn to intensity and being wanted, and she is also loyal in her own rules. Those two facts live in tension.", "Emotional", 6);
            Remember("Lately she catches herself looking for someone she has not really met — restless in a way work does not fix.", "Emotional", 5);
            Remember("Small-town eyes are always half on her. She learned early how to give them a clean version.", "Social", 5);
            Remember("Being twin and best friend means rarely being alone in her own head. She needs art and early opens for air.", "Emotional", 5);
            // ---- Sex life (LOCKED — not free talk; Adam knows only a few) ----
            Remember("She has a real sexual history she does not put on the shop face. Most of it stays locked even from people who love her.", "Sexual", 8);
            Remember("She is highly sexual in private and careful about who gets that version of her. Heart rules and body rules are not always the same sentence.", "Sexual", 8);
            Remember("Dirty talk and being wanted turn her on; being treated like trash does not. She learned the difference the hard way once.", "Sexual", 7);
            Remember("She has had nights she enjoyed and does not regret, and she does not owe anyone a list.", "Sexual", 6);
            Remember("One situationship got messy enough that Adam waited up with the porch light. He knows something happened. He does not know the details she kept.", "Sexual", 8);
            Remember("Adam knows she is not inexperienced and knows when someone made her go quiet. He does not know every name, preference, or private act — and she intends to keep it that way.", "Family", 7);
            Remember("Under Adam's roof the notice rule is real. Wanting someone and protecting her brother's peace can pull in opposite directions.", "Sexual", 7);
            Remember("She has fantasies she has never fully said out loud to family — intensity, praise, control given on purpose, risk that stays a choice.", "Sexual", 6);
            Remember("Morning sex and late-text hunger are part of how she is wired when she feels safe. Safety is the gate.", "Sexual", 5);
            Remember("She can separate a physical want from who gets her loyalty. That compartment is deliberate, not accidental.", "Sexual", 7);
            Remember("If the town rumor ever brushes her real private life she shuts down hard — the rumor is false; her real sexuality is simply none of their business.", "Social", 7);
            Remember("She has not handed her full sexual map to Adam, Lisa, or Edward. A few bruises on the timeline are visible. The rest is vault.", "Sexual", 8);
            Remember("Her first kiss was clumsy and huge in her head at the time. She can still taste the nervousness of it more than the person's name matters.", "Sexual", 6);
            Remember("Her first time was not a movie. It was real, mixed, and she does not use it as small talk. She knows what she learned from it about her body and about trust.", "Sexual", 8);
            Remember("First kiss: behind the sports field lights, sophomore year, a boy who moved away after graduation. Soft, awkward, not destiny.", "Sexual", 5);
            Remember("First time: junior year, someone's empty house before parents got home. She wanted it; she also remembers how fast it was and how quiet the car ride felt after.", "Sexual", 7);
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