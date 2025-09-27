using Shared.Models;

namespace Processor.Base.Interfaces;

/// <summary>
/// Service interface for exposing ProcessorHealthCacheEntry properties as OpenTelemetry metrics.
/// Provides comprehensive processor health and performance metrics for analysis and monitoring.
/// </summary>
public interface IProcessorHealthMetricsService : IDisposable
{
    /// <summary>
    /// Records all metrics from a ProcessorHealthCacheEntry.
    /// This method extracts and publishes all relevant metrics from the health cache entry.
    /// </summary>
    /// <param name="healthEntry">The processor health cache entry containing metrics data</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordHealthCacheEntryMetrics(ProcessorHealthCacheEntry healthEntry, Guid correlationId);

    /// <summary>
    /// Records processor status metrics.
    /// </summary>
    /// <param name="status">Current health status</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordProcessorStatus(HealthStatus status, Guid correlationId);

    /// <summary>
    /// Records processor uptime metrics.
    /// </summary>
    /// <param name="uptime">Current processor uptime</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordProcessorUptime(TimeSpan uptime, Guid correlationId);

    /// <summary>
    /// Records processor performance metrics.
    /// </summary>
    /// <param name="performanceMetrics">Performance metrics data</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordPerformanceMetrics(ProcessorPerformanceMetrics performanceMetrics, Guid correlationId);

    /// <summary>
    /// Records processor metadata metrics.
    /// </summary>
    /// <param name="metadata">Processor metadata</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordProcessorMetadata(ProcessorMetadata metadata, Guid correlationId);

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
    /// Records exception metrics for processor monitoring.
    /// </summary>
    /// <param name="exceptionType">Type of exception (e.g., ValidationException, ProcessingException)</param>
    /// <param name="severity">Severity level (warning, error, critical)</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordException(string exceptionType, string severity, Guid correlationId);



}
