using Manager.Orchestrator.Models;
using Shared.Correlation;

namespace Manager.Orchestrator.Interfaces;

/// <summary>
/// Interface for orchestration cache service operations following ProcessorHealthMonitor pattern
/// </summary>
public interface IOrchestrationCacheService
{
    /// <summary>
    /// Stores orchestration data in cache
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID as cache key</param>
    /// <param name="orchestrationData">The complete orchestration data to cache</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="ttl">Time-to-live for the cache entry</param>
    /// <returns>Task representing the cache operation</returns>
    Task StoreOrchestrationDataAsync(Guid orchestratedFlowId, OrchestrationCacheModel orchestrationData, HierarchicalLoggingContext context, TimeSpan? ttl = null);

    /// <summary>
    /// Retrieves orchestration data from cache
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID as cache key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Cached orchestration data or null if not found/expired</returns>
    Task<OrchestrationCacheModel?> GetOrchestrationDataAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context);

    /// <summary>
    /// Removes orchestration data from cache
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID as cache key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Task representing the cache operation</returns>
    Task RemoveOrchestrationDataAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context);

    /// <summary>
    /// Checks if orchestration data exists in cache and is not expired
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID as cache key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if data exists and is valid, false otherwise</returns>
    Task<bool> ExistsAndValidAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context);
}
