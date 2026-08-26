using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Caching;
using GarageDoctor.Infrastructure.Queries;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class QueryCacheTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string UnreachableRedis =
        "127.0.0.1:6399,abortConnect=false,connectTimeout=250,connectRetry=0,syncTimeout=250";

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public async Task InitializeAsync() => await _redis.StartAsync();

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task TheSecondCallIsServedFromRedisInsteadOfMongo()
    {
        var context = await SeededContextAsync();
        var counter = new CountingCatalogQueries(new MongoVehicleCatalogQueries(context));
        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var queries = new CachedVehicleCatalogQueries(counter, Cache(distributed, options), options);

        var first = await queries.GetMakesAsync(CancellationToken.None);
        var second = await queries.GetMakesAsync(CancellationToken.None);

        Assert.Equal(1, counter.MakesCalls);
        Assert.Equal(first.Select(make => make.MakeSlug), second.Select(make => make.MakeSlug));
        Assert.Equal(first.Select(make => make.ComplaintCount), second.Select(make => make.ComplaintCount));
        Assert.Equal(first.Select(make => make.ModelCount), second.Select(make => make.ModelCount));
        Assert.Equal(["VOLKSWAGEN", "AUDI"], second.Select(make => make.Make));
    }

    [Fact]
    public async Task KeysVaryByEveryArgumentThatChangesTheResult()
    {
        var context = await SeededContextAsync();
        var counter = new CountingCatalogQueries(new MongoVehicleCatalogQueries(context));
        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var cache = Cache(distributed, options);
        var queries = new CachedVehicleCatalogQueries(counter, cache, options);

        var volkswagen = await queries.GetMakeAsync("volkswagen", CancellationToken.None);
        var audi = await queries.GetMakeAsync("audi", CancellationToken.None);
        var volkswagenAgain = await queries.GetMakeAsync("volkswagen", CancellationToken.None);

        Assert.Equal(2, counter.MakeCalls);
        Assert.Equal("VOLKSWAGEN", volkswagen?.Make);
        Assert.Equal("AUDI", audi?.Make);
        Assert.Equal("VOLKSWAGEN", volkswagenAgain?.Make);

        Assert.NotNull(await distributed.GetAsync(cache.BuildKey("catalog:make", "volkswagen"), CancellationToken.None));
        Assert.NotNull(await distributed.GetAsync(cache.BuildKey("catalog:make", "audi"), CancellationToken.None));
        Assert.Null(await distributed.GetAsync(cache.BuildKey("catalog:make", "delorean"), CancellationToken.None));
    }

    [Fact]
    public void KeysCarryTheApplicationPrefixAndSchemaVersion()
    {
        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var cache = Cache(distributed, options);

        Assert.Equal($"garagedoctor:{options.SchemaVersion}:catalog:makes", cache.BuildKey("catalog:makes"));
        Assert.Equal(
            $"garagedoctor:{options.SchemaVersion}:catalog:most-reported:5",
            cache.BuildKey("catalog:most-reported", 5));
        Assert.StartsWith("garagedoctor:", cache.BuildKey("catalog:overview"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AColdCacheFallsThroughToMongoAndAgreesWithTheUncachedQuery()
    {
        var context = await SeededContextAsync();
        var direct = new MongoVehicleCatalogQueries(context);
        var counter = new CountingCatalogQueries(direct);
        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var queries = new CachedVehicleCatalogQueries(counter, Cache(distributed, options), options);

        var expected = await direct.GetOverviewAsync(CancellationToken.None);
        var cold = await queries.GetOverviewAsync(CancellationToken.None);
        var warm = await queries.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, counter.OverviewCalls);
        Assert.Equal(expected, cold);
        Assert.Equal(expected, warm);
    }

    [Fact]
    public async Task AMissingResultIsCachedSoTheMissIsNotPaidTwice()
    {
        var context = await SeededContextAsync();
        var counter = new CountingCatalogQueries(new MongoVehicleCatalogQueries(context));
        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var queries = new CachedVehicleCatalogQueries(counter, Cache(distributed, options), options);

        var first = await queries.GetMakeAsync("delorean", CancellationToken.None);
        var second = await queries.GetMakeAsync("delorean", CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, counter.MakeCalls);
    }

    [Fact]
    public async Task AnUnreachableCacheStillServesDataAndWarnsOnlyOnce()
    {
        var context = await SeededContextAsync();
        var counter = new CountingCatalogQueries(new MongoVehicleCatalogQueries(context));
        var logger = new RecordingLogger<QueryCache>();

        var options = TestOptions() with
        {
            OperationTimeout = TimeSpan.FromMilliseconds(500),
            FailureCooldown = TimeSpan.FromMilliseconds(1)
        };

        using var distributed = new RedisCache(Options.Create(new RedisCacheOptions
        {
            Configuration = UnreachableRedis
        }));

        var queries = new CachedVehicleCatalogQueries(
            counter,
            new QueryCache(distributed, options, logger),
            options);

        var first = await queries.GetMakesAsync(CancellationToken.None);
        var second = await queries.GetMakesAsync(CancellationToken.None);
        var third = await queries.GetMakesAsync(CancellationToken.None);

        Assert.Equal(["VOLKSWAGEN", "AUDI"], first.Select(make => make.Make));
        Assert.Equal(["VOLKSWAGEN", "AUDI"], second.Select(make => make.Make));
        Assert.Equal(["VOLKSWAGEN", "AUDI"], third.Select(make => make.Make));
        Assert.Equal(3, counter.MakesCalls);
        Assert.Equal(1, logger.Warnings);
    }

    [Fact]
    public async Task DateOnlyAndPositionalRecordsSurviveTheRoundTripThroughRedis()
    {
        var context = await SeededContextAsync();
        await context.Recalls.InsertManyAsync(
            [
                Recall(1, "23V456000", "volkswagen|golf|2015", "GOLF", 2015, new DateOnly(2024, 3, 17), new DateOnly(2024, 4, 2)),
                Recall(2, "23V456000", "volkswagen|golf|2016", "GOLF", 2016, new DateOnly(2024, 3, 17), null)
            ],
            cancellationToken: CancellationToken.None);

        var counter = new CountingRecallQueries(new MongoRecallQueries(context));
        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var queries = new CachedRecallQueries(counter, Cache(distributed, options), options);

        var freshAdvisories = await queries.GetLatestAdvisoriesAsync(5, CancellationToken.None);
        var cachedAdvisories = await queries.GetLatestAdvisoriesAsync(5, CancellationToken.None);

        var freshDetail = await queries.GetByCampaignNumberAsync("23V456000", CancellationToken.None);
        var cachedDetail = await queries.GetByCampaignNumberAsync("23V456000", CancellationToken.None);

        Assert.Equal(1, counter.AdvisoryCalls);
        Assert.Equal(1, counter.CampaignCalls);

        var advisory = Assert.Single(cachedAdvisories);
        Assert.Equal(new DateOnly(2024, 3, 17), advisory.ReportReceivedDate);
        Assert.Equal(freshAdvisories[0], advisory);

        Assert.NotNull(freshDetail);
        Assert.NotNull(cachedDetail);
        Assert.Equal(new DateOnly(2024, 3, 17), cachedDetail.ReportReceivedDate);
        Assert.Equal(new DateOnly(2024, 4, 2), cachedDetail.OwnersNotifiedDate);
        Assert.Equal(freshDetail.ReportReceivedDate, cachedDetail.ReportReceivedDate);
        Assert.Equal(freshDetail.OwnersNotifiedDate, cachedDetail.OwnersNotifiedDate);
        Assert.Equal(freshDetail.Vehicles, cachedDetail.Vehicles);
        Assert.Equal(2, cachedDetail.Vehicles.Count);
        Assert.Equal(
            ["volkswagen|golf|2015", "volkswagen|golf|2016"],
            cachedDetail.Vehicles
                .Select(vehicle => vehicle.VehicleKey)
                .OrderBy(vehicleKey => vehicleKey, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AMissingDateStaysNullThroughTheRoundTrip()
    {
        var context = await SeededContextAsync();
        await context.Recalls.InsertOneAsync(
            Recall(3, "24V111000", "volkswagen|golf|2015", "GOLF", 2015, null, null),
            cancellationToken: CancellationToken.None);

        var options = TestOptions();

        using var distributed = RedisBackedCache();
        var queries = new CachedRecallQueries(new MongoRecallQueries(context), Cache(distributed, options), options);

        var fresh = await queries.GetByCampaignNumberAsync("24V111000", CancellationToken.None);
        var cached = await queries.GetByCampaignNumberAsync("24V111000", CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.NotNull(cached);
        Assert.Null(fresh.ReportReceivedDate);
        Assert.Null(cached.ReportReceivedDate);
        Assert.Null(cached.OwnersNotifiedDate);
    }

    [Fact]
    public async Task DisablingTheCacheAlwaysReachesMongo()
    {
        var context = await SeededContextAsync();
        var counter = new CountingCatalogQueries(new MongoVehicleCatalogQueries(context));
        var options = TestOptions() with { Enabled = false };

        using var distributed = RedisBackedCache();
        var queries = new CachedVehicleCatalogQueries(counter, Cache(distributed, options), options);

        await queries.GetMakesAsync(CancellationToken.None);
        await queries.GetMakesAsync(CancellationToken.None);

        Assert.Equal(2, counter.MakesCalls);
    }

    private RedisCache RedisBackedCache() =>
        new(Options.Create(new RedisCacheOptions { Configuration = _redis.GetConnectionString() }));

    private static QueryCache Cache(IDistributedCache distributed, QueryCacheOptions options) =>
        new(distributed, options, new RecordingLogger<QueryCache>());

    private static QueryCacheOptions TestOptions() => new() { SchemaVersion = $"test-{Guid.NewGuid():N}" };

    private async Task<MongoContext> SeededContextAsync()
    {
        var context = new MongoContext(mongo.CreateClient(), $"cache_{Guid.NewGuid():N}");

        await context.Complaints.InsertManyAsync(
            [
                Complaint(1, "VOLKSWAGEN", "GOLF", 2015),
                Complaint(2, "VOLKSWAGEN", "GOLF", 2015),
                Complaint(3, "VOLKSWAGEN", "GOLF", 2016),
                Complaint(4, "AUDI", "A3", 2015)
            ],
            cancellationToken: CancellationToken.None);

        await new VehicleCatalogBuilder(context, NullLogger<VehicleCatalogBuilder>.Instance)
            .RebuildVehiclesAsync(CancellationToken.None);

        return context;
    }

    private static Complaint Complaint(int id, string make, string model, int modelYear) => new()
    {
        Id = id,
        OdiNumber = 14_000_000 + id,
        Manufacturer = "Fictional Motors Group",
        Make = make,
        MakeRaw = make,
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        Component = new ComponentPath { Raw = "BRAKES", Group = "BRAKES", Levels = ["BRAKES"] },
        MilesAtFailure = 31_000,
        Description = "Synthetic complaint written for the query cache tests.",
        ReceivedDate = new DateOnly(2021, 5, 4),
        VehicleKey = VehicleKey.Create(VehicleKey.Slug(make), VehicleKey.Slug(model), modelYear)
    };

    private static RecallCampaign Recall(
        int id,
        string campaignNumber,
        string vehicleKey,
        string model,
        int modelYear,
        DateOnly? reportReceivedDate,
        DateOnly? ownersNotifiedDate) => new()
    {
        Id = id,
        CampaignNumber = campaignNumber,
        Manufacturer = "Fictional Motors USA, LLC",
        Make = "VOLKSWAGEN",
        MakeRaw = "VOLKSWAGEN",
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        ComponentName = "POWER TRAIN",
        ComponentGroup = "POWER TRAIN",
        RecallType = "V",
        PotentiallyAffected = 1200,
        ReportReceivedDate = reportReceivedDate,
        OwnersNotifiedDate = ownersNotifiedDate,
        DefectDescription = "Synthetic defect summary.",
        Consequence = "Synthetic consequence summary.",
        CorrectiveAction = "Synthetic corrective action.",
        DoNotDrive = true,
        ParkOutside = false,
        VehicleKey = vehicleKey
    };

    private sealed class CountingCatalogQueries(IVehicleCatalogQueries inner) : IVehicleCatalogQueries
    {
        public int OverviewCalls { get; private set; }

        public int MakesCalls { get; private set; }

        public int MakeCalls { get; private set; }

        public Task<CatalogOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            OverviewCalls++;

            return inner.GetOverviewAsync(cancellationToken);
        }

        public Task<IReadOnlyList<MakeSummary>> GetMakesAsync(CancellationToken cancellationToken = default)
        {
            MakesCalls++;

            return inner.GetMakesAsync(cancellationToken);
        }

        public Task<MakeDetail?> GetMakeAsync(string makeSlug, CancellationToken cancellationToken = default)
        {
            MakeCalls++;

            return inner.GetMakeAsync(makeSlug, cancellationToken);
        }

        public Task<IReadOnlyList<ModelYearSummary>> GetModelYearsAsync(
            string makeSlug,
            string modelSlug,
            CancellationToken cancellationToken = default) =>
            inner.GetModelYearsAsync(makeSlug, modelSlug, cancellationToken);

        public Task<VehicleIdentity?> ResolveVehicleAsync(
            string makeSlug,
            string modelSlug,
            int modelYear,
            CancellationToken cancellationToken = default) =>
            inner.ResolveVehicleAsync(makeSlug, modelSlug, modelYear, cancellationToken);

        public Task<IReadOnlyList<VehicleRanking>> GetMostReportedVehiclesAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            inner.GetMostReportedVehiclesAsync(limit, cancellationToken);

        public Task<IReadOnlyList<ComponentGroupSummary>> GetComponentGroupsAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetComponentGroupsAsync(cancellationToken);
    }

    private sealed class CountingRecallQueries(IRecallQueries inner) : IRecallQueries
    {
        public int AdvisoryCalls { get; private set; }

        public int CampaignCalls { get; private set; }

        public Task<IReadOnlyList<RecallCampaign>> GetForVehicleAsync(
            string vehicleKey,
            CancellationToken cancellationToken = default) =>
            inner.GetForVehicleAsync(vehicleKey, cancellationToken);

        public Task<RecallCampaignDetail?> GetByCampaignNumberAsync(
            string campaignNumber,
            CancellationToken cancellationToken = default)
        {
            CampaignCalls++;

            return inner.GetByCampaignNumberAsync(campaignNumber, cancellationToken);
        }

        public Task<IReadOnlyList<ConsumerAdvisory>> GetLatestAdvisoriesAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            AdvisoryCalls++;

            return inner.GetLatestAdvisoriesAsync(limit, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private int _warnings;

        public int Warnings => Volatile.Read(ref _warnings);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warnings);
            }
        }
    }
}
