using Shared.Services;

namespace Manager.Processor.Services;

/// <summary>
/// HTTP client for communication with other entity managers with resilience patterns
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _stepManagerBaseUrl;
    private readonly string _schemaManagerBaseUrl;

    public ManagerHttpClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ManagerHttpClient> logger)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration
        _stepManagerBaseUrl = configuration["ManagerUrls:Step"] ?? "http://localhost:5170";
        _schemaManagerBaseUrl = configuration["ManagerUrls:Schema"] ?? "http://localhost:5160";
    }

    public async Task<bool> CheckProcessorReferencesInSteps(Guid processorId)
    {
        var url = $"{_stepManagerBaseUrl}/api/step/processor/{processorId}/exists";
        return await ExecuteEntityCheckAsync(url, "CheckProcessorReferencesInSteps", processorId);
    }

    public async Task<bool> CheckSchemaExists(Guid schemaId)
    {
        var url = $"{_schemaManagerBaseUrl}/api/schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "CheckSchemaExists", schemaId);
    }
}
