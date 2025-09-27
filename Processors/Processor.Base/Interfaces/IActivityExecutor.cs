using Processor.Base.Models;
using Shared.Models;

namespace Processor.Base.Interfaces;

/// <summary>
/// Interface for the abstract activity execution logic that concrete processors must implement
/// Enhanced with hierarchical logging support - maintains consistent ID ordering
/// </summary>
public interface IActivityExecutor
{
    /// <summary>
    /// Executes an activity with the provided parameters
    /// This method must be implemented by concrete processor applications
    /// Enhanced with hierarchical logging support - maintains consistent ID ordering
    /// </summary>
    /// <param name="orchestratedFlowId">ID of the orchestrated flow entity (Layer 1)</param>
    /// <param name="workflowId">ID of the workflow (Layer 2)</param>
    /// <param name="correlationId">Correlation ID for tracking (Layer 3)</param>
    /// <param name="stepId">ID of the step being executed (Layer 4)</param>
    /// <param name="processorId">ID of the processor executing the activity (Layer 5)</param>
    /// <param name="publishId">Unique publish ID for this activity instance (Layer 6)</param>
    /// <param name="executionId">Unique execution ID for this activity instance (Layer 6)</param>
    /// <param name="entities">Collection of base entities to process</param>
    /// <param name="inputData">Input data retrieved from cache (raw string that will be deserialized by base class)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Collection of result data that will be validated against OutputSchema and saved to cache</returns>
    Task<IEnumerable<ActivityExecutionResult>> ExecuteActivityAsync(
        // ✅ Consistent order: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId
        Guid orchestratedFlowId,
        Guid workflowId,
        Guid correlationId,
        Guid stepId,
        Guid processorId,
        Guid publishId,
        Guid executionId,

        // Supporting parameters
        List<AssignmentModel> entities,
        string inputData,
        CancellationToken cancellationToken = default);
}
