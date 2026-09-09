using MongoDB.Bson;

namespace GarageDoctor.Infrastructure;

/// <summary>
/// Aggregation fragments that fold <c>milesAtFailure</c> into the fixed <see cref="MileageBuckets"/>.
/// The vehicle profiles and the component taxonomy both build their histograms from these, so the
/// two agree bucket for bucket instead of drifting apart in two copies of the same arithmetic.
/// </summary>
public static class MileageHistogramStages
{
    private const string BucketIndexField = "mileageBucketIndex";

    private const string BucketCountPrefix = "mileageBucket";

    private static int LastBucketIndex => MileageBuckets.LastBucketStart / MileageBuckets.BucketSize;

    /// <summary>
    /// A <c>$set</c> stage stamping every complaint with the index of its bucket, or null when the
    /// complaint carries no odometer reading.
    /// </summary>
    public static BsonDocument SetBucketIndexStage() =>
        new("$set", new BsonDocument(BucketIndexField, BucketIndexExpression()));

    /// <summary>An accumulator counting the complaints that carry an odometer reading.</summary>
    public static BsonDocument WithMileageAccumulator() =>
        new("$sum", new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$ne", new BsonArray { "$milesAtFailure", BsonNull.Value }),
            1,
            0
        }));

    /// <summary>Adds one counting accumulator per bucket to a <c>$group</c> specification.</summary>
    public static void AddBucketAccumulators(BsonDocument group)
    {
        ArgumentNullException.ThrowIfNull(group);

        for (var index = 0; index <= LastBucketIndex; index++)
        {
            group.Add(BucketCountPrefix + index, new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { "$" + BucketIndexField, index }),
                1,
                0
            })));
        }
    }

    /// <summary>
    /// The array expression that turns the per-bucket accumulators back into the histogram shape,
    /// with every bucket present even when its count is zero.
    /// </summary>
    public static BsonArray HistogramExpression()
    {
        var buckets = MileageBuckets.Empty();
        var histogram = new BsonArray();

        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index];

            histogram.Add(new BsonDocument
            {
                { "from", bucket.From },
                { "to", bucket.To is int upperBound ? new BsonInt32(upperBound) : BsonNull.Value },
                { "count", "$" + BucketCountPrefix + index }
            });
        }

        return histogram;
    }

    private static BsonDocument BucketIndexExpression() =>
        new("$cond", new BsonArray
        {
            new BsonDocument("$ne", new BsonArray { "$milesAtFailure", BsonNull.Value }),
            new BsonDocument("$toInt", new BsonDocument("$min", new BsonArray
            {
                new BsonDocument("$max", new BsonArray
                {
                    new BsonDocument("$floor", new BsonDocument("$divide", new BsonArray
                    {
                        "$milesAtFailure",
                        MileageBuckets.BucketSize
                    })),
                    0
                }),
                LastBucketIndex
            })),
            BsonNull.Value
        });
}
