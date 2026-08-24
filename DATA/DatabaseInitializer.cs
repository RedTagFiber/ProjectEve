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
            return Path.Combine(@"D:\ProjectEveData", "Database", "project_eve.db");
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
                CharacterName TEXT,
                Summary TEXT NOT NULL,
                Category TEXT,
                Importance INTEGER DEFAULT 1,
                Strength REAL DEFAULT 50,
                IsLockedPeak INTEGER DEFAULT 0,
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

            -- ============================================================
            -- HISTORY v1
            -- ============================================================
            CREATE TABLE IF NOT EXISTS history_events (
                event_id            TEXT PRIMARY KEY,
                arc_id              TEXT,
                parent_event_id     TEXT,
                season_id           TEXT,
                world_id            TEXT NOT NULL DEFAULT 'ohio',
                title               TEXT NOT NULL,
                summary             TEXT NOT NULL DEFAULT '',
                place_text          TEXT,
                location_id         TEXT,
                channel_mix         TEXT NOT NULL DEFAULT 'text',
                status              TEXT NOT NULL DEFAULT 'closed',
                game_at             TEXT NOT NULL,
                game_at_end         TEXT,
                real_at             TEXT NOT NULL,
                real_at_end         TEXT,
                content_rating      TEXT NOT NULL DEFAULT 'pg',
                hidden_from_packet  INTEGER NOT NULL DEFAULT 0,
                source              TEXT NOT NULL DEFAULT 'live_play',
                confidence          INTEGER NOT NULL DEFAULT 7,
                fatigue             INTEGER,
                alcohol             INTEGER,
                illness             TEXT,
                turn_count          INTEGER NOT NULL DEFAULT 0,
                last_recalled_at    TEXT,
                recall_count        INTEGER NOT NULL DEFAULT 0,
                created_at          TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS ix_history_events_game
                ON history_events(world_id, game_at DESC);
            CREATE INDEX IF NOT EXISTS ix_history_events_place
                ON history_events(place_text);
            CREATE INDEX IF NOT EXISTS ix_history_events_arc
                ON history_events(arc_id);

            CREATE TABLE IF NOT EXISTS history_event_tags (
                event_id    TEXT NOT NULL,
                tag         TEXT NOT NULL,
                PRIMARY KEY (event_id, tag),
                FOREIGN KEY (event_id) REFERENCES history_events(event_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_history_tags_tag ON history_event_tags(tag);

            CREATE TABLE IF NOT EXISTS history_participants (
                event_id                TEXT NOT NULL,
                character_id            INTEGER NOT NULL,
                role                    TEXT NOT NULL DEFAULT 'present',
                relationship_band       TEXT,
                like_score_at_time      REAL,
                PRIMARY KEY (event_id, character_id),
                FOREIGN KEY (event_id) REFERENCES history_events(event_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS history_facts (
                fact_id         INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id        TEXT NOT NULL,
                kind            TEXT NOT NULL DEFAULT 'detail',
                text            TEXT NOT NULL,
                locked          INTEGER NOT NULL DEFAULT 0,
                promise_status  TEXT,
                due_game_at     TEXT,
                FOREIGN KEY (event_id) REFERENCES history_events(event_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_history_facts_event ON history_facts(event_id);

            CREATE TABLE IF NOT EXISTS history_peaks (
                peak_id         INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id        TEXT NOT NULL,
                kind            TEXT NOT NULL,
                text            TEXT NOT NULL,
                intensity       INTEGER NOT NULL DEFAULT 5,
                locked          INTEGER NOT NULL DEFAULT 0,
                photo_path      TEXT,
                cutscene_id     TEXT,
                voice_path      TEXT,
                FOREIGN KEY (event_id) REFERENCES history_events(event_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_history_peaks_event ON history_peaks(event_id);

            CREATE TABLE IF NOT EXISTS history_beats (
                beat_id         INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id        TEXT NOT NULL,
                game_at         TEXT,
                speaker_player  TEXT,
                speaker_npc     TEXT,
                importance      INTEGER NOT NULL DEFAULT 5,
                kind            TEXT,
                FOREIGN KEY (event_id) REFERENCES history_events(event_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_history_beats_event ON history_beats(event_id);

            CREATE TABLE IF NOT EXISTS history_aliases (
                event_id    TEXT NOT NULL,
                alias       TEXT NOT NULL,
                PRIMARY KEY (event_id, alias),
                FOREIGN KEY (event_id) REFERENCES history_events(event_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_history_aliases_alias ON history_aliases(alias);

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
            // Characters
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
            "ALTER TABLE Characters ADD COLUMN Tier INTEGER DEFAULT 2",

            // Memories — required by MemoryRecord / Remember()
            "ALTER TABLE Memories ADD COLUMN CharacterName TEXT",
            "ALTER TABLE Memories ADD COLUMN Strength REAL DEFAULT 50",
            "ALTER TABLE Memories ADD COLUMN IsLockedPeak INTEGER DEFAULT 0",

            // Relationships
            "ALTER TABLE Relationships ADD COLUMN TargetId INTEGER",

            //JobProfile — required by JobProfile
            "ALTER TABLE JobProfile ADD COLUMN JobName TEXT",
            "ALTER TABLE JobProfile ADD COLUMN JobType TEXT",
            "ALTER TABLE JobProfile ADD COLUMN IndustryPath TEXT",
            "ALTER TABLE JobProfile ADD COLUMN StartHour INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN EndHour INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN ShiftType TEXT",
            "ALTER TABLE JobProfile ADD COLUMN WorkLocationMode TEXT",
            "ALTER TABLE JobProfile ADD COLUMN HourlyRate REAL",
            "ALTER TABLE JobProfile ADD COLUMN WeeklyHours REAL",
            "ALTER TABLE JobProfile ADD COLUMN IsSalaried INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN AnnualSalary REAL",
            "ALTER TABLE JobProfile ADD COLUMN StressLoad INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN SocialDemand INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN PhysicalDemand INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN CognitiveDemand INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN BurnoutAccum INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN HasInsurance INTEGER",
            "ALTER TABLE JobProfile ADD COLUMN BossName TEXT",
            "ALTER TABLE JobProfile ADD COLUMN BossRelationship TEXT",
            "ALTER TABLE JobProfile ADD COLUMN TeamClimate TEXT",
        };

        foreach (var sql in alters)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // column already exists
            }
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
                'Bellefontaine / Sidney, Ohio area',
                'Build a life that is hers — work, family, art — without shrinking who she is',
                'A connection that feels honest without demanding she become smaller',
                'Being abandoned once someone sees all of her',
                'To understand why a stranger can catch her attention this hard',
                'Publicly the competent manager at her mother Lisa''s coffee shop. Twin and best friend to Adam — he knows a lot, not everything. Rents a room in Adam''s house in town. Loves art. Family sports: Ohio State, Bengals, Reds. Does not know the player yet but feels a pull toward them. Hates the false town rumor about her and Adam.',
                'Eve',
                'good girl',
                'mine',
                2001, 3, 14, 9, 'Pisces',
                'Bellefontaine, OH',
                'Adam''s house — rents a room (in town)',
                1,
                'Light Brown', 'Shoulder Length', 'Hazel', 'Fair', 'Curvy'
            );

            INSERT OR REPLACE INTO Appearance
            (NpcId, Height, BodyType, HairColor, HairStyle, EyeColor, SkinTone, BreastSize, NotableFeatures, Style)
            VALUES (
                1, '5''5"', 'Curvy', 'Light Brown', 'Shoulder length waves', 'Hazel', 'Fair', '',
                'Warm hazel eyes; confident smile', 'Casual cute / work apron to sundress'
            );

            INSERT OR REPLACE INTO BrainState
            (NpcId, Mood, Stress, Energy, Affection, Attraction, Trust, Tension, LastThought)
            VALUES (
                1, 0.55, 0.28, 0.7, 0.45, 0.50, 0.55, 0.35,
                'Restless in a way the shop does not fix. Wondering who that pull is toward.'
            );

            INSERT OR REPLACE INTO MoneyProfile (NpcId, Cash, Bank, Debt)
            VALUES (1, 95, 1840, 420);

            INSERT OR REPLACE INTO JobProfile (NpcId, Title, Employer, Shift, PayRate)
            VALUES (1, 'Manager', 'Sinclair Coffee (Lisa Sinclair, owner)', '6-14', 18.5);

            DELETE FROM Relationships WHERE NpcId = 1;
            INSERT INTO Relationships
            (NpcId, TargetName, TargetId, Trust, Respect, Affection, Attraction, Tension, RelationshipType, Notes)
            VALUES
            (1, 'Adam', 2, 90, 88, 92, 0, 28, 'sibling', 'Twin + best friend; knows a lot, not everything'),
            (1, 'Lisa', 3, 82, 85, 88, 0, 35, 'parent', 'Mom and boss at the shop'),
            (1, 'Edward', 4, 85, 90, 86, 0, 18, 'parent', 'Dad; Fire Chief');

            DELETE FROM Memories WHERE NpcId = 1;
            INSERT INTO Memories
            (NpcId, CharacterName, Summary, Category, Importance, Strength, IsLockedPeak, Timestamp, RelatedPerson)
            VALUES
            (1, 'Eve Sinclair', 'Adam is her twin and best friend — he reads her better than almost anyone, but he does not get everything.', 'Family', 9, 85, 0, $now, 'Adam'),
            (1, 'Eve Sinclair', 'She rents a room in Adam''s house for $100 a week and helps with food and supplies when she can. House rule: no sex under his roof without telling him first.', 'Family', 8, 80, 0, $now, 'Adam'),
            (1, 'Eve Sinclair', 'Lisa is both her mother and her boss at Sinclair Coffee.', 'Work', 7, 75, 0, $now, 'Lisa'),
            (1, 'Eve Sinclair', 'Sunday dinner started when Adam moved out so the family still had one meal together. Nobody misses it. BBQ and game watch when they can.', 'Family', 8, 80, 0, $now, 'Family'),
            (1, 'Eve Sinclair', 'Town rumor says she and Adam are more than siblings. It is not true and it makes her sick when it surfaces.', 'Social', 7, 78, 1, $now, 'Adam'),
            (1, 'Eve Sinclair', 'Art is hers — one of the few places she does not perform for anyone.', 'Hobby', 6, 70, 0, $now, NULL),
            (1, 'Eve Sinclair', 'Ohio State, Bengals, Reds — family sports, not a costume.', 'Family', 4, 55, 0, $now, 'Family'),
            (1, 'Eve Sinclair', 'Lately she catches herself looking for someone she has not really met — restless in a way work does not fix.', 'Emotional', 5, 65, 0, $now, NULL);
            """;
        insertCmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        insertCmd.ExecuteNonQuery();
    }

    private static void SeedLocations(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO Locations
            (Key, Name, ImagePath, Prompt, Light, Smell, Mood, DefaultNarration, IsGenerated, UpdatedAt)
            VALUES
            ('adams-house', 'Adam''s house', '/images/scenes/adams-house.png',
             'small Ohio house living room, practical furniture, TV, boots by the door, lived-in brother energy',
             'Warm indoor light', 'Laundry + coffee', 'Home — shared with rules',
             'Eve''s door is closed down the hall. Adam''s boots are by the entry. The house is quiet but not empty.',
             0, $now),
            ('eve-room', 'Eve''s room at Adam''s', '/images/scenes/eve-room.png',
             'small bedroom rented in a brother''s house, art prints on the wall, soft lamp, tidy bed',
             'Lamp light', 'Lotion + paper', 'Private corner',
             'Her space is small and deliberate. Art on the wall. Phone face-down on the nightstand.',
             0, $now),
            ('sinclair-coffee', 'Sinclair Coffee', '/images/scenes/sinclair-coffee.png',
             'warm small-town coffee shop, wooden counter, morning window light, family-run feel, espresso machine',
             'Morning window light', 'Espresso and pastry', 'Public face / Mom''s shop',
             'Steam lifts from the machine. Eve is on the floor; Lisa''s standards live in every corner.',
             0, $now),
            ('sinclair-parents', 'Sinclair parents'' house', '/images/scenes/sinclair-parents.png',
             'Ohio family home dining table, Sunday dinner energy, TV for the game, BBQ smell possible',
             'Warm overhead + TV glow', 'Home cooking + coffee', 'Family gravity',
             'Sunday table energy even on a weekday. Somebody always ends up here.',
             0, $now),
            ('fire-station', 'Fire station', '/images/scenes/fire-station.png',
             'small-town fire station bay, gear, fluorescent light, practical Ohio department',
             'Fluorescent / bay light', 'Diesel + gear', 'Work / duty',
             'Apparatus bay quiet between calls. Edward''s world; Adam''s too.',
             0, $now);

            INSERT OR REPLACE INTO NpcCurrentLocation (NpcId, LocationKey, ArrivedAt)
            VALUES (1, 'adams-house', $now);
            """;
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    private static void MarkGeneratedLocations(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Locations SET IsGenerated = 1, UpdatedAt = $now WHERE Key IN
            ('adams-house', 'eve-room', 'sinclair-coffee', 'sinclair-parents', 'fire-station');
            """;
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}