using Shared.Correlation;
using Shared.Services;

namespace Manager.Assignment.Services;

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

    public async Task<bool> CheckEntityExists(Guid entityId)
    {
        // For now, we'll implement a basic validation that checks if the GUID is not empty
        // In a full implementation, this would route to the appropriate manager based on entity type
        // This could be enhanced to check Address, Delivery, Processor, etc. managers

        _logger.LogDebugWithCorrelation("Basic entity existence check for EntityId: {EntityId}", entityId);

        // For demonstration purposes, we'll consider any non-empty GUID as "existing"
        // In reality, this would need to determine the entity type and call the appropriate manager
        if (entityId == Guid.Empty)
        {
            _logger.LogWarningWithCorrelation("Entity existence check failed - empty GUID. EntityId: {EntityId}", entityId);
            return false;
        }

        // Simulate async operation for consistency
        await Task.Delay(1);

        // Simulate a basic check - in reality this would call the appropriate manager
        // For now, we'll assume entities exist if they're not empty GUIDs
        _logger.LogDebugWithCorrelation("Entity existence check passed (basic validation). EntityId: {EntityId}", entityId);
        return true;
    }

    /// <summary>
    /// Check if any OrchestratedFlow entities reference the specified assignment ID
    /// </summary>
    /// <param name="assignmentId">The assignment ID to check for references</param>
    /// <returns>True if any OrchestratedFlow entities reference the assignment, false otherwise</returns>
    public async Task<bool> CheckAssignmentReferencesAsync(Guid assignmentId)
    {
        try
        {
            var url = $"{_orchestratedFlowManagerBaseUrl}/api/orchestratedflow/assignment/{assignmentId}/exists";
            return await ExecuteEntityCheckAsync(url, "AssignmentReferenceCheck", assignmentId);
        }
        catch (InvalidOperationException)
        {
            // Fail-safe: if service is unavailable, assume there are references for data integrity
            _logger.LogWarningWithCorrelation("Assignment reference validation service unavailable - assuming references exist for data integrity. AssignmentId: {AssignmentId}", assignmentId);
            return true;
        }
    }
}
