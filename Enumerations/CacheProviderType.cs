namespace NSCache.Enumerations;

/// <summary>
/// Specifies the type of cache provider to use.
/// </summary>
public enum CacheProviderType
{
    /// <summary>
    /// In-memory cache for single instance applications.
    /// </summary>
    Local,

    /// <summary>
    /// Distributed Redis cache for scalable applications.
    /// </summary>
    Redis,

    /// <summary>
    /// Hybrid cache that tries local first, then Redis.
    /// </summary>
    Hybrid
}
