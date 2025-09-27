using Shared.Correlation;
using Shared.Services;

namespace Manager.Workflow.Services;

/// <summary>
/// HTTP client service for communicating with other managers for validation
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _stepManagerBaseUrl;
    private readonly string _orchestratedFlowManagerBaseUrl;

    public ManagerHttpClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ManagerHttpClient> logger)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration
        _stepManagerBaseUrl = configuration["ManagerUrls:Step"] ?? "http://localhost:5170";
        _orchestratedFlowManagerBaseUrl = configuration["ManagerUrls:OrchestratedFlow"] ?? "http://localhost:5140";
    }

    public async Task<bool> CheckStepExists(Guid stepId)
    {
        var url = $"{_stepManagerBaseUrl}/api/step/{stepId}/exists";
        return await ExecuteEntityCheckAsync(url, "StepExistenceCheck", stepId);
    }

    /// <summary>
    /// Check if any OrchestratedFlow entities reference the specified workflow ID
    /// </summary>
    /// <param name="workflowId">The workflow ID to check for references</param>
    /// <returns>True if any OrchestratedFlow entities reference the workflow, false otherwise</returns>
    public async Task<bool> CheckWorkflowReferencesAsync(Guid workflowId)
    {
        try
        {
            var url = $"{_orchestratedFlowManagerBaseUrl}/api/orchestratedflow/workflow/{workflowId}/exists";
            return await ExecuteEntityCheckAsync(url, "WorkflowReferenceCheck", workflowId);
        }
        catch (Exception ex)
        {
            // Fail-safe: if any error occurs, assume there are references to prevent deletion
            _logger.LogErrorWithCorrelation(ex, "Error validating workflow references - assuming references exist for safety. WorkflowId: {WorkflowId}", workflowId);
            return true;
        }
    }
}
