-- Project Eve Phase 6: Scene Presence + Perception
-- NOTE: ScenePerceptionService auto-creates these tables. This file is the
-- human-readable schema/reference copy for the project.

CREATE TABLE IF NOT EXISTS ActiveScene(
    SceneId TEXT PRIMARY KEY,
    LocationId TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    AmbientNoise REAL NOT NULL DEFAULT 0.15,
    VisualClutter REAL NOT NULL DEFAULT 0.10,
    DefaultRoomZone TEXT NOT NULL DEFAULT 'main',
    DefaultAcousticZone TEXT NOT NULL DEFAULT 'main',
    UpdatedGameTime TEXT NOT NULL,
    UpdatedRealUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ScenePresence(
    SceneId TEXT NOT NULL,
    CharacterKey TEXT NOT NULL,
    NpcId INTEGER NULL,
    PlayerId TEXT NULL,
    DisplayName TEXT NOT NULL,
    IsPlayer INTEGER NOT NULL DEFAULT 0,
    XFeet REAL NOT NULL DEFAULT 0,
    YFeet REAL NOT NULL DEFAULT 0,
    FacingDegrees REAL NOT NULL DEFAULT 0,
    RoomZone TEXT NOT NULL DEFAULT 'main',
    AcousticZone TEXT NOT NULL DEFAULT 'main',
    Attention REAL NOT NULL DEFAULT 0.70,
    Activity TEXT NOT NULL DEFAULT 'idle',
    Concealment REAL NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    UpdatedGameTime TEXT NOT NULL,
    UpdatedRealUtc TEXT NOT NULL,
    PRIMARY KEY(SceneId,CharacterKey)
);

CREATE INDEX IF NOT EXISTS IX_ScenePresence_SceneActive
    ON ScenePresence(SceneId,IsActive);

CREATE TABLE IF NOT EXISTS SceneBarrier(
    SceneId TEXT NOT NULL,
    CharacterAKey TEXT NOT NULL,
    CharacterBKey TEXT NOT NULL,
    Label TEXT NOT NULL DEFAULT 'barrier',
    AcousticPenalty REAL NOT NULL DEFAULT 0,
    VisualPenalty REAL NOT NULL DEFAULT 0,
    UpdatedRealUtc TEXT NOT NULL,
    PRIMARY KEY(SceneId,CharacterAKey,CharacterBKey)
);

-- This is evidence/provenance only. It does NOT automatically grant memory,
-- belief, gossip knowledge, or truth to an NPC.
CREATE TABLE IF NOT EXISTS ScenePerceptionEvidence(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventKey TEXT NOT NULL,
    SceneId TEXT NOT NULL,
    EventKind TEXT NOT NULL,
    SourceCharacterKey TEXT NOT NULL,
    ObserverCharacterKey TEXT NOT NULL,
    Quality TEXT NOT NULL,
    PerceivedText TEXT NOT NULL DEFAULT '',
    Confidence REAL NOT NULL DEFAULT 0,
    DistanceFeet REAL NOT NULL DEFAULT 0,
    GameTime TEXT NOT NULL,
    CreatedRealUtc TEXT NOT NULL,
    UNIQUE(EventKey,ObserverCharacterKey,EventKind)
);

CREATE INDEX IF NOT EXISTS IX_ScenePerceptionEvidence_Observer
    ON ScenePerceptionEvidence(ObserverCharacterKey,Id DESC);

CREATE INDEX IF NOT EXISTS IX_ScenePerceptionEvidence_Event
    ON ScenePerceptionEvidence(EventKey,ObserverCharacterKey);
