using Shared.MCP.Models;

namespace Shared.MCP.Interfaces;

/// <summary>
/// Interface for MCP server functionality
/// </summary>
public interface IMcpServer
{
    /// <summary>
    /// Processes an MCP request and returns a response
    /// </summary>
    /// <param name="request">The MCP request to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>MCP response</returns>
    Task<McpResponse> ProcessRequestAsync(McpRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles an MCP notification (no response expected)
    /// </summary>
    /// <param name="notification">The MCP notification to handle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleNotificationAsync(McpNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets server capabilities
    /// </summary>
    /// <returns>Server capabilities</returns>
    McpServerCapabilities GetCapabilities();
}

/// <summary>
/// Interface for MCP transport layer
/// </summary>
public interface IMcpTransport
{
    /// <summary>
    /// Starts the transport layer
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the transport layer
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a message is received
    /// </summary>
    event Func<string, Task>? MessageReceived;

    /// <summary>
    /// Sends a message through the transport
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// MCP server capabilities
/// </summary>
public class McpServerCapabilities
{
    /// <summary>
    /// Whether the server supports experimental features
    /// </summary>
    public bool Experimental { get; set; }

    /// <summary>
    /// Logging capabilities
    /// </summary>
    public McpLoggingCapabilities? Logging { get; set; }

    /// <summary>
    /// Prompts capabilities
    /// </summary>
    public McpPromptsCapabilities? Prompts { get; set; }

    /// <summary>
    /// Resources capabilities
    /// </summary>
    public McpResourcesCapabilities? Resources { get; set; }

    /// <summary>
    /// Tools capabilities
    /// </summary>
    public McpToolsCapabilities? Tools { get; set; }
}

/// <summary>
/// Logging capabilities
/// </summary>
public class McpLoggingCapabilities
{
    /// <summary>
    /// Whether logging is supported
    /// </summary>
    public bool Supported { get; set; } = true;
}

/// <summary>
/// Prompts capabilities
/// </summary>
public class McpPromptsCapabilities
{
    /// <summary>
    /// Whether list_changed notifications are supported
    /// </summary>
    public bool ListChanged { get; set; } = true;
}

/// <summary>
/// Resources capabilities
/// </summary>
public class McpResourcesCapabilities
{
    /// <summary>
    /// Whether subscribe/unsubscribe is supported
    /// </summary>
    public bool Subscribe { get; set; } = true;

    /// <summary>
    /// Whether list_changed notifications are supported
    /// </summary>
    public bool ListChanged { get; set; } = true;
}

/// <summary>
/// Tools capabilities
/// </summary>
public class McpToolsCapabilities
{
    /// <summary>
    /// Whether list_changed notifications are supported
    /// </summary>
    public bool ListChanged { get; set; } = true;
}
