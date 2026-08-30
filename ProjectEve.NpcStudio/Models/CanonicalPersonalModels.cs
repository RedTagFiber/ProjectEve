namespace ProjectEve.NpcStudio.Models;

public sealed class CanonicalPhoneRow
{
    public string PhoneId { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string PhoneType { get; set; } = "";
    public string CarrierName { get; set; } = "";
    public string DeviceMake { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string DeviceLabel { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CanonicalPhoneContactRow
{
    public string ContactId { get; set; } = "";
    public int ContactNpcId { get; set; }
    public string DisplayName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string RelationshipLabel { get; set; } = "";
    public bool IsFavorite { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class CanonicalVehicleRow
{
    public string VehicleId { get; set; } = "";
    public string VehicleType { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int? ModelYear { get; set; }
    public string Color { get; set; } = "";
    public string Vin { get; set; } = "";
    public string PlateNumber { get; set; } = "";
    public string PlateState { get; set; } = "";
    public string Status { get; set; } = "";
    public double? OdometerMiles { get; set; }
}

public sealed class CanonicalPersonalBundle
{
    public List<CanonicalPhoneRow> Phones { get; set; } = new();
    public List<CanonicalPhoneContactRow> PhoneContacts { get; set; } = new();
    public List<CanonicalVehicleRow> Vehicles { get; set; } = new();
}
