namespace Manager.Workflow.Services;

/// <summary>
/// HTTP client interface for communicating with other managers for validation
/// </summary>
public interface IManagerHttpClient
{
    /// <summary>
    /// Check if a step exists in the Step Manager
    /// </summary>
    /// <param name="stepId">The step ID to check</param>
    /// <returns>True if step exists, false otherwise</returns>
    Task<bool> CheckStepExists(Guid stepId);

    /// <summary>
    /// Check if any OrchestratedFlow entities reference the specified workflow ID
    /// </summary>
    /// <param name="workflowId">The workflow ID to check for references</param>
    /// <returns>True if any OrchestratedFlow entities reference the workflow, false otherwise</returns>
    Task<bool> CheckWorkflowReferencesAsync(Guid workflowId);
}
