using ProjectEve.Characters.Base;

namespace ProjectEve.Characters.Characters
{
    public static class CharacterFactory
    {
        public static SimCharacter? LoadCharacter(int npcId)
        {
            // Use the new repository system
            var npc = CharacterRepository.LoadCharacter(npcId);

            if (npc == null)
                return null;

            // Optional extras still loaded here if you want
            // (relationships, memories, history, appearance)

            return npc;
        }
    }
}