using Shared.Correlation;

namespace Manager.Workflow.Services;

/// <summary>
/// Service for validating referential integrity with OrchestratedFlow entities
/// </summary>
public class OrchestratedFlowValidationService : IOrchestratedFlowValidationService
{
    private readonly IManagerHttpClient _managerHttpClient;
    private readonly ILogger<OrchestratedFlowValidationService> _logger;

    public OrchestratedFlowValidationService(
        IManagerHttpClient managerHttpClient,
        ILogger<OrchestratedFlowValidationService> logger)
    {
        _managerHttpClient = managerHttpClient;
        _logger = logger;
    }

    public async Task<bool> CheckWorkflowReferencesAsync(Guid workflowId)
    {
        _logger.LogInformationWithCorrelation("Delegating workflow reference validation to ManagerHttpClient. WorkflowId: {WorkflowId}", workflowId);

        try
        {
            return await _managerHttpClient.CheckWorkflowReferencesAsync(workflowId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error during workflow reference validation. WorkflowId: {WorkflowId}", workflowId);
            // Fail-safe: if any error occurs, assume there are references
            return true;
        }
    }
}
