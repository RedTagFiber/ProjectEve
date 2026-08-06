using ProjectEve.Worlds.SmallTown;

namespace ProjectEve.Worlds.SmallTownSystems
{
    public static class SmallTownWorldManager
    {
        public static void FinalizeSmallTown(Town world)
        {
            SmallTownScheduleSystem.GenerateDailySchedule(world);
            SmallTownTravelSystem.GenerateTravelPlans(world);
            SmallTownConversationSystem.GenerateConversationTopics(world);
        }
    }
}
