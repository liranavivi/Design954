using System.Diagnostics.Metrics;
using Manager.Orchestrator.Interfaces;
using Microsoft.Extensions.Options;
using Shared.Correlation;
using Shared.Models;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Service for exposing orchestrator health metrics for monitoring system health, performance, cache operations, and exceptions.
/// Provides comprehensive orchestrator health and performance metrics for analysis and monitoring.
/// Uses consistent labeling from appsettings configuration (Name and Version).
/// </summary>
public class OrchestratorHealthMetricsService : IOrchestratorHealthMetricsService, IDisposable
{
    private readonly ManagerConfiguration _config;
    private readonly ILogger<OrchestratorHealthMetricsService> _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // Track recorded start times to prevent duplicate start events
    private readonly HashSet<string> _recordedStartTimes = new();

    // Start time tracking for uptime calculation
    private readonly DateTime _startTime;

    // Status and Health Metrics - inline creation
    private readonly Gauge<int> _orchestratorStatusGauge;
    private readonly Gauge<double> _orchestratorUptimeGauge;
    private readonly Counter<long> _healthCheckCounter;

    // Performance Metrics - inline creation
    private readonly Gauge<double> _cpuUsageGauge;
    private readonly Gauge<long> _memoryUsageGauge;

    // Cache Metrics - inline creation
    private readonly Gauge<double> _cacheAverageEntryAgeGauge;
    private readonly Gauge<long> _cacheActiveEntriesGauge;
    private readonly Counter<long> _cacheOperationsCounter;

    // Metadata Metrics - inline creation
    private readonly Gauge<int> _orchestratorProcessIdGauge;
    private readonly Counter<long> _orchestratorStartCounter;

    // Exception Metrics - inline creation
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public OrchestratorHealthMetricsService(
        IOptions<ManagerConfiguration> config,
        ILogger<OrchestratorHealthMetricsService> logger)
    {
        _config = config.Value;
        _logger = logger;
        _startTime = DateTime.UtcNow;

        // Initialize base labels for this metrics service
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("orchestrator_composite_key", _config.GetCompositeKey()),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Use the recommended unique meter name pattern: {Version}_{Name}
        var meterName = $"{_config.Version}_{_config.Name}";
        var fullMeterName = $"{meterName}.HealthMetrics";
        _meter = new Meter(fullMeterName);

        // Status and Health Metrics - inline creation
        _orchestratorStatusGauge = _meter.CreateGauge<int>(
            "orchestrator_health_status",
            "Current health status of the orchestrator (0=Healthy, 1=Degraded, 2=Unhealthy)");

        _orchestratorUptimeGauge = _meter.CreateGauge<double>(
            "orchestrator_uptime_seconds",
            "Total uptime of the orchestrator in seconds");

        _healthCheckCounter = _meter.CreateCounter<long>(
            "orchestrator_health_checks_total",
            "Total number of health checks performed");

        // Performance Metrics - inline creation
        _cpuUsageGauge = _meter.CreateGauge<double>(
            "orchestrator_cpu_usage_percent",
            "Current CPU usage percentage (0-100)");

        _memoryUsageGauge = _meter.CreateGauge<long>(
            "orchestrator_memory_usage_bytes",
            "Current memory usage in bytes");

        // Cache Metrics - inline creation
        _cacheAverageEntryAgeGauge = _meter.CreateGauge<double>(
            "orchestrator_cache_average_entry_age_seconds",
            "Average age of cache entries in seconds");

        _cacheActiveEntriesGauge = _meter.CreateGauge<long>(
            "orchestrator_cache_active_entries_total",
            "Total number of active cache entries");

        _cacheOperationsCounter = _meter.CreateCounter<long>(
            "orchestrator_cache_operations_total",
            "Total number of cache operations performed");

        // Metadata Metrics - inline creation
        _orchestratorProcessIdGauge = _meter.CreateGauge<int>(
            "orchestrator_process_id",
            "Process ID of the orchestrator");

        _orchestratorStartCounter = _meter.CreateCounter<long>(
            "orchestrator_starts_total",
            "Total number of times the orchestrator has been started");

        // Exception Metrics - inline creation
        _exceptionsCounter = _meter.CreateCounter<long>(
            "orchestrator_exceptions_total",
            "Total number of exceptions thrown by the orchestrator");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "orchestrator_critical_exceptions_total",
            "Total number of critical exceptions that affect orchestrator operation");

        // Single summary log (like ProcessorHealthMetricsService)
        _logger.LogInformationWithCorrelation(
            "OrchestratorHealthMetricsService initialized with meter name: {MeterName}, Composite Key: {CompositeKey}",
            fullMeterName, _config.GetCompositeKey());
    }

    public void RecordOrchestratorStatus(HealthStatus status, Guid correlationId)
    {
        var statusLabels = new KeyValuePair<string, object?>[]
        {
            new("status", status.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + statusLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        statusLabels.CopyTo(tags, _baseLabels.Length);

        var statusValue = (int)status;
        _orchestratorStatusGauge.Record(statusValue, tags);
    }

    public void RecordOrchestratorUptime(Guid correlationId)
    {
        var uptime = DateTime.UtcNow - _startTime;
        var uptimeLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + uptimeLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        uptimeLabels.CopyTo(tags, _baseLabels.Length);

        _orchestratorUptimeGauge.Record(uptime.TotalSeconds, tags);
    }

    public void RecordPerformanceMetrics(double cpuUsagePercent, long memoryUsageBytes, Guid correlationId)
    {
        var performanceLabels = new KeyValuePair<string, object?>[]
        {
            new("cpu_usage_percent", cpuUsagePercent.ToString("F2")),
            new("memory_usage_bytes", memoryUsageBytes.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + performanceLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        performanceLabels.CopyTo(tags, _baseLabels.Length);

        // Record current resource usage as gauges
        _cpuUsageGauge.Record(cpuUsagePercent, tags);
        _memoryUsageGauge.Record(memoryUsageBytes, tags);
    }

    public void RecordOrchestratorMetadata(int processId, DateTime startTime, Guid correlationId)
    {
        var metadataLabels = new KeyValuePair<string, object?>[]
        {
            new("host_name", Environment.MachineName),
            new("process_id", processId.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + metadataLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        metadataLabels.CopyTo(tags, _baseLabels.Length);

        _orchestratorProcessIdGauge.Record(processId, tags);

        // Record orchestrator start event (only once per start time)
        var startTimeKey = $"{_config.GetCompositeKey()}_{startTime:yyyy-MM-ddTHH:mm:ssZ}";

        lock (_recordedStartTimes)
        {
            if (!_recordedStartTimes.Contains(startTimeKey))
            {
                _recordedStartTimes.Add(startTimeKey);

                var startTags = new KeyValuePair<string, object?>[_baseLabels.Length + metadataLabels.Length + 1];
                _baseLabels.CopyTo(startTags, 0);
                metadataLabels.CopyTo(startTags, _baseLabels.Length);
                startTags[startTags.Length - 1] = new("start_time", startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));

                _orchestratorStartCounter.Add(1, startTags);
            }
        }
    }

    public void RecordHealthCheckResults(Dictionary<string, HealthCheckResult> healthChecks, Guid correlationId)
    {
        foreach (var healthCheck in healthChecks)
        {
            var healthCheckLabels = new KeyValuePair<string, object?>[]
            {
                new("health_check_name", healthCheck.Key),
                new("health_check_status", healthCheck.Value.Status.ToString()),
                new("correlation_id", correlationId)
            };

            var tags = new KeyValuePair<string, object?>[_baseLabels.Length + healthCheckLabels.Length];
            _baseLabels.CopyTo(tags, 0);
            healthCheckLabels.CopyTo(tags, _baseLabels.Length);

            _healthCheckCounter.Add(1, tags);
        }
    }

    public void RecordCacheMetrics(double entryAge, long activeEntries, Guid correlationId)
    {
        var cacheLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + cacheLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        cacheLabels.CopyTo(tags, _baseLabels.Length);

        _cacheAverageEntryAgeGauge.Record(entryAge, tags);
        _cacheActiveEntriesGauge.Record(activeEntries, tags);
    }

    public void RecordCacheOperation(bool success, string operationType, Guid correlationId)
    {
        var cacheOpLabels = new KeyValuePair<string, object?>[]
        {
            new("operation_type", operationType),
            new("status", success ? "success" : "failed"),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + cacheOpLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        cacheOpLabels.CopyTo(tags, _baseLabels.Length);

        _cacheOperationsCounter.Add(1, tags);
    }

    public void RecordException(string exceptionType, string severity, bool isCritical, Guid correlationId)
    {
        var exceptionLabels = new KeyValuePair<string, object?>[]
        {
            new("exception_type", exceptionType),
            new("severity", severity),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + exceptionLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        exceptionLabels.CopyTo(tags, _baseLabels.Length);

        _exceptionsCounter.Add(1, tags);

        if (isCritical)
        {
            _criticalExceptionsCounter.Add(1, tags);
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
        _logger.LogInformationWithCorrelation("OrchestratorHealthMetricsService disposed");
    }
}
