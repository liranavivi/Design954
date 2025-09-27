using Shared.Correlation;

namespace Plugin.Standardizer.Interfaces;

/// <summary>
/// Metrics service interface for Standardizer plugin operations.
/// Provides OpenTelemetry-compatible metrics for monitoring plugin performance and health.
/// Aligned with PreFileReaderPluginMetricsService architecture.
/// </summary>
public interface IStandardizerPluginMetricsService : IDisposable
{
    // ========================================
    // STANDARDIZER-SPECIFIC METRICS
    // ========================================

    /// <summary>
    /// Records data standardization operation
    /// </summary>
    void RecordDataStandardization(int recordsProcessed, int recordsSuccessful, TimeSpan standardizationDuration,
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
