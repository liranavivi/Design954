using Shared.Correlation;

namespace Plugin.AudioConverter.Interfaces;

/// <summary>
/// Metrics service interface for AudioConverter plugin operations.
/// Provides OpenTelemetry-compatible metrics for monitoring plugin performance and health.
/// Aligned with StandardizerPluginMetricsService architecture.
/// </summary>
public interface IAudioConverterPluginMetricsService : IDisposable
{
    // ========================================
    // AUDIOCONVERTER-SPECIFIC METRICS
    // ========================================

    /// <summary>
    /// Records data conversion operation
    /// </summary>
    void RecordDataConversion(int recordsProcessed, int recordsSuccessful, TimeSpan conversionDuration,
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
