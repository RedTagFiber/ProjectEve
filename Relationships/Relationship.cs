using System;

namespace ProjectEve.Relationships
{
    /// <summary>
    /// Per-target bond. Parallel to Fast trait.trust/affection — this is "with this person".
    /// History TrustGate checks Relationship.Trust, not global trait.trust alone.
    /// </summary>
    public class Relationship
    {
        public int? TargetId { get; set; }
        public string TargetName { get; set; } = "Unknown";

        /// <summary>friend | family | coworker | rival | ex | partner | acquaintance | other</summary>
        public string RelationshipType { get; set; } = "acquaintance";

        public int Trust { get; set; } = 50;
        public int Respect { get; set; } = 50;
        public int Affection { get; set; } = 50;
        public int Attraction { get; set; } = 50;

        /// <summary>Fight / sexual charge with this person (0–100).</summary>
        public int Tension { get; set; } = 0;

        public string Notes { get; set; } = "";

        public void AdjustTrust(int amount)
            => Trust = Math.Clamp(Trust + amount, 0, 100);

        public void AdjustRespect(int amount)
            => Respect = Math.Clamp(Respect + amount, 0, 100);

        public void AdjustAffection(int amount)
            => Affection = Math.Clamp(Affection + amount, 0, 100);

        public void AdjustAttraction(int amount)
            => Attraction = Math.Clamp(Attraction + amount, 0, 100);

        public void AdjustTension(int amount)
            => Tension = Math.Clamp(Tension + amount, 0, 100);

        /// <summary>Whether this bond is open enough for a history reveal.</summary>
        public bool MeetsTrustGate(int gate)
            => Trust >= gate;

        public override string ToString()
            => $"{TargetName} ({RelationshipType}): trust {Trust} aff {Affection} attr {Attraction} ten {Tension}";
    }
}