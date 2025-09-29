using System.Text.Json.Serialization;

namespace Shared.MCP.Models;

/// <summary>
/// Represents an MCP tool that can be called by clients
/// </summary>
public class McpTool
{
    /// <summary>
    /// Unique identifier for the tool
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what the tool does
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// JSON schema defining the input parameters for this tool
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public object? InputSchema { get; set; }
}

/// <summary>
/// Request to list available tools
/// </summary>
public class ListToolsRequest
{
    /// <summary>
    /// Optional cursor for pagination
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>
/// Response containing list of available tools
/// </summary>
public class ListToolsResponse
{
    /// <summary>
    /// List of available tools
    /// </summary>
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = new();

    /// <summary>
    /// Cursor for next page (if any)
    /// </summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Request to call a specific tool
/// </summary>
public class CallToolRequest
{
    /// <summary>
    /// Name of the tool to call
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arguments to pass to the tool
    /// </summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Content returned by a tool call
/// </summary>
public class McpToolContent
{
    /// <summary>
    /// Type of content (text, image, etc.)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>
    /// Text content
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Response from calling a tool
/// </summary>
public class CallToolResponse
{
    /// <summary>
    /// List of content items returned by the tool
    /// </summary>
    [JsonPropertyName("content")]
    public List<McpToolContent> Content { get; set; } = new();

    /// <summary>
    /// Whether the tool call was successful
    /// </summary>
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}
