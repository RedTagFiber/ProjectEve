using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Foundation schema for canonical phone ownership and vehicle truth.
///
/// Current ownership/state lives in MAIN.
/// Calls, texts, purchases, sales, crashes, plate changes, activation/deactivation,
/// and other objective changes remain HISTORY events.
/// Existing NpcPhoneRuntimeState remains behavioral runtime state and is not replaced.
/// </summary>
public static class ProjectEvePhoneVehicleSchema
{
    public static void Ensure()
    {
        EnsureNpcPhones();
        EnsureVehicles();
    }

    private static void EnsureNpcPhones()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcPhones
            (
                PhoneId TEXT PRIMARY KEY,
                WorldId TEXT NOT NULL DEFAULT 'smalltown',
                NpcId INTEGER NOT NULL,

                PhoneNumber TEXT NOT NULL DEFAULT '',
                PhoneType TEXT NOT NULL DEFAULT 'Mobile',

                CarrierName TEXT NOT NULL DEFAULT '',

                DeviceMake TEXT NOT NULL DEFAULT '',
                DeviceModel TEXT NOT NULL DEFAULT '',
                DeviceLabel TEXT NOT NULL DEFAULT '',

                IsPrimary INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,

                ActivatedGameTime TEXT NOT NULL DEFAULT '',
                DeactivatedGameTime TEXT NOT NULL DEFAULT '',

                ActivatedEventId TEXT NOT NULL DEFAULT '',
                DeactivatedEventId TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_NpcPhones_Npc
                ON NpcPhones(NpcId, IsActive, IsPrimary);

            CREATE INDEX IF NOT EXISTS IX_NpcPhones_Number
                ON NpcPhones(PhoneNumber);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcPhones_ActiveNumber
                ON NpcPhones(WorldId, PhoneNumber)
                WHERE IsActive = 1
                  AND trim(PhoneNumber) <> '';

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcPhones_OneActivePrimary
                ON NpcPhones(NpcId)
                WHERE IsActive = 1
                  AND IsPrimary = 1;
            """);
    }

    private static void EnsureVehicles()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Vehicles
            (
                VehicleId TEXT PRIMARY KEY,
                WorldId TEXT NOT NULL DEFAULT 'smalltown',

                RegisteredOwnerNpcId INTEGER NULL,
                PrimaryDriverNpcId INTEGER NULL,

                VehicleType TEXT NOT NULL DEFAULT 'Car',

                Make TEXT NOT NULL DEFAULT '',
                Model TEXT NOT NULL DEFAULT '',
                ModelYear INTEGER NULL,
                Color TEXT NOT NULL DEFAULT '',

                Vin TEXT NOT NULL DEFAULT '',
                PlateNumber TEXT NOT NULL DEFAULT '',
                PlateState TEXT NOT NULL DEFAULT '',

                Status TEXT NOT NULL DEFAULT 'Active',

                CurrentLocationId TEXT NOT NULL DEFAULT '',
                OdometerMiles REAL NULL,

                AcquiredGameTime TEXT NOT NULL DEFAULT '',
                DisposedGameTime TEXT NOT NULL DEFAULT '',

                AcquisitionEventId TEXT NOT NULL DEFAULT '',
                DisposalEventId TEXT NOT NULL DEFAULT '',
                LastMajorEventId TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (RegisteredOwnerNpcId)
                    REFERENCES Characters(Id)
                    ON DELETE SET NULL,

                FOREIGN KEY (PrimaryDriverNpcId)
                    REFERENCES Characters(Id)
                    ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Vehicles_RegisteredOwner
                ON Vehicles(RegisteredOwnerNpcId, Status);

            CREATE INDEX IF NOT EXISTS IX_Vehicles_PrimaryDriver
                ON Vehicles(PrimaryDriverNpcId, Status);

            CREATE INDEX IF NOT EXISTS IX_Vehicles_CurrentLocation
                ON Vehicles(CurrentLocationId, Status);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Vehicles_Vin
                ON Vehicles(Vin)
                WHERE trim(Vin) <> '';

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Vehicles_ActivePlate
                ON Vehicles(PlateState, PlateNumber)
                WHERE Status = 'Active'
                  AND trim(PlateNumber) <> '';
            """);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");

        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
