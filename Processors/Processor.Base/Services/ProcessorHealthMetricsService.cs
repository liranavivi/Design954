using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Shared.Correlation;
using Shared.Models;

namespace Processor.Base.Services;

/// <summary>
/// Service for exposing ProcessorHealthCacheEntry properties as OpenTelemetry metrics.
/// Provides comprehensive processor health and performance metrics for analysis and monitoring.
/// Uses consistent labeling from appsettings configuration (Name and Version).
/// </summary>
public class ProcessorHealthMetricsService : IProcessorHealthMetricsService
{
    private readonly ProcessorConfiguration _config;
    private readonly ILogger<ProcessorHealthMetricsService> _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // Track recorded start times to prevent duplicate start events
    private readonly HashSet<string> _recordedStartTimes = new();

    // Status and Health Metrics
    private readonly Gauge<int> _processorStatusGauge;
    private readonly Gauge<double> _processorUptimeGauge;
    private readonly Counter<long> _healthCheckCounter;

    // Performance Metrics
    private readonly Gauge<double> _cpuUsageGauge;
    private readonly Gauge<long> _memoryUsageGauge;

    // Cache Metrics (aggregated)
    private readonly Gauge<double> _cacheAverageEntryAgeGauge;
    private readonly Gauge<long> _cacheActiveEntriesGauge;

    // Metadata Metrics
    private readonly Gauge<int> _processorProcessIdGauge;
    private readonly Counter<long> _processorStartCounter;

    // Exception Metrics
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public ProcessorHealthMetricsService(
        IOptions<ProcessorConfiguration> config,
        ILogger<ProcessorHealthMetricsService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Initialize base labels for this metrics service
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("processor_composite_key", _config.GetCompositeKey()),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Use the recommended unique meter name pattern: {Version}_{Name}
        var meterName = $"{_config.Version}_{_config.Name}";
        var fullMeterName = $"{meterName}.HealthMetrics";
        _meter = new Meter(fullMeterName);

        // Status and Health Metrics - inline creation
        _processorStatusGauge = _meter.CreateGauge<int>(
            "processor_health_status",
            "Current health status of the processor (0=Healthy, 1=Degraded, 2=Unhealthy)");

        _processorUptimeGauge = _meter.CreateGauge<double>(
            "processor_uptime_seconds",
            "Total uptime of the processor in seconds");

        _healthCheckCounter = _meter.CreateCounter<long>(
            "processor_health_checks_total",
            "Total number of health checks performed");

        // Performance Metrics - inline creation
        _cpuUsageGauge = _meter.CreateGauge<double>(
            "processor_cpu_usage_percent",
            "Current CPU usage percentage (0-100)");

        _memoryUsageGauge = _meter.CreateGauge<long>(
            "processor_memory_usage_bytes",
            "Current memory usage in bytes");

        // Cache Metrics - inline creation
        _cacheAverageEntryAgeGauge = _meter.CreateGauge<double>(
            "processor_cache_average_entry_age_seconds",
            "Average age of cache entries in seconds");

        _cacheActiveEntriesGauge = _meter.CreateGauge<long>(
            "processor_cache_active_entries_total",
            "Total number of active cache entries");

        // Metadata Metrics - inline creation
        _processorProcessIdGauge = _meter.CreateGauge<int>(
            "processor_process_id",
            "Process ID of the processor");

        _processorStartCounter = _meter.CreateCounter<long>(
            "processor_starts_total",
            "Total number of times the processor has been started");

        // Exception Metrics - inline creation
        _exceptionsCounter = _meter.CreateCounter<long>(
            "processor_exceptions_total",
            "Total number of exceptions thrown by the processor");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "processor_critical_exceptions_total",
            "Total number of critical exceptions that affect processor operation");

        // Single summary log (like FlowMetrics)
        _logger.LogInformationWithCorrelation(
            "ProcessorHealthMetricsService initialized with meter name: {MeterName}, Composite Key: {CompositeKey}",
            fullMeterName, _config.GetCompositeKey());
    }

    public void RecordHealthCacheEntryMetrics(ProcessorHealthCacheEntry healthEntry, Guid correlationId)
    {
        try
        {
            // Record all metrics from the health cache entry
            RecordProcessorStatus(healthEntry.Status, correlationId);
            RecordProcessorUptime(healthEntry.Uptime, correlationId);
            RecordPerformanceMetrics(healthEntry.PerformanceMetrics, correlationId);
            RecordProcessorMetadata(healthEntry.Metadata, correlationId);
            RecordHealthCheckResults(healthEntry.HealthChecks, correlationId);

            // Calculate cache metrics from the entry
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var entryAge = now - healthEntry.LastUpdated;
            RecordCacheMetrics(entryAge, 1, correlationId);

            _logger.LogInformationWithCorrelation(
                "Recorded health cache entry metrics for processor {CompositeKey}, Status: {Status}, Uptime: {Uptime}",
                _config.GetCompositeKey(), healthEntry.Status, healthEntry.Uptime);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex,
                "Failed to record health cache entry metrics for processor {CompositeKey}",
                _config.GetCompositeKey());
        }
    }

    public void RecordProcessorStatus(HealthStatus status, Guid correlationId)
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
        _processorStatusGauge.Record(statusValue, tags);
    }

    public void RecordProcessorUptime(TimeSpan uptime, Guid correlationId)
    {
        var uptimeLabels = new KeyValuePair<string, object?>[]
        {
            new("uptime", uptime.TotalSeconds.ToString("F2")),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + uptimeLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        uptimeLabels.CopyTo(tags, _baseLabels.Length);

        _processorUptimeGauge.Record(uptime.TotalSeconds, tags);
    }

    public void RecordPerformanceMetrics(ProcessorPerformanceMetrics performanceMetrics, Guid correlationId)
    {
        var performanceLabels = new KeyValuePair<string, object?>[]
        {
            new("cpu_usage_percent", performanceMetrics.CpuUsagePercent.ToString("F2")),
            new("memory_usage_bytes", performanceMetrics.MemoryUsageBytes.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + performanceLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        performanceLabels.CopyTo(tags, _baseLabels.Length);

        // Record current resource usage as gauges
        _cpuUsageGauge.Record(performanceMetrics.CpuUsagePercent, tags);
        _memoryUsageGauge.Record(performanceMetrics.MemoryUsageBytes, tags);
    }

    public void RecordProcessorMetadata(ProcessorMetadata metadata, Guid correlationId)
    {
        var metadataLabels = new KeyValuePair<string, object?>[]
        {
            new("host_name", metadata.HostName),
            new("process_id", metadata.ProcessId.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + metadataLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        metadataLabels.CopyTo(tags, _baseLabels.Length);

        _processorProcessIdGauge.Record(metadata.ProcessId, tags);

        // Record processor start event (only once per start time)
        var startTimeKey = $"{_config.GetCompositeKey()}_{metadata.StartTime:yyyy-MM-ddTHH:mm:ssZ}";

        lock (_recordedStartTimes)
        {
            if (!_recordedStartTimes.Contains(startTimeKey))
            {
                _recordedStartTimes.Add(startTimeKey);

                var startLabels = new KeyValuePair<string, object?>[]
                {
                    new("start_time", metadata.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                };

                var startTags = new KeyValuePair<string, object?>[_baseLabels.Length + startLabels.Length];
                _baseLabels.CopyTo(startTags, 0);
                startLabels.CopyTo(startTags, _baseLabels.Length);

                _processorStartCounter.Add(1, startTags);

                _logger.LogInformationWithCorrelation(
                    "Recorded processor start event for {CompositeKey}, StartTime: {StartTime}",
                    _config.GetCompositeKey(), metadata.StartTime);
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
            new("entry_age", entryAge.ToString("F2")),
            new("active_entries", activeEntries.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + cacheLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        cacheLabels.CopyTo(tags, _baseLabels.Length);

        _cacheAverageEntryAgeGauge.Record(entryAge, tags);
        _cacheActiveEntriesGauge.Record(activeEntries, tags);
    }

    public void RecordException(string exceptionType, string severity, Guid correlationId)
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

        if (severity == "critical")
        {
            _criticalExceptionsCounter.Add(1, tags);
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
