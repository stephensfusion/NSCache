using Microsoft.Extensions.Options;
using NSCache.Configuration;
using NSCache.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace NSCache.Implementations;

/// <summary>
/// Redis distributed cache manager implementation.
/// </summary>
public class RedisCacheManager : ICacheManager, IDisposable
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _database;
    private readonly RedisCacheOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <inheritdoc />
    public RedisCacheManager(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisCacheOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _database = _connectionMultiplexer.GetDatabase(_options.Database);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var prefixedKey = GetPrefixedKey(key);
        var value = await _database.StringGetAsync(prefixedKey);

        if (!value.HasValue)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>((string)value!, _jsonOptions);
        }
        catch (JsonException)
        {
            await _database.KeyDeleteAsync(prefixedKey);
            return default;
        }
    }

    

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) 
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var prefixedKey = GetPrefixedKey(key);
        var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);

        await _database.StringSetAsync(prefixedKey, serializedValue, expiration, When.Always);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, T?>> GetManyAsync<T>(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return new Dictionary<string, T?>(StringComparer.Ordinal);
        }

        // Build Redis keys with prefix
        var redisKeys = keys
            .Select(k => (RedisKey)GetPrefixedKey(k))
            .ToArray();

        // Use pipelined GET for optimal performance
        RedisValue[] values = await _database.StringGetAsync(redisKeys);

        var results = new Dictionary<string, T?>(keys.Count, StringComparer.Ordinal);
        var keyList = keys.ToList();

        for (int i = 0; i < redisKeys.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var originalKey = keyList[i];
            var value = values[i];

            if (value.HasValue)
            {
                try
                {
                    results[originalKey] = JsonSerializer.Deserialize<T>((string)value!, _jsonOptions);
                }
                catch (JsonException)
                {
                    // Remove corrupted entry and set to default
                    await _database.KeyDeleteAsync(redisKeys[i]);
                    results[originalKey] = default;
                }
            }
            else
            {
                results[originalKey] = default;
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task RemoveManyAsync<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, string> keySelector, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);

        if (items.Count == 0)
        {
            return;
        }

        var redisKeys = new List<RedisKey>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var k = keySelector(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(k);
            redisKeys.Add(GetPrefixedKey(k));
        }

        if (redisKeys.Count == 0)
            return;

        await _database.KeyDeleteAsync(redisKeys.ToArray());
    }

    /// <inheritdoc />
    public Task<IDictionary<string, T?>> GetManyAsync<T, TItem>(IReadOnlyCollection<TItem> items, Func<TItem, string> keySelector, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);

        if (items.Count == 0)
        {
            return Task.FromResult<IDictionary<string, T?>>(new Dictionary<string, T?>(StringComparer.Ordinal));
        }

        var keys = new List<string>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var k = keySelector(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(k);
            keys.Add(k);
        }

        return GetManyAsync<T>(keys, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetManyAsync<T>(IReadOnlyCollection<KeyValuePair<string, T>> items, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return;
        }

        // Build batch operations for optimal performance
        var batch = _database.CreateBatch();
        var tasks = new List<Task>(items.Count);

        foreach (var (key, value) in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            var prefixedKey = GetPrefixedKey(key);
            var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);

            tasks.Add(batch.StringSetAsync(prefixedKey, serializedValue, expiration, When.Always));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    // tuple-based per-item expiration overload removed (use selector-based overload instead)

    /// <inheritdoc />
    public async Task SetManyAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, 
        Func<TItem, string> keySelector, 
        Func<TItem, TValue> valueSelector, 
        TimeSpan? expiration = null, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(valueSelector);

        if (items.Count == 0)
        {
            return;
        }

        var batch = _database.CreateBatch();
        var tasks = new List<Task>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var key = keySelector(item);
            var value = valueSelector(item);
            
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            var prefixedKey = GetPrefixedKey(key);
            var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);

            tasks.Add(batch.StringSetAsync(prefixedKey, serializedValue, expiration, When.Always));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async Task SetManyAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, 
        Func<TItem, string> keySelector, 
        Func<TItem, TValue> valueSelector, 
        Func<TItem, TimeSpan?> expirationSelector, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(valueSelector);
        ArgumentNullException.ThrowIfNull(expirationSelector);

        if (items.Count == 0)
        {
            return;
        }

        var tasks = new List<Task>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var key = keySelector(item);
            var value = valueSelector(item);
            var itemExpiration = expirationSelector(item);
            
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            var prefixedKey = GetPrefixedKey(key);
            var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);

            tasks.Add(_database.StringSetAsync(prefixedKey, serializedValue, itemExpiration, When.Always));
        }

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async Task SetManyAsListAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, string key, Func<TItem, TValue> valueSelector, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(valueSelector);

        if (items.Count == 0)
        {
            return;
        }

        var list = items.Select(valueSelector).ToList();
        var prefixedKey = GetPrefixedKey(key);
        var serialized = JsonSerializer.Serialize(list, _jsonOptions);

        await _database.StringSetAsync(prefixedKey, serialized, expiration, When.Always);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var prefixedKey = GetPrefixedKey(key);
        await _database.KeyDeleteAsync(prefixedKey);
    }

    /// <inheritdoc />
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var prefixedPattern = GetPrefixedKey(pattern);
        var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints().First());
        
        var keys = server.Keys(_options.Database, prefixedPattern);
        var keyArray = keys.ToArray();

        if (keyArray.Length > 0)
        {
            await _database.KeyDeleteAsync(keyArray);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var prefixedKey = GetPrefixedKey(key);
        return await _database.KeyExistsAsync(prefixedKey);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints().First());
        
        // Clear all keys with our prefix
        var pattern = GetPrefixedKey("*");
        var keys = server.Keys(_options.Database, pattern);
        var keyArray = keys.ToArray();

        if (keyArray.Length > 0)
        {
            await _database.KeyDeleteAsync(keyArray);
        }
    }

    /// <inheritdoc />
    public async Task<T> GetOrSetAsync<T>(
    string key,
    Func<Task<T>> factory,
    TimeSpan? expiration = null,
    CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await GetAsync<T>(key, cancellationToken);
        if (!EqualityComparer<T>.Default.Equals(cached, default!))
        {
            return cached!;
        }

        var lockKey = GetPrefixedKey($"lock:{key}");
        var lockValue = Environment.MachineName + "-" + Thread.CurrentThread.ManagedThreadId;
        var lockExpiry = TimeSpan.FromMinutes(1);

        if (await _database.StringSetAsync(lockKey, lockValue, lockExpiry, When.NotExists))
        {
            try
            {
                cached = await GetAsync<T>(key, cancellationToken);
                if (!EqualityComparer<T>.Default.Equals(cached, default!))
                {
                    return cached!;
                }

                var value = await factory();
                if (!EqualityComparer<T>.Default.Equals(value, default!))
                {
                    await SetAsync(key, value, expiration, cancellationToken);
                }

                return value;
            }
            finally
            {
                const string script = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                else
                    return 0
                end";
                await _database.ScriptEvaluateAsync(script, new RedisKey[] { lockKey }, new RedisValue[] { lockValue });
            }
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(100 * (i + 1), cancellationToken);
                cached = await GetAsync<T>(key, cancellationToken);
                if (!EqualityComparer<T>.Default.Equals(cached, default!))
                {
                    return cached!;
                }
            }

            var fallback = await factory();
            return fallback;
        }
    }


    /// <summary>
    /// Gets the Redis key with the configured prefix.
    /// </summary>
    /// <param name="key">The original key.</param>
    /// <returns>The prefixed key.</returns>
    private string GetPrefixedKey(string key)
    {
        return $"{_options.KeyPrefix}{key}";
    }

    /// <summary>
    /// Throws an exception if the object has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (!_disposed)
        {
            return;
        }
        throw new ObjectDisposedException(nameof(RedisCacheManager));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
