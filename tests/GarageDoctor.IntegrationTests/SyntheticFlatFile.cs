using System.Text;
using GarageDoctor.Domain.Parsing;

namespace GarageDoctor.IntegrationTests;

public sealed class SyntheticComplaintFile : IDisposable
{
    private static readonly string[] Makes = ["VW", "VOLKSWAGEN", "AUDI", "CHEVY", "MERCEDES BENZ"];
    private static readonly string[] Models = ["GOLF", "GOLF", "A3", "MALIBU", "C300"];
    private static readonly string[] Years = ["2015", "2016", "9999", "2017"];
    private static readonly string[] Components =
    [
        "POWER TRAIN:AUTOMATIC TRANSMISSION",
        "ENGINE AND ENGINE COOLING:COOLING SYSTEM:RADIATOR ASSEMBLY",
        "AIR BAGS:FRONTAL:DRIVER SIDE:INFLATOR MODULE",
        "SERVICE BRAKES, HYDRAULIC:ANTILOCK/TRACTION",
        "ELECTRICAL SYSTEM"
    ];
    private static readonly string[] NonVehicleProductTypes = ["T", "C", "E"];

    private SyntheticComplaintFile(string path, int vehicleRows, int nonVehicleRows)
    {
        Path = path;
        VehicleRows = vehicleRows;
        NonVehicleRows = nonVehicleRows;
    }

    public string Path { get; }

    public int VehicleRows { get; }

    public int NonVehicleRows { get; }

    public static SyntheticComplaintFile Create(int totalRows)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"complaints-{Guid.NewGuid():N}.tsv");
        var vehicleRows = 0;
        var nonVehicleRows = 0;

        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            for (var index = 0; index < totalRows; index++)
            {
                var isVehicle = index % 20 != 0;
                if (isVehicle)
                {
                    vehicleRows++;
                }
                else
                {
                    nonVehicleRows++;
                }

                writer.WriteLine(BuildRow(index, isVehicle));
            }
        }

        return new SyntheticComplaintFile(path, vehicleRows, nonVehicleRows);
    }

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }

    private static string BuildRow(int index, bool isVehicle)
    {
        var fields = new string[ComplaintFields.FieldCount];
        Array.Fill(fields, string.Empty);

        fields[ComplaintFields.ComplaintId] = (index + 1).ToString();
        fields[ComplaintFields.OdiNumber] = (900_000 + index).ToString();
        fields[ComplaintFields.ManufacturerName] = "Synthetic Motors, Inc.";
        fields[ComplaintFields.MakeText] = Makes[index % Makes.Length];
        fields[ComplaintFields.ModelText] = Models[index % Models.Length];
        fields[ComplaintFields.YearText] = Years[index % Years.Length];
        fields[ComplaintFields.Crash] = index % 7 == 0 ? "Y" : "N";
        fields[ComplaintFields.FailureDate] = "20180612";
        fields[ComplaintFields.Fire] = index % 11 == 0 ? "Y" : "N";
        fields[ComplaintFields.Injured] = (index % 3).ToString();
        fields[ComplaintFields.Deaths] = "0";
        fields[ComplaintFields.ComponentDescription] = Components[index % Components.Length];
        fields[ComplaintFields.City] = "SPRINGFIELD";
        fields[ComplaintFields.State] = "CA";
        fields[ComplaintFields.Vin] = "1FAKEVIN123";
        fields[ComplaintFields.DateAdded] = "20190315";
        fields[ComplaintFields.ReceivedDate] = index % 2 == 0 ? "20190314" : "20200820";
        fields[ComplaintFields.Miles] = (index % 4 == 0 ? 0 : index % 240_000).ToString();
        fields[ComplaintFields.ComplaintDescription] = $"Synthetic narrative for record {index + 1}.";
        fields[ComplaintFields.PoliceReport] = index % 13 == 0 ? "Y" : "N";
        fields[ComplaintFields.Cylinders] = "4";
        fields[ComplaintFields.DriveTrain] = "FWD";
        fields[ComplaintFields.FuelType] = "GS";
        fields[ComplaintFields.TransmissionType] = "AUTO";
        fields[ComplaintFields.DealerName] = "SYNTHETIC DEALER";
        fields[ComplaintFields.DealerTelephone] = "5550000000";
        fields[ComplaintFields.DealerCity] = "SPRINGFIELD";
        fields[ComplaintFields.DealerState] = "CA";
        fields[ComplaintFields.DealerZip] = "90000";
        fields[ComplaintFields.ProductType] = isVehicle
            ? ComplaintFields.VehicleProductType
            : NonVehicleProductTypes[index % NonVehicleProductTypes.Length];
        fields[ComplaintFields.MedicalAttention] = index % 17 == 0 ? "Y" : "N";
        fields[ComplaintFields.VehicleTowed] = index % 5 == 0 ? "Y" : "N";
        fields[ComplaintFields.StateOfIncident] = "CA";
        fields[ComplaintFields.VehicleOperator] = "SYNTHETIC OPERATOR";

        return string.Join('\t', fields);
    }
}

public sealed class SyntheticRecallFile : IDisposable
{
    private static readonly string[] Makes = ["MERCEDES BENZ", "VW", "AUDI", "FORD"];
    private static readonly string[] Models = ["C300", "GOLF", "A3", "F-150"];

    private SyntheticRecallFile(IReadOnlyList<string> paths, int vehicleRows, int doNotDriveRows, int parkOutsideRows)
    {
        Paths = paths;
        VehicleRows = vehicleRows;
        DoNotDriveRows = doNotDriveRows;
        ParkOutsideRows = parkOutsideRows;
    }

    public IReadOnlyList<string> Paths { get; }

    public int VehicleRows { get; }

    public int DoNotDriveRows { get; }

    public int ParkOutsideRows { get; }

    public static SyntheticRecallFile Create(int rowsPerFile)
    {
        var paths = new List<string>();
        var vehicleRows = 0;
        var doNotDriveRows = 0;
        var parkOutsideRows = 0;
        var recordId = 1;

        foreach (var fileIndex in new[] { 0, 1 })
        {
            var path = Path.Combine(Path.GetTempPath(), $"recalls-{fileIndex}-{Guid.NewGuid():N}.tsv");
            using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\r\n" })
            {
                for (var index = 0; index < rowsPerFile; index++)
                {
                    var isVehicle = index % 10 != 0;
                    var doNotDrive = isVehicle && index % 25 == 0;
                    var parkOutside = isVehicle && index % 40 == 0;

                    if (isVehicle)
                    {
                        vehicleRows++;
                    }

                    if (doNotDrive)
                    {
                        doNotDriveRows++;
                    }

                    if (parkOutside)
                    {
                        parkOutsideRows++;
                    }

                    writer.WriteLine(BuildRow(recordId++, index, isVehicle, doNotDrive, parkOutside));
                }

                writer.WriteLine();
            }

            paths.Add(path);
        }

        return new SyntheticRecallFile(paths, vehicleRows, doNotDriveRows, parkOutsideRows);
    }

    public void Dispose()
    {
        foreach (var path in Paths.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    private static string BuildRow(int recordId, int index, bool isVehicle, bool doNotDrive, bool parkOutside)
    {
        var fields = new string[RecallFields.FieldCount];
        Array.Fill(fields, string.Empty);

        fields[RecallFields.RecordId] = recordId.ToString();
        fields[RecallFields.CampaignNumber] = $"{20 + index % 6}V{index % 900:000}000";
        fields[RecallFields.MakeText] = Makes[index % Makes.Length];
        fields[RecallFields.ModelText] = Models[index % Models.Length];
        fields[RecallFields.YearText] = (2015 + index % 5).ToString();
        fields[RecallFields.ComponentName] = "POWER TRAIN";
        fields[RecallFields.ManufacturerName] = "Synthetic Motors USA, LLC";
        fields[RecallFields.RecallType] = isVehicle ? RecallFields.VehicleRecallType : "E";
        fields[RecallFields.PotentiallyAffected] = (1000 + index).ToString();
        fields[RecallFields.OwnersNotifiedDate] = "20210408";
        fields[RecallFields.ReportReceivedDate] = "20210301";
        fields[RecallFields.DefectDescription] = "Synthetic defect summary.";
        fields[RecallFields.Consequence] = "Synthetic consequence summary.";
        fields[RecallFields.CorrectiveAction] = "Synthetic corrective action.";
        fields[RecallFields.DoNotDrive] = doNotDrive ? "Yes" : "No";
        fields[RecallFields.ParkOutside] = parkOutside ? "Yes" : "No";

        return string.Join('\t', fields);
    }
}
