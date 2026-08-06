using Microsoft.Data.Sqlite;
using ProjectEve.Characters;
using ProjectEve.Characters.Characters;
using ProjectEve.Characters.Base;

namespace ProjectEve.Memory

{
    /// <summary>
    /// MemoryDatabase handles storing and retrieving memories for characters.
    /// 
    /// IMPORTANT:
    /// - This database stores *memories*, not traits.
    /// - Memories will later influence TraitControl (psychological evolution).
    /// - Each memory belongs to a specific character.
    /// </summary>
    public class MemoryDatabase
    {
        // The SQLite database file where memories are stored.
        private const string DbFile = "eve_memory.db";

        public MemoryDatabase()
        {
            if (!File.Exists(DbFile))
                CreateDatabase();
        }

        /// <summary>
        /// Creates the Memories table inside SQLite.
        /// This table stores:
        /// - CharacterName: who the memory belongs to
        /// - Summary: short description of the memory
        /// - Category: type of memory (Emotional, Trauma, Social, etc.)
        /// - Importance: how strong the memory is (1–100)
        /// - Timestamp: when the memory happened
        /// </summary>
        private void CreateDatabase()
        {
            using var conn = new SqliteConnection($"Data Source={DbFile}");
            conn.Open();

            string sql = @"
                CREATE TABLE IF NOT EXISTS Memories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CharacterName TEXT,
                    Summary TEXT,
                    Category TEXT,
                    Importance INTEGER,
                    Timestamp TEXT
                );
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Adds a new memory to the database.
        /// </summary>
        public void AddMemory(MemoryRecord memory)
        {
            using var conn = new SqliteConnection($"Data Source={DbFile}");
            conn.Open();

            string sql = @"
                INSERT INTO Memories (CharacterName, Summary, Category, Importance, Timestamp)
                VALUES (@name, @summary, @category, @importance, @timestamp);
            ";

            using var cmd = new SqliteCommand(sql, conn);

            cmd.Parameters.AddWithValue("@name", memory.CharacterName);
            cmd.Parameters.AddWithValue("@summary", memory.Summary);
            cmd.Parameters.AddWithValue("@category", memory.Category);
            cmd.Parameters.AddWithValue("@importance", memory.Importance);
            cmd.Parameters.AddWithValue("@timestamp", memory.Timestamp.ToString("o"));

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Retrieves all memories belonging to a specific character.
        /// </summary>
        public List<MemoryRecord> GetMemories(string characterName)
        {
            var list = new List<MemoryRecord>();

            using var conn = new SqliteConnection($"Data Source={DbFile}");
            conn.Open();

            string sql = "SELECT * FROM Memories WHERE CharacterName = @name";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@name", characterName);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new MemoryRecord
                {
                    Id = reader.GetInt32(0),
                    CharacterName = reader.GetString(1),
                    Summary = reader.GetString(2),
                    Category = reader.GetString(3),
                    Importance = reader.GetInt32(4),
                    Timestamp = DateTime.Parse(reader.GetString(5))
                });
            }

            return list;
        }

        /// <summary>
        /// Automatically calculates importance for a new memory,
        /// based on summary text, category, Eve's traits, and relationships.
        /// </summary>
        public static int AutoImportance(SimCharacter eve, string summary, string category)
        {
            int importance = 0;

            // Category base values
            importance += category.ToLower() switch
            {
                "positive" => 30,
                "negative" => 50,
                "trauma" => 90,
                "social" => 20,
                _ => 10
            };

            string s = summary.ToLower();
            string c = category.ToLower();

            // Keyword modifiers
            if (s.Contains("love") || s.Contains("date") || s.Contains("romantic"))
                importance += 20;

            if (s.Contains("fight") || s.Contains("hurt") || s.Contains("cry"))
                importance += 30;

            if (s.Contains("job") || s.Contains("work"))
                importance += 10;

            if (s.Contains("family") || s.Contains("friend"))
                importance += 15;

            // Trait modifiers
            if (eve.HasTrait("HeartOnSleeve"))
                importance += 15;

            if (eve.HasTrait("HopelessRomantic") && c == "positive")
                importance += 20;

            if (eve.HasTrait("FaithfulPartner") && c == "negative")
                importance += 25;

            // Relationship modifiers
            var rel = eve.Relationships.Find(r => r.TargetName == "Ryan");
            if (rel != null)
            {
                importance += rel.Affection / 10;
                importance += rel.Trust / 10;
            }

            // Clamp final result
            return Math.Clamp(importance, 1, 100);
        }
    }
}
