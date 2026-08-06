using ProjectEve.Characters.Characters;
using ProjectEve.Worlds.SmallTown;

namespace ProjectEve.Worlds.SmallTownSystems
{
    public static class SmallTownConversationSystem
    {
        public static void GenerateConversationTopics(Town world)
        {
            var rng = new Random();

            foreach (var npc in world.Residents)
            {
                npc.ConversationTopics = new List<string>
        {
            $"My job: {npc.Occupation}",
            $"I live on {npc.Location}",
            $"Favorite event: {world.Events[rng.Next(world.Events.Count)].Name}",
            $"Places I’ve visited: {npc.TravelPlan}",
            "People I know: coworkers, neighbors, family"
        };
            }
        }

    }
}
    

