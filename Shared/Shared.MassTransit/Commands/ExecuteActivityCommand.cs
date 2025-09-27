using Shared.Models;

namespace Shared.MassTransit.Commands;

/// <summary>
/// Command to execute an activity in the processor
/// Enhanced with hierarchical logging support - maintains consistent ID ordering
/// </summary>
public class ExecuteActivityCommand
{
    // ✅ Consistent order: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId

    /// <summary>
    /// ID of the orchestrated flow entity (Layer 1)
    /// </summary>
    public Guid OrchestratedFlowId { get; set; }

    /// <summary>
    /// ID of the workflow (Layer 2) - Added for hierarchical logging
    /// </summary>
    public Guid WorkflowId { get; set; }

    /// <summary>
    /// Correlation ID for tracking (Layer 3) - defaults to Guid.Empty
    /// </summary>
    public Guid CorrelationId { get; set; } = Guid.Empty;

    /// <summary>
    /// ID of the step being executed (Layer 4)
    /// </summary>
    public Guid StepId { get; set; }

    /// <summary>
    /// ID of the processor that should handle this activity (Layer 5)
    /// </summary>
    public Guid ProcessorId { get; set; }

    /// <summary>
    /// Unique publish ID generated for each command publication (Layer 6) - Guid.Empty for entry points
    /// </summary>
    public Guid PublishId { get; set; } = Guid.Empty;

    /// <summary>
    /// Unique execution ID for this activity instance (Layer 6)
    /// </summary>
    public Guid ExecutionId { get; set; }

    // Supporting properties (not part of hierarchy)

    /// <summary>
    /// Collection of assignment models to process
    /// </summary>
    public List<AssignmentModel> Entities { get; set; } = new();

    /// <summary>
    /// Timestamp when the command was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional timeout for the activity execution
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Priority of the activity (higher numbers = higher priority)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Additional metadata for the activity
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}



/// <summary>
/// Command to get statistics for a processor
/// </summary>
public class GetStatisticsCommand
{
    /// <summary>
    /// ID of the processor to get statistics for
    /// </summary>
    public Guid ProcessorId { get; set; }

    /// <summary>
    /// Request ID for tracking
    /// </summary>
    public Guid RequestId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Start date for statistics period (null for all time)
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// End date for statistics period (null for current time)
    /// </summary>
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// Timestamp when the request was made
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Include detailed metrics breakdown
    /// </summary>
    public bool IncludeDetailedMetrics { get; set; } = false;
}
