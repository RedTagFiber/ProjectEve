using Microsoft.Data.Sqlite;
using System;
using System.IO;

public static class DatabaseInitializer
{
    public static string DbPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("EVE_DB_PATH");
            if (!string.IsNullOrWhiteSpace(env))
                return env;

            return Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");
        }
    }

    private static string ConnectionString => $"Data Source={DbPath}";

    public static void Initialize()
    {
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        CreateCoreTables(connection);
        EnsureExtendedColumns(connection);
        SeedEve(connection);
        SeedLocations(connection);
        MarkGeneratedLocations(connection);
    }

    private static void CreateCoreTables(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Characters (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER,
                Gender TEXT,
                Occupation TEXT,
                Location TEXT,
                Goal TEXT,
                Need TEXT,
                Fear TEXT,
                Want TEXT,
                PersonalityContext TEXT,
                Nickname TEXT,
                DirtyName TEXT,
                DarkName TEXT
            );

            CREATE TABLE IF NOT EXISTS Appearance (
                NpcId INTEGER PRIMARY KEY,
                Height TEXT,
                BodyType TEXT,
                HairColor TEXT,
                HairStyle TEXT,
                EyeColor TEXT,
                SkinTone TEXT,
                BreastSize TEXT,
                NotableFeatures TEXT,
                Style TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS Traits (
                NpcId INTEGER NOT NULL,
                TraitId TEXT NOT NULL,
                Value REAL NOT NULL DEFAULT 50,
                PRIMARY KEY (NpcId, TraitId),
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS TraitControl (
                NpcId INTEGER NOT NULL,
                TraitId TEXT NOT NULL,
                Control INTEGER NOT NULL DEFAULT 50,
                LastUpdated TEXT,
                PRIMARY KEY (NpcId, TraitId)
            );

            CREATE TABLE IF NOT EXISTS BrainState (
                NpcId INTEGER PRIMARY KEY,
                Mood REAL DEFAULT 0.5,
                Stress REAL DEFAULT 0.2,
                Energy REAL DEFAULT 0.7,
                Affection REAL DEFAULT 0.5,
                Attraction REAL DEFAULT 0.5,
                Trust REAL DEFAULT 0.5,
                Tension REAL DEFAULT 0.2,
                LastThought TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS MoneyProfile (
                NpcId INTEGER PRIMARY KEY,
                Cash REAL DEFAULT 0,
                Bank REAL DEFAULT 0,
                Debt REAL DEFAULT 0,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS JobProfile (
                NpcId INTEGER PRIMARY KEY,
                Title TEXT,
                Employer TEXT,
                Shift TEXT,
                PayRate REAL,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS Relationships (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                TargetName TEXT NOT NULL,
                TargetId INTEGER,
                Trust REAL DEFAULT 50,
                Respect REAL DEFAULT 50,
                Affection REAL DEFAULT 50,
                Attraction REAL DEFAULT 50,
                Tension REAL DEFAULT 0,
                RelationshipType TEXT,
                Notes TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS Memories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                Summary TEXT NOT NULL,
                Category TEXT,
                Importance INTEGER DEFAULT 1,
                Timestamp TEXT,
                RelatedPerson TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS History (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                EventId TEXT,
                Age INTEGER,
                Summary TEXT NOT NULL,
                Category TEXT,
                PrimaryTraitId TEXT,
                Importance INTEGER DEFAULT 1,
                TrustGate INTEGER DEFAULT 40,
                StoryText TEXT,
                EmotionalImpact INTEGER DEFAULT 0,
                Timestamp TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS NameReactions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                UsedName TEXT NOT NULL,
                ReactionScore INTEGER,
                Timestamp TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS ConversationLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                Speaker TEXT,
                Message TEXT,
                Timestamp TEXT,
                FOREIGN KEY (NpcId) REFERENCES Characters(Id)
            );

            CREATE TABLE IF NOT EXISTS Locations (
                Key TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ImagePath TEXT,
                Prompt TEXT,
                Light TEXT,
                Smell TEXT,
                Mood TEXT,
                DefaultNarration TEXT,
                IsGenerated INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT
            );

            CREATE TABLE IF NOT EXISTS NpcCurrentLocation (
                NpcId INTEGER PRIMARY KEY,
                LocationKey TEXT NOT NULL,
                ArrivedAt TEXT
            );

            CREATE TABLE IF NOT EXISTS NpcLocationVisits (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                LocationKey TEXT NOT NULL,
                FirstVisitAt TEXT,
                LastVisitAt TEXT,
                VisitCount INTEGER NOT NULL DEFAULT 1,
                Notes TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureExtendedColumns(SqliteConnection connection)
    {
        string[] alters =
        {
            "ALTER TABLE Characters ADD COLUMN BirthYear INTEGER",
            "ALTER TABLE Characters ADD COLUMN BirthMonth INTEGER",
            "ALTER TABLE Characters ADD COLUMN BirthDay INTEGER",
            "ALTER TABLE Characters ADD COLUMN BirthHour INTEGER",
            "ALTER TABLE Characters ADD COLUMN Zodiac TEXT",
            "ALTER TABLE Characters ADD COLUMN HeightCm INTEGER",
            "ALTER TABLE Characters ADD COLUMN WeightKg INTEGER",
            "ALTER TABLE Characters ADD COLUMN BodyShape TEXT",
            "ALTER TABLE Characters ADD COLUMN HairColor TEXT",
            "ALTER TABLE Characters ADD COLUMN HairStyle TEXT",
            "ALTER TABLE Characters ADD COLUMN EyeColor TEXT",
            "ALTER TABLE Characters ADD COLUMN EyeStyle TEXT",
            "ALTER TABLE Characters ADD COLUMN SkinTone TEXT",
            "ALTER TABLE Characters ADD COLUMN Glasses TEXT",
            "ALTER TABLE Characters ADD COLUMN ScarNotes TEXT",
            "ALTER TABLE Characters ADD COLUMN Hometown TEXT",
            "ALTER TABLE Characters ADD COLUMN Address TEXT",
            "ALTER TABLE Characters ADD COLUMN Tier INTEGER DEFAULT 2"
        };

        foreach (var sql in alters)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    private static void SeedEve(SqliteConnection connection)
    {
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT OR REPLACE INTO Characters
            (Id, Name, Age, Gender, Occupation, Location, Goal, Need, Fear, Want,
             PersonalityContext, Nickname, DirtyName, DarkName,
             BirthYear, BirthMonth, BirthDay, BirthHour, Zodiac,
             Hometown, Address, Tier,
             HairColor, HairStyle, EyeColor, SkinTone, BodyShape)
            VALUES (
                1,
                'Eve Sinclair',
                25,
                'Female',
                'Coffee Shop Manager',
                'Bellefontaine / Sidney, Ohio',
                'Live honestly without losing the people she loves',
                'Connection without judgment',
                'Being abandoned once the truth is known',
                'To be fully known and still chosen',
                'Eve appears to be a sweet, competent good girl. Privately she is highly sexual, self-aware, and loyal in her own way. Her heart belongs to Ryan. Her body is free. She is drawn to secrecy, intensity, and being wanted. She comes home to Ryan.',
                'Eve',
                'good girl',
                'mine',
                2001, 3, 14, 9, 'Pisces',
                'Bellefontaine, OH', 'near downtown Bellefontaine', 1,
                'Light Brown', 'Shoulder Length', 'Hazel', 'Fair', 'Average'
            );

            INSERT OR REPLACE INTO Appearance
            (NpcId, Height, BodyType, HairColor, HairStyle, EyeColor, SkinTone, BreastSize, NotableFeatures, Style)
            VALUES (
                1, '5''6"', 'Average', 'Light Brown', 'Shoulder Length', 'Hazel', 'Fair', '',
                'Warm presence, expressive eyes', 'Casual, slightly fitted, approachable'
            );

            INSERT OR REPLACE INTO BrainState
            (NpcId, Mood, Stress, Energy, Affection, Attraction, Trust, Tension, LastThought)
            VALUES (1, 0.55, 0.25, 0.7, 0.7, 0.65, 0.75, 0.2, 'Wondering what Ryan is doing right now.');

            INSERT OR REPLACE INTO MoneyProfile (NpcId, Cash, Bank, Debt)
            VALUES (1, 95, 320, 0);

            INSERT OR REPLACE INTO JobProfile (NpcId, Title, Employer, Shift, PayRate)
            VALUES (1, 'Manager', 'Local coffee shop', '6-14', 18.5);
            """;
        insertCmd.ExecuteNonQuery();
    }

    private static void SeedLocations(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO Locations
            (Key, Name, ImagePath, Prompt, Light, Smell, Mood, DefaultNarration, IsGenerated, UpdatedAt)
            VALUES
            ('eve-apartment', 'Eve''s apartment', '/images/scenes/eve-apartment.png',
             'cozy small apartment living room, warm lamp light, couch, soft evening mood',
             'Warm lamp', 'Coffee + lotion', 'Private',
             'She''s on the couch when you come in, one leg tucked under her, phone face-down on the cushion.',
             0, $now),
            ('coffee-shop', 'Coffee shop', '/images/scenes/coffee-shop.png',
             'warm modern coffee shop interior, wooden counter, morning window light, espresso machine',
             'Morning window light', 'Espresso and pastry', 'Public face',
             'Steam lifts from the machine. She''s behind the counter, apron on, already watching the door.',
             0, $now),
            ('ryans-house', 'Ryan''s house', '/images/scenes/ryans-house.png',
             'small clean American living room, desk with computer, warm indoor light',
             'Warm indoor light', 'Clean laundry + PC heat', 'Familiar',
             'The house is quiet. Her shoes are by the door like she already decided to stay a while.',
             0, $now);

            INSERT OR IGNORE INTO NpcCurrentLocation (NpcId, LocationKey, ArrivedAt)
            VALUES (1, 'eve-apartment', $now);
            """;
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    private static void MarkGeneratedLocations(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Locations SET IsGenerated = 1, ImagePath = '/images/scenes/eve-apartment.png', UpdatedAt = $now WHERE Key = 'eve-apartment';
            UPDATE Locations SET IsGenerated = 1, ImagePath = '/images/scenes/coffee-shop.png', UpdatedAt = $now WHERE Key = 'coffee-shop';
            UPDATE Locations SET IsGenerated = 1, ImagePath = '/images/scenes/ryans-house.png', UpdatedAt = $now WHERE Key = 'ryans-house';
            """;
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}