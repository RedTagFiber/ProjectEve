-- Conversation Memory v1 reference schema.
-- ConversationManager.Initialize() creates these automatically.

CREATE TABLE IF NOT EXISTS ConversationSession(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NpcId INTEGER NOT NULL,
    NpcName TEXT NOT NULL,
    PlayerName TEXT NOT NULL,
    Channel TEXT NOT NULL,
    Location TEXT NOT NULL,
    StartedGameTime TEXT NOT NULL,
    EndedGameTime TEXT NULL,
    StartedUtc TEXT NOT NULL,
    EndedUtc TEXT NULL,
    LastMessageUtc TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'open',
    EndReason TEXT NULL
);

CREATE TABLE IF NOT EXISTS ConversationMessage(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL,
    Sequence INTEGER NOT NULL,
    Role TEXT NOT NULL,
    Speaker TEXT NOT NULL,
    SpeakerNpcId INTEGER NULL,
    MessageText TEXT NOT NULL,
    GameTime TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY(SessionId) REFERENCES ConversationSession(Id) ON DELETE CASCADE,
    UNIQUE(SessionId,Sequence)
);

CREATE TABLE IF NOT EXISTS ConversationEvent(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL UNIQUE,
    NpcId INTEGER NOT NULL,
    NpcName TEXT NOT NULL,
    PlayerName TEXT NOT NULL,
    Channel TEXT NOT NULL,
    Location TEXT NOT NULL,
    StartedGameTime TEXT NOT NULL,
    EndedGameTime TEXT NOT NULL,
    Summary TEXT NOT NULL,
    EmotionalOutcome TEXT NOT NULL DEFAULT '',
    RelationshipOutcome TEXT NOT NULL DEFAULT '',
    EndReason TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY(SessionId) REFERENCES ConversationSession(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ConversationFact(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId INTEGER NOT NULL,
    NpcId INTEGER NOT NULL,
    PlayerName TEXT NOT NULL,
    Subject TEXT NOT NULL,
    FactKey TEXT NOT NULL,
    FactValue TEXT NOT NULL,
    Confidence INTEGER NOT NULL DEFAULT 100,
    SourceType TEXT NOT NULL DEFAULT 'conversation',
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY(EventId) REFERENCES ConversationEvent(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ConversationPlan(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId INTEGER NOT NULL,
    NpcId INTEGER NOT NULL,
    PlayerName TEXT NOT NULL,
    Description TEXT NOT NULL,
    TimeText TEXT NOT NULL DEFAULT '',
    Location TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT 'planned',
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    FOREIGN KEY(EventId) REFERENCES ConversationEvent(Id) ON DELETE CASCADE
);
