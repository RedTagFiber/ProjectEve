using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.Traits.History
{
    /// <summary>
    /// One past event on an NPC timeline.
    /// Can be rolled from TraitJson history patterns, then expanded by LLM into a short story.
    /// </summary>
    public class HistoryEvent
    {
        /// <summary>Stable id for anti-repeat + SQLite (e.g. hist.anger.boss_01).</summary>
        public string Id { get; set; } = "";

        /// <summary>Short title — "Parents Divorced", "First real fight with Ryan".</summary>
        public string Name { get; set; } = "";

        /// <summary>One-line summary (hook). Full prose lives in StoryText when expanded.</summary>
        public string Description { get; set; } = "";

        /// <summary>Expanded short-story / flashback body (optional until LLM pass).</summary>
        public string StoryText { get; set; } = "";

        /// <summary>Age when it happened (timeline).</summary>
        public int Age { get; set; }

        /// <summary>Optional calendar year if known.</summary>
        public int? Year { get; set; }

        /// <summary>1–100 importance / intensity. High = harder to forget, stronger trait lock.</summary>
        public int Importance { get; set; } = 1;

        /// <summary>family | friend | school | work | romance | stranger | trauma | joy | other</summary>
        public string Category { get; set; } = "other";

        /// <summary>Which Fast/Mid trait this mainly explains (e.g. trait.anger, mid.loyal).</summary>
        public string PrimaryTraitId { get; set; } = "";

        /// <summary>Extra trait ids this event supports.</summary>
        public List<string> LinkedTraitIds { get; set; } = new();

        /// <summary>Pattern id from TraitJson history catalog (anti-repeat key).</summary>
        public string PatternId { get; set; } = "";

        /// <summary>0–100 trust needed before NPC tells player the real version.</summary>
        public int TrustGate { get; set; } = 40;

        /// <summary>How distorted the story gets when retold by others (telephone game).</summary>
        public float TelephoneNoise { get; set; } = 0.25f;

        /// <summary>Who was involved (names or roles: "boss", "mom", tier-5 id).</summary>
        public List<string> ActorSlots { get; set; } = new();

        /// <summary>true = locked peak (first kiss, death) — degrades very slowly.</summary>
        public bool IsLockedPeak { get; set; }

        /// <summary>Current memory strength 0–100 (degrades over time unless locked).</summary>
        public float MemoryStrength { get; set; } = 80f;

        public DateTime? CreatedAt { get; set; }
        public DateTime? LastRecalledAt { get; set; }

        public HistoryEvent() { }

        public HistoryEvent(string name, string description, int age, int importance = 1)
        {
            Name = name;
            Description = description;
            Age = age;
            Importance = Math.Clamp(importance, 1, 100);
            MemoryStrength = Math.Clamp(40f + importance * 0.5f, 20f, 100f);
            Id = $"hist.{Guid.NewGuid():N}".Substring(0, 20);
        }

        /// <summary>Whether this NPC would open up about it at this trust level.</summary>
        public bool CanReveal(int relationshipTrust)
            => relationshipTrust >= TrustGate;

        /// <summary>
        /// Soft degrade. Locked peaks barely move.
        /// </summary>
        public void Degrade(float amount = 1f)
        {
            if (IsLockedPeak)
                amount *= 0.1f;
            MemoryStrength = Math.Clamp(MemoryStrength - amount, IsLockedPeak ? 70f : 5f, 100f);
        }

        public void MarkRecalled()
        {
            LastRecalledAt = DateTime.UtcNow;
            // recall slightly refreshes
            MemoryStrength = Math.Clamp(MemoryStrength + 2f, 0f, 100f);
        }
    }
}