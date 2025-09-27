using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Shared.Correlation;

namespace Plugin.FileWriter.Services;

/// <summary>
/// Implementation of metrics service for FileWriter plugin operations.
/// Aligned with FileReaderPluginMetricsService architecture.
/// </summary>
public class FileWriterPluginMetricsService : IFileWriterPluginMetricsService
{
    private readonly string _pluginCompositeKey;
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // FileWriter-specific instruments (aligned with FileReaderPlugin structure)
    private readonly Counter<long> _fileWriteCounter;
    private readonly Counter<long> _fileWriteFailureCounter;
    private readonly Counter<long> _contentProcessingCounter;
    private readonly Counter<long> _dataOutputCounter;
    private readonly Counter<long> _bytesWrittenCounter;
    private readonly Counter<long> _filesWrittenCounter;
    private readonly Counter<long> _recordsWrittenCounter;
    private readonly Histogram<double> _fileWriteDurationHistogram;
    private readonly Histogram<double> _contentProcessingDurationHistogram;
    private readonly Histogram<double> _dataOutputDurationHistogram;
    private readonly Gauge<long> _writingThroughputBytesGauge;
    private readonly Gauge<long> _writingThroughputFilesGauge;
    private readonly Gauge<long> _writingThroughputRecordsGauge;

    // Exception Metrics (same as FileReaderPluginMetricsService)
    private readonly Counter<long> _exceptionsCounter;
    private readonly Counter<long> _criticalExceptionsCounter;

    public FileWriterPluginMetricsService(
        string pluginCompositeKey,
        ILogger logger)
    {
        _pluginCompositeKey = pluginCompositeKey;
        _logger = logger;

        // Initialize base labels (same pattern as FileReaderPluginMetricsService)
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("plugin_composite_key", _pluginCompositeKey),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Create meter with composite key + .Plugin suffix
        var meterName = $"{_pluginCompositeKey}.Plugin";
        _meter = new Meter(meterName);

        // Initialize FileWriter-specific instruments (aligned with FileReaderPlugin structure)
        _fileWriteCounter = _meter.CreateCounter<long>(
            "plugin_file_write_total",
            "Total number of file write operations");

        _fileWriteFailureCounter = _meter.CreateCounter<long>(
            "plugin_file_write_failure_total",
            "Total number of file write failures");

        _contentProcessingCounter = _meter.CreateCounter<long>(
            "plugin_content_processing_total",
            "Total number of content processing operations");

        _dataOutputCounter = _meter.CreateCounter<long>(
            "plugin_data_output_total",
            "Total number of data output operations");

        _bytesWrittenCounter = _meter.CreateCounter<long>(
            "plugin_bytes_written_total",
            "Total number of bytes written");

        _filesWrittenCounter = _meter.CreateCounter<long>(
            "plugin_files_written_total",
            "Total number of files written");

        _recordsWrittenCounter = _meter.CreateCounter<long>(
            "plugin_records_written_total",
            "Total number of records written");

        _fileWriteDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_file_write_duration_ms",
            "Duration of file write operations in milliseconds");

        _contentProcessingDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_content_processing_duration_ms",
            "Duration of content processing operations in milliseconds");

        _dataOutputDurationHistogram = _meter.CreateHistogram<double>(
            "plugin_data_output_duration_ms",
            "Duration of data output operations in milliseconds");

        _writingThroughputBytesGauge = _meter.CreateGauge<long>(
            "plugin_writing_throughput_bytes_per_second",
            "Current writing throughput in bytes per second");

        _writingThroughputFilesGauge = _meter.CreateGauge<long>(
            "plugin_writing_throughput_files_per_second",
            "Current writing throughput in files per second");

        _writingThroughputRecordsGauge = _meter.CreateGauge<long>(
            "plugin_writing_throughput_records_per_second",
            "Current writing throughput in records per second");

        // Initialize exception instruments (same as FileReaderPluginMetricsService)
        _exceptionsCounter = _meter.CreateCounter<long>(
            "plugin_exceptions_total",
            "Total number of exceptions thrown by plugin");

        _criticalExceptionsCounter = _meter.CreateCounter<long>(
            "plugin_critical_exceptions_total",
            "Total number of critical exceptions that affect plugin operation");

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    public void RecordFileWrite(long bytesWritten, string filePath, TimeSpan writeDuration, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var fileWriteLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("file_path", filePath),
            new("file_type", fileType),
            new("bytes_written", bytesWritten.ToString()),
            new("write_duration_ms", writeDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + fileWriteLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        fileWriteLabels.CopyTo(tags, _baseLabels.Length);

        _fileWriteCounter.Add(1, tags);
        _bytesWrittenCounter.Add(bytesWritten, tags);
        _filesWrittenCounter.Add(1, tags);
        _fileWriteDurationHistogram.Record(writeDuration.TotalMilliseconds, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded file write - Path: {FilePath}, Type: {FileType}, Bytes: {Bytes}, Duration: {Duration}ms",
            filePath, fileType, bytesWritten, writeDuration.TotalMilliseconds);
    }

    public void RecordFileWriteFailure(string filePath, string failureReason, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var failureLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("file_path", filePath),
            new("failure_reason", failureReason),
            new("file_type", fileType)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + failureLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        failureLabels.CopyTo(tags, _baseLabels.Length);

        _fileWriteFailureCounter.Add(1, tags);

        _logger.LogWarningWithHierarchy(context,
            "Recorded file write failure - Path: {FilePath}, Type: {FileType}, Reason: {Reason}",
            filePath, fileType, failureReason);
    }

    public void RecordContentProcessing(long contentSize, string contentType, TimeSpan processingDuration, int filesWritten,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var contentLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("content_size", contentSize.ToString()),
            new("content_type", contentType),
            new("files_written", filesWritten.ToString()),
            new("processing_duration_ms", processingDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + contentLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        contentLabels.CopyTo(tags, _baseLabels.Length);

        _contentProcessingCounter.Add(1, tags);
        _contentProcessingDurationHistogram.Record(processingDuration.TotalMilliseconds, tags);
        _filesWrittenCounter.Add(filesWritten, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded content processing - Type: {ContentType}, Size: {Size}, Files: {Files}, Duration: {Duration}ms",
            contentType, contentSize, filesWritten, processingDuration.TotalMilliseconds);
    }

    public void RecordDataOutput(string outputType, int recordsProcessed, int recordsSuccessful, TimeSpan outputDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context)
    {
        var outputLabels = new KeyValuePair<string, object?>[]
        {
            new("correlation_id", correlationId),
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("output_type", outputType),
            new("records_processed", recordsProcessed.ToString()),
            new("records_successful", recordsSuccessful.ToString()),
            new("output_duration_ms", outputDuration.TotalMilliseconds.ToString("F2"))
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + outputLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        outputLabels.CopyTo(tags, _baseLabels.Length);

        _dataOutputCounter.Add(1, tags);
        _dataOutputDurationHistogram.Record(outputDuration.TotalMilliseconds, tags);
        _recordsWrittenCounter.Add(recordsSuccessful, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded data output - Type: {OutputType}, Processed: {Processed}, Successful: {Successful}, Duration: {Duration}ms",
            outputType, recordsProcessed, recordsSuccessful, outputDuration.TotalMilliseconds);
    }

    public void RecordWritingThroughput(long bytesPerSecond, long filesPerSecond, long recordsPerSecond,
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

        _writingThroughputBytesGauge.Record(bytesPerSecond, tags);
        _writingThroughputFilesGauge.Record(filesPerSecond, tags);
        _writingThroughputRecordsGauge.Record(recordsPerSecond, tags);

        _logger.LogInformationWithHierarchy(context,
            "Recorded writing throughput - Bytes/s: {BytesPerSecond}, Files/s: {FilesPerSecond}, Records/s: {RecordsPerSecond}",
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
