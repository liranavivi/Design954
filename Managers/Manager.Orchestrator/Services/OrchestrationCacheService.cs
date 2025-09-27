using System.Diagnostics;
using System.Text.Json;
using Manager.Orchestrator.Interfaces;
using Manager.Orchestrator.Models;
using Shared.Correlation;
using Shared.Services.Interfaces;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Orchestration cache service implementation following ProcessorHealthMonitor pattern
/// </summary>
public class OrchestrationCacheService : IOrchestrationCacheService
{
    private readonly ICacheService _cacheService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrchestrationCacheService> _logger;
    private readonly IOrchestratorHealthMetricsService _metricsService;
    private readonly string _mapName;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly JsonSerializerOptions _jsonOptions;

    public OrchestrationCacheService(
        ICacheService cacheService,
        IConfiguration configuration,
        ILogger<OrchestrationCacheService> logger,
        IOrchestratorHealthMetricsService metricsService)
    {
        _cacheService = cacheService;
        _configuration = configuration;
        _logger = logger;
        _metricsService = metricsService;

        _mapName = _configuration["OrchestrationCache:MapName"] ?? "orchestration-data";
        _maxRetries = _configuration.GetValue<int>("OrchestrationCache:MaxRetries", 3);
        _retryDelay = TimeSpan.FromMilliseconds(_configuration.GetValue<int>("OrchestrationCache:RetryDelayMs", 1000));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task StoreOrchestrationDataAsync(Guid orchestratedFlowId, OrchestrationCacheModel orchestrationData, HierarchicalLoggingContext context, TimeSpan? ttl = null)
    {
        var cacheKey = orchestratedFlowId.ToString();
        // No TTL - orchestration data persists until manually removed
        orchestrationData.ExpiresAt = DateTime.MaxValue; // Never expires

        _logger.LogInformationWithHierarchy(context, "Storing orchestration data in cache");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var cacheValue = JsonSerializer.Serialize(orchestrationData, _jsonOptions);
            await StoreWithRetryAsync(cacheKey, cacheValue, context);
            stopwatch.Stop();

            // Record successful cache operation metrics
            _metricsService.RecordCacheOperation(
                success: true,
                operationType: "store",
                correlationId: context.CorrelationId);

            _logger.LogInformationWithHierarchy(context, "Successfully stored orchestration data in cache. StepCount: {StepCount}, AssignmentCount: {AssignmentCount}",
                orchestrationData.StepEntities.Count, orchestrationData.Assignments.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Record failed cache operation metrics
            _metricsService.RecordCacheOperation(
                success: false,
                operationType: "store",
                correlationId: context.CorrelationId);

            _logger.LogErrorWithHierarchy(context, ex, "Failed to store orchestration data in cache");
            throw;
        }
    }

    public async Task<OrchestrationCacheModel?> GetOrchestrationDataAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context)
    {
        var cacheKey = orchestratedFlowId.ToString();

        _logger.LogDebugWithHierarchy(context, "Retrieving orchestration data from cache");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogInformationWithHierarchy(context, "Attempting to retrieve orchestration data from cache. MapName: {MapName}, CacheKey: {CacheKey}",
                _mapName, cacheKey);

            var cacheValue = await _cacheService.GetAsync(_mapName, cacheKey, context);

            if (string.IsNullOrEmpty(cacheValue))
            {
                stopwatch.Stop();

                // Record cache miss
                _metricsService.RecordCacheOperation(
                    success: false,
                    operationType: "get",
                    correlationId: context.CorrelationId);

                _logger.LogWarningWithHierarchy(context, "No orchestration data found in cache. MapName: {MapName}, CacheKey: {CacheKey}",
                    _mapName, cacheKey);
                return null;
            }

            _logger.LogDebugWithHierarchy(context, "Raw cache value retrieved. ValueLength: {ValueLength}",
                cacheValue.Length);

            var orchestrationData = JsonSerializer.Deserialize<OrchestrationCacheModel>(cacheValue, _jsonOptions);
            stopwatch.Stop();

            if (orchestrationData == null)
            {
                // Record failed deserialization as cache miss
                _metricsService.RecordCacheOperation(
                    success: false,
                    operationType: "get",
                    correlationId: context.CorrelationId);

                _logger.LogWarningWithHierarchy(context, "Failed to deserialize orchestration data from cache");
                return null;
            }

            // Check if the entry has expired
            if (orchestrationData.IsExpired)
            {
                // Record expired entry as cache miss
                _metricsService.RecordCacheOperation(
                    success: false,
                    operationType: "get",
                    correlationId: context.CorrelationId);

                _logger.LogWarningWithHierarchy(context, "Orchestration data in cache has expired. ExpiresAt: {ExpiresAt}",
                    orchestrationData.ExpiresAt);

                // Remove expired entry
                await RemoveOrchestrationDataAsync(orchestratedFlowId, context);
                return null;
            }

            // Record successful cache hit
            _metricsService.RecordCacheOperation(
                success: true,
                operationType: "get",
                correlationId: context.CorrelationId);

            _logger.LogDebugWithHierarchy(context, "Successfully retrieved orchestration data from cache. StepCount: {StepCount}, AssignmentCount: {AssignmentCount}",
                orchestrationData.StepEntities.Count, orchestrationData.Assignments.Count);

            return orchestrationData;
        }
        catch (Exception ex)
        {
            // Record failed cache operation
            _metricsService.RecordCacheOperation(
                success: false,
                operationType: "get",
                correlationId: context.CorrelationId);

            _logger.LogErrorWithHierarchy(context, ex, "Error retrieving orchestration data from cache");
            return null;
        }
    }

    public async Task RemoveOrchestrationDataAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context)
    {
        var cacheKey = orchestratedFlowId.ToString();

        _logger.LogInformationWithHierarchy(context, "Removing orchestration data from cache");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _cacheService.RemoveAsync(_mapName, cacheKey, context);
            stopwatch.Stop();

            // Record successful cache remove operation
            _metricsService.RecordCacheOperation(
                success: true,
                operationType: "remove",
                correlationId: context.CorrelationId);

            _logger.LogInformationWithHierarchy(context, "Successfully removed orchestration data from cache");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Record failed cache remove operation
            _metricsService.RecordCacheOperation(
                success: false,
                operationType: "remove",
                correlationId: context.CorrelationId);

            _logger.LogErrorWithHierarchy(context, ex, "Error removing orchestration data from cache");
            throw;
        }
    }

    public async Task<bool> ExistsAndValidAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Checking if orchestration data exists and is valid");

        try
        {
            var orchestrationData = await GetOrchestrationDataAsync(orchestratedFlowId, context);
            var exists = orchestrationData != null && !orchestrationData.IsExpired;

            _logger.LogDebugWithHierarchy(context, "Orchestration data existence check result. Exists: {Exists}",
                exists);

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Error checking orchestration data existence");
            return false;
        }
    }

    private async Task StoreWithRetryAsync(string cacheKey, string cacheValue, HierarchicalLoggingContext context)
    {
        var retryCount = 0;

        while (retryCount <= _maxRetries)
        {
            try
            {
                // TTL is controlled by Hazelcast map configuration (2 hours)
                await _cacheService.SetAsync(_mapName, cacheKey, cacheValue, context);

                // Log simple success message for orchestration data (only map and key)
                _logger.LogInformationWithHierarchy(context, "Saved data to cache. MapName: {MapName}, Key: {Key}", _mapName, cacheKey);
                _logger.LogDebugWithHierarchy(context, "Successfully stored cache entry. Key: {CacheKey}", cacheKey);
                return; // Success
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount > _maxRetries)
                {
                    _logger.LogErrorWithHierarchy(context, ex, "Failed to store cache entry after {MaxRetries} retries. Key: {CacheKey}",
                        _maxRetries, cacheKey);
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));

                _logger.LogWarningWithHierarchy(context, ex, "Failed to store cache entry, retry {RetryCount}/{MaxRetries} in {Delay}ms. Key: {CacheKey}",
                    retryCount, _maxRetries, delay.TotalMilliseconds, cacheKey);

                await Task.Delay(delay);
            }
        }
    }


}
