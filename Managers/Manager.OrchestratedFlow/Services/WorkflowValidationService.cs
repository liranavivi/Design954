using Shared.Correlation;

namespace Manager.OrchestratedFlow.Services;

/// <summary>
/// Service for validating references to Workflow entities
/// </summary>
public class WorkflowValidationService : IWorkflowValidationService
{
    private readonly IManagerHttpClient _managerHttpClient;
    private readonly ILogger<WorkflowValidationService> _logger;

    public WorkflowValidationService(
        IManagerHttpClient managerHttpClient,
        ILogger<WorkflowValidationService> logger)
    {
        _managerHttpClient = managerHttpClient;
        _logger = logger;
    }

    public async Task<bool> ValidateWorkflowExistsAsync(Guid workflowId)
    {
        _logger.LogInformationWithCorrelation("Delegating workflow existence validation to ManagerHttpClient. WorkflowId: {WorkflowId}", workflowId);

        try
        {
            return await _managerHttpClient.ValidateWorkflowExistsAsync(workflowId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error during workflow existence validation. WorkflowId: {WorkflowId}", workflowId);
            // Fail-safe: if any error occurs, assume workflow doesn't exist
            return false;
        }
    }
}
