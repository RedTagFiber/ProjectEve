using ProjectEve.AI.Brain;
using ProjectEve.Characters.Emotion;
using ProjectEve.Characters.NPCs;
using ProjectEve.History;
using ProjectEve.Memory;
using ProjectEve.Money;
using ProjectEve.Relationships;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.Base
{
    /// <summary>
    /// Base for every NPC / player character.
    /// Traits live in NpcTraits (Fast / Mid / Slow). No InitializeFromRegistry.
    /// </summary>
    public class SimCharacter
    {
        // ============================================================
        // BASIC IDENTITY
        // ============================================================
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Gender { get; set; } = "Unknown";
        public string Occupation { get; set; } = "";
        public string Location { get; set; } = "Unknown";

        /// <summary>1 = close, 5 = lore/family graph only.</summary>
        public int Tier { get; set; } = 2;

        public string Hometown { get; set; } = "";
        /// <summary>Street / area. Not named Address (avoids type name clash).</summary>
        public string HomeAddress { get; set; } = "";

        public DateTime? BirthDate { get; set; }
        public string Zodiac { get; set; } = "";

        public string PersonalityContext { get; set; } = "";
        public List<string> PersonalityTags { get; set; } = new();

        // ============================================================
        // APPEARANCE (prompt + sheet)
        // ============================================================
        public NPCAppearance Appearance { get; set; } = new();

        public int? HeightCm { get; set; }
        public int? WeightKg { get; set; }
        public string BodyShape { get; set; } = "";
        public string HairColor { get; set; } = "";
        public string HairStyle { get; set; } = "";
        public string EyeColor { get; set; } = "";
        public string EyeStyle { get; set; } = "";
        public string SkinTone { get; set; } = "";
        public string Glasses { get; set; } = "";   // none | reading | always | style
        public string ScarNotes { get; set; } = "";

        // ============================================================
        // BRAIN / MONEY / JOB
        // ============================================================
        public Brain Brain { get; set; } = new();
        public MoneyProfile Money { get; set; } = new();
        public JobProfile Job { get; set; } = new();

        // ============================================================
        // DRIVES
        // ============================================================
        public string Goal { get; set; } = "";
        public string Need { get; set; } = "";
        public string Fear { get; set; } = "";
        public string Want { get; set; } = "";

        // ============================================================
        // TRAITS (Fast / Mid / Slow bag)
        // ============================================================
        public NpcTraits Traits { get; set; } = new();

        public float GetTraitValue(string traitId) => Traits.Get(traitId);
        public float GetTraitIntensity(string traitId) => Traits.Get(traitId);

        public bool HasTrait(string traitId, float threshold = 60f)
            => Traits.Get(traitId) >= threshold;

        public void SetTrait(string traitId, float value)
            => Traits.Set(traitId, value);

        public void AdjustTrait(string traitId, float amount)
            => Traits.Adjust(traitId, amount);

        public void DebugSetTrait(string traitId, int value)
            => Traits.Set(traitId, value);

        // ============================================================
        // EMOTION (legacy mirror — prefer Traits Fast)
        // ============================================================
        public EmotionalProfile Emotion { get; set; } = new();

        // ============================================================
        // RELATIONSHIPS / MEMORY / HISTORY
        // ============================================================
        public List<Relationship> Relationships { get; set; } = new();

        public MemoryDatabase MemoryDB { get; set; } = new();

        public void Remember(string summary, string category, int importance = 1)
        {
            MemoryDB.AddMemory(new MemoryRecord
            {
                NpcId = Id,
                CharacterName = Name,
                Summary = summary,
                Category = category,
                Importance = Math.Clamp(importance, 1, 100),
                Strength = Math.Clamp(40f + importance * 0.5f, 20f, 100f),
                IsLockedPeak = importance >= 85,
                Timestamp = DateTime.UtcNow
            });
        }

        public List<HistoryRecord> History { get; set; } = new();

        public List<string> Schedule { get; set; } = new();
        public List<string> ConversationTopics { get; set; } = new();
        public object? TravelPlan { get; set; }

        // ============================================================
        // CONSTRUCTOR
        // ============================================================
        public SimCharacter(string name, int age)
        {
            Name = name ?? "";
            Age = age;
            Appearance = new NPCAppearance();
            Schedule = new();
            ConversationTopics = new();
            TravelPlan = null;
            // Traits stay empty until CharacterFactory / TraitJsonLoader.ApplyRolledLayers
        }

        public SimCharacter() : this("Unknown", 25) { }

        /// <summary>Age from BirthDate when set; else stored Age.</summary>
        public int DerivedAge(DateTime? asOf = null)
        {
            if (BirthDate == null) return Age;
            var now = asOf ?? DateTime.Now;
            int years = now.Year - BirthDate.Value.Year;
            if (now.Date < BirthDate.Value.Date.AddYears(years))
                years--;
            return Math.Max(0, years);
        }
    }
}