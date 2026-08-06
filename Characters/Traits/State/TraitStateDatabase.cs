using Microsoft.Data.Sqlite;
using Project_Eve.Characters.Traits.State;      // Gives access to SQLite database features

namespace Project_Eve.Characters.Traits
{
    /// <summary>
    /// Handles saving and storing dynamic trait data (Intensity + Control)
    /// for each character inside the SQLite database.
    /// 
    /// This is NOT the static trait definition from JSON.
    /// This stores the evolving psychological state of each NPC.
    /// </summary>
    public class TraitStateDatabase
    {
        // The SQLite database file where trait states are stored.
        private const string DbFile = "eve_memory.db";

        /// <summary>
        /// Constructor runs when the database object is created.
        /// If the database file does not exist yet, we create it.
        /// </summary>
        public TraitStateDatabase()
        {
            if (!File.Exists(DbFile))
                CreateDatabase();
        }

        /// <summary>
        /// Creates the TraitState table inside SQLite.
        /// This table stores dynamic per-character trait information:
        /// - CharacterName: which NPC this trait belongs to
        /// - TraitName: the name of the trait (matches JSON trait name)
        /// - Intensity: stable personality strength (1–100)
        /// - Control: how much the NPC can regulate the trait (1–100)
        /// - LastUpdated: timestamp for drift rules later
        /// </summary>
        private void CreateDatabase()
        {
            using var conn = new SqliteConnection($"Data Source={DbFile}");
            conn.Open();

            string sql = @"
                CREATE TABLE IF NOT EXISTS TraitState (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CharacterName TEXT,
                    TraitName TEXT,
                    Intensity INTEGER,
                    Control INTEGER,
                    LastUpdated TEXT
                );
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Saves a TraitState record into the database.
        /// This is used when:
        /// - A character is created (initial trait values)
        /// - A trait changes due to memories, events, relationships
        /// 
        /// Each call inserts a NEW row. Later you can add an UPDATE method
        /// to modify existing trait states.
        /// </summary>
        public void SaveTraitState(TraitState state)
        {
            using var conn = new SqliteConnection($"Data Source={DbFile}");
            conn.Open();

            string sql = @"
                INSERT INTO TraitState (CharacterName, TraitName, Intensity, Control, LastUpdated)
                VALUES (@name, @trait, @intensity, @control, @updated);
            ";

            using var cmd = new SqliteCommand(sql, conn);

            // Bind the C# object values to the SQL parameters
            cmd.Parameters.AddWithValue("@name", state.CharacterName);
            cmd.Parameters.AddWithValue("@trait", state.TraitName);
            cmd.Parameters.AddWithValue("@intensity", state.Intensity);
            cmd.Parameters.AddWithValue("@control", state.Control);
            cmd.Parameters.AddWithValue("@updated", state.LastUpdated.ToString("o")); // ISO 8601 format

            cmd.ExecuteNonQuery();
        }
    }
}
