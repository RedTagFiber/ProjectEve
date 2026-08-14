CREATE TABLE IF NOT EXISTS NpcBodyProfile (
    NpcId INTEGER PRIMARY KEY,
    SchemaVersion TEXT NOT NULL DEFAULT '1.0',
    AppearanceJson TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_NpcBodyProfile_UpdatedUtc ON NpcBodyProfile(UpdatedUtc);
