namespace Manager.Orchestrator.Interfaces;

/// <summary>
/// Service interface for recording orchestrator flow metrics optimized for anomaly detection.
/// Follows the processor pattern with focused metrics: consume counter, publish counter, and anomaly detection.
/// Reduces metric volume while focusing on important operational issues.
/// </summary>
public interface IOrchestratorFlowMetricsService : IDisposable
{
    /// <summary>
    /// Records ExecuteActivityCommand consumption metrics
    /// </summary>
    /// <param name="success">Whether the command was consumed successfully</param>
    /// <param name="orchestratedFlowId">The orchestrated flow entity ID</param>
    /// <param name="stepId">The step ID</param>
    /// <param name="executionId">The execution ID</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordCommandConsumed(bool success, Guid orchestratedFlowId, Guid stepId, Guid executionId, Guid correlationId);

    /// <summary>
    /// Records activity event publishing metrics
    /// </summary>
    /// <param name="success">Whether the event was published successfully</param>
    /// <param name="orchestratedFlowId">The orchestrated flow entity ID</param>
    /// <param name="stepId">The step ID</param>
    /// <param name="executionId">The execution ID</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordEventPublished(bool success, Guid orchestratedFlowId, Guid stepId, Guid executionId, Guid correlationId);

    /// <summary>
    /// Records flow anomaly detection metrics
    /// </summary>
    /// <param name="consumedCount">Number of commands consumed</param>
    /// <param name="publishedCount">Number of events published</param>
    /// <param name="orchestratedFlowId">The orchestrated flow entity ID</param>
    /// <param name="correlationId">Request correlation identifier</param>
    void RecordFlowAnomaly(long consumedCount, long publishedCount, Guid orchestratedFlowId, Guid correlationId);
}
