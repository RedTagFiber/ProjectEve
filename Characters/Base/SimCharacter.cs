using ProjectEve.Characters.Emotion;
using ProjectEve.History;
using ProjectEve.Characters.NPCs;
using ProjectEve.Memory;
using ProjectEve.Money;
using ProjectEve.Relationships;
using ProjectEve.Traits;
using ProjectEve.AI.Brain;
using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.Base
{
    /// <summary>
    /// Base class for all NPCs in Project Eve.
    /// Holds identity, traits, emotions, relationships, memory, and history.
    /// </summary>
    public class SimCharacter
    {
        // ============================================================
        // BASIC IDENTITY
        // ============================================================
        public int Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public NPCAppearance Appearance { get; set; }
        public string Gender { get; set; } = "Unknown";

        public Brain Brain { get; set; } = new Brain();

        // ============================================================
        // MONEY PROFILE
        // ============================================================
        public MoneyProfile Money { get; set; } = new MoneyProfile();
        public JobProfile Job { get; set; } = new JobProfile();
        // ============================================================
        // CORE MOTIVATIONS
        // ============================================================
        public string Goal { get; set; } = "";
        public string Need { get; set; } = "";
        public string Fear { get; set; } = "";
        public string Want { get; set; } = "";

        // ============================================================
        // LOCATION
        // ============================================================
        public string Location { get; set; } = "Unknown";
        public string Occupation { get; set; } = string.Empty;
        public List<string> PersonalityTags { get; set; } = new();

        // ============================================================
        // TRAITS (NEW 0–100 SYSTEM)
        // ============================================================
        public NpcTraits Traits { get; set; } = new NpcTraits();

        /// <summary>
        /// Returns the current value of a trait (0–100).
        /// </summary>
        public float GetTraitValue(string traitId)
        {
            return Traits.Get(traitId);
        }

        /// <summary>
        /// Alias for older code that called GetTraitIntensity.
        /// </summary>
        public float GetTraitIntensity(string traitId)
        {
            return GetTraitValue(traitId);
        }

        /// <summary>
        /// Returns true if the trait value meets or exceeds the threshold.
        /// </summary>
        public bool HasTrait(string traitId, float threshold = 60f)
        {
            return GetTraitValue(traitId) >= threshold;
        }

        /// <summary>
        /// Directly set a trait value (0–100).
        /// </summary>
        public void SetTrait(string traitId, float value)
        {
            Traits.Set(traitId, value);
        }

        /// <summary>
        /// Adjust a trait by a positive or negative amount.
        /// </summary>
        public void AdjustTrait(string traitId, float amount)
        {
            Traits.Adjust(traitId, amount);
        }

        /// <summary>
        /// Debug helper (keeps old name working).
        /// </summary>
        public void DebugSetTrait(string traitId, int value)
        {
            Traits.Set(traitId, value);
        }

        public void UpdateTraits()
        {
            // Optional: call your TraitEngine here later
            // TraitEngine.UpdateTraits(this);
        }

        // ============================================================
        // EMOTIONAL STATE
        // ============================================================
        public EmotionalProfile Emotion { get; set; } = new EmotionalProfile();

        // ============================================================
        // RELATIONSHIPS
        // ============================================================
        public List<Relationship> Relationships { get; set; } = new();

        // ============================================================
        // MEMORY SYSTEM
        // ============================================================
        public MemoryDatabase MemoryDB { get; set; } = new MemoryDatabase();

        /// <summary>
        /// Adds a memory to the character's memory database.
        /// </summary>
        public void Remember(string summary, string category, int importance = 1)
        {
            MemoryDB.AddMemory(new MemoryRecord
            {
                CharacterName = Name,
                Summary = summary,
                Category = category,
                Importance = importance,
                Timestamp = DateTime.Now
            });
        }

        // ============================================================
        // HISTORY SYSTEM
        // ============================================================
        public List<HistoryRecord> History { get; set; } = new();
        public List<string> Schedule { get; internal set; } = new();
        public List<string> ConversationTopics { get; internal set; } = new();
        public object? TravelPlan { get; internal set; }
        public string PersonalityContext { get; set; } = "";

        // ============================================================
        // CONSTRUCTOR
        // ============================================================
        public SimCharacter(string name, int age)
        {
            Name = name;
            Age = age;
            Appearance = new NPCAppearance();
            Schedule = new List<string>();
            ConversationTopics = new List<string>();
            TravelPlan = null;

            // Load every trait at its default value
            Traits.InitializeFromRegistry();
        }
    }
}