using Shared.Correlation;

namespace Manager.Assignment.Services;

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

    public async Task<bool> CheckAssignmentReferencesAsync(Guid assignmentId)
    {
        _logger.LogInformationWithCorrelation("Delegating assignment reference validation to ManagerHttpClient. AssignmentId: {AssignmentId}", assignmentId);

        try
        {
            return await _managerHttpClient.CheckAssignmentReferencesAsync(assignmentId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error during assignment reference validation. AssignmentId: {AssignmentId}", assignmentId);
            // Fail-safe: if any error occurs, assume there are references
            return true;
        }
    }
}
