namespace NSCache.Interfaces;

/// <summary>
/// Interface for building type-safe cache keys.
/// </summary>
public interface ICacheKeyBuilder
{
    /// <summary>
    /// Builds a cache key for a specific entity type and identifier.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="id">The entity identifier.</param>
    /// <returns>The cache key.</returns>
    string BuildKey<T>(object id);

    /// <summary>
    /// Builds a cache key with a custom prefix and parameters.
    /// </summary>
    /// <param name="prefix">The key prefix.</param>
    /// <param name="parameters">The key parameters.</param>
    /// <returns>The cache key.</returns>
    string BuildKey(string prefix, params object[] parameters);

    /// <summary>
    /// Builds a pattern for removing multiple keys.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>The cache key pattern.</returns>
    string BuildPattern<T>();

    /// <summary>
    /// Builds a pattern with a custom prefix.
    /// </summary>
    /// <param name="prefix">The pattern prefix.</param>
    /// <returns>The cache key pattern.</returns>
    string BuildPattern(string prefix);
}
