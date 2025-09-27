namespace Manager.OrchestratedFlow.Services;

/// <summary>
/// Interface for HTTP client to communicate with other managers
/// </summary>
public interface IManagerHttpClient
{
    /// <summary>
    /// Validate that a workflow exists in the Workflow Manager
    /// </summary>
    /// <param name="workflowId">The workflow ID to validate</param>
    /// <returns>True if workflow exists, false otherwise</returns>
    Task<bool> ValidateWorkflowExistsAsync(Guid workflowId);

    /// <summary>
    /// Validate that an assignment exists in the Assignment Manager
    /// </summary>
    /// <param name="assignmentId">The assignment ID to validate</param>
    /// <returns>True if assignment exists, false otherwise</returns>
    Task<bool> ValidateAssignmentExistsAsync(Guid assignmentId);

    /// <summary>
    /// Validate that multiple assignments exist in the Assignment Manager
    /// </summary>
    /// <param name="assignmentIds">The assignment IDs to validate</param>
    /// <returns>True if all assignments exist, false otherwise</returns>
    Task<bool> ValidateAssignmentsExistAsync(IEnumerable<Guid> assignmentIds);
}
