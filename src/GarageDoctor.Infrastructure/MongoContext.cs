using GarageDoctor.Domain.Models;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure;

public sealed class MongoContext
{
    public MongoContext(IMongoClient client, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        MongoConventions.Register();

        Database = client.GetDatabase(databaseName);
        Complaints = Database.GetCollection<Complaint>(CollectionNames.Complaints);
        Recalls = Database.GetCollection<RecallCampaign>(CollectionNames.Recalls);
        Vehicles = Database.GetCollection<VehicleCatalogEntry>(CollectionNames.Vehicles);
        Components = Database.GetCollection<ComponentTaxonomyEntry>(CollectionNames.Components);
        Profiles = Database.GetCollection<VehicleProfile>(CollectionNames.Profiles);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Complaint> Complaints { get; }

    public IMongoCollection<RecallCampaign> Recalls { get; }

    public IMongoCollection<VehicleCatalogEntry> Vehicles { get; }

    public IMongoCollection<ComponentTaxonomyEntry> Components { get; }

    public IMongoCollection<VehicleProfile> Profiles { get; }
}
