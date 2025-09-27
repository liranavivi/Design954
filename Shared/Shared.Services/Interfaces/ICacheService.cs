using Shared.Correlation;

namespace Shared.Services.Interfaces;
/// <summary>
/// Interface for cache service operations
/// </summary>
public interface ICacheService : IDisposable
{
    /// <summary>
    /// Retrieves data from cache
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <returns>Cached data or null if not found</returns>
    Task<string?> GetAsync(string mapName, string key);

    /// <summary>
    /// Retrieves data from cache using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Cached data or null if not found</returns>
    Task<string?> GetAsync(string mapName, string key, HierarchicalLoggingContext context);

    /// <summary>
    /// Stores data in cache
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Data to store</param>
    /// <returns>Task representing the operation</returns>
    Task SetAsync(string mapName, string key, string value);

    /// <summary>
    /// Stores data in cache using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Data to store</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Task representing the operation</returns>
    Task SetAsync(string mapName, string key, string value, HierarchicalLoggingContext context);

    /// <summary>
    /// Stores data in cache with time-to-live (TTL)
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Data to store</param>
    /// <param name="ttl">Time-to-live for the cache entry</param>
    /// <returns>Task representing the operation</returns>
    Task SetAsync(string mapName, string key, string value, TimeSpan ttl);

    /// <summary>
    /// Stores data in cache with time-to-live (TTL) using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Data to store</param>
    /// <param name="ttl">Time-to-live for the cache entry</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Task representing the operation</returns>
    Task SetAsync(string mapName, string key, string value, TimeSpan ttl, HierarchicalLoggingContext context);



    /// <summary>
    /// Checks if a key exists in cache
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <returns>True if key exists, false otherwise</returns>
    Task<bool> ExistsAsync(string mapName, string key);

    /// <summary>
    /// Checks if a key exists in cache using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if key exists, false otherwise</returns>
    Task<bool> ExistsAsync(string mapName, string key, HierarchicalLoggingContext context);

    /// <summary>
    /// Removes data from cache
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <returns>Task representing the operation</returns>
    Task RemoveAsync(string mapName, string key);

    /// <summary>
    /// Removes data from cache using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Task representing the operation</returns>
    Task RemoveAsync(string mapName, string key, HierarchicalLoggingContext context);

    /// <summary>
    /// Checks if the cache service is healthy and accessible
    /// </summary>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// Checks if the cache service is healthy and accessible using hierarchical context
    /// </summary>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(HierarchicalLoggingContext context);

    /// <summary>
    /// Gets cache statistics for a specific map
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <returns>Tuple containing entry count and average age in seconds</returns>
    Task<(long entryCount, double averageAgeSeconds)> GetCacheStatisticsAsync(string mapName);

    /// <summary>
    /// Gets cache statistics for a specific map using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Tuple containing entry count and average age in seconds</returns>
    Task<(long entryCount, double averageAgeSeconds)> GetCacheStatisticsAsync(string mapName, HierarchicalLoggingContext context);

    /// <summary>
    /// Generates a processor-specific cache key for activity data using the pattern:
    /// {processorId}:{orchestratedFlowId}:{correlationId}:{executionId}:{stepId}:{publishId}
    /// This ensures processor isolation in the shared processor-activity cache map.
    /// </summary>
    string GetProcessorCacheKey(Guid processorId, Guid orchestratedFlowId, Guid correlationId, Guid executionId, Guid stepId, Guid publishId);

    /// <summary>
    /// Atomically sets a value only if the key doesn't exist (uses map-level TTL configuration)
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to set</param>
    /// <returns>Previous value if key existed, null if key was absent and value was set</returns>
    Task<string?> PutIfAbsentAsync(string mapName, string key, string value);

    /// <summary>
    /// Atomically sets a value only if the key doesn't exist (uses map-level TTL configuration) using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to set</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Previous value if key existed, null if key was absent and value was set</returns>
    Task<string?> PutIfAbsentAsync(string mapName, string key, string value, HierarchicalLoggingContext context);

    /// <summary>
    /// Atomically sets a value only if the key doesn't exist
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to set</param>
    /// <param name="ttl">Time-to-live for the entry</param>
    /// <returns>Previous value if key existed, null if key was absent and value was set</returns>
    Task<string?> PutIfAbsentAsync(string mapName, string key, string value, TimeSpan ttl);

    /// <summary>
    /// Atomically sets a value only if the key doesn't exist using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to set</param>
    /// <param name="ttl">Time-to-live for the entry</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Previous value if key existed, null if key was absent and value was set</returns>
    Task<string?> PutIfAbsentAsync(string mapName, string key, string value, TimeSpan ttl, HierarchicalLoggingContext context);

    /// <summary>
    /// Gets all key-value pairs from a cache map
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <returns>Collection of key-value pairs</returns>
    Task<IEnumerable<KeyValuePair<string, string>>> GetAllEntriesAsync(string mapName);

    /// <summary>
    /// Gets all key-value pairs from a cache map using hierarchical context
    /// </summary>
    /// <param name="mapName">Name of the cache map</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Collection of key-value pairs</returns>
    Task<IEnumerable<KeyValuePair<string, string>>> GetAllEntriesAsync(string mapName, HierarchicalLoggingContext context);
}
