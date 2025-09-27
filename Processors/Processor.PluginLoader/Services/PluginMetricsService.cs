using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Processor.PluginLoader.Interfaces;
using Shared.Correlation;

namespace Processor.PluginLoader.Services
{
    /// <summary>
    /// Plugin metrics service for collecting and reporting plugin performance and operational metrics.
    /// Aligned with ProcessorFlowMetricsService and ProcessorHealthMetricsService architecture.
    /// </summary>
    public class PluginMetricsService : IPluginMetricsService
    {
        private readonly string _pluginCompositeKey;
        private readonly ILogger<PluginMetricsService> _logger;
        private readonly Meter _meter;
        private readonly KeyValuePair<string, object?>[] _baseLabels;

        // PreFileReader-specific metric instruments
        private readonly Counter<long> _fileDiscoveryCounter;
        private readonly Counter<long> _directoryScanCounter;
        private readonly Histogram<double> _directoryScanDurationHistogram;
        private readonly Gauge<long> _processingThroughputFilesGauge;
        private readonly Gauge<long> _processingThroughputBytesGauge;
        private readonly Counter<long> _fileSizeDistributionCounter;
        private readonly Histogram<double> _fileSizeHistogram;

        // Base plugin metric instruments
        private readonly Counter<long> _pluginExecutionCounter;
        private readonly Counter<long> _pluginExecutionSuccessfulCounter;
        private readonly Counter<long> _pluginExecutionFailedCounter;
        private readonly Histogram<double> _pluginExecutionDurationHistogram;
        private readonly Counter<long> _pluginExceptionsCounter;
        private readonly Counter<long> _pluginCriticalExceptionsCounter;

        public PluginMetricsService(
            string pluginCompositeKey,
            ILogger<PluginMetricsService> logger)
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

            // Initialize PreFileReader-specific metrics instruments
            _fileDiscoveryCounter = _meter.CreateCounter<long>(
                "plugin_file_discovery_total",
                "Total number of file discovery operations performed by the plugin");

            _directoryScanCounter = _meter.CreateCounter<long>(
                "plugin_directory_scan_total",
                "Total number of directory scan operations performed by the plugin");

            _directoryScanDurationHistogram = _meter.CreateHistogram<double>(
                "plugin_directory_scan_duration_ms",
                "Duration of directory scan operations in milliseconds");

            _processingThroughputFilesGauge = _meter.CreateGauge<long>(
                "plugin_processing_throughput_files_per_second",
                "Current processing throughput in files per second");

            _processingThroughputBytesGauge = _meter.CreateGauge<long>(
                "plugin_processing_throughput_bytes_per_second",
                "Current processing throughput in bytes per second");

            _fileSizeDistributionCounter = _meter.CreateCounter<long>(
                "plugin_file_size_distribution_total",
                "Distribution of file sizes processed by the plugin");

            _fileSizeHistogram = _meter.CreateHistogram<double>(
                "plugin_file_size_bytes",
                "Size distribution of files processed by the plugin in bytes");

            // Initialize base plugin metrics instruments
            _pluginExecutionCounter = _meter.CreateCounter<long>(
                "plugin_executions_total",
                "Total number of plugin executions");

            _pluginExecutionSuccessfulCounter = _meter.CreateCounter<long>(
                "plugin_executions_successful_total",
                "Total number of successful plugin executions");

            _pluginExecutionFailedCounter = _meter.CreateCounter<long>(
                "plugin_executions_failed_total",
                "Total number of failed plugin executions");

            _pluginExecutionDurationHistogram = _meter.CreateHistogram<double>(
                "plugin_execution_duration_ms",
                "Duration of plugin executions in milliseconds");

            _pluginExceptionsCounter = _meter.CreateCounter<long>(
                "plugin_exceptions_total",
                "Total number of exceptions thrown by the plugin");

            _pluginCriticalExceptionsCounter = _meter.CreateCounter<long>(
                "plugin_critical_exceptions_total",
                "Total number of critical exceptions that affect plugin operation");

            // Note: Constructor logging will be updated when hierarchical context is available during initialization
        }

        public void RecordFileDiscovery(long filesFound, string directoryPath, TimeSpan scanDuration,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context)
        {
            var discoveryLabels = new KeyValuePair<string, object?>[]
            {
                new("correlation_id", correlationId),
                new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
                new("step_id", stepId.ToString()),
                new("execution_id", executionId.ToString()),
                new("processor_id", processorId),
                new("files_found", filesFound.ToString()),
                new("directory_path", directoryPath),
                new("scan_duration_ms", scanDuration.TotalMilliseconds.ToString("F2"))
            };

            var tags = new KeyValuePair<string, object?>[_baseLabels.Length + discoveryLabels.Length];
            _baseLabels.CopyTo(tags, 0);
            discoveryLabels.CopyTo(tags, _baseLabels.Length);

            _fileDiscoveryCounter.Add(1, tags);

            _logger.LogInformationWithHierarchy(context,
                "File discovery recorded - Files Found: {FilesFound}, Directory: {DirectoryPath}, Duration: {Duration}ms",
                filesFound, directoryPath, scanDuration.TotalMilliseconds);
        }

        public void RecordDirectoryScan(bool success, string directoryPath, TimeSpan duration,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context)
        {
            var scanLabels = new KeyValuePair<string, object?>[]
            {
                new("correlation_id", correlationId),
                new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
                new("step_id", stepId.ToString()),
                new("execution_id", executionId.ToString()),
                new("processor_id", processorId),
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
                "Directory scan recorded - Success: {Success}, Directory: {DirectoryPath}, Duration: {Duration}ms",
                success, directoryPath, duration.TotalMilliseconds);
        }

        public void RecordProcessingThroughput(long filesPerSecond, long bytesPerSecond,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context)
        {
            var throughputLabels = new KeyValuePair<string, object?>[]
            {
                new("correlation_id", correlationId),
                new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
                new("step_id", stepId.ToString()),
                new("execution_id", executionId.ToString()),
                new("processor_id", processorId),
                new("files_per_second", filesPerSecond.ToString()),
                new("bytes_per_second", bytesPerSecond.ToString())
            };

            var tags = new KeyValuePair<string, object?>[_baseLabels.Length + throughputLabels.Length];
            _baseLabels.CopyTo(tags, 0);
            throughputLabels.CopyTo(tags, _baseLabels.Length);

            _processingThroughputFilesGauge.Record(filesPerSecond, tags);
            _processingThroughputBytesGauge.Record(bytesPerSecond, tags);

            _logger.LogInformationWithHierarchy(context,
                "Processing throughput recorded - Files/sec: {FilesPerSecond}, Bytes/sec: {BytesPerSecond}",
                filesPerSecond, bytesPerSecond);
        }

        public void RecordFileSizeDistribution(long fileSize, string sizeCategory,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context)
        {
            var sizeLabels = new KeyValuePair<string, object?>[]
            {
                new("correlation_id", correlationId),
                new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
                new("step_id", stepId.ToString()),
                new("execution_id", executionId.ToString()),
                new("processor_id", processorId),
                new("file_size_bytes", fileSize.ToString()),
                new("size_category", sizeCategory)
            };

            var tags = new KeyValuePair<string, object?>[_baseLabels.Length + sizeLabels.Length];
            _baseLabels.CopyTo(tags, 0);
            sizeLabels.CopyTo(tags, _baseLabels.Length);

            _fileSizeDistributionCounter.Add(1, tags);
            _fileSizeHistogram.Record(fileSize, tags);

            _logger.LogInformationWithHierarchy(context,
                "File size distribution recorded - Size: {FileSize} bytes, Category: {SizeCategory}",
                fileSize, sizeCategory);
        }

        public void RecordPluginExecution(bool success, TimeSpan duration, string operationType,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context)
        {
            var executionLabels = new KeyValuePair<string, object?>[]
            {
                new("correlation_id", correlationId),
                new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
                new("step_id", stepId.ToString()),
                new("execution_id", executionId.ToString()),
                new("processor_id", processorId),
                new("operation_type", operationType),
                new("success", success.ToString()),
                new("duration_ms", duration.TotalMilliseconds.ToString("F2"))
            };

            var tags = new KeyValuePair<string, object?>[_baseLabels.Length + executionLabels.Length];
            _baseLabels.CopyTo(tags, 0);
            executionLabels.CopyTo(tags, _baseLabels.Length);

            _pluginExecutionCounter.Add(1, tags);
            _pluginExecutionDurationHistogram.Record(duration.TotalMilliseconds, tags);

            if (success)
                _pluginExecutionSuccessfulCounter.Add(1, tags);
            else
                _pluginExecutionFailedCounter.Add(1, tags);

            _logger.LogInformationWithHierarchy(context,
                "Plugin execution recorded - Success: {Success}, Operation: {OperationType}, Duration: {Duration}ms",
                success, operationType, duration.TotalMilliseconds);
        }

        public void RecordPluginException(string exceptionType, string severity,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context)
        {
            var exceptionLabels = new KeyValuePair<string, object?>[]
            {
                new("correlation_id", correlationId),
                new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
                new("step_id", stepId.ToString()),
                new("execution_id", executionId.ToString()),
                new("processor_id", processorId),
                new("exception_type", exceptionType),
                new("severity", severity)
            };

            var tags = new KeyValuePair<string, object?>[_baseLabels.Length + exceptionLabels.Length];
            _baseLabels.CopyTo(tags, 0);
            exceptionLabels.CopyTo(tags, _baseLabels.Length);

            _pluginExceptionsCounter.Add(1, tags);

            if (severity == "critical")
            {
                _pluginCriticalExceptionsCounter.Add(1, tags);
            }

            _logger.LogWarningWithHierarchy(context,
                "Plugin exception recorded - Type: {ExceptionType}, Severity: {Severity}",
                exceptionType, severity);
        }

        public void Dispose()
        {
            _meter?.Dispose();
        }
    }
}
