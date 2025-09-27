using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Plugin.PreFileReader.Interfaces;
using Shared.Correlation;

namespace Plugin.PreFileReader.Services;

/// <summary>
/// Implementation of metrics service for PreFileReader plugin operations.
/// Aligned with ProcessorFlowMetricsService and ProcessorHealthMetricsService architecture.
/// </summary>
public class PreFileReaderPluginMetricsService : IPreFileReaderPluginMetricsService
{
    private readonly string _pluginCompositeKey;
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // PreFileReader-specific instruments
    private readonly Counter<long> _fileDiscoveryCounter;
    private readonly Counter<long> _directoryScanCounter;
    private readonly Histogram<double> _directoryScanDurationHistogram;



    // Exception Metrics (same as ProcessorHealthMetricsService)
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public PreFileReaderPluginMetricsService(
        string pluginCompositeKey,
        ILogger logger)
    {
        _pluginCompositeKey = pluginCompositeKey;
        _logger = logger;

        // Initialize base labels (same pattern as ProcessorHealthMetricsService)
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("plugin_composite_key", _pluginCompositeKey),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Create meter with composite key + .Plugin suffix
        var meterName = $"{_pluginCompositeKey}.Plugin";
        _meter = new Meter(meterName);

        // Initialize PreFileReader-specific instruments
        _fileDiscoveryCounter = _meter.CreateCounter<long>(
            "plugin_file_discovery_total",
            "Total number of file discovery operations");

        _directoryScanCounter = _meter.CreateCounter<long>(
            "plugin_directory_scan_total",
            "Total number of directory scan operations");

        _directoryScanDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_directory_scan_duration_ms",
            "Duration of directory scan operations in milliseconds");



        // Initialize exception instruments (same as ProcessorHealthMetricsService)
        _exceptionsCounter = _meter.CreateCounter<long>(
            "plugin_exceptions_total",
            "Total number of exceptions thrown by plugin");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "plugin_critical_exceptions_total",
            "Total number of critical exceptions that affect plugin operation");

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    public void RecordFileDiscovery(long filesFound, string directoryPath, TimeSpan scanDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var discoveryLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("files_found", filesFound.ToString()),
            new("directory_path", directoryPath),
            new("scan_duration_ms", scanDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + discoveryLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        discoveryLabels.CopyTo(tags, _baseLabels.Length);

        _fileDiscoveryCounter.Add(1, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded file discovery - Files Found: {FilesFound}, Directory: {DirectoryPath}, Duration: {Duration}ms",
            filesFound, directoryPath, scanDuration.TotalMilliseconds);
    }

    public void RecordDirectoryScan(bool success, string directoryPath, TimeSpan duration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var scanLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("success", success.ToString()),
            new("directory_path", directoryPath),
            new("duration_ms", duration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + scanLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        scanLabels.CopyTo(tags, _baseLabels.Length);

        _directoryScanCounter.Add(1, tags);
        _directoryScanDurationHistogram.Record(duration.TotalMilliseconds, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded directory scan - Success: {Success}, Directory: {DirectoryPath}, Duration: {Duration}ms",
            success, directoryPath, duration.TotalMilliseconds);
    }

    public void RecordPluginException(string exceptionType, string severity,
            string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var exceptionLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("exception_type", exceptionType),
            new("severity", severity)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + exceptionLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        exceptionLabels.CopyTo(tags, _baseLabels.Length);

        _exceptionsCounter.Add(1, tags);

        if (severity == "critical")
        {
            _criticalExceptionsCounter.Add(1, tags);
        }

        _logger.LogWarningWithHierarchy(context,
            "Recorded plugin exception - Type: {ExceptionType}, Severity: {Severity}",
            exceptionType, severity);
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
