using Shared.Services;

namespace Manager.Address.Services;

/// <summary>
/// HTTP client for communicating with other entity managers with resilience patterns
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _assignmentManagerBaseUrl;
    private readonly string _schemaManagerBaseUrl;

    public ManagerHttpClient(HttpClient httpClient, IConfiguration configuration, ILogger<ManagerHttpClient> logger)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration
        _assignmentManagerBaseUrl = configuration["ManagerUrls:Assignment"] ?? "http://localhost:5130";
        _schemaManagerBaseUrl = configuration["ManagerUrls:Schema"] ?? "http://localhost:5160";
    }

    public async Task<bool> CheckEntityReferencesInAssignments(Guid entityId)
    {
        var url = $"{_assignmentManagerBaseUrl}/api/assignment/entity/{entityId}/exists";
        return await ExecuteEntityCheckAsync(url, "AssignmentEntityCheck", entityId);
    }

    public async Task<bool> CheckSchemaExists(Guid schemaId)
    {
        var url = $"{_schemaManagerBaseUrl}/api/schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "SchemaExistenceCheck", schemaId);
    }
}
