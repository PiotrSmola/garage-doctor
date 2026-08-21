using MongoDB.Bson;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoFixtureSmokeTests(MongoFixture fixture)
{
    [Fact]
    public async Task ContainerAnswersPing()
    {
        var database = fixture.CreateClient().GetDatabase("smoke");

        var response = await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

        Assert.Equal(1.0, response["ok"].ToDouble());
    }
}
