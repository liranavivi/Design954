using Shared.Correlation;

namespace Plugin.Enricher.Interfaces;

/// <summary>
/// Metrics service interface for Enricher plugin operations.
/// Provides OpenTelemetry-compatible metrics for monitoring plugin performance and health.
/// Aligned with StandardizerPluginMetricsService architecture.
/// </summary>
public interface IEnricherPluginMetricsService : IDisposable
{
    // ========================================
    // ENRICHER-SPECIFIC METRICS
    // ========================================

    /// <summary>
    /// Records data enrichment operation
    /// </summary>
    void RecordDataEnrichment(int recordsProcessed, int recordsSuccessful, TimeSpan enrichmentDuration,
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
