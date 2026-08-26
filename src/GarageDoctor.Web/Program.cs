using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Caching;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddMongo(new MongoSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://mongo:27017",
    DatabaseName = builder.Configuration["MongoDatabase"] ?? "garagedoctor"
});

var cacheOptions = builder.Configuration
    .GetSection(QueryCacheOptions.SectionName)
    .Get<QueryCacheOptions>() ?? new QueryCacheOptions();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options =>
        options.ConfigurationOptions = RedisConfiguration(redisConnectionString));
}

builder.Services.AddQueryCaching(cacheOptions);

builder.Services.AddOutputCache(options =>
{
    options.SizeLimit = 128L * 1024 * 1024;

    options.AddPolicy(CachedPages.VehicleProfile, policy => VehicleProfilePolicy(policy, cacheOptions.PageTtl));
    options.AddPolicy(CachedPages.MakesBrowser, policy => MakesBrowserPolicy(policy, cacheOptions.PageTtl));
    options.AddPolicy(CachedPages.ComponentPages, policy => ComponentPagesPolicy(policy, cacheOptions.PageTtl));
    options.AddPolicy(CachedPages.About, policy => AboutPolicy(policy, cacheOptions.PageTtl));

    options.AddBasePolicy(policy => VehicleProfilePolicy(policy, cacheOptions.PageTtl)
        .With(context => IsVehicleProfile(context.HttpContext.Request.Path)));

    options.AddBasePolicy(policy => MakesBrowserPolicy(policy, cacheOptions.PageTtl)
        .With(context => IsMakesBrowser(context.HttpContext.Request.Path)));

    options.AddBasePolicy(policy => ComponentPagesPolicy(policy, cacheOptions.PageTtl)
        .With(context => IsComponentPage(context.HttpContext.Request.Path)));

    options.AddBasePolicy(policy => AboutPolicy(policy, cacheOptions.PageTtl)
        .With(context => IsAbout(context.HttpContext.Request.Path)));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseRouting();

app.UseOutputCache();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Logger.LogInformation(
    "Query cache is {CacheState} under {KeyPrefix}:{SchemaVersion} with catalogue {CatalogTtl}, advisories {AdvisoryTtl} and rendered pages {PageTtl}",
    cacheOptions.Enabled ? "enabled" : "disabled",
    cacheOptions.KeyPrefix,
    cacheOptions.SchemaVersion,
    cacheOptions.CatalogTtl,
    cacheOptions.AdvisoryTtl,
    cacheOptions.PageTtl);

app.Run();

static ConfigurationOptions RedisConfiguration(string connectionString)
{
    var configuration = ConfigurationOptions.Parse(connectionString);

    configuration.AbortOnConnectFail = false;
    configuration.ConnectRetry = 1;
    configuration.ConnectTimeout = 2000;
    configuration.SyncTimeout = 2000;

    return configuration;
}

static OutputCachePolicyBuilder VehicleProfilePolicy(OutputCachePolicyBuilder policy, TimeSpan timeToLive) => policy
    .SetVaryByRouteValue("makeSlug", "modelSlug", "modelYear")
    .AddPolicy<IgnoreQueryStringPolicy>()
    .Expire(timeToLive);

static OutputCachePolicyBuilder MakesBrowserPolicy(OutputCachePolicyBuilder policy, TimeSpan timeToLive) => policy
    .SetVaryByRouteValue("makeSlug")
    .AddPolicy<IgnoreQueryStringPolicy>()
    .Expire(timeToLive);

static OutputCachePolicyBuilder ComponentPagesPolicy(OutputCachePolicyBuilder policy, TimeSpan timeToLive) => policy
    .Expire(timeToLive);

static OutputCachePolicyBuilder AboutPolicy(OutputCachePolicyBuilder policy, TimeSpan timeToLive) => policy
    .AddPolicy<IgnoreQueryStringPolicy>()
    .Expire(timeToLive);

static bool IsVehicleProfile(PathString path) =>
    Segments(path) is ["vehicles", _, _, var modelYear] && int.TryParse(modelYear, out _);

static bool IsMakesBrowser(PathString path) => Segments(path) is ["makes"] or ["makes", _];

static bool IsComponentPage(PathString path) => Segments(path) is ["components"] or ["components", _];

static bool IsAbout(PathString path) => Segments(path) is ["about"];

static string[] Segments(PathString path) =>
    path.Value?.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];

public static class CachedPages
{
    public const string VehicleProfile = "VehicleProfile";

    public const string MakesBrowser = "MakesBrowser";

    public const string ComponentPages = "ComponentPages";

    public const string About = "About";
}

internal sealed class IgnoreQueryStringPolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.CacheVaryByRules.QueryKeys = new StringValues(string.Empty);

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
