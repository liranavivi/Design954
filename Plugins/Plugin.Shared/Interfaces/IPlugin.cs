using Shared.Models;

namespace Plugin.Shared.Interfaces;

/// <summary>
/// Interface for plugin implementations that can be dynamically loaded and executed
/// by the PluginLoaderProcessor. Plugin must implement this interface to be compatible
/// with the plugin loading system.
/// Enhanced with hierarchical logging support - maintains consistent ID ordering
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Processes activity data with hierarchical logging support
    /// This method will be called by the PluginLoaderProcessor to execute the plugin's business logic
    /// </summary>
    /// <param name="orchestratedFlowId">ID of the orchestrated flow entity (Layer 1)</param>
    /// <param name="workflowId">ID of the workflow (Layer 2)</param>
    /// <param name="correlationId">Correlation ID for tracking (Layer 3)</param>
    /// <param name="stepId">ID of the step being executed (Layer 4)</param>
    /// <param name="processorId">ID of the processor executing the activity (Layer 5)</param>
    /// <param name="publishId">Unique publish ID for this activity instance (Layer 6)</param>
    /// <param name="executionId">Unique execution ID for this activity instance (Layer 6)</param>
    /// <param name="entities">Collection of assignment entities to process</param>
    /// <param name="inputData">Deserialized input data from previous processor (JsonElement if JSON, null if empty)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Collection of processed activity data that will be validated against OutputSchema and saved to cache</returns>
    Task<IEnumerable<ProcessedActivityData>> ProcessActivityDataAsync(
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
        object? inputData,
        CancellationToken cancellationToken = default);
}
