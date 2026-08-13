using ProjectEve.Traits;
using System;

namespace ProjectEve.Characters.Emotion
{
    /// <summary>
    /// Legacy emotion meters kept for compile / UI compatibility.
    /// Gameplay truth is Fast traits on NpcTraits.
    /// Call SyncFromFast(traits) after trait updates if something still reads this.
    /// </summary>
    public class EmotionalProfile
    {
        public EmotionState State { get; private set; } = EmotionState.Neutral;
        public string Mood { get; private set; } = "Neutral";
        public float Intensity { get; set; } = 0.35f;

        public int Comfort { get; private set; } = 50;
        public int Stress { get; private set; } = 10;
        public int Happiness { get; private set; } = 45;
        public int Sadness { get; private set; } = 10;
        public int Anger { get; private set; } = 5;
        public int Affection { get; private set; } = 50;

        public int Desire { get; private set; } = 20;
        public int Resentment { get; private set; } = 0;
        public int Shame { get; private set; } = 0;
        public int Restlessness { get; private set; } = 15;
        public int Energy { get; private set; } = 70;

        public int CurrentMoodScore =>
            Happiness - Stress - Sadness - (Anger / 2) + (Comfort / 5);

        // =====================================================
        // PREFERRED: pull from Fast traits
        // =====================================================
        public void SyncFromFast(NpcTraits? traits)
        {
            if (traits == null)
                return;

            float T(string id) => traits.Get(id);

            Anger = Round(T("trait.anger"));
            Stress = Round((T("trait.anxiety") + T("trait.fear") + T("trait.tension")) / 3f);
            Affection = Round(T("trait.affection"));
            Desire = Round(T("trait.desire"));
            Resentment = Round(T("trait.resentment"));
            Shame = Round(Math.Max(T("trait.shame"), T("trait.guilt") * 0.85f));
            Sadness = Round((T("trait.hurt") + T("trait.loneliness")) / 2f);
            Happiness = Round((T("trait.hope") + T("trait.playfulness") + T("trait.affection")) / 3f);
            Comfort = Round(100f - (T("trait.anxiety") * 0.4f + T("trait.guard") * 0.3f + T("trait.hurt") * 0.3f));
            Restlessness = Round((T("trait.tension") + T("trait.desire") + (100f - T("trait.patience"))) / 3f);
            // Energy stays body-ish; leave unless you wire fatigue later

            RecalculateFromMeters();
        }

        // =====================================================
        // Direct setters — still work, but prefer SyncFromFast
        // =====================================================
        public void SetState(EmotionState state, float intensity = 0.5f)
        {
            State = state;
            Mood = state.ToString();
            Intensity = Math.Clamp(intensity, 0f, 1f);
        }

        public void AdjustComfort(int amount) { Comfort = Clamp(Comfort + amount); RecalculateFromMeters(); }
        public void AdjustStress(int amount) { Stress = Clamp(Stress + amount); RecalculateFromMeters(); }
        public void AddHappiness(int amount) { Happiness = Clamp(Happiness + amount); RecalculateFromMeters(); }
        public void AddSadness(int amount) { Sadness = Clamp(Sadness + amount); RecalculateFromMeters(); }
        public void AddAnger(int amount) { Anger = Clamp(Anger + amount); RecalculateFromMeters(); }
        public void AddStress(int amount) { Stress = Clamp(Stress + amount); RecalculateFromMeters(); }
        public void AddAffection(int amount) { Affection = Clamp(Affection + amount); RecalculateFromMeters(); }
        public void AddDesire(int amount) { Desire = Clamp(Desire + amount); RecalculateFromMeters(); }
        public void AddResentment(int amount) { Resentment = Clamp(Resentment + amount); RecalculateFromMeters(); }
        public void AddShame(int amount) { Shame = Clamp(Shame + amount); RecalculateFromMeters(); }
        public void AddRestlessness(int amount) { Restlessness = Clamp(Restlessness + amount); RecalculateFromMeters(); }
        public void AddEnergy(int amount) { Energy = Clamp(Energy + amount); RecalculateFromMeters(); }

        /// <summary>Old name kept.</summary>
        public void Recalculate() => RecalculateFromMeters();

        /// <summary>
        /// Recalculate the NPC's dominant outward/current mood.
        ///
        /// State/Mood is only the dominant presentation. The other meters remain
        /// active at the same time and can still influence behavior/prompts.
        /// </summary>
        public void RecalculateFromMeters()
        {
            if (Anger >= 80) { SetState(EmotionState.Angry, Anger / 100f); return; }
            if (Anger >= 60) { SetState(EmotionState.Irritated, Anger / 100f); return; }

            if (Stress >= 85) { SetState(EmotionState.Overwhelmed, Stress / 100f); return; }
            if (Stress >= 65) { SetState(EmotionState.Stressed, Stress / 100f); return; }

            if (Shame >= 70) { SetState(EmotionState.Ashamed, Shame / 100f); return; }
            if (Shame >= 55 && Desire >= 50) { SetState(EmotionState.GuiltyPleasure, 0.6f); return; }

            if (Resentment >= 75) { SetState(EmotionState.Vindictive, Resentment / 100f); return; }
            if (Resentment >= 55) { SetState(EmotionState.Spiteful, Resentment / 100f); return; }
            if (Resentment >= 40 && Anger >= 40) { SetState(EmotionState.Bitter, 0.55f); return; }

            if (Desire >= 75 && Stress < 50) { SetState(EmotionState.Horny, Desire / 100f); return; }
            if (Desire >= 60 && Shame >= 30) { SetState(EmotionState.Tempted, 0.65f); return; }
            if (Desire >= 55 && Restlessness >= 50) { SetState(EmotionState.Reckless, 0.6f); return; }
            if (Desire >= 50 && Energy >= 55 && Resentment >= 35) { SetState(EmotionState.Predatory, 0.6f); return; }

            if (Restlessness >= 70 && Happiness < 40) { SetState(EmotionState.Hollow, 0.55f); return; }
            if (Restlessness >= 60) { SetState(EmotionState.Restless, Restlessness / 100f); return; }

            // Check the more specific severe-fatigue state first.
            // Otherwise Energy <= 20 would make Numb unreachable.
            if (Energy <= 10 && Sadness >= 40) { SetState(EmotionState.Numb, 0.6f); return; }
            if (Energy <= 20) { SetState(EmotionState.Tired, 0.7f); return; }

            if (Sadness >= 70) { SetState(EmotionState.Sad, Sadness / 100f); return; }
            if (Sadness >= 50 && Affection >= 40) { SetState(EmotionState.Lonely, 0.55f); return; }

            if (Happiness >= 75 && Affection >= 70) { SetState(EmotionState.InLove, 0.7f); return; }
            if (Happiness >= 70 && Affection >= 55) { SetState(EmotionState.Affectionate, 0.6f); return; }
            if (Happiness >= 70) { SetState(EmotionState.Happy, Happiness / 100f); return; }
            if (Happiness >= 55 && Stress < 35) { SetState(EmotionState.Content, 0.45f); return; }

            if (Comfort >= 65 && Stress < 30) { SetState(EmotionState.Calm, 0.4f); return; }
            if (Comfort >= 55 && Stress < 35) { SetState(EmotionState.Soft, 0.4f); return; }

            if (Stress >= 40) { SetState(EmotionState.Uneasy, 0.45f); return; }
            if (Stress >= 30 && Happiness < 40) { SetState(EmotionState.Anxious, 0.5f); return; }

            SetState(EmotionState.Neutral, 0.3f);
        }

        /// <summary>
        /// Optional day drift on meters only. Prefer TraitEngine.UpdateTraitsDay on Fast.
        /// </summary>
        public void DriftTowardBaseline()
        {
            Comfort = Drift(Comfort, 50, 8);
            Stress = Drift(Stress, 10, 10);
            Happiness = Drift(Happiness, 45, 8);
            Sadness = Drift(Sadness, 10, 10);
            Anger = Drift(Anger, 5, 12);
            Desire = Drift(Desire, 20, 8);
            Resentment = Drift(Resentment, 0, 12);
            Shame = Drift(Shame, 0, 10);
            Restlessness = Drift(Restlessness, 15, 8);
            Energy = Drift(Energy, 70, 6);
            RecalculateFromMeters();
        }

        private static int Clamp(int value) => Math.Clamp(value, 0, 100);

        private static int Round(float v) => Clamp((int)Math.Round(v));

        private static int Drift(int value, int target, int divisor)
        {
            if (divisor < 1) divisor = 1;
            int step = (target - value) / divisor;
            if (step == 0 && value != target)
                step = value < target ? 1 : -1;
            return Clamp(value + step);
        }
    }
}