using NSCache.Interfaces;

namespace NSCache.Implementations;

/// <summary>
/// Implementation of cache key builder for type-safe cache key generation.
/// </summary>
public class CacheKeyBuilder : ICacheKeyBuilder
{
    private const string KeySeparator = ":";
    private const string WildcardPattern = "*";

    /// <inheritdoc />
    public string BuildKey<T>(object id)
    {
        var typeName = typeof(T).Name.ToLowerInvariant();
        return $"{typeName}{KeySeparator}{id}";
    }

    /// <inheritdoc />
    public string BuildKey(string prefix, params object[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        
        if (parameters == null || parameters.Length == 0)
        {
            return prefix.ToLowerInvariant();
        }

        var keyParts = new List<string> { prefix.ToLowerInvariant() };
        keyParts.AddRange(parameters.Select(p => p?.ToString()?.ToLowerInvariant() ?? "null"));
        
        return string.Join(KeySeparator, keyParts);
    }

    /// <inheritdoc />
    public string BuildPattern<T>()
    {
        var typeName = typeof(T).Name.ToLowerInvariant();
        return $"{typeName}{KeySeparator}{WildcardPattern}";
    }

    /// <inheritdoc />
    public string BuildPattern(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{prefix.ToLowerInvariant()}{KeySeparator}{WildcardPattern}";
    }
}
