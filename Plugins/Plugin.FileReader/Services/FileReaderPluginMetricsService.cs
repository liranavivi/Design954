using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Plugin.FileReader.Interfaces;
using Shared.Correlation;

namespace Plugin.FileReader.Services;

/// <summary>
/// Implementation of metrics service for FileReader plugin operations.
/// Aligned with PreFileReaderPluginMetricsService architecture.
/// </summary>
public class FileReaderPluginMetricsService : IFileReaderPluginMetricsService
{
    private readonly string _pluginCompositeKey;
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // FileReader-specific instruments
    private readonly Counter<long> _fileReadCounter;
    private readonly Counter<long> _fileReadBytesCounter;
    private readonly Counter<long> _fileReadFailuresCounter;
    private readonly Histogram<double> _fileReadDurationHistogram;
    private readonly Counter<long> _contentProcessingCounter;
    private readonly Histogram<double> _contentProcessingDurationHistogram;
    private readonly Counter<long> _dataExtractionsCounter;
    private readonly Histogram<double> _extractionDurationHistogram;
    private readonly Counter<long> _recordsProcessedCounter;
    private readonly Counter<long> _recordsSuccessfulCounter;
    private readonly Gauge<long> _readingThroughputBytesGauge;
    private readonly Gauge<long> _readingThroughputFilesGauge;
    private readonly Gauge<long> _readingThroughputRecordsGauge;

    // Exception Metrics (same as PreFileReaderPluginMetricsService)
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public FileReaderPluginMetricsService(
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

        // Initialize FileReader-specific instruments
        _fileReadCounter = _meter.CreateCounter<long>(
            "plugin_file_reads_total",
            "Total number of file read operations");

        _fileReadBytesCounter = _meter.CreateCounter<long>(
            "plugin_file_read_bytes_total",
            "Total number of bytes read from files");

        _fileReadFailuresCounter = _meter.CreateCounter<long>(
            "plugin_file_read_failures_total",
            "Total number of failed file read operations");

        _fileReadDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_file_read_duration_ms",
            "Duration of file read operations in milliseconds");

        _contentProcessingCounter = _meter.CreateCounter<long>(
            "plugin_content_processing_total",
            "Total number of content processing operations");

        _contentProcessingDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_content_processing_duration_ms",
            "Duration of content processing operations in milliseconds");

        _dataExtractionsCounter = _meter.CreateCounter<long>(
            "plugin_data_extractions_total",
            "Total number of data extraction operations");

        _extractionDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_extraction_duration_ms",
            "Duration of data extraction operations in milliseconds");

        _recordsProcessedCounter = _meter.CreateCounter<long>(
            "plugin_records_processed_total",
            "Total number of records processed");

        _recordsSuccessfulCounter = _meter.CreateCounter<long>(
            "plugin_records_successful_total",
            "Total number of successfully processed records");

        _readingThroughputBytesGauge = _meter.CreateGauge<long>(
            "plugin_reading_throughput_bytes_per_second",
            "Current reading throughput in bytes per second");

        _readingThroughputFilesGauge = _meter.CreateGauge<long>(
            "plugin_reading_throughput_files_per_second",
            "Current reading throughput in files per second");

        _readingThroughputRecordsGauge = _meter.CreateGauge<long>(
            "plugin_reading_throughput_records_per_second",
            "Current reading throughput in records per second");

        // Initialize exception instruments (same as PreFileReaderPluginMetricsService)
        _exceptionsCounter = _meter.CreateCounter<long>(
            "plugin_exceptions_total",
            "Total number of exceptions thrown by plugin");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "plugin_critical_exceptions_total",
            "Total number of critical exceptions that affect plugin operation");

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    public void RecordFileRead(long bytesRead, string filePath, TimeSpan readDuration, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var readLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("file_type", fileType),
            new("file_path", filePath),
            new("bytes_read", bytesRead.ToString()),
            new("read_duration_ms", readDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + readLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        readLabels.CopyTo(tags, _baseLabels.Length);

        _fileReadCounter.Add(1, tags);
        _fileReadBytesCounter.Add(bytesRead, tags);
        _fileReadDurationHistogram.Record(readDuration.TotalMilliseconds, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded file read - File: {FilePath}, Type: {FileType}, Bytes: {BytesRead}, Duration: {Duration}ms",
            filePath, fileType, bytesRead, readDuration.TotalMilliseconds);
    }

    public void RecordFileReadFailure(string filePath, string failureReason, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var failureLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("file_type", fileType),
            new("file_path", filePath),
            new("failure_reason", failureReason)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + failureLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        failureLabels.CopyTo(tags, _baseLabels.Length);

        _fileReadFailuresCounter.Add(1, tags);

        _logger.LogWarningWithHierarchy(context,
            "Recorded file read failure - File: {FilePath}, Type: {FileType}, Reason: {FailureReason}",
            filePath, fileType, failureReason);
    }

    public void RecordContentProcessing(long contentSize, string contentType, TimeSpan processingDuration, int recordsExtracted,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var processingLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("content_type", contentType),
            new("content_size_bytes", contentSize.ToString()),
            new("processing_duration_ms", processingDuration.TotalMilliseconds.ToString("F2")),
            new("records_extracted", recordsExtracted.ToString())
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + processingLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        processingLabels.CopyTo(tags, _baseLabels.Length);

        _contentProcessingCounter.Add(1, tags);
        _contentProcessingDurationHistogram.Record(processingDuration.TotalMilliseconds, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded content processing - Type: {ContentType}, Size: {ContentSize} bytes, Records: {RecordsExtracted}, Duration: {Duration}ms",
            contentType, contentSize, recordsExtracted, processingDuration.TotalMilliseconds);
    }

    public void RecordDataExtraction(string extractionType, int recordsProcessed, int recordsSuccessful, TimeSpan extractionDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var extractionLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("extraction_type", extractionType),
            new("records_processed", recordsProcessed.ToString()),
            new("records_successful", recordsSuccessful.ToString())
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + extractionLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        extractionLabels.CopyTo(tags, _baseLabels.Length);

        _dataExtractionsCounter.Add(1, tags);
        _extractionDurationHistogram.Record(extractionDuration.TotalMilliseconds, tags);
        _recordsProcessedCounter.Add(recordsProcessed, tags);
        _recordsSuccessfulCounter.Add(recordsSuccessful, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded data extraction - Type: {ExtractionType}, Processed: {RecordsProcessed}, Successful: {RecordsSuccessful}, Duration: {Duration}ms",
            extractionType, recordsProcessed, recordsSuccessful, extractionDuration.TotalMilliseconds);
    }

    public void RecordReadingThroughput(long bytesPerSecond, long filesPerSecond, long recordsPerSecond,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var throughputLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("bytes_per_second", bytesPerSecond.ToString()),
            new("files_per_second", filesPerSecond.ToString()),
            new("records_per_second", recordsPerSecond.ToString())
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + throughputLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        throughputLabels.CopyTo(tags, _baseLabels.Length);

        _readingThroughputBytesGauge.Record(bytesPerSecond, tags);
        _readingThroughputFilesGauge.Record(filesPerSecond, tags);
        _readingThroughputRecordsGauge.Record(recordsPerSecond, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded reading throughput - Bytes/sec: {BytesPerSecond}, Files/sec: {FilesPerSecond}, Records/sec: {RecordsPerSecond}",
            bytesPerSecond, filesPerSecond, recordsPerSecond);
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
