using System.Text.Json.Serialization;

namespace Shared.MCP.Models;

/// <summary>
/// Represents an MCP resource that can be accessed by clients
/// </summary>
public class McpResource
{
    /// <summary>
    /// Unique identifier for the resource
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the resource
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the resource contains
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// MIME type of the resource content
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Additional metadata about the resource
    /// </summary>
    [JsonPropertyName("annotations")]
    public Dictionary<string, object>? Annotations { get; set; }
}

/// <summary>
/// Content of an MCP resource
/// </summary>
public class McpResourceContent
{
    /// <summary>
    /// URI of the resource
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the content
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// The actual content (text or base64 encoded binary)
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// Base64 encoded binary content
    /// </summary>
    [JsonPropertyName("blob")]
    public string? Blob { get; set; }
}

/// <summary>
/// Request to list available resources
/// </summary>
public class ListResourcesRequest
{
    /// <summary>
    /// Optional cursor for pagination
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>
/// Response containing list of available resources
/// </summary>
public class ListResourcesResponse
{
    /// <summary>
    /// List of available resources
    /// </summary>
    [JsonPropertyName("resources")]
    public List<McpResource> Resources { get; set; } = new();

    /// <summary>
    /// Cursor for next page (if any)
    /// </summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Request to read a specific resource
/// </summary>
public class ReadResourceRequest
{
    /// <summary>
    /// URI of the resource to read
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Response containing resource content
/// </summary>
public class ReadResourceResponse
{
    /// <summary>
    /// List of resource contents (usually one, but can be multiple for compound resources)
    /// </summary>
    [JsonPropertyName("contents")]
    public List<McpResourceContent> Contents { get; set; } = new();
}
