using Shared.Correlation;

namespace Plugin.FileWriter.Services;

/// <summary>
/// Metrics service interface for FileWriter plugin operations.
/// Provides OpenTelemetry-compatible metrics for monitoring plugin performance and health.
/// Aligned with FileReaderPluginMetricsService architecture.
/// </summary>
public interface IFileWriterPluginMetricsService : IDisposable
{
    // ========================================
    // FILEWRITER-SPECIFIC METRICS
    // ========================================

    /// <summary>
    /// Records file writing operation results
    /// </summary>
    void RecordFileWrite(long bytesWritten, string filePath, TimeSpan writeDuration, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records file writing failure
    /// </summary>
    void RecordFileWriteFailure(string filePath, string failureReason, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records content processing operation
    /// </summary>
    void RecordContentProcessing(long contentSize, string contentType, TimeSpan processingDuration, int filesWritten,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records data output operation
    /// </summary>
    void RecordDataOutput(string outputType, int recordsProcessed, int recordsSuccessful, TimeSpan outputDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records writing throughput metrics
    /// </summary>
    void RecordWritingThroughput(long bytesPerSecond, long filesPerSecond, long recordsPerSecond,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    // ========================================
    // EXCEPTION METRICS
    // ========================================

    /// <summary>
    /// Records plugin exception with severity level
    /// </summary>
    void RecordPluginException(string exceptionType, string severity,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);
}
