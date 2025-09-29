using Shared.MCP.Models;

namespace Shared.MCP.Interfaces;

/// <summary>
/// Interface for providing MCP resources
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>
    /// Lists all available resources
    /// </summary>
    /// <param name="request">List resources request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available resources</returns>
    Task<ListResourcesResponse> ListResourcesAsync(ListResourcesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the content of a specific resource
    /// </summary>
    /// <param name="request">Read resource request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource content</returns>
    Task<ReadResourceResponse> ReadResourceAsync(ReadResourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a resource exists
    /// </summary>
    /// <param name="uri">Resource URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if resource exists</returns>
    Task<bool> ResourceExistsAsync(string uri, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for providing MCP prompts
/// </summary>
public interface IMcpPromptProvider
{
    /// <summary>
    /// Lists all available prompts
    /// </summary>
    /// <param name="request">List prompts request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available prompts</returns>
    Task<ListPromptsResponse> ListPromptsAsync(ListPromptsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific prompt with arguments applied
    /// </summary>
    /// <param name="request">Get prompt request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Prompt content</returns>
    Task<GetPromptResponse> GetPromptAsync(GetPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a prompt exists
    /// </summary>
    /// <param name="name">Prompt name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if prompt exists</returns>
    Task<bool> PromptExistsAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for providing MCP tools
/// </summary>
public interface IMcpToolProvider
{
    /// <summary>
    /// Lists all available tools
    /// </summary>
    /// <param name="request">List tools request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available tools</returns>
    Task<ListToolsResponse> ListToolsAsync(ListToolsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls a specific tool
    /// </summary>
    /// <param name="request">Call tool request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<CallToolResponse> CallToolAsync(CallToolRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a tool exists
    /// </summary>
    /// <param name="name">Tool name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tool exists</returns>
    Task<bool> ToolExistsAsync(string name, CancellationToken cancellationToken = default);
}
