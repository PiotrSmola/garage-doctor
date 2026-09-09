using GarageDoctor.Domain.Models;

namespace GarageDoctor.Infrastructure;

public static class MileageBuckets
{
    public const int BucketSize = 25_000;

    public const int LastBucketStart = 250_000;

    private static readonly MileageBucket[] ZeroCounts = BuildZeroCounts();

    public static IReadOnlyList<MileageBucket> Empty() => ZeroCounts;

    private static MileageBucket[] BuildZeroCounts()
    {
        var lastIndex = LastBucketStart / BucketSize;
        var buckets = new MileageBucket[lastIndex + 1];

        for (var index = 0; index < lastIndex; index++)
        {
            buckets[index] = new MileageBucket
            {
                From = index * BucketSize,
                To = (index + 1) * BucketSize,
                Count = 0
            };
        }

        buckets[lastIndex] = new MileageBucket
        {
            From = LastBucketStart,
            To = null,
            Count = 0
        };

        return buckets;
    }
}
