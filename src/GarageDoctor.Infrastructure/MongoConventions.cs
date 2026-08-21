using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace GarageDoctor.Infrastructure;

public static class MongoConventions
{
    private const string PackName = "GarageDoctor";

    private static readonly Lock RegistrationGate = new();

    private static bool alreadyRegistered;

    public static void Register()
    {
        lock (RegistrationGate)
        {
            if (alreadyRegistered)
            {
                return;
            }

            var pack = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true)
            };

            ConventionRegistry.Register(PackName, pack, _ => true);

            _ = BsonSerializer.TryRegisterSerializer(new DateOnlySerializer(BsonType.DateTime));

            alreadyRegistered = true;
        }
    }
}
