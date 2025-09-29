using Microsoft.Extensions.Logging;
using Shared.MCP.Interfaces;
using Shared.MCP.Models;
using System.Text.Json;

namespace Shared.MCP.Server;

/// <summary>
/// Base implementation of MCP server functionality
/// </summary>
public class BaseMcpServer : IMcpServer
{
    private readonly ILogger<BaseMcpServer> _logger;
    private readonly IMcpResourceProvider? _resourceProvider;
    private readonly IMcpPromptProvider? _promptProvider;
    private readonly IMcpToolProvider? _toolProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    public BaseMcpServer(
        ILogger<BaseMcpServer> logger,
        IMcpResourceProvider? resourceProvider = null,
        IMcpPromptProvider? promptProvider = null,
        IMcpToolProvider? toolProvider = null)
    {
        _logger = logger;
        _resourceProvider = resourceProvider;
        _promptProvider = promptProvider;
        _toolProvider = toolProvider;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Processes an MCP request and returns a response
    /// </summary>
    public async Task<McpResponse> ProcessRequestAsync(McpRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing MCP request: {Method} with ID: {Id}", request.Method, request.Id);

        try
        {
            var result = request.Method switch
            {
                "initialize" => await HandleInitializeAsync(request, cancellationToken),
                "resources/list" => await HandleListResourcesAsync(request, cancellationToken),
                "resources/read" => await HandleReadResourceAsync(request, cancellationToken),
                "prompts/list" => await HandleListPromptsAsync(request, cancellationToken),
                "prompts/get" => await HandleGetPromptAsync(request, cancellationToken),
                "tools/list" => await HandleListToolsAsync(request, cancellationToken),
                "tools/call" => await HandleCallToolAsync(request, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown method: {request.Method}")
            };

            return new McpResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MCP request: {Method}", request.Method);
            
            return new McpResponse
            {
                Id = request.Id,
                Error = new McpError
                {
                    Code = McpErrorCodes.InternalError,
                    Message = ex.Message,
                    Data = ex.GetType().Name
                }
            };
        }
    }

    /// <summary>
    /// Handles an MCP notification (no response expected)
    /// </summary>
    public async Task HandleNotificationAsync(McpNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Handling MCP notification: {Method}", notification.Method);

        try
        {
            switch (notification.Method)
            {
                case "notifications/initialized":
                    await HandleInitializedNotificationAsync(notification, cancellationToken);
                    break;
                case "notifications/cancelled":
                    await HandleCancelledNotificationAsync(notification, cancellationToken);
                    break;
                default:
                    _logger.LogWarning("Unknown notification method: {Method}", notification.Method);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling MCP notification: {Method}", notification.Method);
        }
    }

    /// <summary>
    /// Gets server capabilities
    /// </summary>
    public McpServerCapabilities GetCapabilities()
    {
        return new McpServerCapabilities
        {
            Experimental = false,
            Logging = new McpLoggingCapabilities { Supported = true },
            Prompts = _promptProvider != null ? new McpPromptsCapabilities { ListChanged = true } : null,
            Resources = _resourceProvider != null ? new McpResourcesCapabilities { Subscribe = true, ListChanged = true } : null,
            Tools = _toolProvider != null ? new McpToolsCapabilities { ListChanged = true } : null
        };
    }

    private Task<object> HandleInitializeAsync(McpRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling initialize request");

        var result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = GetCapabilities(),
            serverInfo = new
            {
                name = "MCP.Schema",
                version = "1.0.0"
            }
        };

        return Task.FromResult<object>(result);
    }

    private async Task<object> HandleListResourcesAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (_resourceProvider == null)
            throw new InvalidOperationException("Resources not supported");

        var listRequest = DeserializeParams<ListResourcesRequest>(request.Params);
        return await _resourceProvider.ListResourcesAsync(listRequest, cancellationToken);
    }

    private async Task<object> HandleReadResourceAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (_resourceProvider == null)
            throw new InvalidOperationException("Resources not supported");

        var readRequest = DeserializeParams<ReadResourceRequest>(request.Params);
        return await _resourceProvider.ReadResourceAsync(readRequest, cancellationToken);
    }

    private async Task<object> HandleListPromptsAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (_promptProvider == null)
            throw new InvalidOperationException("Prompts not supported");

        var listRequest = DeserializeParams<ListPromptsRequest>(request.Params);
        return await _promptProvider.ListPromptsAsync(listRequest, cancellationToken);
    }

    private async Task<object> HandleGetPromptAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (_promptProvider == null)
            throw new InvalidOperationException("Prompts not supported");

        var getRequest = DeserializeParams<GetPromptRequest>(request.Params);
        return await _promptProvider.GetPromptAsync(getRequest, cancellationToken);
    }

    private async Task<object> HandleListToolsAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (_toolProvider == null)
            throw new InvalidOperationException("Tools not supported");

        var listRequest = DeserializeParams<ListToolsRequest>(request.Params);
        return await _toolProvider.ListToolsAsync(listRequest, cancellationToken);
    }

    private async Task<object> HandleCallToolAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (_toolProvider == null)
            throw new InvalidOperationException("Tools not supported");

        var callRequest = DeserializeParams<CallToolRequest>(request.Params);
        return await _toolProvider.CallToolAsync(callRequest, cancellationToken);
    }

    private Task HandleInitializedNotificationAsync(McpNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Client initialized notification received");
        return Task.CompletedTask;
    }

    private Task HandleCancelledNotificationAsync(McpNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Request cancelled notification received");
        return Task.CompletedTask;
    }

    private T DeserializeParams<T>(object? parameters) where T : new()
    {
        if (parameters == null)
            return new T();

        if (parameters is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), _jsonOptions) ?? new T();
        }

        var json = JsonSerializer.Serialize(parameters, _jsonOptions);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? new T();
    }
}
