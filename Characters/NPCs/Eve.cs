using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Relationships;

namespace ProjectEve.Characters.NPCs
{
    public class Eve : SimCharacter
    {
        public Eve() : base("Eve", 25)
        {
            // ============================================================
            // IDENTITY
            // ============================================================
            Gender = "Female";
            Occupation = "Coffee shop manager";
            Location = "Bellefontaine / Sidney, Ohio area";

            PersonalityContext =
                "Publicly the competent good-girl manager. " +
                "Privately highly sexual, self-aware, and loyal in her own rules. " +
                "Heart stays with Ryan. Body can be free. Double life turns her on.";
            // ============================================================
            // MONEY (tight coffee-shop manager life)
            // ============================================================
            Money.Cash = 95m;
            Money.Bank = 320m;
            Money.Debt = 0m;
            Money.Bills = 0.42m;
            Money.Food = 0.25m;
            Money.Entertainment = 0.12m;
            Money.SavingsRate = 0.08m;
            Money.HobbySpending = 0.13m;
            Job = new JobProfile
            {
                JobName = "Manager",
                Employer = "Local coffee shop",
                JobType = "Retail/Food service",
                StartHour = 6,
                EndHour = 14,
                HourlyRate = 18.50m,
                WeeklyHours = 40,
                MonthlyBills = 1800m,
                StressLevel = 45,
                SocialLevel = 70,
                PhysicalDemand = 40,
                BurnoutLevel = 25,
                HasInsurance = false
            };

            // ============================================================
            // CORE MOTIVATIONS
            // ============================================================
            Goal = "Build a real life with Ryan without shrinking who she is";
            Need = "Connection and honesty that doesn’t demand she become smaller";
            Fear = "Being abandoned once someone sees all of her";
            Want = "To be wanted as both the soft partner and the dirty one";

            // ============================================================
            // RELATIONSHIPS
            // ============================================================
            Relationships.Add(new Relationship
            {
                TargetName = "Ryan",
                Trust = 80,
                Respect = 75,
                Affection = 90,
                Attraction = 95
            });

            // ============================================================
            // EMOTION BASELINE
            // ============================================================
            Emotion.SetState(EmotionState.Calm, 0.35f);
            Emotion.AdjustComfort(15);     // toward warmer baseline
            Emotion.AddAffection(20);
            Emotion.AddDesire(10);
            Emotion.AddEnergy(5);

            // ============================================================
            // TRAIT SEED (TraitRegistry ids)
            // Keep identity traits high/low. Leave fillers near default.
            // ============================================================
            SetTrait("trait.secrecyKink", 85);
            SetTrait("trait.doubleLifeComfort", 80);
            SetTrait("trait.sexualCompartmentalization", 70);
            SetTrait("trait.nonMonogamyComfort", 75);
            SetTrait("trait.compersion", 65);
            SetTrait("trait.cuckoldInterest", 60);

            SetTrait("trait.sexualConfidence", 75);
            SetTrait("trait.sexualCuriosity", 78);
            SetTrait("trait.sexualShame", 20);
            SetTrait("trait.roughnessPreference", 60);
            SetTrait("trait.degradationDesire", 55);
            SetTrait("trait.praiseKink", 70);
            SetTrait("trait.aftercareNeed", 72);
            SetTrait("trait.oralFixation", 65);
            SetTrait("trait.submission", 58);
            SetTrait("trait.dominance", 42);
            SetTrait("trait.possessiveDesire", 55);
            SetTrait("trait.ownershipDesire", 50);
            SetTrait("trait.publicRisk", 55);
            SetTrait("trait.exhibitionism", 45);
            SetTrait("trait.groupInterest", 50);

            SetTrait("trait.empathy", 70);
            SetTrait("trait.sensitivity", 65);
            SetTrait("trait.confidence", 62);
            SetTrait("trait.insecurity", 40);
            SetTrait("trait.anxiety", 38);
            SetTrait("trait.anger", 32);
            SetTrait("trait.impulsiveness", 58);
            SetTrait("trait.moodStability", 55);
            SetTrait("trait.stoicism", 40);
            SetTrait("trait.optimism", 55);
            SetTrait("trait.pessimism", 35);

            SetTrait("trait.extroversion", 60);
            SetTrait("trait.introversion", 40);
            SetTrait("trait.creativity", 55);
            SetTrait("trait.focus", 58);
            SetTrait("trait.logic", 55);

            // ============================================================
            // MEMORY SEED
            // ============================================================
            Remember("Met Ryan at the coffee shop and kept noticing him.", "Social", 4);
            Remember("Ryan accepted the dirty side without treating her like trash.", "Emotional", 8);
            Remember("Sleeping over at Ryan's felt safer than it should have.", "Emotional", 7);
            Remember("Tessa crossed a line at work and it complicated everything.", "Social", 6);

            Appearance = new NPCAppearance();
        }
    }
}