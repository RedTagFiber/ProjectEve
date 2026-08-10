using System;
using System.Collections.Generic;

namespace ProjectEve.History
{
    /// <summary>
    /// One life-history node on an NPC.
    /// Built from trait history patterns; may reference Tier-5 actors (family/friends off-stage).
    /// </summary>
    public class HistoryRecord
    {
        public string Id { get; set; } = "";

        /// <summary>Age when it happened.</summary>
        public int Age { get; set; }

        /// <summary>Optional calendar year.</summary>
        public int? Year { get; set; }

        /// <summary>Short summary (hook line).</summary>
        public string? Summary { get; set; }

        /// <summary>LLM-expanded flashback text (optional until generated).</summary>
        public string? StoryText { get; set; }

        /// <summary>family | friend | school | work | romance | stranger | trauma | joy | other</summary>
        public string? Category { get; set; }

        /// <summary>1–100 impact / intensity.</summary>
        public int Importance { get; set; } = 1;

        /// <summary>trait.anger, mid.loyal, etc.</summary>
        public string PrimaryTraitId { get; set; } = "";

        public List<string> LinkedTraitIds { get; set; } = new();

        /// <summary>Pattern id from TraitJson history catalog (anti-repeat).</summary>
        public string PatternId { get; set; } = "";

        /// <summary>Trust needed before this NPC tells the player the real version.</summary>
        public int TrustGate { get; set; } = 40;

        /// <summary>0–1 how much the story mutates when Tier-1–4 retell it.</summary>
        public float TelephoneNoise { get; set; } = 0.25f;

        /// <summary>
        /// People in the event: display names and/or Tier-5 NpcIds.
        /// Example: "Mom", "npc:50012", "first boss".
        /// </summary>
        public List<string> ActorSlots { get; set; } = new();

        /// <summary>True if any ActorSlot is a Tier-5 (lore / family graph) character.</summary>
        public bool InvolvesTier5 { get; set; }

        /// <summary>Peak events (first kiss, death) resist memory decay.</summary>
        public bool IsLockedPeak { get; set; }

        public float MemoryStrength { get; set; } = 80f;

        public DateTime? CreatedAt { get; set; }
        public DateTime? LastRecalledAt { get; set; }

        public HistoryRecord() { }

        public HistoryRecord(int age, string summary, string category = "other", int importance = 1)
        {
            Age = age;
            Summary = summary;
            Category = category;
            Importance = Math.Clamp(importance, 1, 100);
            MemoryStrength = Math.Clamp(40f + importance * 0.5f, 20f, 100f);
            Id = $"hr.{Guid.NewGuid():N}".Substring(0, 16);
            CreatedAt = DateTime.UtcNow;
        }

        public bool CanReveal(int relationshipTrust)
            => relationshipTrust >= TrustGate;

        public void MarkRecalled()
        {
            LastRecalledAt = DateTime.UtcNow;
            MemoryStrength = Math.Clamp(MemoryStrength + 2f, 0f, 100f);
        }

        public void Degrade(float amount = 1f)
        {
            if (IsLockedPeak) amount *= 0.1f;
            MemoryStrength = Math.Clamp(MemoryStrength - amount, IsLockedPeak ? 70f : 5f, 100f);
        }

        /// <summary>
        /// Attach a Tier-5 subject (family graph NPC id + label).
        /// </summary>
        public void AddTier5Actor(int tier5NpcId, string label)
        {
            ActorSlots.Add($"npc:{tier5NpcId}");
            if (!string.IsNullOrWhiteSpace(label))
                ActorSlots.Add(label);
            InvolvesTier5 = true;
        }
    }
}