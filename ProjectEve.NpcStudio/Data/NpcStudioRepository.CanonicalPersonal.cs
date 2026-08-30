using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    public Task<CanonicalPersonalBundle> GetCanonicalPersonalBundleAsync(int npcId)
    {
        using var conn = Open();
        EnsurePhoneContactTable(conn);

        var bundle = new CanonicalPersonalBundle();

        if (CanonicalPersonalTableExists(conn, "NpcPhones"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT PhoneId, PhoneNumber, PhoneType, CarrierName,
                       DeviceMake, DeviceModel, DeviceLabel,
                       IsPrimary, IsActive
                FROM NpcPhones
                WHERE NpcId=$id
                ORDER BY IsPrimary DESC, IsActive DESC, PhoneId;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            using var r=cmd.ExecuteReader();
            while(r.Read())
                bundle.Phones.Add(new CanonicalPhoneRow {
                    PhoneId=S(r,0), PhoneNumber=S(r,1), PhoneType=S(r,2),
                    CarrierName=S(r,3), DeviceMake=S(r,4), DeviceModel=S(r,5),
                    DeviceLabel=S(r,6), IsPrimary=I(r,7), IsActive=I(r,8)
                });
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT ContactId, ContactNpcId, DisplayName, PhoneNumber,
                       RelationshipLabel, IsFavorite, IsBlocked
                FROM NpcPhoneContacts
                WHERE NpcId=$id
                ORDER BY IsFavorite DESC, DisplayName, ContactId;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            using var r=cmd.ExecuteReader();
            while(r.Read())
                bundle.PhoneContacts.Add(new CanonicalPhoneContactRow {
                    ContactId=S(r,0), ContactNpcId=r.IsDBNull(1)?0:r.GetInt32(1),
                    DisplayName=S(r,2), PhoneNumber=S(r,3), RelationshipLabel=S(r,4),
                    IsFavorite=I(r,5), IsBlocked=I(r,6)
                });
        }

        if (CanonicalPersonalTableExists(conn, "Vehicles"))
        {
            using var cmd=conn.CreateCommand();
            cmd.CommandText = """
                SELECT VehicleId, VehicleType, Make, Model, ModelYear, Color,
                       Vin, PlateNumber, PlateState, Status, OdometerMiles
                FROM Vehicles
                WHERE RegisteredOwnerNpcId=$id OR PrimaryDriverNpcId=$id
                ORDER BY CASE WHEN RegisteredOwnerNpcId=$id THEN 0 ELSE 1 END, VehicleId;
                """;
            cmd.Parameters.AddWithValue("$id",npcId);
            using var r=cmd.ExecuteReader();
            while(r.Read())
                bundle.Vehicles.Add(new CanonicalVehicleRow {
                    VehicleId=S(r,0), VehicleType=S(r,1), Make=S(r,2), Model=S(r,3),
                    ModelYear=r.IsDBNull(4)?null:r.GetInt32(4), Color=S(r,5), Vin=S(r,6),
                    PlateNumber=S(r,7), PlateState=S(r,8), Status=S(r,9),
                    OdometerMiles=r.IsDBNull(10)?null:r.GetDouble(10)
                });
        }

        return Task.FromResult(bundle);
    }

    private static void EnsurePhoneContactTable(SqliteConnection conn)
    {
        using var cmd=conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcPhoneContacts
            (
                ContactId TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,
                ContactNpcId INTEGER NULL,
                DisplayName TEXT NOT NULL DEFAULT '',
                PhoneNumber TEXT NOT NULL DEFAULT '',
                RelationshipLabel TEXT NOT NULL DEFAULT '',
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                IsBlocked INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS IX_NpcPhoneContacts_Npc
                ON NpcPhoneContacts(NpcId, IsFavorite, DisplayName);
            """;
        cmd.ExecuteNonQuery();
    }

    private static bool CanonicalPersonalTableExists(SqliteConnection conn,string name)
    {
        using var cmd=conn.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n;";
        cmd.Parameters.AddWithValue("$n",name);
        return Convert.ToInt32(cmd.ExecuteScalar())>0;
    }

    private static string S(SqliteDataReader r,int i)=>r.IsDBNull(i)?"":r.GetString(i);
    private static bool I(SqliteDataReader r,int i)=>!r.IsDBNull(i)&&Convert.ToInt32(r.GetValue(i))!=0;
}

