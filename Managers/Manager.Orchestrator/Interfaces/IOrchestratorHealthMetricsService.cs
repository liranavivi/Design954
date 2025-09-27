using Shared.Models;

namespace Manager.Orchestrator.Interfaces;

/// <summary>
/// Service interface for exposing orchestrator health metrics for monitoring system health, performance, cache operations, and exceptions.
/// Provides comprehensive orchestrator health and performance metrics for analysis and monitoring.
/// </summary>
public interface IOrchestratorHealthMetricsService : IDisposable
{
    /// <summary>
    /// Records orchestrator status metrics.
    /// </summary>
    /// <param name="status">Current health status</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordOrchestratorStatus(HealthStatus status, Guid correlationId);

    /// <summary>
    /// Records orchestrator uptime metrics.
    /// </summary>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordOrchestratorUptime(Guid correlationId);

    /// <summary>
    /// Records orchestrator performance metrics.
    /// </summary>
    /// <param name="cpuUsagePercent">Current CPU usage percentage (0-100)</param>
    /// <param name="memoryUsageBytes">Current memory usage in bytes</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordPerformanceMetrics(double cpuUsagePercent, long memoryUsageBytes, Guid correlationId);

    /// <summary>
    /// Records orchestrator metadata metrics.
    /// </summary>
    /// <param name="processId">Process ID of the orchestrator</param>
    /// <param name="startTime">Start time of the orchestrator</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordOrchestratorMetadata(int processId, DateTime startTime, Guid correlationId);

    /// <summary>
    /// Records health check results metrics.
    /// </summary>
    /// <param name="healthChecks">Health check results</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordHealthCheckResults(Dictionary<string, HealthCheckResult> healthChecks, Guid correlationId);

    /// <summary>
    /// Records aggregated cache metrics.
    /// </summary>
    /// <param name="entryAge">Age of cache entry in seconds</param>
    /// <param name="activeEntries">Number of active cache entries</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordCacheMetrics(double entryAge, long activeEntries, Guid correlationId);

    /// <summary>
    /// Records cache operation metrics.
    /// </summary>
    /// <param name="success">Whether the cache operation was successful</param>
    /// <param name="operationType">Type of cache operation (e.g., "read", "write", "delete")</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordCacheOperation(bool success, string operationType, Guid correlationId);

    /// <summary>
    /// Records exception metrics for orchestrator monitoring.
    /// </summary>
    /// <param name="exceptionType">Type of exception (e.g., ValidationException, ProcessingException)</param>
    /// <param name="severity">Severity level (warning, error, critical)</param>
    /// <param name="isCritical">Whether this exception affects orchestrator operation</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordException(string exceptionType, string severity, bool isCritical, Guid correlationId);
}
