using Microsoft.Extensions.Logging;
using Shared.MCP.Interfaces;
using Shared.MCP.Models;

namespace MCP.Schema.Services;

/// <summary>
/// Provides MCP prompts for schema management operations
/// </summary>
public class SchemaPromptProvider : IMcpPromptProvider
{
    private readonly ISchemaManagerClient _schemaClient;
    private readonly ILogger<SchemaPromptProvider> _logger;

    public SchemaPromptProvider(ISchemaManagerClient schemaClient, ILogger<SchemaPromptProvider> logger)
    {
        _schemaClient = schemaClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists all available schema prompts
    /// </summary>
    public Task<ListPromptsResponse> ListPromptsAsync(ListPromptsRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing schema prompts");

        var prompts = new List<McpPrompt>
        {
            new McpPrompt
            {
                Name = "analyze-schema",
                Description = "Analyze a schema definition for structure, patterns, and potential issues",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "schema_id", Description = "ID of the schema to analyze", Required = false },
                    new McpPromptArgument { Name = "composite_key", Description = "Composite key (version_name) of the schema to analyze", Required = false },
                    new McpPromptArgument { Name = "definition", Description = "Schema definition to analyze directly", Required = false }
                }
            },
            new McpPrompt
            {
                Name = "compare-schemas",
                Description = "Compare two schema versions and identify differences and breaking changes",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "old_schema", Description = "ID or composite key of the old schema version", Required = true },
                    new McpPromptArgument { Name = "new_schema", Description = "ID or composite key of the new schema version", Required = true }
                }
            },
            new McpPrompt
            {
                Name = "schema-evolution-plan",
                Description = "Generate a plan for evolving a schema while maintaining backward compatibility",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "current_schema", Description = "ID or composite key of the current schema", Required = true },
                    new McpPromptArgument { Name = "requirements", Description = "New requirements or changes needed", Required = true }
                }
            },
            new McpPrompt
            {
                Name = "schema-documentation",
                Description = "Generate comprehensive documentation for a schema",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "schema_id", Description = "ID of the schema to document", Required = false },
                    new McpPromptArgument { Name = "composite_key", Description = "Composite key of the schema to document", Required = false },
                    new McpPromptArgument { Name = "include_examples", Description = "Whether to include usage examples", Required = false }
                }
            },
            new McpPrompt
            {
                Name = "schema-validation-guide",
                Description = "Provide guidance on validating data against a schema",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "schema_id", Description = "ID of the schema", Required = false },
                    new McpPromptArgument { Name = "composite_key", Description = "Composite key of the schema", Required = false },
                    new McpPromptArgument { Name = "data_sample", Description = "Sample data to validate", Required = false }
                }
            }
        };

        _logger.LogDebug("Listed {Count} schema prompts", prompts.Count);

        return Task.FromResult(new ListPromptsResponse
        {
            Prompts = prompts
        });
    }

    /// <summary>
    /// Gets a specific prompt with arguments applied
    /// </summary>
    public async Task<GetPromptResponse> GetPromptAsync(GetPromptRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting schema prompt: {Name}", request.Name);

            var messages = new List<McpPromptMessage>();

            switch (request.Name)
            {
                case "analyze-schema":
                    messages = await GenerateAnalyzeSchemaPrompt(request.Arguments, cancellationToken);
                    break;
                case "compare-schemas":
                    messages = await GenerateCompareSchemasPrompt(request.Arguments, cancellationToken);
                    break;
                case "schema-evolution-plan":
                    messages = await GenerateEvolutionPlanPrompt(request.Arguments, cancellationToken);
                    break;
                case "schema-documentation":
                    messages = await GenerateDocumentationPrompt(request.Arguments, cancellationToken);
                    break;
                case "schema-validation-guide":
                    messages = await GenerateValidationGuidePrompt(request.Arguments, cancellationToken);
                    break;
                default:
                    throw new ArgumentException($"Unknown prompt: {request.Name}");
            }

            return new GetPromptResponse
            {
                Description = $"Generated prompt for {request.Name}",
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schema prompt: {Name}", request.Name);
            throw;
        }
    }

    /// <summary>
    /// Checks if a prompt exists
    /// </summary>
    public Task<bool> PromptExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var validPrompts = new[] { "analyze-schema", "compare-schemas", "schema-evolution-plan", "schema-documentation", "schema-validation-guide" };
        return Task.FromResult(validPrompts.Contains(name));
    }

    private async Task<List<McpPromptMessage>> GenerateAnalyzeSchemaPrompt(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var schema = await GetSchemaFromArguments(arguments, cancellationToken);
        
        var systemMessage = new McpPromptMessage
        {
            Role = "system",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = "You are a schema analysis expert. Analyze the provided schema definition for structure, patterns, potential issues, and best practices."
            }
        };

        var userMessage = new McpPromptMessage
        {
            Role = "user",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = schema != null 
                    ? $"Please analyze this schema:\n\nName: {schema.Name}\nVersion: {schema.Version}\nType: {schema.SchemaType}\n\nDefinition:\n{schema.Definition}"
                    : "Please analyze the schema definition provided in the arguments."
            }
        };

        return new List<McpPromptMessage> { systemMessage, userMessage };
    }

    private async Task<List<McpPromptMessage>> GenerateCompareSchemasPrompt(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var oldSchemaKey = arguments?.GetValueOrDefault("old_schema")?.ToString();
        var newSchemaKey = arguments?.GetValueOrDefault("new_schema")?.ToString();

        var oldSchema = await GetSchemaByKeyOrId(oldSchemaKey, cancellationToken);
        var newSchema = await GetSchemaByKeyOrId(newSchemaKey, cancellationToken);

        var systemMessage = new McpPromptMessage
        {
            Role = "system",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = "You are a schema comparison expert. Compare two schema versions and identify differences, breaking changes, and migration considerations."
            }
        };

        var userMessage = new McpPromptMessage
        {
            Role = "user",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = $"Please compare these two schemas:\n\nOLD SCHEMA:\nName: {oldSchema?.Name}\nVersion: {oldSchema?.Version}\nDefinition:\n{oldSchema?.Definition}\n\nNEW SCHEMA:\nName: {newSchema?.Name}\nVersion: {newSchema?.Version}\nDefinition:\n{newSchema?.Definition}"
            }
        };

        return new List<McpPromptMessage> { systemMessage, userMessage };
    }

    private async Task<List<McpPromptMessage>> GenerateEvolutionPlanPrompt(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var currentSchemaKey = arguments?.GetValueOrDefault("current_schema")?.ToString();
        var requirements = arguments?.GetValueOrDefault("requirements")?.ToString();

        var currentSchema = await GetSchemaByKeyOrId(currentSchemaKey, cancellationToken);

        var systemMessage = new McpPromptMessage
        {
            Role = "system",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = "You are a schema evolution expert. Create a plan for evolving a schema while maintaining backward compatibility and following best practices."
            }
        };

        var userMessage = new McpPromptMessage
        {
            Role = "user",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = $"Create an evolution plan for this schema:\n\nCURRENT SCHEMA:\nName: {currentSchema?.Name}\nVersion: {currentSchema?.Version}\nDefinition:\n{currentSchema?.Definition}\n\nNEW REQUIREMENTS:\n{requirements}"
            }
        };

        return new List<McpPromptMessage> { systemMessage, userMessage };
    }

    private async Task<List<McpPromptMessage>> GenerateDocumentationPrompt(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var schema = await GetSchemaFromArguments(arguments, cancellationToken);
        var includeExamples = arguments?.GetValueOrDefault("include_examples")?.ToString()?.ToLower() == "true";

        var systemMessage = new McpPromptMessage
        {
            Role = "system",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = "You are a technical documentation expert. Generate comprehensive, clear documentation for the provided schema."
            }
        };

        var userMessage = new McpPromptMessage
        {
            Role = "user",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = $"Generate documentation for this schema{(includeExamples ? " (include usage examples)" : "")}:\n\nName: {schema?.Name}\nVersion: {schema?.Version}\nType: {schema?.SchemaType}\nDescription: {schema?.Description}\n\nDefinition:\n{schema?.Definition}"
            }
        };

        return new List<McpPromptMessage> { systemMessage, userMessage };
    }

    private async Task<List<McpPromptMessage>> GenerateValidationGuidePrompt(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var schema = await GetSchemaFromArguments(arguments, cancellationToken);
        var dataSample = arguments?.GetValueOrDefault("data_sample")?.ToString();

        var systemMessage = new McpPromptMessage
        {
            Role = "system",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = "You are a data validation expert. Provide guidance on validating data against the provided schema."
            }
        };

        var userText = $"Provide validation guidance for this schema:\n\nName: {schema?.Name}\nVersion: {schema?.Version}\nDefinition:\n{schema?.Definition}";
        
        if (!string.IsNullOrEmpty(dataSample))
        {
            userText += $"\n\nSample data to validate:\n{dataSample}";
        }

        var userMessage = new McpPromptMessage
        {
            Role = "user",
            Content = new McpPromptContent
            {
                Type = "text",
                Text = userText
            }
        };

        return new List<McpPromptMessage> { systemMessage, userMessage };
    }

    private async Task<SchemaEntityDto?> GetSchemaFromArguments(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        if (arguments == null) return null;

        var schemaId = arguments.GetValueOrDefault("schema_id")?.ToString();
        var compositeKey = arguments.GetValueOrDefault("composite_key")?.ToString();

        if (!string.IsNullOrEmpty(schemaId) && Guid.TryParse(schemaId, out var id))
        {
            return await _schemaClient.GetSchemaByIdAsync(id, cancellationToken);
        }

        if (!string.IsNullOrEmpty(compositeKey))
        {
            return await _schemaClient.GetSchemaByCompositeKeyAsync(compositeKey, cancellationToken);
        }

        return null;
    }

    private async Task<SchemaEntityDto?> GetSchemaByKeyOrId(string? keyOrId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(keyOrId)) return null;

        if (Guid.TryParse(keyOrId, out var id))
        {
            return await _schemaClient.GetSchemaByIdAsync(id, cancellationToken);
        }

        return await _schemaClient.GetSchemaByCompositeKeyAsync(keyOrId, cancellationToken);
    }
}
