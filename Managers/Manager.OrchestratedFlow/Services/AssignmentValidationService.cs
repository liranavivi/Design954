using Shared.Correlation;

namespace Manager.OrchestratedFlow.Services;

/// <summary>
/// Service for validating references to Assignment entities
/// </summary>
public class AssignmentValidationService : IAssignmentValidationService
{
    private readonly IManagerHttpClient _managerHttpClient;
    private readonly ILogger<AssignmentValidationService> _logger;

    public AssignmentValidationService(
        IManagerHttpClient managerHttpClient,
        ILogger<AssignmentValidationService> logger)
    {
        _managerHttpClient = managerHttpClient;
        _logger = logger;
    }

    public async Task<bool> ValidateAssignmentExistsAsync(Guid assignmentId)
    {
        _logger.LogInformationWithCorrelation("Delegating assignment existence validation to ManagerHttpClient. AssignmentId: {AssignmentId}", assignmentId);

        try
        {
            return await _managerHttpClient.ValidateAssignmentExistsAsync(assignmentId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error during assignment existence validation. AssignmentId: {AssignmentId}", assignmentId);
            // Fail-safe: if any error occurs, assume assignment doesn't exist
            return false;
        }
    }

    public async Task<bool> ValidateAssignmentsExistAsync(IEnumerable<Guid> assignmentIds)
    {
        _logger.LogInformationWithCorrelation("Delegating batch assignment existence validation to ManagerHttpClient. AssignmentIds: {AssignmentIds}",
            assignmentIds != null ? string.Join(",", assignmentIds) : "null");

        try
        {
            return await _managerHttpClient.ValidateAssignmentsExistAsync(assignmentIds ?? Enumerable.Empty<Guid>());
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error during batch assignment existence validation. AssignmentIds: {AssignmentIds}",
                assignmentIds != null ? string.Join(",", assignmentIds) : "null");
            // Fail-safe: if any error occurs, assume assignments don't exist
            return false;
        }
    }
}
