using System;

namespace ProjectEve.Memory
{
    public class MemoryRecord
    {
        public int Id { get; set; }

        public int NpcId { get; set; }

        /// <summary>Legacy / display.</summary>
        public string CharacterName { get; set; } = "";

        public string Summary { get; set; } = "";

        /// <summary>Emotional | Trauma | Social | Work | Romance | Peak | General</summary>
        public string Category { get; set; } = "General";

        /// <summary>1–100 how hard it hits / how long it should last.</summary>
        public int Importance { get; set; } = 1;

        /// <summary>0–100 recall strength (degrades unless locked peak).</summary>
        public float Strength { get; set; } = 70f;

        public bool IsLockedPeak { get; set; }

        public string? RelatedPerson { get; set; }

        /// <summary>Optional link to HistoryRecord.Id / event id.</summary>
        public string? EventId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public void Degrade(float amount = 1f)
        {
            if (IsLockedPeak) amount *= 0.1f;
            Strength = Math.Clamp(Strength - amount, IsLockedPeak ? 60f : 0f, 100f);
        }
    }
}