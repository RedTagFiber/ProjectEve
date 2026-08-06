using ProjectEve.Characters.Characters;
using ProjectEve.Worlds.SmallTown;

namespace ProjectEve.Worlds.SmallTownSystems
{
    public static class SmallTownTravelSystem
    {
        private static readonly string[] TravelDestinations =
        {
            "Columbus", "Cincinnati", "Cleveland",
            "Kings Island", "Cedar Point", "Dayton",
            "Indian Lake", "Mad River Mountain"
        };

        public static void GenerateTravelPlans(Town world)
        {
            var rng = new Random();

            foreach (var npc in world.Residents)
            {
                npc.TravelPlan = rng.NextDouble() < 0.25
                    ? TravelDestinations[rng.Next(TravelDestinations.Length)]
                    : "Staying local";
            }
        }
    }
}
