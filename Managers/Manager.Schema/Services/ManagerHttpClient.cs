using Shared.Services;

namespace Manager.Schema.Services;

/// <summary>
/// HTTP client for communicating with other entity managers with resilience patterns
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _addressManagerBaseUrl;
    private readonly string _deliveryManagerBaseUrl;
    private readonly string _processorManagerBaseUrl;
    private readonly string _pluginManagerBaseUrl;

    public ManagerHttpClient(HttpClient httpClient, IConfiguration configuration, ILogger<ManagerHttpClient> logger)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration
        _addressManagerBaseUrl = configuration["ManagerUrls:Address"] ?? "http://localhost:5120";
        _deliveryManagerBaseUrl = configuration["ManagerUrls:Delivery"] ?? "http://localhost:5150";
        _processorManagerBaseUrl = configuration["ManagerUrls:Processor"] ?? "http://localhost:5110";
        _pluginManagerBaseUrl = configuration["ManagerUrls:Plugin"] ?? "http://localhost:5190";
    }

    public async Task<bool> CheckAddressSchemaReferences(Guid schemaId)
    {
        var url = $"{_addressManagerBaseUrl}/api/address/schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "AddressSchemaCheck", schemaId);
    }

    public async Task<bool> CheckDeliverySchemaReferences(Guid schemaId)
    {
        var url = $"{_deliveryManagerBaseUrl}/api/delivery/schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "DeliverySchemaCheck", schemaId);
    }

    public async Task<bool> CheckProcessorInputSchemaReferences(Guid schemaId)
    {
        var url = $"{_processorManagerBaseUrl}/api/processor/input-schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "ProcessorInputSchemaCheck", schemaId);
    }

    public async Task<bool> CheckProcessorOutputSchemaReferences(Guid schemaId)
    {
        var url = $"{_processorManagerBaseUrl}/api/processor/output-schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "ProcessorOutputSchemaCheck", schemaId);
    }

    public async Task<bool> CheckPluginInputReferencesAsync(Guid schemaId)
    {
        var url = $"{_pluginManagerBaseUrl}/api/plugin/input-schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "PluginInputSchemaCheck", schemaId);
    }

    public async Task<bool> CheckPluginOutputReferencesAsync(Guid schemaId)
    {
        var url = $"{_pluginManagerBaseUrl}/api/plugin/output-schema/{schemaId}/exists";
        return await ExecuteEntityCheckAsync(url, "PluginOutputSchemaCheck", schemaId);
    }
}
