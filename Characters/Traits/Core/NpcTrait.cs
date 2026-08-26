using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Traits
{
    /// <summary>
    /// Live 0–100 trait values for one character.
    /// Fast = chat speed, Mid = character, Slow = taste.
    /// Do NOT init from old TraitRegistry.AllTraits.
    /// </summary>
    public class NpcTraits
    {
        private readonly Dictionary<string, float> _values = new(StringComparer.OrdinalIgnoreCase);

        // Optional: which style is active for a Fast trait (e.g. anger.verbal)
        private readonly Dictionary<string, string> _styles = new(StringComparer.OrdinalIgnoreCase);

        // Set-points for Fast drift (prior intensity * 10, or explicit)
        private readonly Dictionary<string, float> _setPoints = new(StringComparer.OrdinalIgnoreCase);

        public NpcTraits() { }

        // ------------------------------------------------------------------
        // INIT — call from CharacterFactory after rolling the character
        // ------------------------------------------------------------------

        /// <summary>
        /// Seed only the traits this NPC actually has.
        /// fast: all 20 Fast ids with starting values.
        /// mid: 3–6 Mid ids.
        /// slow: parents (and later subs as separate keys if you want).
        /// </summary>
        public void InitializeFromLayers(
            IDictionary<string, float> fast,
            IDictionary<string, float>? mid = null,
            IDictionary<string, float>? slow = null,
            IDictionary<string, float>? setPoints = null,
            IDictionary<string, string>? styles = null)
        {
            _values.Clear();
            _styles.Clear();
            _setPoints.Clear();

            if (fast != null)
            {
                foreach (var kv in fast)
                    _values[kv.Key] = Clamp(kv.Value);
            }

            if (mid != null)
            {
                foreach (var kv in mid)
                    _values[kv.Key] = Clamp(kv.Value);
            }

            if (slow != null)
            {
                foreach (var kv in slow)
                    _values[kv.Key] = Clamp(kv.Value);
            }

            if (setPoints != null)
            {
                foreach (var kv in setPoints)
                    _setPoints[kv.Key] = Clamp(kv.Value);
            }
            else
            {
                // default set-point = starting value for anything we have
                foreach (var kv in _values)
                    _setPoints[kv.Key] = kv.Value;
            }

            if (styles != null)
            {
                foreach (var kv in styles)
                    _styles[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Minimal boot when factory is not ready yet: Fast 20 at neutral 40–60.
        /// Prefer InitializeFromLayers in real create path.
        /// </summary>
        public void InitializeFastDefaults()
        {
            string[] fast20 =
            {
                "trait.anger", "trait.anxiety", "trait.fear", "trait.shame", "trait.guilt",
                "trait.hurt", "trait.jealousy", "trait.resentment", "trait.trust", "trait.affection",
                "trait.desire", "trait.attraction", "trait.tension", "trait.playfulness", "trait.pride",
                "trait.patience", "trait.guard", "trait.openness", "trait.loneliness", "trait.hope"
            };

            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in fast20)
                map[id] = 45f;

            InitializeFromLayers(map);
        }

        // ------------------------------------------------------------------
        // READ / WRITE
        // ------------------------------------------------------------------

        public float Get(string traitId)
        {
            if (string.IsNullOrWhiteSpace(traitId))
                return 50f;

            return _values.TryGetValue(traitId, out var value) ? value : 50f;
        }

        public void Set(string traitId, float value)
        {
            if (string.IsNullOrWhiteSpace(traitId))
                return;

            _values[traitId] = Clamp(value);
        }

        public void Adjust(string traitId, float amount)
        {
            if (string.IsNullOrWhiteSpace(traitId))
                return;

            Set(traitId, Get(traitId) + amount);
        }

        public bool Has(string traitId)
        {
            return !string.IsNullOrWhiteSpace(traitId) && _values.ContainsKey(traitId);
        }

        public IReadOnlyDictionary<string, float> GetAll() => _values;

        public float GetSetPoint(string traitId)
        {
            if (_setPoints.TryGetValue(traitId, out var sp))
                return sp;
            return 50f;
        }

        public void SetSetPoint(string traitId, float value)
        {
            if (string.IsNullOrWhiteSpace(traitId))
                return;
            _setPoints[traitId] = Clamp(value);
        }

        public string? GetStyle(string traitId)
        {
            return _styles.TryGetValue(traitId, out var s) ? s : null;
        }

        public void SetStyle(string traitId, string styleId)
        {
            if (string.IsNullOrWhiteSpace(traitId) || string.IsNullOrWhiteSpace(styleId))
                return;
            _styles[traitId] = styleId;
        }

        /// <summary>
        /// Derived Fast20 stage for the current value.
        /// Stage and Wildcard are intentionally NOT persisted in SQL.
        /// </summary>
        public FastTraitStage GetFastStage(string traitId)
        {
            return FastTraitStageRules.Get(Get(traitId));
        }

        /// <summary>
        /// True only for the locked 95-100 Fast20 wildcard range.
        /// </summary>
        public bool IsFastWildcard(string traitId)
        {
            return FastTraitStageRules.IsWildcard(Get(traitId));
        }

        /// <summary>
        /// Drift Fast traits a step toward set-point (call on day tick / scene end).
        /// Mid/Slow should not be passed here often.
        /// </summary>
        public void DriftTowardSetPoints(IEnumerable<string> traitIds, float step = 1f)
        {
            foreach (var id in traitIds)
            {
                if (!Has(id)) continue;
                float cur = Get(id);
                float target = GetSetPoint(id);
                if (Math.Abs(cur - target) < 0.5f) continue;
                float dir = cur < target ? step : -step;
                Set(id, cur + dir);
            }
        }

        // ------------------------------------------------------------------
        // LLM / DEBUG
        // ------------------------------------------------------------------

        public List<(string TraitId, float Value, float Deviation)> GetMostExtreme(int count = 8)
        {
            return _values
                .Select(kvp =>
                {
                    float baseline = GetSetPoint(kvp.Key);
                    return (TraitId: kvp.Key, Value: kvp.Value, Deviation: Math.Abs(kvp.Value - baseline));
                })
                .OrderByDescending(x => x.Deviation)
                .Take(count)
                .ToList();
        }

        public string BuildLlmSummary(int maxTraits = 10)
        {
            var extremes = GetMostExtreme(maxTraits);
            if (extremes.Count == 0)
                return "No strong traits.";

            var lines = new List<string>();
            foreach (var (traitId, value, _) in extremes)
            {
                string level = value >= 70 ? "High" :
                               value <= 30 ? "Low" : "Moderate";
                string style = GetStyle(traitId) ?? "";
                string styleBit = string.IsNullOrEmpty(style) ? "" : $" [{style}]";
                lines.Add($"{traitId}: {level} ({value:0}){styleBit}");
            }

            return string.Join("\n", lines);
        }
        

        // ------------------------------------------------------------------
        // Fast convenience (optional)
        // ------------------------------------------------------------------
        public float Anger => Get("trait.anger");
        public float Anxiety => Get("trait.anxiety");
        public float Fear => Get("trait.fear");
        public float Shame => Get("trait.shame");
        public float Guilt => Get("trait.guilt");
        public float Hurt => Get("trait.hurt");
        public float Jealousy => Get("trait.jealousy");
        public float Resentment => Get("trait.resentment");
        public float Trust => Get("trait.trust");
        public float Affection => Get("trait.affection");
        public float Desire => Get("trait.desire");
        public float Attraction => Get("trait.attraction");
        public float Tension => Get("trait.tension");
        public float Playfulness => Get("trait.playfulness");
        public float Pride => Get("trait.pride");
        public float Patience => Get("trait.patience");
        public float Guard => Get("trait.guard");
        public float Openness => Get("trait.openness");
        public float Loneliness => Get("trait.loneliness");
        public float Hope => Get("trait.hope");

        private static float Clamp(float v) => Math.Clamp(v, 0f, 100f);
    }
}