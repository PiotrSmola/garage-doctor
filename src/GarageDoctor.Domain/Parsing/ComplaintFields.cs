namespace GarageDoctor.Domain.Parsing;

public static class ComplaintFields
{
    public const int FieldCount = 51;

    public const int ComplaintId = 0;
    public const int OdiNumber = 1;
    public const int ManufacturerName = 2;
    public const int MakeText = 3;
    public const int ModelText = 4;
    public const int YearText = 5;
    public const int Crash = 6;
    public const int FailureDate = 7;
    public const int Fire = 8;
    public const int Injured = 9;
    public const int Deaths = 10;
    public const int ComponentDescription = 11;
    public const int City = 12;
    public const int State = 13;
    public const int Vin = 14;
    public const int DateAdded = 15;
    public const int ReceivedDate = 16;
    public const int Miles = 17;
    public const int Occurrences = 18;
    public const int ComplaintDescription = 19;
    public const int ComplaintType = 20;
    public const int PoliceReport = 21;
    public const int PurchaseDate = 22;
    public const int OriginalOwner = 23;
    public const int AntiLockBrakes = 24;
    public const int CruiseControl = 25;
    public const int Cylinders = 26;
    public const int DriveTrain = 27;
    public const int FuelSystem = 28;
    public const int FuelType = 29;
    public const int TransmissionType = 30;
    public const int VehicleSpeed = 31;
    public const int TireIdentifier = 32;
    public const int TireSize = 33;
    public const int TireLocation = 34;
    public const int TireFailureType = 35;
    public const int OriginalEquipment = 36;
    public const int ManufactureDate = 37;
    public const int SeatType = 38;
    public const int RestraintType = 39;
    public const int DealerName = 40;
    public const int DealerTelephone = 41;
    public const int DealerCity = 42;
    public const int DealerState = 43;
    public const int DealerZip = 44;
    public const int ProductType = 45;
    public const int Repaired = 46;
    public const int MedicalAttention = 47;
    public const int VehicleTowed = 48;
    public const int StateOfIncident = 49;
    public const int VehicleOperator = 50;

    public const string VehicleProductType = "V";

    public static readonly IReadOnlyList<int> PersonalDataFields =
    [
        City,
        Vin,
        DealerName,
        DealerTelephone,
        DealerCity,
        DealerState,
        DealerZip,
        VehicleOperator
    ];
}
