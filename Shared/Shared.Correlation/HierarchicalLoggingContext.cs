namespace Shared.Correlation;

/// <summary>
/// Hierarchical logging context that maintains the 6-layer ID hierarchy for structured logging.
/// Provides consistent ordering: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId
/// </summary>
public class HierarchicalLoggingContext
{
    /// <summary>
    /// Layer 1: Orchestrated flow identifier (always present)
    /// </summary>
    public Guid OrchestratedFlowId { get; set; }

    /// <summary>
    /// Layer 2: Workflow identifier (present from orchestration level)
    /// </summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>
    /// Layer 3: Correlation identifier (always present for tracing)
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Layer 4: Step identifier (present from step execution level)
    /// </summary>
    public Guid? StepId { get; set; }

    /// <summary>
    /// Layer 5: Processor identifier (present from processor execution level)
    /// </summary>
    public Guid? ProcessorId { get; set; }

    /// <summary>
    /// Layer 6: Publish identifier (present from message publishing level)
    /// </summary>
    public Guid? PublishId { get; set; }

    /// <summary>
    /// Layer 6: Execution identifier (present from individual execution level)
    /// </summary>
    public Guid? ExecutionId { get; set; }

    /// <summary>
    /// Gets the current hierarchy layer based on available IDs
    /// </summary>
    public int Layer => CalculateLayer();

    /// <summary>
    /// Calculates the current layer based on which IDs are present
    /// Optimized with ternary operators for best performance with early exit
    /// </summary>
    private int CalculateLayer() =>
        ExecutionId.HasValue ? 6 :
        PublishId.HasValue ? 5 :
        ProcessorId.HasValue ? 4 :
        StepId.HasValue ? 3 :
        WorkflowId.HasValue ? 2 : 1;
}
