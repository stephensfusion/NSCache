namespace NSCache.Interfaces;

/// <summary>
/// Common interface for all cache configuration options.
/// </summary>
public interface ICacheOption
{
    /// <summary>
    /// Default expiration time for cached items in this configuration.
    /// </summary>
    TimeSpan DefaultExpiration { get; set; }

    /// <summary>
    /// Validates the configuration settings.
    /// </summary>
    /// <returns>True if the configuration is valid, false otherwise.</returns>
    bool IsValid();

    /// <summary>
    /// Gets a description of this cache configuration.
    /// </summary>
    /// <returns>A human-readable description of the configuration.</returns>
    string GetDescription();
}
