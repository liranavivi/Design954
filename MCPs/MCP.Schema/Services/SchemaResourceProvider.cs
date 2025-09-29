using Microsoft.Extensions.Logging;
using Shared.MCP.Interfaces;
using Shared.MCP.Models;

namespace MCP.Schema.Services;

/// <summary>
/// Provides MCP resources for schema management
/// </summary>
public class SchemaResourceProvider : IMcpResourceProvider
{
    private readonly ISchemaManagerClient _schemaClient;
    private readonly ILogger<SchemaResourceProvider> _logger;

    public SchemaResourceProvider(ISchemaManagerClient schemaClient, ILogger<SchemaResourceProvider> logger)
    {
        _schemaClient = schemaClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists all available schema resources
    /// </summary>
    public async Task<ListResourcesResponse> ListResourcesAsync(ListResourcesRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing schema resources");

            var schemas = await _schemaClient.GetAllSchemasAsync(cancellationToken);
            var resources = new List<McpResource>();

            // Add individual schema resources
            foreach (var schema in schemas)
            {
                resources.Add(new McpResource
                {
                    Uri = $"schema://{schema.CompositeKey}",
                    Name = $"Schema: {schema.Name} (v{schema.Version})",
                    Description = schema.Description ?? $"Schema definition for {schema.Name}",
                    MimeType = "application/json",
                    Annotations = new Dictionary<string, object>
                    {
                        ["id"] = schema.Id,
                        ["version"] = schema.Version,
                        ["name"] = schema.Name,
                        ["schemaType"] = schema.SchemaType ?? "unknown",
                        ["createdAt"] = schema.CreatedAt,
                        ["updatedAt"] = schema.UpdatedAt
                    }
                });
            }

            // Add collection resources
            resources.Add(new McpResource
            {
                Uri = "schema://collection/all",
                Name = "All Schemas",
                Description = "Complete collection of all schema definitions",
                MimeType = "application/json",
                Annotations = new Dictionary<string, object>
                {
                    ["count"] = schemas.Count,
                    ["type"] = "collection"
                }
            });

            // Group by version
            var versionGroups = schemas.GroupBy(s => s.Version).ToList();
            foreach (var versionGroup in versionGroups)
            {
                resources.Add(new McpResource
                {
                    Uri = $"schema://collection/version/{versionGroup.Key}",
                    Name = $"Schemas v{versionGroup.Key}",
                    Description = $"All schemas for version {versionGroup.Key}",
                    MimeType = "application/json",
                    Annotations = new Dictionary<string, object>
                    {
                        ["version"] = versionGroup.Key,
                        ["count"] = versionGroup.Count(),
                        ["type"] = "version-collection"
                    }
                });
            }

            _logger.LogDebug("Listed {Count} schema resources", resources.Count);

            return new ListResourcesResponse
            {
                Resources = resources
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing schema resources");
            throw;
        }
    }

    /// <summary>
    /// Reads the content of a specific schema resource
    /// </summary>
    public async Task<ReadResourceResponse> ReadResourceAsync(ReadResourceRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Reading schema resource: {Uri}", request.Uri);

            var uri = new Uri(request.Uri);
            var contents = new List<McpResourceContent>();

            if (uri.Scheme != "schema")
            {
                throw new ArgumentException($"Unsupported URI scheme: {uri.Scheme}");
            }

            var path = uri.Host + uri.AbsolutePath;

            if (path.StartsWith("collection/all"))
            {
                // Return all schemas
                var schemas = await _schemaClient.GetAllSchemasAsync(cancellationToken);
                contents.Add(new McpResourceContent
                {
                    Uri = request.Uri,
                    MimeType = "application/json",
                    Text = System.Text.Json.JsonSerializer.Serialize(schemas, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                });
            }
            else if (path.StartsWith("collection/version/"))
            {
                // Return schemas for specific version
                var version = path.Substring("collection/version/".Length);
                var schemas = await _schemaClient.GetSchemasByVersionAsync(version, cancellationToken);
                contents.Add(new McpResourceContent
                {
                    Uri = request.Uri,
                    MimeType = "application/json",
                    Text = System.Text.Json.JsonSerializer.Serialize(schemas, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                });
            }
            else
            {
                // Individual schema by composite key
                var compositeKey = uri.Host;
                var schema = await _schemaClient.GetSchemaByCompositeKeyAsync(compositeKey, cancellationToken);
                
                if (schema == null)
                {
                    throw new FileNotFoundException($"Schema not found: {compositeKey}");
                }

                contents.Add(new McpResourceContent
                {
                    Uri = request.Uri,
                    MimeType = "application/json",
                    Text = schema.Definition ?? System.Text.Json.JsonSerializer.Serialize(schema, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                });
            }

            _logger.LogDebug("Read schema resource: {Uri} with {Count} content items", request.Uri, contents.Count);

            return new ReadResourceResponse
            {
                Contents = contents
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading schema resource: {Uri}", request.Uri);
            throw;
        }
    }

    /// <summary>
    /// Checks if a schema resource exists
    /// </summary>
    public async Task<bool> ResourceExistsAsync(string uri, CancellationToken cancellationToken = default)
    {
        try
        {
            var uriObj = new Uri(uri);
            
            if (uriObj.Scheme != "schema")
            {
                return false;
            }

            var path = uriObj.Host + uriObj.AbsolutePath;

            if (path.StartsWith("collection/"))
            {
                return true; // Collection resources always exist
            }

            // Individual schema by composite key
            var compositeKey = uriObj.Host;
            var schema = await _schemaClient.GetSchemaByCompositeKeyAsync(compositeKey, cancellationToken);
            return schema != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if schema resource exists: {Uri}", uri);
            return false;
        }
    }
}
