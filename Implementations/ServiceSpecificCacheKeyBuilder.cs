using NSCache.Interfaces;

namespace NSCache.Implementations;

/// <summary>
/// Cache key builder with service-specific prefixes to prevent key collisions between services.
/// </summary>
public class ServiceSpecificCacheKeyBuilder : ICacheKeyBuilder
{
    private const string KeySeparator = ":";
    private const string WildcardPattern = "*";
    private readonly string _servicePrefix;

    /// <summary>
    /// Initializes a new instance of the ServiceSpecificCacheKeyBuilder.
    /// </summary>
    /// <param name="servicePrefix">The service-specific prefix for cache keys.</param>
    public ServiceSpecificCacheKeyBuilder(string servicePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrefix);
        _servicePrefix = servicePrefix.ToLowerInvariant();
    }

    /// <inheritdoc />
    public string BuildKey<T>(object id)
    {
        var typeName = typeof(T).Name.ToLowerInvariant();
        return $"{_servicePrefix}{KeySeparator}{typeName}{KeySeparator}{id}";
    }

    /// <inheritdoc />
    public string BuildKey(string prefix, params object[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        
        if (parameters == null || parameters.Length == 0)
        {
            return $"{_servicePrefix}{KeySeparator}{prefix.ToLowerInvariant()}";
        }

        var keyParts = new List<string> { _servicePrefix, prefix.ToLowerInvariant() };
        keyParts.AddRange(parameters.Select(p => p?.ToString()?.ToLowerInvariant() ?? "null"));
        
        return string.Join(KeySeparator, keyParts);
    }

    /// <inheritdoc />
    public string BuildPattern<T>()
    {
        var typeName = typeof(T).Name.ToLowerInvariant();
        return $"{_servicePrefix}{KeySeparator}{typeName}{KeySeparator}{WildcardPattern}";
    }

    /// <inheritdoc />
    public string BuildPattern(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{_servicePrefix}{KeySeparator}{prefix.ToLowerInvariant()}{KeySeparator}{WildcardPattern}";
    }
}
