using System.Collections.Generic;

namespace ProjectEve.Narrative.Texting
{
    public class MessageHistory
    {
        private readonly List<string> _messages = new();

        public void Add(string msg)
        {
            _messages.Add(msg);
        }

        public IReadOnlyList<string> GetAll()
        {
            return _messages.AsReadOnly();
        }
    }
}
