using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSCache.Configuration;
using NSCache.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NSCache.Implementations;

/// <summary>
/// Local in-memory cache manager implementation.
/// </summary>
public class LocalCacheManager : ICacheManager, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly LocalCacheOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <inheritdoc />
    public LocalCacheManager(IMemoryCache memoryCache, IOptions<LocalCacheOptions> options)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) 
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = _memoryCache.Get<T>(key);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) 
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var options = CreateEntryOptions(value, expiration);
        _memoryCache.Set(key, value, options);
        
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IDictionary<string, T?>> GetManyAsync<T>(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return Task.FromResult<IDictionary<string, T?>>(new Dictionary<string, T?>(StringComparer.Ordinal));
        }

        var results = new Dictionary<string, T?>(keys.Count, StringComparer.Ordinal);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            results[key] = _memoryCache.TryGetValue(key, out T? value) ? value : default;
        }

        return Task.FromResult<IDictionary<string, T?>>(results);
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
    public Task SetManyAsync<T>(IReadOnlyCollection<KeyValuePair<string, T>> items, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var (key, value) in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            var options = CreateEntryOptions(value, expiration);
            _memoryCache.Set(key, value, options);
        }

        return Task.CompletedTask;
    }

    // tuple-based per-item expiration overload removed (use selector-based overload instead)

    /// <inheritdoc />
    public Task SetManyAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, 
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
            return Task.CompletedTask;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var key = keySelector(item);
            var value = valueSelector(item);
            
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            var options = CreateEntryOptions(value, expiration);
            _memoryCache.Set(key, value, options);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetManyAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, 
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
            return Task.CompletedTask;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var key = keySelector(item);
            var value = valueSelector(item);
            var itemExpiration = expirationSelector(item);
            
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            var options = CreateEntryOptions(value, itemExpiration);
            _memoryCache.Set(key, value, options);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetManyAsListAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, string key, Func<TItem, TValue> valueSelector, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(valueSelector);

        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        var list = items.Select(valueSelector).ToList();
        var options = CreateEntryOptions(list, expiration);
        _memoryCache.Set(key, list, options);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveManyAsync<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, string> keySelector, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);

        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = keySelector(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _memoryCache.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _memoryCache.Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Get all keys from memory cache using reflection (not ideal but necessary for pattern matching)
        if (_memoryCache is MemoryCache mc)
        {
            var field = typeof(MemoryCache).GetField("_coherentState", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field?.GetValue(mc) is object coherentState)
            {
                var entriesField = coherentState.GetType().GetProperty("EntriesCollection",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (entriesField?.GetValue(coherentState) is IDictionary<object, object> entries)
                {
                    var keysToRemove = entries.Keys
                        .OfType<string>()
                        .Where(key => regex.IsMatch(key))
                        .ToList();

                    foreach (var key in keysToRemove)
                    {
                        _memoryCache.Remove(key);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var exists = _memoryCache.TryGetValue(key, out _);
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Clear all entries by disposing and recreating the cache
        // This is not ideal but MemoryCache doesn't have a built-in clear method
        if (_memoryCache is IDisposable disposableCache)
        {
            // Note: This would require reconstruction of the cache which is not possible here
            // In practice, this method might need to be implemented differently
            // or not supported for local cache
        }

        return Task.CompletedTask;
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
    /// Estimates the size of an object for memory cache sizing.
    /// </summary>
    /// <param name="value">The object to estimate size for.</param>
    /// <returns>Estimated size in bytes.</returns>
    private long EstimateSize<T>(T value) 
    {
        try
        {
            // Simple estimation based on JSON serialization
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            return System.Text.Encoding.UTF8.GetByteCount(json);
        }
        catch
        {
            // Fallback to a reasonable default size
            return 1024; // 1KB default
        }
    }

    private MemoryCacheEntryOptions CreateEntryOptions<T>(T value, TimeSpan? expiration)
    {
        var options = new MemoryCacheEntryOptions();

        if (expiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiration.Value;
        }

        if (_options.SizeLimit.HasValue)
        {
            options.Size = EstimateSize(value);
        }

        return options;
    }

    /// <summary>
    /// Throws an exception if the object has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalCacheManager));
        }
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
