using Shared.Correlation;
using Shared.Services;

namespace Manager.OrchestratedFlow.Services;

/// <summary>
/// HTTP client for communicating with other managers with resilience patterns
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _workflowManagerBaseUrl;
    private readonly string _assignmentManagerBaseUrl;

    public ManagerHttpClient(
        HttpClient httpClient,
        ILogger<ManagerHttpClient> logger,
        IConfiguration configuration)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration (lazy resolution)
        _workflowManagerBaseUrl = configuration["ManagerUrls:Workflow"] ?? "http://localhost:5180";
        _assignmentManagerBaseUrl = configuration["ManagerUrls:Assignment"] ?? "http://localhost:5130";
    }

    /// <summary>
    /// Validate that a workflow exists in the Workflow Manager
    /// </summary>
    /// <param name="workflowId">The workflow ID to validate</param>
    /// <returns>True if workflow exists, false otherwise</returns>
    public async Task<bool> ValidateWorkflowExistsAsync(Guid workflowId)
    {
        try
        {
            var url = $"{_workflowManagerBaseUrl}/api/workflow/{workflowId}";
            var response = await ExecuteHttpRequestAsync(url, "WorkflowExistenceCheck");

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformationWithCorrelation("Successfully validated workflow exists. WorkflowId: {WorkflowId}", workflowId);
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarningWithCorrelation("Workflow not found. WorkflowId: {WorkflowId}", workflowId);
                return false;
            }

            // For other non-success status codes, log and return false
            _logger.LogWarningWithCorrelation("Workflow validation returned non-success status. WorkflowId: {WorkflowId}, StatusCode: {StatusCode}",
                workflowId, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            // Fail-safe: if service is unavailable, assume workflow doesn't exist
            _logger.LogErrorWithCorrelation(ex, "Error validating workflow existence. WorkflowId: {WorkflowId}", workflowId);
            return false;
        }
    }

    /// <summary>
    /// Validate that an assignment exists in the Assignment Manager
    /// </summary>
    /// <param name="assignmentId">The assignment ID to validate</param>
    /// <returns>True if assignment exists, false otherwise</returns>
    public async Task<bool> ValidateAssignmentExistsAsync(Guid assignmentId)
    {
        try
        {
            var url = $"{_assignmentManagerBaseUrl}/api/assignment/{assignmentId}";
            var response = await ExecuteHttpRequestAsync(url, "AssignmentExistenceCheck");

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformationWithCorrelation("Successfully validated assignment exists. AssignmentId: {AssignmentId}", assignmentId);
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarningWithCorrelation("Assignment not found. AssignmentId: {AssignmentId}", assignmentId);
                return false;
            }

            // For other non-success status codes, log and return false
            _logger.LogWarningWithCorrelation("Assignment validation returned non-success status. AssignmentId: {AssignmentId}, StatusCode: {StatusCode}",
                assignmentId, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            // Fail-safe: if service is unavailable, assume assignment doesn't exist
            _logger.LogErrorWithCorrelation(ex, "Error validating assignment existence. AssignmentId: {AssignmentId}", assignmentId);
            return false;
        }
    }

    /// <summary>
    /// Validate that multiple assignments exist in the Assignment Manager
    /// </summary>
    /// <param name="assignmentIds">The assignment IDs to validate</param>
    /// <returns>True if all assignments exist, false otherwise</returns>
    public async Task<bool> ValidateAssignmentsExistAsync(IEnumerable<Guid> assignmentIds)
    {
        if (assignmentIds == null || !assignmentIds.Any())
        {
            _logger.LogInformationWithCorrelation("No assignment IDs provided for validation - returning true");
            return true;
        }

        var assignmentIdsList = assignmentIds.ToList();
        _logger.LogInformationWithCorrelation("Starting batch assignment existence validation. AssignmentIds: {AssignmentIds}", 
            string.Join(",", assignmentIdsList));

        // Validate all assignments in parallel for performance
        var validationTasks = assignmentIdsList.Select(ValidateAssignmentExistsAsync);
        var results = await Task.WhenAll(validationTasks);

        var allExist = results.All(exists => exists);
        
        _logger.LogInformationWithCorrelation("Completed batch assignment existence validation. AssignmentIds: {AssignmentIds}, AllExist: {AllExist}", 
            string.Join(",", assignmentIdsList), allExist);

        return allExist;
    }
}
