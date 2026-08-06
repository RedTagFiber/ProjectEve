using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Traits
{
    /// <summary>
    /// Holds the live 0–100 trait values for a single NPC.
    /// Every trait from TraitRegistry can grow or shrink over time.
    /// </summary>
    public class NpcTraits
    {
        // traitId → current value (0–100)
        private readonly Dictionary<string, float> _values = new();

        /// <summary>
        /// Creates an empty trait set. Call InitializeFromRegistry() after.
        /// </summary>
        public NpcTraits() { }

        /// <summary>
        /// Loads every trait from TraitRegistry and sets it to its DefaultValue.
        /// Call this once when the NPC is created.
        /// </summary>
        public void InitializeFromRegistry()
        {
            _values.Clear();

            if (TraitRegistry.AllTraits == null || TraitRegistry.AllTraits.Count == 0)
            {
                TraitRegistry.LoadBaseTraits();
            }

            var allTraits = TraitRegistry.AllTraits;
            if (allTraits == null || allTraits.Count == 0)
                return;

            foreach (var def in allTraits)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Id))
                    continue;

                _values[def.Id] = def.DefaultValue;
            }
        }

        /// <summary>
        /// Gets the current value of a trait (0–100).
        /// Returns 50 if the trait is unknown.
        /// </summary>
        public float Get(string traitId)
        {
            if (string.IsNullOrWhiteSpace(traitId))
                return 50f;

            return _values.TryGetValue(traitId, out var value) ? value : 50f;
        }

        /// <summary>
        /// Sets a trait to an exact value (clamped to the trait's min/max).
        /// </summary>
        public void Set(string traitId, float value)
        {
            if (string.IsNullOrWhiteSpace(traitId))
                return;

            var def = TraitRegistry.GetDefinition(traitId);
            float min = def?.MinValue ?? 0f;
            float max = def?.MaxValue ?? 100f;

            _values[traitId] = Math.Clamp(value, min, max);
        }

        /// <summary>
        /// Adjusts a trait by a positive or negative amount.
        /// Example: Adjust("trait.libido", +5) or Adjust("trait.anxiety", -3)
        /// </summary>
        public void Adjust(string traitId, float amount)
        {
            Set(traitId, Get(traitId) + amount);
        }

        /// <summary>
        /// Returns true if this NPC has the given trait stored.
        /// </summary>
        public bool Has(string traitId)
        {
            return !string.IsNullOrWhiteSpace(traitId) && _values.ContainsKey(traitId);
        }

        /// <summary>
        /// Returns all current trait values (read-only copy).
        /// </summary>
        public IReadOnlyDictionary<string, float> GetAll()
        {
            return _values;
        }

        /// <summary>
        /// Returns the top N traits that deviate most from 50 (most "extreme").
        /// Useful for building short personality summaries for the LLM.
        /// </summary>
        public List<(string TraitId, float Value, float Deviation)> GetMostExtreme(int count = 8)
        {
            return _values
                .Select(kvp => (TraitId: kvp.Key, Value: kvp.Value, Deviation: Math.Abs(kvp.Value - 50f)))
                .OrderByDescending(x => x.Deviation)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Builds a short text block of the most relevant traits for LLM prompts.
        /// Only includes traits that are noticeably high or low.
        /// </summary>
        public string BuildLlmSummary(int maxTraits = 10)
        {
            var extremes = GetMostExtreme(maxTraits);
            if (extremes.Count == 0)
                return "No strong traits.";

            var lines = new List<string>();

            foreach (var (traitId, value, _) in extremes)
            {
                var def = TraitRegistry.GetDefinition(traitId);
                string name = def?.Name ?? traitId;
                string context = def?.LlmContext ?? "";

                string level = value >= 70 ? "High" :
                               value <= 30 ? "Low" : "Moderate";

                lines.Add($"{name}: {level} ({value:0}) — {context}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Convenience helpers for common traits (optional but nice).
        /// </summary>
        public float Libido => Get("trait.libido");
        public float SecrecyKink => Get("trait.secrecyKink");
        public float Submission => Get("trait.submission");
        public float Dominance => Get("trait.dominance");
        public float PlayerAffection => Get("trait.playerAffection");
        public float PlayerTrust => Get("trait.playerTrust");
        public float Anxiety => Get("trait.anxiety");
        public float Empathy => Get("trait.empathy");
    }
}