namespace ProjectEve.Relationships
{
    public class Relationship
    {
        public string TargetName { get; set; } = "Unknown";

        public int Trust { get; set; } = 50;
        public int Respect { get; set; } = 50;
        public int Affection { get; set; } = 50;
        public int Attraction { get; set; } = 50;

        public void AdjustTrust(int amount) => Trust = Math.Clamp(Trust + amount, 0, 100);
        public void AdjustRespect(int amount) => Respect = Math.Clamp(Respect + amount, 0, 100);
        public void AdjustAffection(int amount) => Affection = Math.Clamp(Affection + amount, 0, 100);
        public void AdjustAttraction(int amount) => Attraction = Math.Clamp(Attraction + amount, 0, 100);
    }
}