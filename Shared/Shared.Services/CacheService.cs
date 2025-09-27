using System.Diagnostics;
using Hazelcast;
using Microsoft.Extensions.Logging;
using Shared.Correlation;
using Shared.Services.Interfaces;
namespace Shared.Services;


/// <summary>
/// Hazelcast-specific cache service implementation
/// </summary>
public class CacheService : ICacheService
{
    private readonly Lazy<Task<IHazelcastClient>> _hazelcastClientFactory;
    private readonly ILogger<CacheService> _logger;
    private readonly ActivitySource _activitySource;

    public CacheService(
        Lazy<Task<IHazelcastClient>> hazelcastClientFactory,
        ILogger<CacheService> logger)
    {
        _hazelcastClientFactory = hazelcastClientFactory;
        _logger = logger;
        _activitySource = new System.Diagnostics.ActivitySource("BaseProcessorApplication.Cache");
    }

    private async Task<IHazelcastClient> GetClientAsync()
    {
        return await _hazelcastClientFactory.Value;
    }

    public async Task<string?> GetAsync(string mapName, string key)
    {
        using var activity = _activitySource.StartActivity("Cache.Get");
        activity?.SetTag("cache.operation", "get")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebugWithCorrelation("Starting cache GET operation. MapName: {MapName}, Key: {Key}",
                mapName, key);

            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            _logger.LogDebugWithCorrelation("Hazelcast client obtained. MapName: {MapName}, Key: {Key}",
                mapName, key);

            var map = await client.GetMapAsync<string, string>(mapName);
            _logger.LogDebugWithCorrelation("Hazelcast map obtained. MapName: {MapName}, Key: {Key}",
                mapName, key);

            var result = await map.GetAsync(key);
            stopwatch.Stop();

            activity?.SetTag("cache.hit", result != null);
            activity?.SetTag("cache.operation_duration_ms", stopwatch.ElapsedMilliseconds);

            _logger.LogInformationWithCorrelation("Cache GET operation completed. MapName: {MapName}, Key: {Key}, Found: {Found}, Duration: {Duration}ms, ResultLength: {ResultLength}",
                mapName, key, result != null, stopwatch.ElapsedMilliseconds, result?.Length ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("cache.operation_duration_ms", stopwatch.ElapsedMilliseconds);

            _logger.LogErrorWithCorrelation(ex, "Failed to retrieve data from cache. MapName: {MapName}, Key: {Key}, Duration: {Duration}ms, ExceptionType: {ExceptionType}",
                mapName, key, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Retrieves data from cache using hierarchical context
    /// </summary>
    public async Task<string?> GetAsync(string mapName, string key, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.Get");
        activity?.SetTag("cache.operation", "get")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebugWithHierarchy(context, "Starting cache GET operation. MapName: {MapName}, Key: {Key}",
                mapName, key);

            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            _logger.LogDebugWithHierarchy(context, "Hazelcast client obtained. MapName: {MapName}, Key: {Key}",
                mapName, key);

            var map = await client.GetMapAsync<string, string>(mapName);
            _logger.LogDebugWithHierarchy(context, "Hazelcast map obtained. MapName: {MapName}, Key: {Key}",
                mapName, key);

            var result = await map.GetAsync(key);
            stopwatch.Stop();

            activity?.SetTag("cache.hit", result != null);
            activity?.SetTag("cache.operation_duration_ms", stopwatch.ElapsedMilliseconds);

            _logger.LogInformationWithHierarchy(context, "Cache GET operation completed. MapName: {MapName}, Key: {Key}, Found: {Found}, Duration: {Duration}ms, ResultLength: {ResultLength}",
                mapName, key, result != null, stopwatch.ElapsedMilliseconds, result?.Length ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("cache.operation_duration_ms", stopwatch.ElapsedMilliseconds);

            _logger.LogErrorWithHierarchy(context, ex, "Failed to retrieve data from cache. MapName: {MapName}, Key: {Key}, Duration: {Duration}ms, ExceptionType: {ExceptionType}",
                mapName, key, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    public async Task SetAsync(string mapName, string key, string value)
    {
        using var activity = _activitySource.StartActivity("Cache.Set");
        activity?.SetTag("cache.operation", "set")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            await map.SetAsync(key, value);

            // Success logging is now handled by callers to provide appropriate context
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to save data to cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    /// <summary>
    /// Stores data in cache using hierarchical context
    /// </summary>
    public async Task SetAsync(string mapName, string key, string value, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.Set");
        activity?.SetTag("cache.operation", "set")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            await map.SetAsync(key, value);

            // Success logging is now handled by callers to provide appropriate context
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to save data to cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task SetAsync(string mapName, string key, string value, TimeSpan ttl)
    {
        using var activity = _activitySource.StartActivity("Cache.SetWithTtl");
        activity?.SetTag("cache.operation", "set_with_ttl")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key)
                ?.SetTag("cache.ttl_seconds", ttl.TotalSeconds);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            await map.SetAsync(key, value, ttl);

            // Success logging is now handled by callers to provide appropriate context
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to save data to cache with TTL. MapName: {MapName}, Key: {Key}, TTL: {TTL}s",
                mapName, key, ttl.TotalSeconds);
            throw;
        }
    }

    /// <summary>
    /// Stores data in cache with time-to-live (TTL) using hierarchical context
    /// </summary>
    public async Task SetAsync(string mapName, string key, string value, TimeSpan ttl, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.SetWithTtl");
        activity?.SetTag("cache.operation", "set_with_ttl")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key)
                ?.SetTag("cache.ttl_seconds", ttl.TotalSeconds);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            await map.SetAsync(key, value, ttl);

            // Success logging is now handled by callers to provide appropriate context
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to save data to cache with TTL. MapName: {MapName}, Key: {Key}, TTL: {TTL}s",
                mapName, key, ttl.TotalSeconds);
            throw;
        }
    }



    public async Task<bool> ExistsAsync(string mapName, string key)
    {
        using var activity = _activitySource.StartActivity("Cache.Exists");
        activity?.SetTag("cache.operation", "exists")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var exists = await map.ContainsKeyAsync(key);

            activity?.SetTag("cache.hit", exists);

            _logger.LogDebugWithCorrelation("Checked cache key existence. MapName: {MapName}, Key: {Key}, Exists: {Exists}",
                mapName, key, exists);

            return exists;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to check cache key existence. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    /// <summary>
    /// Checks if a key exists in cache using hierarchical context
    /// </summary>
    public async Task<bool> ExistsAsync(string mapName, string key, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.Exists");
        activity?.SetTag("cache.operation", "exists")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var exists = await map.ContainsKeyAsync(key);

            activity?.SetTag("cache.hit", exists);

            _logger.LogDebugWithHierarchy(context, "Checked cache key existence. MapName: {MapName}, Key: {Key}, Exists: {Exists}",
                mapName, key, exists);

            return exists;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to check cache key existence. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task RemoveAsync(string mapName, string key)
    {
        using var activity = _activitySource.StartActivity("Cache.Remove");
        activity?.SetTag("cache.operation", "remove")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            await map.RemoveAsync(key);

            _logger.LogDebugWithCorrelation("Removed data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to remove data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    /// <summary>
    /// Removes data from cache using hierarchical context
    /// </summary>
    public async Task RemoveAsync(string mapName, string key, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.Remove");
        activity?.SetTag("cache.operation", "remove")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            await map.RemoveAsync(key);

            _logger.LogDebugWithHierarchy(context, "Removed data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to remove data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            // Simple health check by trying to get a map
            var client = await GetClientAsync();
            if (client == null)
            {
                return false;
            }

            var testMap = await client.GetMapAsync<string, string>("health-check");
            return testMap != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithCorrelation(ex, "Hazelcast health check failed");
            return false;
        }
    }

    /// <summary>
    /// Checks if the cache service is healthy and accessible using hierarchical context
    /// </summary>
    public async Task<bool> IsHealthyAsync(HierarchicalLoggingContext context)
    {
        try
        {
            // Simple health check by trying to get a map
            var client = await GetClientAsync();
            if (client == null)
            {
                return false;
            }

            var testMap = await client.GetMapAsync<string, string>("health-check");
            return testMap != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithHierarchy(context, ex, "Hazelcast health check failed");
            return false;
        }
    }

    public async Task<(long entryCount, double averageAgeSeconds)> GetCacheStatisticsAsync(string mapName)
    {
        using var activity = _activitySource.StartActivity("Cache.GetStatistics");
        activity?.SetTag("cache.operation", "get_statistics")
                ?.SetTag("cache.map_name", mapName);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                _logger.LogWarningWithCorrelation("Failed to obtain Hazelcast client for cache statistics");
                return (0, 0);
            }

            var map = await client.GetMapAsync<string, string>(mapName);

            // Get entry count
            var entryCount = await map.GetSizeAsync();

            // For average age, we would need to iterate through entries and parse their timestamps
            // This is a simplified implementation - in production you might want to store creation timestamps
            var averageAgeSeconds = 0.0; // Placeholder - would need more complex implementation

            _logger.LogDebugWithCorrelation("Retrieved cache statistics. MapName: {MapName}, EntryCount: {EntryCount}, AverageAge: {AverageAge}s",
                mapName, entryCount, averageAgeSeconds);

            return (entryCount, averageAgeSeconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to get cache statistics. MapName: {MapName}", mapName);
            return (0, 0);
        }
    }

    /// <summary>
    /// Gets cache statistics for a specific map using hierarchical context
    /// </summary>
    public async Task<(long entryCount, double averageAgeSeconds)> GetCacheStatisticsAsync(string mapName, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.GetStatistics");
        activity?.SetTag("cache.operation", "get_statistics")
                ?.SetTag("cache.map_name", mapName);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                _logger.LogWarningWithHierarchy(context, "Failed to obtain Hazelcast client for cache statistics");
                return (0, 0);
            }

            var map = await client.GetMapAsync<string, string>(mapName);

            // Get entry count
            var entryCount = await map.GetSizeAsync();

            // For average age, we would need to iterate through entries and parse their timestamps
            // This is a simplified implementation - in production you might want to store creation timestamps
            var averageAgeSeconds = 0.0; // Placeholder - would need more complex implementation

            _logger.LogDebugWithHierarchy(context, "Retrieved cache statistics. MapName: {MapName}, EntryCount: {EntryCount}, AverageAge: {AverageAge}s",
                mapName, entryCount, averageAgeSeconds);

            return (entryCount, averageAgeSeconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to get cache statistics. MapName: {MapName}", mapName);
            return (0, 0);
        }
    }

    public string GetProcessorCacheKey(Guid processorId, Guid orchestratedFlowId, Guid correlationId, Guid executionId, Guid stepId, Guid publishId)
    {
        return $"{processorId}:{orchestratedFlowId}:{correlationId}:{executionId}:{stepId}:{publishId}";
    }



    public async Task<string?> PutIfAbsentAsync(string mapName, string key, string value)
    {
        using var activity = _activitySource.StartActivity("Cache.PutIfAbsent");
        activity?.SetTag("cache.operation", "putIfAbsent")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var previousValue = await map.PutIfAbsentAsync(key, value);

            var wasAbsent = previousValue == null;
            activity?.SetTag("cache.was_absent", wasAbsent);

            _logger.LogDebugWithCorrelation(
                "PutIfAbsent operation completed (using map TTL). MapName: {MapName}, Key: {Key}, WasAbsent: {WasAbsent}",
                mapName, key, wasAbsent);

            return previousValue;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to execute PutIfAbsent. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    /// <summary>
    /// Atomically sets a value only if the key doesn't exist (uses map-level TTL configuration) using hierarchical context
    /// </summary>
    public async Task<string?> PutIfAbsentAsync(string mapName, string key, string value, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.PutIfAbsent");
        activity?.SetTag("cache.operation", "putIfAbsent")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var previousValue = await map.PutIfAbsentAsync(key, value);

            var wasAbsent = previousValue == null;
            activity?.SetTag("cache.was_absent", wasAbsent);

            _logger.LogDebugWithHierarchy(context,
                "PutIfAbsent operation completed (using map TTL). MapName: {MapName}, Key: {Key}, WasAbsent: {WasAbsent}",
                mapName, key, wasAbsent);

            return previousValue;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to execute PutIfAbsent. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task<string?> PutIfAbsentAsync(string mapName, string key, string value, TimeSpan ttl)
    {
        using var activity = _activitySource.StartActivity("Cache.PutIfAbsent");
        activity?.SetTag("cache.operation", "putIfAbsent")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key)
                ?.SetTag("cache.ttl_seconds", ttl.TotalSeconds);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var previousValue = await map.PutIfAbsentAsync(key, value, ttl);

            var wasAbsent = previousValue == null;
            activity?.SetTag("cache.was_absent", wasAbsent);

            _logger.LogDebugWithCorrelation(
                "PutIfAbsent operation completed. MapName: {MapName}, Key: {Key}, WasAbsent: {WasAbsent}, TTL: {TTL}s",
                mapName, key, wasAbsent, ttl.TotalSeconds);

            return previousValue;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to execute PutIfAbsent with TTL. MapName: {MapName}, Key: {Key}, TTL: {TTL}s",
                mapName, key, ttl.TotalSeconds);
            throw;
        }
    }

    /// <summary>
    /// Atomically sets a value only if the key doesn't exist using hierarchical context
    /// </summary>
    public async Task<string?> PutIfAbsentAsync(string mapName, string key, string value, TimeSpan ttl, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.PutIfAbsent");
        activity?.SetTag("cache.operation", "putIfAbsent")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key)
                ?.SetTag("cache.ttl_seconds", ttl.TotalSeconds);

        try
        {
            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var previousValue = await map.PutIfAbsentAsync(key, value, ttl);

            var wasAbsent = previousValue == null;
            activity?.SetTag("cache.was_absent", wasAbsent);

            _logger.LogDebugWithHierarchy(context,
                "PutIfAbsent operation completed. MapName: {MapName}, Key: {Key}, WasAbsent: {WasAbsent}, TTL: {TTL}s",
                mapName, key, wasAbsent, ttl.TotalSeconds);

            return previousValue;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(context, ex, "Failed to execute PutIfAbsent with TTL. MapName: {MapName}, Key: {Key}, TTL: {TTL}s",
                mapName, key, ttl.TotalSeconds);
            throw;
        }
    }

    public async Task<IEnumerable<KeyValuePair<string, string>>> GetAllEntriesAsync(string mapName)
    {
        using var activity = _activitySource.StartActivity("Cache.GetAllEntries");
        activity?.SetTag("cache.operation", "get_all_entries")
                ?.SetTag("cache.map_name", mapName);

        try
        {
            _logger.LogDebugWithCorrelation("Starting cache GET ALL ENTRIES operation. MapName: {MapName}", mapName);

            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var entries = await map.GetEntriesAsync();

            var result = entries.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value)).ToList();

            _logger.LogDebugWithCorrelation("Cache GET ALL ENTRIES operation completed. MapName: {MapName}, EntryCount: {EntryCount}",
                mapName, result.Count);

            activity?.SetTag("cache.entry_count", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Cache GET ALL ENTRIES operation failed. MapName: {MapName}", mapName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<KeyValuePair<string, string>>> GetAllEntriesAsync(string mapName, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("Cache.GetAllEntries");
        activity?.SetTag("cache.operation", "get_all_entries")
                ?.SetTag("cache.map_name", mapName);

        try
        {
            _logger.LogDebugWithHierarchy(context, "Starting cache GET ALL ENTRIES operation. MapName: {MapName}", mapName);

            var client = await GetClientAsync();
            if (client == null)
            {
                throw new InvalidOperationException("Failed to obtain Hazelcast client");
            }

            var map = await client.GetMapAsync<string, string>(mapName);
            var entries = await map.GetEntriesAsync();

            var result = entries.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value)).ToList();

            _logger.LogDebugWithHierarchy(context, "Cache GET ALL ENTRIES operation completed. MapName: {MapName}, EntryCount: {EntryCount}",
                mapName, result.Count);

            activity?.SetTag("cache.entry_count", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Cache GET ALL ENTRIES operation failed. MapName: {MapName}", mapName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public void Dispose()
    {
        _activitySource?.Dispose();
    }
}
