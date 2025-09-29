using System.Text.Json.Serialization;

namespace Shared.MCP.Models;

/// <summary>
/// Base class for all MCP JSON-RPC messages
/// </summary>
public abstract class McpMessage
{
    /// <summary>
    /// JSON-RPC version (always "2.0")
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
}

/// <summary>
/// MCP JSON-RPC request message
/// </summary>
public class McpRequest : McpMessage
{
    /// <summary>
    /// Unique identifier for the request
    /// </summary>
    [JsonPropertyName("id")]
    public object? Id { get; set; }

    /// <summary>
    /// Method name to invoke
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Parameters for the method
    /// </summary>
    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

/// <summary>
/// MCP JSON-RPC response message
/// </summary>
public class McpResponse : McpMessage
{
    /// <summary>
    /// Request identifier this response corresponds to
    /// </summary>
    [JsonPropertyName("id")]
    public object? Id { get; set; }

    /// <summary>
    /// Result data (present on success)
    /// </summary>
    [JsonPropertyName("result")]
    public object? Result { get; set; }

    /// <summary>
    /// Error information (present on failure)
    /// </summary>
    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

/// <summary>
/// MCP JSON-RPC notification message (no response expected)
/// </summary>
public class McpNotification : McpMessage
{
    /// <summary>
    /// Method name
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Parameters for the method
    /// </summary>
    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

/// <summary>
/// MCP error information
/// </summary>
public class McpError
{
    /// <summary>
    /// Error code
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Error message
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional error data
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// Standard MCP error codes
/// </summary>
public static class McpErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServerError = -32000;
}
