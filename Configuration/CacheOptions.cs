using NSCache.Interfaces;

namespace NSCache.Configuration;

/// <summary>
/// Configuration options for local (in-memory) caching.
/// </summary>
public class LocalCacheOptions : ICacheOption
{
    /// <inheritdoc />
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum size limit for the local cache.
    /// </summary>
    public long? SizeLimit { get; set; } = 1024 * 1024 * 100; // 100MB default

    /// <summary>
    /// Compaction percentage when size limit is reached.
    /// </summary>
    public double CompactionPercentage { get; set; } = 0.25;

    /// <summary>
    /// Scan frequency for expired items cleanup.
    /// </summary>
    public TimeSpan ExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public bool IsValid()
    {
        return DefaultExpiration > TimeSpan.Zero &&
               (!SizeLimit.HasValue || SizeLimit.Value > 0) &&
               CompactionPercentage is > 0 and <= 1 &&
               ExpirationScanFrequency > TimeSpan.Zero;
    }

    /// <inheritdoc />
    public string GetDescription()
    {
        return $"Local Cache - Size: {(SizeLimit.HasValue ? $"{SizeLimit.Value / (1024 * 1024)}MB" : "Unlimited")}, " +
               $"Expiration: {DefaultExpiration.TotalMinutes}min, " +
               $"Compaction: {CompactionPercentage:P0}";
    }
}

/// <summary>
/// Configuration options for Redis caching.
/// </summary>
public class RedisCacheOptions : ICacheOption
{
    /// <inheritdoc />
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Redis database number to use.
    /// </summary>
    public int Database { get; set; } = 0;

    /// <summary>
    /// Key prefix for all cache entries.
    /// </summary>
    public string KeyPrefix { get; set; } = "NSCache:";

    /// <summary>
    /// Connection timeout in milliseconds.
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// Sync timeout in milliseconds.
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>
    /// Whether to allow admin operations.
    /// </summary>
    public bool AllowAdmin { get; set; } = false;

    /// <summary>
    /// Whether to abort connection on connect failure.
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// Number of connection retry attempts.
    /// </summary>
    public int ConnectRetry { get; set; } = 3;

    /// <inheritdoc />
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ConnectionString) &&
               DefaultExpiration > TimeSpan.Zero &&
               Database >= 0 &&
               ConnectTimeout > 0 &&
               SyncTimeout > 0 &&
               ConnectRetry >= 0;
    }

    /// <inheritdoc />
    public string GetDescription()
    {
        return $"Redis Cache - Server: {ConnectionString}, " +
               $"DB: {Database}, " +
               $"Prefix: {KeyPrefix}, " +
               $"Expiration: {DefaultExpiration.TotalMinutes}min";
    }
}

/// <summary>
/// Configuration options for hybrid caching (local + Redis).
/// </summary>
public class HybridCacheOptions : ICacheOption
{
    /// <inheritdoc />
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Local cache configuration for the hybrid setup.
    /// </summary>
    public LocalCacheOptions Local { get; set; } = new();

    /// <summary>
    /// Redis cache configuration for the hybrid setup.
    /// </summary>
    public RedisCacheOptions Redis { get; set; } = new();

    /// <summary>
    /// Expiration time for items in the local cache layer.
    /// Items will be kept in local cache for this duration before checking Redis.
    /// </summary>
    public TimeSpan LocalCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to sync local cache with Redis on startup.
    /// </summary>
    public bool SyncOnStartup { get; set; } = false;

    /// <summary>
    /// Whether to use write-through caching (write to both local and Redis simultaneously).
    /// </summary>
    public bool WriteThrough { get; set; } = true;

    /// <summary>
    /// Whether to use write-behind caching (write to local immediately, Redis asynchronously).
    /// </summary>
    public bool WriteBehind { get; set; } = false;

    /// <summary>
    /// Interval for write-behind synchronization with Redis.
    /// </summary>
    public TimeSpan WriteBehindInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public bool IsValid()
    {
        return DefaultExpiration > TimeSpan.Zero &&
               LocalCacheExpiration > TimeSpan.Zero &&
               Local.IsValid() &&
               Redis.IsValid() &&
               (!WriteBehind || WriteBehindInterval > TimeSpan.Zero) &&
               !(WriteThrough && WriteBehind); // Cannot be both write-through and write-behind
    }

    /// <inheritdoc />
    public string GetDescription()
    {
        var strategy = WriteThrough ? "Write-Through" : WriteBehind ? "Write-Behind" : "Read-Through";
        return $"Hybrid Cache - Strategy: {strategy}, " +
               $"Local: {LocalCacheExpiration.TotalMinutes}min, " +
               $"Global: {DefaultExpiration.TotalMinutes}min, " +
               $"Redis: {Redis.ConnectionString}";
    }
}
