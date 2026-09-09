using MongoDB.Bson;

namespace GarageDoctor.IntegrationTests;

/// <summary>Pulls the interesting names out of an <c>explain</c> result regardless of how deeply the server nests them.</summary>
internal static class ExplainPlans
{
    public static string[] Stages(BsonDocument explain) => ValuesNamed(explain, "stage").ToArray();

    public static string[] IndexNames(BsonDocument explain) => ValuesNamed(explain, "indexName").ToArray();

    private static IEnumerable<string> ValuesNamed(BsonValue value, string elementName)
    {
        switch (value)
        {
            case BsonDocument document:
                foreach (var element in document)
                {
                    if (element.Name == elementName && element.Value.IsString)
                    {
                        yield return element.Value.AsString;
                    }

                    foreach (var nested in ValuesNamed(element.Value, elementName))
                    {
                        yield return nested;
                    }
                }

                break;

            case BsonArray array:
                foreach (var item in array)
                {
                    foreach (var nested in ValuesNamed(item, elementName))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
