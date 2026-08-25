using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Parsing;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Ingest;
using GarageDoctor.Ingest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var arguments = IngestArguments.Parse(args);

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog(configuration => configuration
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

builder.Services.AddMongo(new MongoSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://mongo:27017",
    DatabaseName = builder.Configuration["MongoDatabase"] ?? "garagedoctor"
});

builder.Services.AddSingleton(arguments);
builder.Services.AddSingleton(provider => new NhtsaDataFiles(
    arguments.DataDirectory,
    provider.GetRequiredService<ILogger<NhtsaDataFiles>>()));

builder.Services.AddSingleton(MakeCanonicalizer.Load(Path.Combine(arguments.DataDirectory, "make-aliases.json")));
builder.Services.AddSingleton(ComponentCanonicalizer.Load(Path.Combine(arguments.DataDirectory, "component-groups.json")));
builder.Services.AddSingleton<ModelCanonicalizer>();
builder.Services.AddSingleton<ComplaintMapper>();
builder.Services.AddSingleton<RecallMapper>();
builder.Services.AddSingleton<ComplaintIngestService>();
builder.Services.AddSingleton<RecallIngestService>();
builder.Services.AddSingleton<VehicleCatalogBuilder>();
builder.Services.AddSingleton<ProfileBuilder>();
builder.Services.AddSingleton<IngestPipeline>();

using var host = builder.Build();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var pipeline = host.Services.GetRequiredService<IngestPipeline>();
var logger = host.Services.GetRequiredService<ILogger<IngestPipeline>>();

try
{
    await pipeline.RunAsync(arguments, cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Ingest cancelled");
    return 2;
}
catch (Exception exception)
{
    logger.LogCritical(exception, "Ingest failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
