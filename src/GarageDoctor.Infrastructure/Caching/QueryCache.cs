using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GarageDoctor.Infrastructure.Caching;

public sealed record QueryCacheOptions
{
    public const string SectionName = "QueryCache";

    public bool Enabled { get; init; } = true;

    public string KeyPrefix { get; init; } = "garagedoctor";

    public string SchemaVersion { get; init; } = "v1";

    public TimeSpan CatalogTtl { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan ComponentTtl { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan RecallTtl { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan AdvisoryTtl { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan PageTtl { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan FailureCooldown { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class QueryCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly QueryCacheOptions _options;
    private readonly ILogger<QueryCache> _logger;

    private long _suppressedUntil;
    private int _outageReported;

    public QueryCache(IDistributedCache cache, QueryCacheOptions options, ILogger<QueryCache> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public string BuildKey(string name, params ReadOnlySpan<object?> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var key = new StringBuilder(_options.KeyPrefix)
            .Append(':')
            .Append(_options.SchemaVersion)
            .Append(':')
            .Append(name);

        foreach (var argument in arguments)
        {
            key.Append(':').Append(Format(argument));
        }

        return key.ToString();
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_options.Enabled)
        {
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        var payload = await ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (payload is not null && TryDeserialize<T>(payload, key, out var cached))
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);

        await WriteAsync(key, value, timeToLive, cancellationToken).ConfigureAwait(false);

        return value;
    }

    private async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        if (Suppressed())
        {
            return null;
        }

        try
        {
            var payload = await _cache
                .GetAsync(key, cancellationToken)
                .WaitAsync(_options.OperationTimeout, cancellationToken)
                .ConfigureAwait(false);

            RecordReachable();

            return payload;
        }
        catch (Exception exception) when (IsCacheFailure(exception, cancellationToken))
        {
            RecordUnreachable(exception, "read", key);

            return null;
        }
    }

    private async Task WriteAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        if (Suppressed())
        {
            return;
        }

        byte[] payload;

        try
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            _logger.LogWarning(
                exception,
                "Cannot serialize the result for cache key {CacheKey}; it will be read from MongoDB every time",
                key);

            return;
        }

        try
        {
            var entry = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive };

            await _cache
                .SetAsync(key, payload, entry, cancellationToken)
                .WaitAsync(_options.OperationTimeout, cancellationToken)
                .ConfigureAwait(false);

            RecordReachable();
        }
        catch (Exception exception) when (IsCacheFailure(exception, cancellationToken))
        {
            RecordUnreachable(exception, "write", key);
        }
    }

    private bool TryDeserialize<T>(byte[] payload, string key, [MaybeNullWhen(false)] out T value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(payload, SerializerOptions)!;

            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            _logger.LogWarning(
                exception,
                "Discarding the cached entry for key {CacheKey} because it no longer matches the expected shape",
                key);

            value = default;

            return false;
        }
    }

    private static bool IsCacheFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private bool Suppressed() => Environment.TickCount64 < Interlocked.Read(ref _suppressedUntil);

    private void RecordUnreachable(Exception exception, string operation, string key)
    {
        Interlocked.Exchange(
            ref _suppressedUntil,
            Environment.TickCount64 + (long)_options.FailureCooldown.TotalMilliseconds);

        if (Interlocked.Exchange(ref _outageReported, 1) == 0)
        {
            _logger.LogWarning(
                exception,
                "The Redis query cache is unreachable during {CacheOperation} of key {CacheKey}; serving from MongoDB and retrying in {CooldownSeconds}s",
                operation,
                key,
                _options.FailureCooldown.TotalSeconds);
        }
    }

    private void RecordReachable()
    {
        if (Volatile.Read(ref _outageReported) == 1 && Interlocked.Exchange(ref _outageReported, 0) == 1)
        {
            _logger.LogInformation("The Redis query cache is reachable again");
        }
    }

    private static string Format(object? argument) => argument switch
    {
        null => "none",
        string text => text.ToLowerInvariant(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => argument.ToString() ?? "none"
    };
}
