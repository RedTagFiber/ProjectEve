using System.Collections.Generic;

namespace ProjectEve.Narrative.Texting
{
    public class ContactListGenerator
    {
        public class ContactEntry
        {
            public required string Name { get; set; }
            public bool HasNewMessage { get; set; }
        }

        public List<ContactEntry> Generate(List<string> npcNames)
        {
            var list = new List<ContactEntry>();

            foreach (var name in npcNames)
            {
                list.Add(new ContactEntry
                {
                    Name = name,
                    HasNewMessage = false // later: hook into message system
                });
            }

            return list;
        }
    }
}
