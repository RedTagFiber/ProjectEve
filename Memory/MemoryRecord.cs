namespace ProjectEve.Memory

{
    public class MemoryRecord
    {
        public int Id { get; set; }
        public string CharacterName { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Category { get; set; } = "General";   
        public int Importance { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

