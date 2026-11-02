using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSCache.Configuration;
using NSCache.Implementations;
using NSCache.Interfaces;
using StackExchange.Redis;

namespace NSCache.Extensions;

/// <summary>
/// Extension methods for configuring NSCache cache services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds NSCache cache services using a specific ICacheOption implementation.
    /// The cache type is automatically determined by the ICacheOption type.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheOption">The cache option instance implementing ICacheOption.</param>
    /// <param name="cacheKey">The unique cache key for this cache instance (used for service isolation).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNSCache(this IServiceCollection services, ICacheOption? cacheOption = null, string cacheKey = "default")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        // Use default local cache options if none provided
        cacheOption ??= ReturnDefaultLocalCacheOptions();

        // Validate the cache option
        if (!cacheOption.IsValid())
        {
            throw new InvalidOperationException($"Invalid cache configuration: {cacheOption.GetDescription()}");
        }

        // Register the cache option directly with cache key
        services.AddKeyedSingleton(cacheKey, cacheOption);

        // Register core cache services based on the option type
        RegisterCoreServices(services, cacheOption, cacheKey);

        return services;
    }

    /// <summary>
    /// Registers core cache services based on the specific ICacheOption type.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheOption">The cache option to configure services for.</param>
    /// <param name="cacheKey">The unique cache key for this cache instance.</param>
    private static void RegisterCoreServices(IServiceCollection services, ICacheOption cacheOption, string cacheKey)
    {
        // Register services based on the cache option type
        switch (cacheOption)
        {
            case LocalCacheOptions:
                RegisterLocalCacheServices(services);
                break;

            case RedisCacheOptions redisOpts:
                RegisterRedisCacheServices(services, redisOpts);
                break;

            case HybridCacheOptions hybridOpts:
                RegisterHybridCacheServices(services, hybridOpts);
                break;

            default:
                throw new ArgumentException($"Unsupported cache option type: {cacheOption.GetType().Name}");
        }

        // Register the cache manager directly based on option type with cache key
        services.AddKeyedSingleton<ICacheManager>(cacheKey, (provider, key) => CreateCacheManager(provider, cacheOption));

        // Register cache key builder with service-specific prefix
        services.AddKeyedSingleton<ICacheKeyBuilder>(cacheKey, (provider, key) =>
            new ServiceSpecificCacheKeyBuilder(cacheKey));
    }

    /// <summary>
    /// Creates the appropriate cache manager based on the cache option type.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="cacheOption">The cache option.</param>
    /// <returns>The configured cache manager.</returns>
    private static ICacheManager CreateCacheManager(IServiceProvider provider, ICacheOption cacheOption)
    {
        return cacheOption switch
        {
            LocalCacheOptions localOpts => CreateLocalCacheManager(provider, localOpts),
            RedisCacheOptions redisOpts => CreateRedisCacheManager(provider, redisOpts),
            HybridCacheOptions hybridOpts => CreateHybridCacheManager(provider, hybridOpts),
            _ => throw new InvalidOperationException($"Unsupported cache option type: {cacheOption.GetType().Name}")
        };
    }

    /// <summary>
    /// Creates a local cache manager.
    /// </summary>
    private static ICacheManager CreateLocalCacheManager(IServiceProvider provider, LocalCacheOptions options)
    {
        // Create MemoryCache with the specific options from LocalCacheOptions
        var memoryCacheOptions = new MemoryCacheOptions();

        // Apply size limit if specified
        if (options.SizeLimit.HasValue)
        {
            memoryCacheOptions.SizeLimit = options.SizeLimit.Value;
        }

        // Apply compaction percentage
        memoryCacheOptions.CompactionPercentage = options.CompactionPercentage;

        // Apply expiration scan frequency
        memoryCacheOptions.ExpirationScanFrequency = options.ExpirationScanFrequency;

        // Create a new MemoryCache instance with the configured options
        var memoryCache = new MemoryCache(memoryCacheOptions);

        var wrappedOptions = Microsoft.Extensions.Options.Options.Create(options);
        return new LocalCacheManager(memoryCache, wrappedOptions);
    }

    /// <summary>
    /// Creates a Redis cache manager.
    /// </summary>
    private static ICacheManager CreateRedisCacheManager(IServiceProvider provider, RedisCacheOptions options)
    {
        var connectionMultiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        var wrappedOptions = Microsoft.Extensions.Options.Options.Create(options);
        return new RedisCacheManager(connectionMultiplexer, wrappedOptions);
    }

    /// <summary>
    /// Creates a hybrid cache manager.
    /// </summary>
    private static ICacheManager CreateHybridCacheManager(IServiceProvider provider, HybridCacheOptions options)
    {
        var localManager = CreateLocalCacheManager(provider, options.Local);

        ICacheManager? redisManager = null;
        try
        {
            redisManager = CreateRedisCacheManager(provider, options.Redis);
        }
        catch
        {
            // Redis is optional in hybrid mode
        }

        var wrappedOptions = Microsoft.Extensions.Options.Options.Create(options);
        return new HybridCacheManager(localManager, redisManager, wrappedOptions);
    }

    /// <summary>
    /// Registers services specific to local caching.
    /// </summary>
    /// <param name="services">The service collection.</param>
    private static void RegisterLocalCacheServices(IServiceCollection services)
    {
        // Local cache only needs memory cache, which is already registered
        // No additional services required
    }

    /// <summary>
    /// Registers services specific to Redis caching.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="redisOptions">The Redis cache options.</param>
    private static void RegisterRedisCacheServices(IServiceCollection services, RedisCacheOptions redisOptions)
    {
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            try
            {
                var configuration = ConfigurationOptions.Parse(redisOptions.ConnectionString);
                configuration.ConnectTimeout = redisOptions.ConnectTimeout;
                configuration.SyncTimeout = redisOptions.SyncTimeout;
                configuration.AllowAdmin = redisOptions.AllowAdmin;
                configuration.AbortOnConnectFail = redisOptions.AbortOnConnectFail;
                configuration.ConnectRetry = redisOptions.ConnectRetry;

                return ConnectionMultiplexer.Connect(configuration);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to connect to Redis with connection string: {redisOptions.ConnectionString}", ex);
            }
        });
    }

    /// <summary>
    /// Registers services specific to hybrid caching.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="hybridOptions">The hybrid cache options.</param>
    private static void RegisterHybridCacheServices(IServiceCollection services, HybridCacheOptions hybridOptions)
    {
        // Register Redis connection for hybrid cache
        RegisterRedisCacheServices(services, hybridOptions.Redis);
    }

    private static LocalCacheOptions ReturnDefaultLocalCacheOptions() {
        return new LocalCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(5), SizeLimit= 1024 * 1024 * 50 };
    }
}