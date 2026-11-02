using Microsoft.Extensions.Options;
using NSCache.Configuration;
using NSCache.Interfaces;

namespace NSCache.Implementations;

/// <summary>
/// Hybrid cache manager that combines local and Redis caching for optimal performance.
/// </summary>
public class HybridCacheManager : ICacheManager, IDisposable
{
    private readonly ICacheManager _localCache;
    private readonly ICacheManager? _redisCache;
    private readonly HybridCacheOptions _options;
    private readonly Timer? _writeBehindTimer;
    private readonly Queue<CacheOperation> _pendingOperations;
    private readonly object _operationsLock = new();
    private bool _disposed;

    /// <inheritdoc />
    public HybridCacheManager(
        ICacheManager localCache, 
        ICacheManager? redisCache, 
        IOptions<HybridCacheOptions> options)
    {
        _localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
        _redisCache = redisCache; // Can be null if Redis is unavailable
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        
        _pendingOperations = new Queue<CacheOperation>();

        // Set up write-behind timer if enabled
        if (_options.WriteBehind && _redisCache != null)
        {
            _writeBehindTimer = new Timer(ProcessPendingOperations, null, 
                _options.WriteBehindInterval, _options.WriteBehindInterval);
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        T? localResult = await _localCache.GetAsync<T>(key, cancellationToken);
        if (!EqualityComparer<T?>.Default.Equals(localResult, default))
        {
            return localResult;
        }

        if (_redisCache != null)
        {
            T? redisResult = await _redisCache.GetAsync<T>(key, cancellationToken);
            if (!EqualityComparer<T?>.Default.Equals(redisResult, default))
            {
                await _localCache.SetAsync(key, redisResult, _options.LocalCacheExpiration, cancellationToken);
                return redisResult;
            }
        }

        return default;
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) 
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        // Always set in local cache immediately
        var localExpiration = _options.LocalCacheExpiration;
        if (expiration.HasValue && expiration.Value < localExpiration)
        {
            localExpiration = expiration.Value;
        }
        await _localCache.SetAsync(key, value, localExpiration, cancellationToken);

        // Handle Redis storage based on configuration
        if (_redisCache != null)
        {
            if (_options.WriteThrough)
            {
                // Write-through: write to Redis immediately
                try
                {
                    await _redisCache.SetAsync(key, value, expiration, cancellationToken);
                }
                catch
                {
                    // Redis write failed, but local cache is still updated
                    // Log error but don't throw - graceful degradation
                }
            }
            else if (_options.WriteBehind)
            {
                // Write-behind: queue for later processing
                lock (_operationsLock)
                {
                    _pendingOperations.Enqueue(new CacheOperation
                    {
                        Type = CacheOperationType.Set,
                        Key = key,
                        Value = value,
                        Expiration = expiration,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<IDictionary<string, T?>> GetManyAsync<T, TItem>(IReadOnlyCollection<TItem> items,
        Func<TItem, string> keySelector, CancellationToken cancellationToken = default)
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
    public async Task RemoveManyAsync<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, string> keySelector, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);

        if (items.Count == 0)
            return;

        var keys = new List<string>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var k = keySelector(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(k);
            keys.Add(k);
            // remove from local immediately
            await _localCache.RemoveAsync(k, cancellationToken);
        }

        if (_redisCache == null)
            return;

        if (_options.WriteThrough)
        {
            var tasks = new List<Task>(keys.Count);
            foreach (var k in keys)
            {
                tasks.Add(_redisCache.RemoveAsync(k, cancellationToken));
            }
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // ignore Redis failures for graceful degradation
            }
        }
        else if (_options.WriteBehind)
        {
            lock (_operationsLock)
            {
                foreach (var k in keys)
                {
                    _pendingOperations.Enqueue(new CacheOperation
                    {
                        Type = CacheOperationType.Remove,
                        Key = k,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
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

        // Try local cache first
        var localResults = await _localCache.GetManyAsync<T>(keys, cancellationToken);
        
        // Check which keys are missing from local cache
        var missingKeys = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!localResults.ContainsKey(key) || EqualityComparer<T?>.Default.Equals(localResults[key], default))
            {
                missingKeys.Add(key);
            }
        }

        // If all keys found in local cache, return immediately
        if (missingKeys.Count == 0 || _redisCache == null)
        {
            return localResults;
        }

        // Fetch missing keys from Redis
        try
        {
            var redisResults = await _redisCache.GetManyAsync<T>(missingKeys, cancellationToken);
            
            // Populate local cache with Redis results and merge
            var itemsToCache = new List<KeyValuePair<string, T>>(redisResults.Count);
            foreach (var kvp in redisResults)
            {
                if (!EqualityComparer<T?>.Default.Equals(kvp.Value, default))
                {
                    localResults[kvp.Key] = kvp.Value;
                    itemsToCache.Add(new KeyValuePair<string, T>(kvp.Key, kvp.Value!));
                }
            }

            // Cache the Redis results locally with local expiration
            if (itemsToCache.Count > 0)
            {
                await _localCache.SetManyAsync(itemsToCache, _options.LocalCacheExpiration, cancellationToken);
            }
        }
        catch
        {
            // Redis failed, return local results only (graceful degradation)
        }

        return localResults;
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

        // Always set in local cache immediately
        var localExpiration = _options.LocalCacheExpiration;
        if (expiration.HasValue && expiration.Value < localExpiration)
        {
            localExpiration = expiration.Value;
        }

        var itemsForLocal = items
            .Select(x => new KeyValuePair<string, T>(x.Key, x.Value))
            .ToList();

        await _localCache.SetManyAsync(itemsForLocal, localExpiration, cancellationToken);

        // Handle Redis storage based on configuration
        if (_redisCache != null)
        {
            if (_options.WriteThrough)
            {
                // Write-through: write to Redis immediately
                try
                {
                    await _redisCache.SetManyAsync(items, expiration, cancellationToken);
                }
                catch
                {
                    // Redis write failed, but local cache is still updated (graceful degradation)
                }
            }
            else if (_options.WriteBehind)
            {
                // Write-behind: queue for later processing
                lock (_operationsLock)
                {
                    foreach (var item in items)
                    {
                        _pendingOperations.Enqueue(new CacheOperation
                        {
                            Type = CacheOperationType.Set,
                            Key = item.Key,
                            Value = item.Value,
                            Expiration = expiration,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }
        }
    }
    

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

        // Always set in local cache immediately
        var localExpiration = _options.LocalCacheExpiration;
        if (expiration.HasValue && expiration.Value < localExpiration)
        {
            localExpiration = expiration.Value;
        }

        await _localCache.SetManyAsync(items, keySelector, valueSelector, localExpiration, cancellationToken);

        // Handle Redis storage based on configuration
        if (_redisCache != null)
        {
            if (_options.WriteThrough)
            {
                // Write-through: write to Redis immediately
                try
                {
                    await _redisCache.SetManyAsync(items, keySelector, valueSelector, expiration, cancellationToken);
                }
                catch
                {
                    // Redis write failed, but local cache is still updated (graceful degradation)
                }
            }
            else if (_options.WriteBehind)
            {
                // Write-behind: queue for later processing
                lock (_operationsLock)
                {
                    foreach (var item in items)
                    {
                        var key = keySelector(item);
                        var value = valueSelector(item);

                        _pendingOperations.Enqueue(new CacheOperation
                        {
                            Type = CacheOperationType.Set,
                            Key = key,
                            Value = value,
                            Expiration = expiration,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }
        }
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

        // Set in local cache with local expiration or item expiration (whichever is shorter)
        await _localCache.SetManyAsync(items, keySelector, 
            valueSelector, item =>
            {
                var itemExpiration = expirationSelector(item);
                var localExpiration = _options.LocalCacheExpiration;
                if (itemExpiration.HasValue && itemExpiration.Value < localExpiration)
                {
                    localExpiration = itemExpiration.Value;
                }
                return localExpiration;
            }, cancellationToken);

        // Handle Redis storage based on configuration
        if (_redisCache != null)
        {
            if (_options.WriteThrough)
            {
                // Write-through: write to Redis immediately with per-item expiration
                try
                {
                    await _redisCache.SetManyAsync(items, keySelector, valueSelector, expirationSelector, cancellationToken);
                }
                catch
                {
                    // Redis write failed, but local cache is still updated (graceful degradation)
                }
            }
            else if (_options.WriteBehind)
            {
                // Write-behind: queue for later processing
                lock (_operationsLock)
                {
                    foreach (var item in items)
                    {
                        var key = keySelector(item);
                        var value = valueSelector(item);
                        var itemExpiration = expirationSelector(item);

                        _pendingOperations.Enqueue(new CacheOperation
                        {
                            Type = CacheOperationType.Set,
                            Key = key,
                            Value = value,
                            Expiration = itemExpiration,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }
        }
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

        // Local cache expiration: the shorter of provided expiration and LocalCacheExpiration
        var localExpiration = _options.LocalCacheExpiration;
        if (expiration.HasValue && expiration.Value < localExpiration)
        {
            localExpiration = expiration.Value;
        }

        await _localCache.SetAsync(key, list, localExpiration, cancellationToken);

        if (_redisCache != null)
        {
            if (_options.WriteThrough)
            {
                try
                {
                    await _redisCache.SetAsync(key, list, expiration, cancellationToken);
                }
                catch
                {
                    // ignore
                }
            }
            else if (_options.WriteBehind)
            {
                lock (_operationsLock)
                {
                    _pendingOperations.Enqueue(new CacheOperation
                    {
                        Type = CacheOperationType.Set,
                        Key = key,
                        Value = list,
                        Expiration = expiration,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Remove from local cache
        await _localCache.RemoveAsync(key, cancellationToken);

        // Remove from Redis if available
        if (_redisCache != null)
        {
            if (_options.WriteThrough)
            {
                try
                {
                    await _redisCache.RemoveAsync(key, cancellationToken);
                }
                catch
                {
                    // Redis remove failed, but local cache is still cleared
                }
            }
            else if (_options.WriteBehind)
            {
                lock (_operationsLock)
                {
                    _pendingOperations.Enqueue(new CacheOperation
                    {
                        Type = CacheOperationType.Remove,
                        Key = key,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Remove from local cache
        await _localCache.RemoveByPatternAsync(pattern, cancellationToken);

        // Remove from Redis if available
        if (_redisCache != null)
        {
            try
            {
                await _redisCache.RemoveByPatternAsync(pattern, cancellationToken);
            }
            catch
            {
                // Redis remove failed, but local cache is still cleared
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Check local cache first
        if (await _localCache.ExistsAsync(key, cancellationToken))
        {
            return true;
        }

        // Check Redis if available
        if (_redisCache != null)
        {
            try
            {
                return await _redisCache.ExistsAsync(key, cancellationToken);
            }
            catch
            {
                // Redis check failed, return false
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Clear local cache
        await _localCache.ClearAsync(cancellationToken);

        // Clear Redis if available
        if (_redisCache != null)
        {
            try
            {
                await _redisCache.ClearAsync(cancellationToken);
            }
            catch
            {
                // Redis clear failed, but local cache is still cleared
            }
        }

        // Clear pending operations
        lock (_operationsLock)
        {
            _pendingOperations.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Try to get from cache first
        var cached = await GetAsync<T>(key, cancellationToken);
        if (!EqualityComparer<T>.Default.Equals(cached, default!))
        {
            return cached!;
        }

        // Not in cache, create new value
        var value = await factory();
        if (!EqualityComparer<T>.Default.Equals(value, default!))
        {
            await SetAsync(key, value, expiration, cancellationToken);
        }

        return value;
    }

    /// <summary>
    /// Processes pending write-behind operations.
    /// </summary>
    /// <param name="state">Timer state (not used).</param>
    private async void ProcessPendingOperations(object? state)
    {
        if (_redisCache == null || _disposed)
            return;

        List<CacheOperation> operations;
        lock (_operationsLock)
        { 
            if (_pendingOperations.Count == 0)
                return;

            operations = new List<CacheOperation>(_pendingOperations);
            _pendingOperations.Clear();
        }

        foreach (var operation in operations)
        {
            try
            {
                switch (operation.Type)
                {
                    case CacheOperationType.Set:
                        if (operation.Value != null)
                        {
                            await _redisCache.SetAsync(operation.Key, operation.Value, operation.Expiration);
                        }
                        break;

                    case CacheOperationType.Remove:
                        await _redisCache.RemoveAsync(operation.Key);
                        break;
                }
            }
            catch
            {
                // Log error but continue processing other operations
                // In a real implementation, you might want to retry failed operations
            }
        }
    }

    /// <summary>
    /// Throws an exception if the object has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HybridCacheManager));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _writeBehindTimer?.Dispose();
            
            if (_localCache is IDisposable localDisposable)
                localDisposable.Dispose();
            
            if (_redisCache is IDisposable redisDisposable)
                redisDisposable.Dispose();

            _disposed = true;
        }
    }

    /// <summary>
    /// Represents a pending cache operation for write-behind processing.
    /// </summary>
    private class CacheOperation
    {
        public CacheOperationType Type { get; set; }
        public string Key { get; set; } = string.Empty;
        public object? Value { get; set; }
        public TimeSpan? Expiration { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Types of cache operations.
    /// </summary>
    private enum CacheOperationType
    {
        Set,
        Remove
    }
}
