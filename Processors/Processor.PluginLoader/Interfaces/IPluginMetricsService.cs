using Shared.Correlation;

namespace Processor.PluginLoader.Interfaces
{
    /// <summary>
    /// Interface for plugin metrics collection and reporting.
    /// Provides standardized metrics collection for plugins with flow context awareness.
    /// </summary>
    public interface IPluginMetricsService : IDisposable
    {
        /// <summary>
        /// Records file discovery operation results.
        /// Tracks the number of files found during directory scanning operations.
        /// </summary>
        /// <param name="filesFound">Number of files discovered</param>
        /// <param name="directoryPath">Path of the directory scanned</param>
        /// <param name="scanDuration">Time taken to complete the discovery operation</param>
        /// <param name="correlationId">Request correlation identifier</param>
        /// <param name="orchestratedFlowId">Flow entity identifier</param>
        /// <param name="stepId">Step identifier within the flow</param>
        /// <param name="executionId">Execution identifier</param>
        /// <param name="processorId">Processor identifier running the plugin</param>
        /// <param name="context">Hierarchical logging context</param>
        void RecordFileDiscovery(long filesFound, string directoryPath, TimeSpan scanDuration,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context);

        /// <summary>
        /// Records directory scanning operation success/failure.
        /// Tracks the operational success of directory access and scanning attempts.
        /// </summary>
        /// <param name="success">Whether the directory scan operation succeeded</param>
        /// <param name="directoryPath">Path of the directory being scanned</param>
        /// <param name="duration">Time taken for the scan operation</param>
        /// <param name="correlationId">Request correlation identifier</param>
        /// <param name="orchestratedFlowId">Flow entity identifier</param>
        /// <param name="stepId">Step identifier within the flow</param>
        /// <param name="executionId">Execution identifier</param>
        /// <param name="processorId">Processor identifier running the plugin</param>
        /// <param name="context">Hierarchical logging context</param>
        void RecordDirectoryScan(bool success, string directoryPath, TimeSpan duration,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context);

        /// <summary>
        /// Records processing throughput metrics.
        /// Tracks the rate of file and data processing performance.
        /// </summary>
        /// <param name="filesPerSecond">Number of files processed per second</param>
        /// <param name="bytesPerSecond">Number of bytes processed per second</param>
        /// <param name="correlationId">Request correlation identifier</param>
        /// <param name="orchestratedFlowId">Flow entity identifier</param>
        /// <param name="stepId">Step identifier within the flow</param>
        /// <param name="executionId">Execution identifier</param>
        /// <param name="processorId">Processor identifier running the plugin</param>
        /// <param name="context">Hierarchical logging context</param>
        void RecordProcessingThroughput(long filesPerSecond, long bytesPerSecond,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context);

        /// <summary>
        /// Records file size distribution metrics.
        /// Tracks the distribution of file sizes being processed for capacity planning.
        /// </summary>
        /// <param name="fileSize">Size of the file in bytes</param>
        /// <param name="sizeCategory">Category classification (e.g., "small", "medium", "large")</param>
        /// <param name="correlationId">Request correlation identifier</param>
        /// <param name="orchestratedFlowId">Flow entity identifier</param>
        /// <param name="stepId">Step identifier within the flow</param>
        /// <param name="executionId">Execution identifier</param>
        /// <param name="processorId">Processor identifier running the plugin</param>
        /// <param name="context">Hierarchical logging context</param>
        void RecordFileSizeDistribution(long fileSize, string sizeCategory,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context);

        /// <summary>
        /// Records plugin execution operations.
        /// Tracks general plugin operation success, duration, and operation types.
        /// </summary>
        /// <param name="success">Whether the plugin execution succeeded</param>
        /// <param name="duration">Time taken for the execution</param>
        /// <param name="operationType">Type of operation performed</param>
        /// <param name="correlationId">Request correlation identifier</param>
        /// <param name="orchestratedFlowId">Flow entity identifier</param>
        /// <param name="stepId">Step identifier within the flow</param>
        /// <param name="executionId">Execution identifier</param>
        /// <param name="processorId">Processor identifier running the plugin</param>
        /// <param name="context">Hierarchical logging context</param>
        void RecordPluginExecution(bool success, TimeSpan duration, string operationType,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context);

        /// <summary>
        /// Records plugin exceptions and errors.
        /// Tracks exception types and severity levels for plugin error monitoring.
        /// </summary>
        /// <param name="exceptionType">Type of exception that occurred</param>
        /// <param name="severity">Severity level ("critical", "error", "warning")</param>
        /// <param name="correlationId">Request correlation identifier</param>
        /// <param name="orchestratedFlowId">Flow entity identifier</param>
        /// <param name="stepId">Step identifier within the flow</param>
        /// <param name="executionId">Execution identifier</param>
        /// <param name="processorId">Processor identifier running the plugin</param>
        /// <param name="context">Hierarchical logging context</param>
        void RecordPluginException(string exceptionType, string severity,
            Guid correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, string processorId, HierarchicalLoggingContext context);
    }
}
