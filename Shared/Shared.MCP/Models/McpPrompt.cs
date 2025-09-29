using System.Text.Json.Serialization;

namespace Shared.MCP.Models;

/// <summary>
/// Represents an MCP prompt template
/// </summary>
public class McpPrompt
{
    /// <summary>
    /// Unique identifier for the prompt
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the prompt
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// List of arguments this prompt accepts
    /// </summary>
    [JsonPropertyName("arguments")]
    public List<McpPromptArgument>? Arguments { get; set; }
}

/// <summary>
/// Argument definition for a prompt
/// </summary>
public class McpPromptArgument
{
    /// <summary>
    /// Name of the argument
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the argument
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this argument is required
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>
/// Message content in a prompt
/// </summary>
public class McpPromptMessage
{
    /// <summary>
    /// Role of the message sender (user, assistant, system)
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Content of the message
    /// </summary>
    [JsonPropertyName("content")]
    public McpPromptContent Content { get; set; } = new();
}

/// <summary>
/// Content within a prompt message
/// </summary>
public class McpPromptContent
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
/// Request to list available prompts
/// </summary>
public class ListPromptsRequest
{
    /// <summary>
    /// Optional cursor for pagination
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>
/// Response containing list of available prompts
/// </summary>
public class ListPromptsResponse
{
    /// <summary>
    /// List of available prompts
    /// </summary>
    [JsonPropertyName("prompts")]
    public List<McpPrompt> Prompts { get; set; } = new();

    /// <summary>
    /// Cursor for next page (if any)
    /// </summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Request to get a specific prompt
/// </summary>
public class GetPromptRequest
{
    /// <summary>
    /// Name of the prompt to get
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arguments to pass to the prompt
    /// </summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Response containing prompt content
/// </summary>
public class GetPromptResponse
{
    /// <summary>
    /// Description of the prompt
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// List of messages that make up the prompt
    /// </summary>
    [JsonPropertyName("messages")]
    public List<McpPromptMessage> Messages { get; set; } = new();
}
