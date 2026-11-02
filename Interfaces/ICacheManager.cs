namespace NSCache.Interfaces;

/// <summary>
/// Defines the contract for cache management operations.
/// </summary>
public interface ICacheManager
{
    /// <summary>
    /// Gets a cached value by key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value or null if not found.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple cached values by their keys in a single call.
    /// Returned dictionary will contain all requested keys; missing keys map to default/null.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="keys">Collection of cache keys to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A dictionary mapping each requested key to its cached value (or null/default if missing).
    /// </returns>
    Task<IDictionary<string, T?>> GetManyAsync<T>(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple cached values by transforming a collection of items to keys using <paramref name="keySelector"/>.
    /// Returned dictionary will contain all requested keys; missing keys map to default/null.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <typeparam name="TItem">The source item type used to derive keys.</typeparam>
    /// <param name="items">Collection of source items to derive cache keys from.</param>
    /// <param name="keySelector">Function that maps a source item to a cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A dictionary mapping each requested key (derived from <paramref name="items"/>) to its cached value (or null/default if missing).
    /// </returns>
    Task<IDictionary<string, T?>> GetManyAsync<T, TItem>(IReadOnlyCollection<TItem> items,
        Func<TItem, string> keySelector, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value in the cache with an optional expiration time.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="expiration">Optional expiration time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
  
    /// <summary>
    /// Sets multiple key/value pairs in a single call.
    /// Applies a single expiration for all items (for per-item expirations, see the overload below).
    /// </summary>
    /// <typeparam name="T">The type of the values to cache.</typeparam>
    /// <param name="items">Collection of key-value pairs to cache.</param>
    /// <param name="expiration">Optional expiration time applied to all items uniformly.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetManyAsync<T>(IReadOnlyCollection<KeyValuePair<string, T>> items, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets multiple items using custom key and value selectors.
    /// Useful for transforming domain objects into cache entries.
    /// Applies a single expiration for all items.
    /// </summary>
    /// <typeparam name="TItem">The type of source items.</typeparam>
    /// <typeparam name="TValue">The type of values to cache.</typeparam>
    /// <param name="items">Collection of source items to transform and cache.</param>
    /// <param name="keySelector">Function to extract cache key from each item.</param>
    /// <param name="valueSelector">Function to extract cache value from each item.</param>
    /// <param name="expiration">Optional expiration time applied to all items uniformly.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetManyAsync<TItem, TValue>(IReadOnlyCollection<TItem> items,
        Func<TItem, string> keySelector,
        Func<TItem, TValue> valueSelector,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets multiple items using custom key, value, and expiration selectors.
    /// Enables per-item expiration times, useful when different items have different cache lifetimes.
    /// Example: Hot data (5 min) vs cold data (30 min).
    /// </summary>
    /// <typeparam name="TItem">The type of source items.</typeparam>
    /// <typeparam name="TValue">The type of values to cache.</typeparam>
    /// <param name="items">Collection of source items to transform and cache.</param>
    /// <param name="keySelector">Function to extract cache key from each item.</param>
    /// <param name="valueSelector">Function to extract cache value from each item.</param>
    /// <param name="expirationSelector">Function to extract per-item expiration time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetManyAsync<TItem, TValue>(
            IReadOnlyCollection<TItem> items,
            Func<TItem, string> keySelector,
            Func<TItem, TValue> valueSelector,
            Func<TItem, TimeSpan?> expirationSelector,
            CancellationToken cancellationToken = default);

    /// <summary>
    ///  Stores a list of selected member values from a collection under a single cache key.
    /// Example: pass a collection of user objects and a selector u => u.Id to cache the list of ids under one key.
    /// </summary>
    /// <typeparam name="TItem">Source item type.</typeparam>
    /// <typeparam name="TValue">Selected member type to store (e.g., int for Id).</typeparam>
    /// <param name="items">Collection of source items.</param>
    /// <param name="key">Single cache key under which the list is stored.</param>
    /// <param name="valueSelector">Selector that extracts the member value from each item.</param>
    /// <param name="expiration">Optional expiration applied to the stored list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetManyAsListAsync<TItem, TValue>(IReadOnlyCollection<TItem> items, 
    string key, 
    Func<TItem, TValue> valueSelector, 
    TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached value by key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes multiple cached values by deriving keys from a collection of source items using <paramref name="keySelector"/>.
    /// </summary>
    /// <typeparam name="TItem">Source item type used to derive keys.</typeparam>
    /// <param name="items">Collection of source items.</param>
    /// <param name="keySelector">Function that maps a source item to a cache key to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task RemoveManyAsync<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, string> keySelector, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all cached values matching a pattern.
    /// </summary>
    /// <param name="pattern">The pattern to match keys (supports wildcards like *).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a key exists in the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached values.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets a cached value with a factory function.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">Factory function to create the value if not cached.</param>
    /// <param name="expiration">Optional expiration time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value.</returns>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}
