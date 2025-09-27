using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Plugin.AudioConverter.Interfaces;
using Shared.Correlation;

namespace Plugin.AudioConverter.Services;

/// <summary>
/// Implementation of metrics service for AudioConverter plugin operations.
/// Aligned with StandardizerPluginMetricsService architecture.
/// </summary>
public class AudioConverterPluginMetricsService : IAudioConverterPluginMetricsService
{
    private readonly string _pluginCompositeKey;
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // AudioConverter-specific instruments
    private readonly Counter<long> _dataConversionCounter;
    private readonly Histogram<double> _conversionDurationHistogram;
    private readonly Counter<long> _recordsProcessedCounter;
    private readonly Counter<long> _recordsSuccessfulCounter;

    // Exception Metrics (same as StandardizerPluginMetricsService)
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public AudioConverterPluginMetricsService(
        string pluginCompositeKey,
        ILogger logger)
    {
        _pluginCompositeKey = pluginCompositeKey;
        _logger = logger;

        // Initialize base labels (same pattern as StandardizerPluginMetricsService)
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("plugin_composite_key", _pluginCompositeKey),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Create meter with composite key + .Plugin suffix
        var meterName = $"{_pluginCompositeKey}.Plugin";
        _meter = new Meter(meterName);

        // Initialize AudioConverter-specific instruments
        _dataConversionCounter = _meter.CreateCounter<long>(
            "plugin_data_conversion_total",
            "Total number of data conversion operations");

        _conversionDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_conversion_duration_ms",
            "Duration of data conversion operations in milliseconds");

        _recordsProcessedCounter = _meter.CreateCounter<long>(
            "plugin_records_processed_total",
            "Total number of records processed");

        _recordsSuccessfulCounter = _meter.CreateCounter<long>(
            "plugin_records_successful_total",
            "Total number of successfully processed records");

        // Initialize exception instruments (same as StandardizerPluginMetricsService)
        _exceptionsCounter = _meter.CreateCounter<long>(
            "plugin_exceptions_total",
            "Total number of exceptions thrown by plugin");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "plugin_critical_exceptions_total",
            "Total number of critical exceptions that affect plugin operation");

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    public void RecordDataConversion(int recordsProcessed, int recordsSuccessful, TimeSpan conversionDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var conversionLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("records_processed", recordsProcessed.ToString()),
            new("records_successful", recordsSuccessful.ToString()),
            new("conversion_duration_ms", conversionDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + conversionLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        conversionLabels.CopyTo(tags, _baseLabels.Length);

        _dataConversionCounter.Add(1, tags);
        _conversionDurationHistogram.Record(conversionDuration.TotalMilliseconds, tags);
        _recordsProcessedCounter.Add(recordsProcessed, tags);
        _recordsSuccessfulCounter.Add(recordsSuccessful, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded data conversion - Processed: {RecordsProcessed}, Successful: {RecordsSuccessful}, Duration: {Duration}ms",
            recordsProcessed, recordsSuccessful, conversionDuration.TotalMilliseconds);
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
