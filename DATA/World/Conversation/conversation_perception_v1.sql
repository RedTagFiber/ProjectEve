-- Project Eve Phase 6: observer-specific conversation perception overlay.
-- ConversationMessage remains the exact physical transcript/evidence.
-- This table stores what the NPC participant actually perceived for a player line.
-- ConversationPerceptionStore auto-creates this table; this file is reference only.

CREATE TABLE IF NOT EXISTS ConversationMessagePerception(
    MessageId INTEGER NOT NULL,
    SessionId INTEGER NOT NULL,
    ObserverNpcId INTEGER NOT NULL,
    PerceivedText TEXT NOT NULL,
    SourceEventKey TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    PRIMARY KEY(MessageId,ObserverNpcId)
);

CREATE INDEX IF NOT EXISTS IX_ConversationMessagePerception_SessionNpc
    ON ConversationMessagePerception(SessionId,ObserverNpcId,MessageId);
