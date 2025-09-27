using Shared.Correlation;

namespace Plugin.FileReader.Interfaces;

/// <summary>
/// Metrics service interface for FileReader plugin operations.
/// Provides OpenTelemetry-compatible metrics for monitoring plugin performance and health.
/// Aligned with PreFileReaderPluginMetricsService architecture.
/// </summary>
public interface IFileReaderPluginMetricsService : IDisposable
{
    // ========================================
    // FILEREADER-SPECIFIC METRICS
    // ========================================

    /// <summary>
    /// Records file reading operation results
    /// </summary>
    void RecordFileRead(long bytesRead, string filePath, TimeSpan readDuration, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records file reading failure
    /// </summary>
    void RecordFileReadFailure(string filePath, string failureReason, string fileType,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records content processing operation
    /// </summary>
    void RecordContentProcessing(long contentSize, string contentType, TimeSpan processingDuration, int recordsExtracted,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records data extraction operation
    /// </summary>
    void RecordDataExtraction(string extractionType, int recordsProcessed, int recordsSuccessful, TimeSpan extractionDuration,
        string correlationId, Guid orchestratedFlowId, Guid stepId, Guid executionId, HierarchicalLoggingContext context);

    /// <summary>
    /// Records reading throughput metrics
    /// </summary>
    void RecordReadingThroughput(long bytesPerSecond, long filesPerSecond, long recordsPerSecond,
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
