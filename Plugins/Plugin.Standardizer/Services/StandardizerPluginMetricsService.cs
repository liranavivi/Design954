using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Plugin.Standardizer.Interfaces;
using Shared.Correlation;

namespace Plugin.Standardizer.Services;

/// <summary>
/// Implementation of metrics service for Standardizer plugin operations.
/// Aligned with PreFileReaderPluginMetricsService architecture.
/// </summary>
public class StandardizerPluginMetricsService : IStandardizerPluginMetricsService
{
    private readonly string _pluginCompositeKey;
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // Standardizer-specific instruments
    private readonly Counter<long> _dataStandardizationCounter;
    private readonly Histogram<double> _standardizationDurationHistogram;
    private readonly Counter<long> _recordsProcessedCounter;
    private readonly Counter<long> _recordsSuccessfulCounter;

    // Exception Metrics (same as PreFileReaderPluginMetricsService)
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public StandardizerPluginMetricsService(
        string pluginCompositeKey,
        ILogger logger)
    {
        _pluginCompositeKey = pluginCompositeKey;
        _logger = logger;

        // Initialize base labels (same pattern as PreFileReaderPluginMetricsService)
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("plugin_composite_key", _pluginCompositeKey),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Create meter with composite key + .Plugin suffix
        var meterName = $"{_pluginCompositeKey}.Plugin";
        _meter = new Meter(meterName);

        // Initialize Standardizer-specific instruments
        _dataStandardizationCounter = _meter.CreateCounter<long>(
            "plugin_data_standardization_total",
            "Total number of data standardization operations");

        _standardizationDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_standardization_duration_ms",
            "Duration of data standardization operations in milliseconds");

        _recordsProcessedCounter = _meter.CreateCounter<long>(
            "plugin_records_processed_total",
            "Total number of records processed");

        _recordsSuccessfulCounter = _meter.CreateCounter<long>(
            "plugin_records_successful_total",
            "Total number of successfully processed records");

        // Initialize exception instruments (same as PreFileReaderPluginMetricsService)
        _exceptionsCounter = _meter.CreateCounter<long>(
            "plugin_exceptions_total",
            "Total number of exceptions thrown by plugin");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "plugin_critical_exceptions_total",
            "Total number of critical exceptions that affect plugin operation");

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    public void RecordDataStandardization(int recordsProcessed, int recordsSuccessful, TimeSpan standardizationDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var standardizationLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("records_processed", recordsProcessed.ToString()),
            new("records_successful", recordsSuccessful.ToString()),
            new("standardization_duration_ms", standardizationDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + standardizationLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        standardizationLabels.CopyTo(tags, _baseLabels.Length);

        _dataStandardizationCounter.Add(1, tags);
        _standardizationDurationHistogram.Record(standardizationDuration.TotalMilliseconds, tags);
        _recordsProcessedCounter.Add(recordsProcessed, tags);
        _recordsSuccessfulCounter.Add(recordsSuccessful, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded data standardization - Processed: {RecordsProcessed}, Successful: {RecordsSuccessful}, Duration: {Duration}ms",
            recordsProcessed, recordsSuccessful, standardizationDuration.TotalMilliseconds);
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
