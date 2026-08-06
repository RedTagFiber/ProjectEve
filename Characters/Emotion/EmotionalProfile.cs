using System;

namespace ProjectEve.Characters.Emotion
{
    /// <summary>
    /// Current emotional state for any NPC.
    /// Changes from time, traits, memories, and events.
    /// </summary>
    public class EmotionalProfile
    {
        // =====================================================
        // CORE STATE
        // =====================================================
        public EmotionState State { get; private set; } = EmotionState.Neutral;
        public string Mood { get; private set; } = "Neutral";
        public float Intensity { get; set; } = 0.35f; // 0..1 how strong the current state feels

        // =====================================================
        // BASIC METERS (0..100)
        // =====================================================
        public int Comfort { get; private set; } = 50;
        public int Stress { get; private set; } = 10;
        public int Happiness { get; private set; } = 45;
        public int Sadness { get; private set; } = 10;
        public int Anger { get; private set; } = 5;
        public int Affection { get; private set; } = 50;

        // =====================================================
        // SHADOW METERS (0..100)
        // These unlock the less-nice states
        // =====================================================
        public int Desire { get; private set; } = 20;      // wanting / temptation
        public int Resentment { get; private set; } = 0;   // spite, bitterness
        public int Shame { get; private set; } = 0;        // guilt/shame after acts
        public int Restlessness { get; private set; } = 15; // boredom, need for stimulation
        public int Energy { get; private set; } = 70;

        // Optional simple score if something still reads it
        public int CurrentMoodScore =>
            Happiness - Stress - Sadness - (Anger / 2) + (Comfort / 5);

        // =====================================================
        // SETTERS / ADJUSTERS
        // =====================================================
        public void SetState(EmotionState state, float intensity = 0.5f)
        {
            State = state;
            Mood = state.ToString();
            Intensity = Math.Clamp(intensity, 0f, 1f);
        }

        public void AdjustComfort(int amount)
        {
            Comfort = Clamp(Comfort + amount);
            Recalculate();
        }

        public void AdjustStress(int amount)
        {
            Stress = Clamp(Stress + amount);
            Recalculate();
        }

        public void AddHappiness(int amount)
        {
            Happiness = Clamp(Happiness + amount);
            Recalculate();
        }

        public void AddSadness(int amount)
        {
            Sadness = Clamp(Sadness + amount);
            Recalculate();
        }

        public void AddAnger(int amount)
        {
            Anger = Clamp(Anger + amount);
            Recalculate();
        }

        public void AddStress(int amount)
        {
            Stress = Clamp(Stress + amount);
            Recalculate();
        }

        public void AddAffection(int amount)
        {
            Affection = Clamp(Affection + amount);
            Recalculate();
        }

        public void AddDesire(int amount)
        {
            Desire = Clamp(Desire + amount);
            Recalculate();
        }

        public void AddResentment(int amount)
        {
            Resentment = Clamp(Resentment + amount);
            Recalculate();
        }

        public void AddShame(int amount)
        {
            Shame = Clamp(Shame + amount);
            Recalculate();
        }

        public void AddRestlessness(int amount)
        {
            Restlessness = Clamp(Restlessness + amount);
            Recalculate();
        }

        public void AddEnergy(int amount)
        {
            Energy = Clamp(Energy + amount);
            Recalculate();
        }

        // =====================================================
        // MAIN MOOD RESOLUTION
        // Priority: strong negative/shadow first, then soft states
        // =====================================================
        public void Recalculate()
        {
            // Hard negatives / danger
            if (Anger >= 80) { SetState(EmotionState.Angry, Anger / 100f); return; }
            if (Anger >= 60) { SetState(EmotionState.Irritated, Anger / 100f); return; }

            if (Stress >= 85) { SetState(EmotionState.Overwhelmed, Stress / 100f); return; }
            if (Stress >= 65) { SetState(EmotionState.Stressed, Stress / 100f); return; }

            if (Shame >= 70) { SetState(EmotionState.Ashamed, Shame / 100f); return; }
            if (Shame >= 55 && Desire >= 50) { SetState(EmotionState.GuiltyPleasure, 0.6f); return; }

            if (Resentment >= 75) { SetState(EmotionState.Vindictive, Resentment / 100f); return; }
            if (Resentment >= 55) { SetState(EmotionState.Spiteful, Resentment / 100f); return; }
            if (Resentment >= 40 && Anger >= 40) { SetState(EmotionState.Bitter, 0.55f); return; }

            // Desire / bad-urge states
            if (Desire >= 75 && Stress < 50) { SetState(EmotionState.Horny, Desire / 100f); return; }
            if (Desire >= 60 && Shame >= 30) { SetState(EmotionState.Tempted, 0.65f); return; }
            if (Desire >= 55 && Restlessness >= 50) { SetState(EmotionState.Reckless, 0.6f); return; }
            if (Desire >= 50 && Energy >= 55 && Resentment >= 35) { SetState(EmotionState.Predatory, 0.6f); return; }

            if (Restlessness >= 70 && Happiness < 40) { SetState(EmotionState.Hollow, 0.55f); return; }
            if (Restlessness >= 60) { SetState(EmotionState.Restless, Restlessness / 100f); return; }

            if (Energy <= 20) { SetState(EmotionState.Tired, 0.7f); return; }
            if (Energy <= 10 && Sadness >= 40) { SetState(EmotionState.Numb, 0.6f); return; }

            // Soft / social
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

        // =====================================================
        // DRIFT OVER TIME
        // Call on day tick / between scenes
        // =====================================================
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

            Recalculate();
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private static int Clamp(int value) => Math.Clamp(value, 0, 100);

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