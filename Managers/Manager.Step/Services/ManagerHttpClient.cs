using Shared.Services;

namespace Manager.Step.Services;

/// <summary>
/// HTTP client for communication with other entity managers with resilience patterns
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _assignmentManagerBaseUrl;
    private readonly string _workflowManagerBaseUrl;
    private readonly string _processorManagerBaseUrl;

    public ManagerHttpClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ManagerHttpClient> logger)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration
        _assignmentManagerBaseUrl = configuration["ManagerUrls:Assignment"] ?? "http://localhost:5130";
        _workflowManagerBaseUrl = configuration["ManagerUrls:Workflow"] ?? "http://localhost:5180";
        _processorManagerBaseUrl = configuration["ManagerUrls:Processor"] ?? "http://localhost:5110";
    }

    public async Task<bool> CheckAssignmentStepReferences(Guid stepId)
    {
        var url = $"{_assignmentManagerBaseUrl}/api/assignment/step/{stepId}/exists";
        return await ExecuteEntityCheckAsync(url, "AssignmentStepCheck", stepId);
    }

    public async Task<bool> CheckWorkflowStepReferences(Guid stepId)
    {
        var url = $"{_workflowManagerBaseUrl}/api/workflow/step/{stepId}/exists";
        return await ExecuteEntityCheckAsync(url, "WorkflowStepCheck", stepId);
    }

    public async Task<bool> CheckProcessorExists(Guid processorId)
    {
        var url = $"{_processorManagerBaseUrl}/api/processor/{processorId}/exists";
        return await ExecuteEntityCheckAsync(url, "ProcessorExistenceCheck", processorId);
    }
}
