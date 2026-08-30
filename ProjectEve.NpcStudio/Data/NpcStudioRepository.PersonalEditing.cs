using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    public Task SavePersonalCurrentLifeAsync(int npcId, PersonalCurrentLifeEdit e)
    {
        using var conn=Open();

        using(var cmd=conn.CreateCommand())
        {
            cmd.CommandText="""
            UPDATE Characters SET
                Occupation=$occupation,
                Employer=$employer,
                Location=$location,
                CurrentLocationId=$current,
                HomeLocationId=$home,
                WorkLocationId=$work,
                Hometown=$hometown,
                Address=$address,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE Id=$id;
            """;
            cmd.Parameters.AddWithValue("$id",npcId);
            cmd.Parameters.AddWithValue("$occupation",e.Occupation??"");
            cmd.Parameters.AddWithValue("$employer",e.Employer??"");
            cmd.Parameters.AddWithValue("$location",e.Location??"");
            cmd.Parameters.AddWithValue("$current",e.CurrentLocationId??"");
            cmd.Parameters.AddWithValue("$home",e.HomeLocationId??"");
            cmd.Parameters.AddWithValue("$work",e.WorkLocationId??"");
            cmd.Parameters.AddWithValue("$hometown",e.Hometown??"");
            cmd.Parameters.AddWithValue("$address",e.Address??"");
            cmd.ExecuteNonQuery();
        }
        // Keep the existing Studio appearance profile synchronized while
        // the physical-profile architecture is being consolidated.
        using (var ensureAppearance = conn.CreateCommand())
        {
            ensureAppearance.CommandText = """
        INSERT INTO NpcAppearanceProfiles
        (
            NpcId,
            AppearanceStatus,
            BodyType,
            HeightText,
            HairColor,
            HairStyle,
            EyeColor,
            SkinTone,
            ClothingStyle,
            DistinguishingFeatures,
            UpdatedRealAt
        )
        VALUES
        (
            $id,
            'Active',
            $body,
            $height,
            $hairColor,
            $hairStyle,
            $eyes,
            $skin,
            $clothing,
            $features,
            CURRENT_TIMESTAMP
        )
        ON CONFLICT(NpcId) DO UPDATE SET
            BodyType = $body,
            HeightText = $height,
            HairColor = $hairColor,
            HairStyle = $hairStyle,
            EyeColor = $eyes,
            SkinTone = $skin,
            ClothingStyle = $clothing,
            DistinguishingFeatures = $features,
            UpdatedRealAt = CURRENT_TIMESTAMP;
        """;

            ensureAppearance.Parameters.AddWithValue("$id", npcId);
            ensureAppearance.Parameters.AddWithValue("$body", e.BodyType ?? "");
            ensureAppearance.Parameters.AddWithValue("$height", e.Height ?? "");
            ensureAppearance.Parameters.AddWithValue("$hairColor", e.HairColor ?? "");
            ensureAppearance.Parameters.AddWithValue("$hairStyle", e.HairStyle ?? "");
            ensureAppearance.Parameters.AddWithValue("$eyes", e.EyeColor ?? "");
            ensureAppearance.Parameters.AddWithValue("$skin", e.SkinTone ?? "");
            ensureAppearance.Parameters.AddWithValue("$clothing", e.ClothingStyle ?? "");
            ensureAppearance.Parameters.AddWithValue("$features", e.DistinguishingFeatures ?? "");

            ensureAppearance.ExecuteNonQuery();
        }
        // Preserve compatibility with the canonical physical-profile table.
        using (var ensure=conn.CreateCommand())
        {
            ensure.CommandText="""
            INSERT INTO NpcPhysicalProfiles(NpcId,UpdatedRealAt)
            VALUES($id,CURRENT_TIMESTAMP)
            ON CONFLICT(NpcId) DO NOTHING;
            """;
            ensure.Parameters.AddWithValue("$id",npcId);
            ensure.ExecuteNonQuery();
        }

        using(var cmd=conn.CreateCommand())
        {
            cmd.CommandText="""
            UPDATE NpcPhysicalProfiles SET
                BodyType=$body,
                HairColor=$hairColor,
                HairStyle=$hairStyle,
                EyeColor=$eyes,
                SkinTone=$skin,
                DefaultClothingStyle=$clothing,
                DistinctiveFeatures=$features,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE NpcId=$id;
            """;
            cmd.Parameters.AddWithValue("$id",npcId);
            cmd.Parameters.AddWithValue("$body",e.BodyType??"");
            cmd.Parameters.AddWithValue("$hairColor",e.HairColor??"");
            cmd.Parameters.AddWithValue("$hairStyle",e.HairStyle??"");
            cmd.Parameters.AddWithValue("$eyes",e.EyeColor??"");
            cmd.Parameters.AddWithValue("$skin",e.SkinTone??"");
            cmd.Parameters.AddWithValue("$clothing",e.ClothingStyle??"");
            cmd.Parameters.AddWithValue("$features",e.DistinguishingFeatures??"");
            cmd.ExecuteNonQuery();
        }

        // Height is represented differently in older Studio data. Save the display text
        // only if the live Characters table has a compatible HeightCm numeric field.
        if(double.TryParse((e.Height??"").Replace("\"","").Replace("'","."),out _))
        {
            // no-op: do not guess units from display text.
        }

        return Task.CompletedTask;
    }

    public Task SavePersonalHomeAsync(int npcId, PersonalHomeEdit e)
    {
        using var conn=Open();
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        UPDATE Characters SET
            HomeLocationId=$home,
            CurrentLocationId=$current,
            WorkLocationId=$work,
            Location=$location,
            Address=$address,
            UpdatedRealAt=CURRENT_TIMESTAMP
        WHERE Id=$id;
        """;
        cmd.Parameters.AddWithValue("$id",npcId);
        cmd.Parameters.AddWithValue("$home",e.HomeLocationId??"");
        cmd.Parameters.AddWithValue("$current",e.CurrentLocationId??"");
        cmd.Parameters.AddWithValue("$work",e.WorkLocationId??"");
        cmd.Parameters.AddWithValue("$location",e.Location??"");
        cmd.Parameters.AddWithValue("$address",e.Address??"");
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SavePhoneAsync(int npcId, CanonicalPhoneRow phone)
    {
        using var conn=Open();
        var id=string.IsNullOrWhiteSpace(phone.PhoneId)
            ? $"phone-{npcId}-{Guid.NewGuid():N}"
            : phone.PhoneId;

        if(phone.IsPrimary)
        {
            using var clear=conn.CreateCommand();
            clear.CommandText="UPDATE NpcPhones SET IsPrimary=0 WHERE NpcId=$id;";
            clear.Parameters.AddWithValue("$id",npcId);
            clear.ExecuteNonQuery();
        }

        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        INSERT INTO NpcPhones
        (PhoneId,WorldId,NpcId,PhoneNumber,PhoneType,CarrierName,DeviceMake,DeviceModel,DeviceLabel,
         IsPrimary,IsActive,UpdatedRealAt)
        VALUES($phoneId,'smalltown',$npcId,$number,$type,$carrier,$make,$model,$label,$primary,$active,CURRENT_TIMESTAMP)
        ON CONFLICT(PhoneId) DO UPDATE SET
            PhoneNumber=excluded.PhoneNumber,
            PhoneType=excluded.PhoneType,
            CarrierName=excluded.CarrierName,
            DeviceMake=excluded.DeviceMake,
            DeviceModel=excluded.DeviceModel,
            DeviceLabel=excluded.DeviceLabel,
            IsPrimary=excluded.IsPrimary,
            IsActive=excluded.IsActive,
            UpdatedRealAt=CURRENT_TIMESTAMP;
        """;
        cmd.Parameters.AddWithValue("$phoneId",id);
        cmd.Parameters.AddWithValue("$npcId",npcId);
        cmd.Parameters.AddWithValue("$number",phone.PhoneNumber??"");
        cmd.Parameters.AddWithValue("$type",string.IsNullOrWhiteSpace(phone.PhoneType)?"Mobile":phone.PhoneType);
        cmd.Parameters.AddWithValue("$carrier",phone.CarrierName??"");
        cmd.Parameters.AddWithValue("$make",phone.DeviceMake??"");
        cmd.Parameters.AddWithValue("$model",phone.DeviceModel??"");
        cmd.Parameters.AddWithValue("$label",phone.DeviceLabel??"");
        cmd.Parameters.AddWithValue("$primary",phone.IsPrimary?1:0);
        cmd.Parameters.AddWithValue("$active",phone.IsActive?1:0);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SavePhoneContactAsync(int npcId, CanonicalPhoneContactRow c)
    {
        using var conn=Open();
        EnsurePersonalPhoneContactTable(conn);
        var id=string.IsNullOrWhiteSpace(c.ContactId)?$"contact-{npcId}-{Guid.NewGuid():N}":c.ContactId;
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        INSERT INTO NpcPhoneContacts
        (ContactId,NpcId,ContactNpcId,DisplayName,PhoneNumber,RelationshipLabel,IsFavorite,IsBlocked,UpdatedRealAt)
        VALUES($cid,$id,$contactNpc,$name,$number,$relationship,$favorite,$blocked,CURRENT_TIMESTAMP)
        ON CONFLICT(ContactId) DO UPDATE SET
            ContactNpcId=excluded.ContactNpcId,
            DisplayName=excluded.DisplayName,
            PhoneNumber=excluded.PhoneNumber,
            RelationshipLabel=excluded.RelationshipLabel,
            IsFavorite=excluded.IsFavorite,
            IsBlocked=excluded.IsBlocked,
            UpdatedRealAt=CURRENT_TIMESTAMP;
        """;
        cmd.Parameters.AddWithValue("$cid",id);
        cmd.Parameters.AddWithValue("$id",npcId);
        cmd.Parameters.AddWithValue("$contactNpc",c.ContactNpcId>0?c.ContactNpcId:DBNull.Value);
        cmd.Parameters.AddWithValue("$name",c.DisplayName??"");
        cmd.Parameters.AddWithValue("$number",c.PhoneNumber??"");
        cmd.Parameters.AddWithValue("$relationship",c.RelationshipLabel??"");
        cmd.Parameters.AddWithValue("$favorite",c.IsFavorite?1:0);
        cmd.Parameters.AddWithValue("$blocked",c.IsBlocked?1:0);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeletePhoneContactAsync(string contactId)
    {
        using var conn=Open();
        EnsurePersonalPhoneContactTable(conn);
        using var cmd=conn.CreateCommand();
        cmd.CommandText="DELETE FROM NpcPhoneContacts WHERE ContactId=$id;";
        cmd.Parameters.AddWithValue("$id",contactId);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SaveVehicleAsync(int npcId, CanonicalVehicleRow v)
    {
        using var conn=Open();
        var id=string.IsNullOrWhiteSpace(v.VehicleId)?$"vehicle-{npcId}-{Guid.NewGuid():N}":v.VehicleId;
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        INSERT INTO Vehicles
        (VehicleId,WorldId,RegisteredOwnerNpcId,PrimaryDriverNpcId,VehicleType,Make,Model,ModelYear,Color,
         Vin,PlateNumber,PlateState,Status,OdometerMiles,UpdatedRealAt)
        VALUES($vid,'smalltown',$npc,$npc,$type,$make,$model,$year,$color,$vin,$plate,$state,$status,$miles,CURRENT_TIMESTAMP)
        ON CONFLICT(VehicleId) DO UPDATE SET
            VehicleType=excluded.VehicleType,
            Make=excluded.Make,
            Model=excluded.Model,
            ModelYear=excluded.ModelYear,
            Color=excluded.Color,
            Vin=excluded.Vin,
            PlateNumber=excluded.PlateNumber,
            PlateState=excluded.PlateState,
            Status=excluded.Status,
            OdometerMiles=excluded.OdometerMiles,
            UpdatedRealAt=CURRENT_TIMESTAMP;
        """;
        cmd.Parameters.AddWithValue("$vid",id);
        cmd.Parameters.AddWithValue("$npc",npcId);
        cmd.Parameters.AddWithValue("$type",string.IsNullOrWhiteSpace(v.VehicleType)?"Car":v.VehicleType);
        cmd.Parameters.AddWithValue("$make",v.Make??"");
        cmd.Parameters.AddWithValue("$model",v.Model??"");
        cmd.Parameters.AddWithValue("$year",(object?)v.ModelYear??DBNull.Value);
        cmd.Parameters.AddWithValue("$color",v.Color??"");
        cmd.Parameters.AddWithValue("$vin",v.Vin??"");
        cmd.Parameters.AddWithValue("$plate",v.PlateNumber??"");
        cmd.Parameters.AddWithValue("$state",v.PlateState??"");
        cmd.Parameters.AddWithValue("$status",string.IsNullOrWhiteSpace(v.Status)?"Active":v.Status);
        cmd.Parameters.AddWithValue("$miles",(object?)v.OdometerMiles??DBNull.Value);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<FamilyBuildPlanEdit> GetFamilyBuildPlanAsync(int npcId)
    {
        using var conn=Open();
        EnsureFamilyPlanTable(conn);
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        SELECT CreateMother,MotherSiblingCount,CreateFather,FatherSiblingCount,
               BrotherCount,SisterCount,SiblingBirthPattern,
               CreateMaternalGrandmother,CreateMaternalGrandfather,
               CreatePaternalGrandmother,CreatePaternalGrandfather,
               GenerateAuntsUncles,GenerateCousins,GenerateSpousesInLaws,
               ReuseExistingTownNpcForSpouses,ExtendedFamilyDepth
        FROM NpcFamilyBuildPlans WHERE RootNpcId=$id;
        """;
        cmd.Parameters.AddWithValue("$id",npcId);
        using var r=cmd.ExecuteReader();
        if(!r.Read()) return Task.FromResult(new FamilyBuildPlanEdit());

        return Task.FromResult(new FamilyBuildPlanEdit{
            CreateMother=B(r,0), MotherSiblingCount=Int(r,1),
            CreateFather=B(r,2), FatherSiblingCount=Int(r,3),
            BrotherCount=Int(r,4), SisterCount=Int(r,5), SiblingBirthPattern=Str(r,6,"Auto"),
            CreateMaternalGrandmother=B(r,7),CreateMaternalGrandfather=B(r,8),
            CreatePaternalGrandmother=B(r,9),CreatePaternalGrandfather=B(r,10),
            GenerateAuntsUncles=B(r,11),GenerateCousins=B(r,12),GenerateSpousesInLaws=B(r,13),
            ReuseExistingTownNpcForSpouses=B(r,14),ExtendedFamilyDepth=Str(r,15,"Deep")
        });
    }

    public Task SaveFamilyBuildPlanAsync(int npcId, FamilyBuildPlanEdit e)
    {
        using var conn=Open();
        EnsureFamilyPlanTable(conn);
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        INSERT INTO NpcFamilyBuildPlans
        (RootNpcId,CreateMother,MotherSiblingCount,CreateFather,FatherSiblingCount,BrotherCount,SisterCount,
         SiblingBirthPattern,CreateMaternalGrandmother,CreateMaternalGrandfather,CreatePaternalGrandmother,
         CreatePaternalGrandfather,GenerateAuntsUncles,GenerateCousins,GenerateSpousesInLaws,
         ReuseExistingTownNpcForSpouses,ExtendedFamilyDepth,Status,UpdatedRealAt)
        VALUES($id,$cm,$ms,$cf,$fs,$bro,$sis,$pattern,$mgm,$mgf,$pgm,$pgf,$au,$co,$sp,$reuse,$depth,'Draft',CURRENT_TIMESTAMP)
        ON CONFLICT(RootNpcId) DO UPDATE SET
          CreateMother=excluded.CreateMother,MotherSiblingCount=excluded.MotherSiblingCount,
          CreateFather=excluded.CreateFather,FatherSiblingCount=excluded.FatherSiblingCount,
          BrotherCount=excluded.BrotherCount,SisterCount=excluded.SisterCount,
          SiblingBirthPattern=excluded.SiblingBirthPattern,
          CreateMaternalGrandmother=excluded.CreateMaternalGrandmother,
          CreateMaternalGrandfather=excluded.CreateMaternalGrandfather,
          CreatePaternalGrandmother=excluded.CreatePaternalGrandmother,
          CreatePaternalGrandfather=excluded.CreatePaternalGrandfather,
          GenerateAuntsUncles=excluded.GenerateAuntsUncles,GenerateCousins=excluded.GenerateCousins,
          GenerateSpousesInLaws=excluded.GenerateSpousesInLaws,
          ReuseExistingTownNpcForSpouses=excluded.ReuseExistingTownNpcForSpouses,
          ExtendedFamilyDepth=excluded.ExtendedFamilyDepth,Status='Draft',UpdatedRealAt=CURRENT_TIMESTAMP;
        """;
        cmd.Parameters.AddWithValue("$id",npcId);
        cmd.Parameters.AddWithValue("$cm",e.CreateMother?1:0); cmd.Parameters.AddWithValue("$ms",Math.Max(0,e.MotherSiblingCount));
        cmd.Parameters.AddWithValue("$cf",e.CreateFather?1:0); cmd.Parameters.AddWithValue("$fs",Math.Max(0,e.FatherSiblingCount));
        cmd.Parameters.AddWithValue("$bro",Math.Max(0,e.BrotherCount)); cmd.Parameters.AddWithValue("$sis",Math.Max(0,e.SisterCount));
        cmd.Parameters.AddWithValue("$pattern",e.SiblingBirthPattern??"Auto");
        cmd.Parameters.AddWithValue("$mgm",e.CreateMaternalGrandmother?1:0);cmd.Parameters.AddWithValue("$mgf",e.CreateMaternalGrandfather?1:0);
        cmd.Parameters.AddWithValue("$pgm",e.CreatePaternalGrandmother?1:0);cmd.Parameters.AddWithValue("$pgf",e.CreatePaternalGrandfather?1:0);
        cmd.Parameters.AddWithValue("$au",e.GenerateAuntsUncles?1:0);cmd.Parameters.AddWithValue("$co",e.GenerateCousins?1:0);
        cmd.Parameters.AddWithValue("$sp",e.GenerateSpousesInLaws?1:0);cmd.Parameters.AddWithValue("$reuse",e.ReuseExistingTownNpcForSpouses?1:0);
        cmd.Parameters.AddWithValue("$depth",e.ExtendedFamilyDepth??"Deep");
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static void EnsurePersonalPhoneContactTable(SqliteConnection conn)
    {
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS NpcPhoneContacts
        (
            ContactId TEXT PRIMARY KEY,NpcId INTEGER NOT NULL,ContactNpcId INTEGER NULL,
            DisplayName TEXT NOT NULL DEFAULT '',PhoneNumber TEXT NOT NULL DEFAULT '',
            RelationshipLabel TEXT NOT NULL DEFAULT '',IsFavorite INTEGER NOT NULL DEFAULT 0,
            IsBlocked INTEGER NOT NULL DEFAULT 0,Notes TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureFamilyPlanTable(SqliteConnection conn)
    {
        using var cmd=conn.CreateCommand();
        cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS NpcFamilyBuildPlans
        (
            RootNpcId INTEGER PRIMARY KEY,CreateMother INTEGER NOT NULL DEFAULT 1,MotherSiblingCount INTEGER NOT NULL DEFAULT 0,
            CreateFather INTEGER NOT NULL DEFAULT 1,FatherSiblingCount INTEGER NOT NULL DEFAULT 0,BrotherCount INTEGER NOT NULL DEFAULT 0,
            SisterCount INTEGER NOT NULL DEFAULT 0,SiblingBirthPattern TEXT NOT NULL DEFAULT 'Auto',
            CreateMaternalGrandmother INTEGER NOT NULL DEFAULT 1,CreateMaternalGrandfather INTEGER NOT NULL DEFAULT 1,
            CreatePaternalGrandmother INTEGER NOT NULL DEFAULT 1,CreatePaternalGrandfather INTEGER NOT NULL DEFAULT 1,
            GenerateAuntsUncles INTEGER NOT NULL DEFAULT 1,GenerateCousins INTEGER NOT NULL DEFAULT 1,
            GenerateSpousesInLaws INTEGER NOT NULL DEFAULT 1,ReuseExistingTownNpcForSpouses INTEGER NOT NULL DEFAULT 1,
            ExtendedFamilyDepth TEXT NOT NULL DEFAULT 'Deep',Status TEXT NOT NULL DEFAULT 'Draft',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """;
        cmd.ExecuteNonQuery();
    }

    private static bool B(SqliteDataReader r,int i)=>!r.IsDBNull(i)&&Convert.ToInt32(r.GetValue(i))!=0;
    private static int Int(SqliteDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt32(r.GetValue(i));
    private static string Str(SqliteDataReader r,int i,string f)=>r.IsDBNull(i)?f:(r.GetString(i)??f);
}
