using System.Diagnostics;
using System.Text.Json;
using Json.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Correlation;
using Shared.Services.Interfaces;
using Shared.Services.Models;

namespace Shared.Services;

/// <summary>
/// JSON schema validator implementation using JsonSchema.Net with full JSON Schema compliance
/// </summary>
public class SchemaValidator : ISchemaValidator
{
    private readonly ILogger<SchemaValidator> _logger;
    private readonly SchemaValidationConfiguration _config;
    private readonly ActivitySource _activitySource;
    private readonly Dictionary<string, JsonSchema> _schemaCache;
    private readonly object _schemaCacheLock = new();

    public SchemaValidator(
        ILogger<SchemaValidator> logger,
        IOptions<SchemaValidationConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _activitySource = new ActivitySource("SchemaValidation");
        _schemaCache = new Dictionary<string, JsonSchema>();
    }

    public async Task<bool> ValidateAsync(string jsonData, string jsonSchema)
    {
        var result = await ValidateWithDetailsAsync(jsonData, jsonSchema);
        return result.IsValid;
    }

    /// <summary>
    /// Validates JSON data against a JSON schema using hierarchical context
    /// </summary>
    public async Task<bool> ValidateAsync(string jsonData, string jsonSchema, HierarchicalLoggingContext context)
    {
        var result = await ValidateWithDetailsAsync(jsonData, jsonSchema, context);
        return result.IsValid;
    }

    public async Task<SchemaValidationResult> ValidateWithDetailsAsync(string jsonData, string jsonSchema)
    {
        using var activity = _activitySource.StartActivity("ValidateSchema");
        var stopwatch = Stopwatch.StartNew();

        var result = new SchemaValidationResult();

        try
        {
            // Parse and cache schema
            var schema = await GetOrCreateSchemaAsync(jsonSchema);

            // Parse JSON data
            JsonDocument jsonDocument;
            try
            {
                jsonDocument = JsonDocument.Parse(jsonData);
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Invalid JSON format: {ex.Message}");
                result.ErrorPath = ex.Path;

                activity?.SetTag("validation.success", false);
                activity?.SetTag("validation.error_count", 1);
                activity?.SetTag("validation.error_path", ex.Path);

                if (_config.LogValidationErrors)
                {
                    _logger.LogErrorWithCorrelation(ex, "JSON parsing failed during validation");
                }

                return result;
            }

            // Perform full JSON Schema validation using JsonSchema.Net
            var validationErrors = new List<string>();
            var isValid = ValidateWithJsonSchemaNet(jsonDocument, schema, validationErrors, result);

            result.IsValid = isValid;
            result.Errors = validationErrors;

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            // Set telemetry tags
            activity?.SetTag("validation.success", isValid);
            activity?.SetTag("validation.error_count", validationErrors.Count);
            activity?.SetTag("validation.type", "json_schema");
            activity?.SetTag("validation.error_path", result.ErrorPath);

            // Log results based on configuration
            if (isValid)
            {
                _logger.LogDebugWithCorrelation("Schema validation passed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                if (_config.LogValidationErrors)
                {
                    _logger.LogErrorWithCorrelation("Schema validation failed. Errors: {Errors}. Duration: {Duration}ms",
                        string.Join("; ", validationErrors), stopwatch.ElapsedMilliseconds);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsValid = false;
            result.Errors.Add($"Validation exception: {ex.Message}");
            result.Duration = stopwatch.Elapsed;

            activity?.SetTag("validation.success", false);
            activity?.SetTag("validation.error_count", 1);
            activity?.SetTag("validation.exception", ex.GetType().Name);

            if (_config.LogValidationErrors)
            {
                _logger.LogErrorWithCorrelation(ex, "Exception during schema validation");
            }

            return result;
        }
    }

    /// <summary>
    /// Validates JSON data against a JSON schema and returns detailed validation results using hierarchical context
    /// </summary>
    public async Task<SchemaValidationResult> ValidateWithDetailsAsync(string jsonData, string jsonSchema, HierarchicalLoggingContext context)
    {
        using var activity = _activitySource.StartActivity("ValidateSchema");
        var stopwatch = Stopwatch.StartNew();

        var result = new SchemaValidationResult();

        try
        {
            // Parse and cache schema
            var schema = await GetOrCreateSchemaAsync(jsonSchema, context);

            // Parse JSON data
            JsonDocument jsonDocument;
            try
            {
                jsonDocument = JsonDocument.Parse(jsonData);
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Invalid JSON format: {ex.Message}");
                result.ErrorPath = ex.Path;

                activity?.SetTag("validation.success", false);
                activity?.SetTag("validation.error_count", 1);
                activity?.SetTag("validation.error_path", ex.Path);

                if (_config.LogValidationErrors)
                {
                    _logger.LogErrorWithHierarchy(context, ex, "JSON parsing failed during validation");
                }

                return result;
            }

            // Perform full JSON Schema validation using JsonSchema.Net
            var validationErrors = new List<string>();
            var isValid = ValidateWithJsonSchemaNet(jsonDocument, schema, validationErrors, result, context);

            result.IsValid = isValid;
            result.Errors = validationErrors;

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            // Set telemetry tags
            activity?.SetTag("validation.success", isValid);
            activity?.SetTag("validation.error_count", validationErrors.Count);
            activity?.SetTag("validation.type", "json_schema");
            activity?.SetTag("validation.error_path", result.ErrorPath);

            // Log results based on configuration
            if (isValid)
            {
                _logger.LogDebugWithHierarchy(context, "Schema validation passed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                if (_config.LogValidationErrors)
                {
                    _logger.LogErrorWithHierarchy(context, "Schema validation failed. Errors: {Errors}. Duration: {Duration}ms",
                        string.Join("; ", validationErrors), stopwatch.ElapsedMilliseconds);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsValid = false;
            result.Errors.Add($"Validation exception: {ex.Message}");
            result.Duration = stopwatch.Elapsed;

            activity?.SetTag("validation.success", false);
            activity?.SetTag("validation.error_count", 1);
            activity?.SetTag("validation.exception", ex.GetType().Name);

            if (_config.LogValidationErrors)
            {
                _logger.LogErrorWithHierarchy(context, ex, "Exception during schema validation");
            }

            return result;
        }
    }

    private async Task<JsonSchema> GetOrCreateSchemaAsync(string jsonSchema)
    {
        // Create a cache key based on schema content hash
        var schemaHash = jsonSchema.GetHashCode().ToString();

        lock (_schemaCacheLock)
        {
            if (_schemaCache.TryGetValue(schemaHash, out var cachedSchema))
            {
                return cachedSchema;
            }
        }

        // Parse schema (this is CPU-bound, so we'll run it on a background thread)
        var schema = await Task.Run(() =>
        {
            try
            {
                return JsonSchema.FromText(jsonSchema);
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(ex, "Failed to parse JSON schema");
                throw new InvalidOperationException($"Invalid JSON schema: {ex.Message}", ex);
            }
        });

        // Cache the parsed schema
        lock (_schemaCacheLock)
        {
            if (!_schemaCache.ContainsKey(schemaHash))
            {
                _schemaCache[schemaHash] = schema;
            }
        }

        return schema;
    }

    private async Task<JsonSchema> GetOrCreateSchemaAsync(string jsonSchema, HierarchicalLoggingContext context)
    {
        // Create a cache key based on schema content hash
        var schemaHash = jsonSchema.GetHashCode().ToString();

        lock (_schemaCacheLock)
        {
            if (_schemaCache.TryGetValue(schemaHash, out var cachedSchema))
            {
                return cachedSchema;
            }
        }

        // Parse schema (this is CPU-bound, so we'll run it on a background thread)
        var schema = await Task.Run(() =>
        {
            try
            {
                return JsonSchema.FromText(jsonSchema);
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithHierarchy(context, ex, "Failed to parse JSON schema");
                throw new InvalidOperationException($"Invalid JSON schema: {ex.Message}", ex);
            }
        });

        // Cache the parsed schema
        lock (_schemaCacheLock)
        {
            if (!_schemaCache.ContainsKey(schemaHash))
            {
                _schemaCache[schemaHash] = schema;
            }
        }

        return schema;
    }

    /// <summary>
    /// Performs full JSON Schema validation using JsonSchema.Net
    /// </summary>
    private bool ValidateWithJsonSchemaNet(JsonDocument jsonDocument, JsonSchema schema, List<string> validationErrors, SchemaValidationResult result)
    {
        try
        {
            var validationResult = schema.Evaluate(jsonDocument.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.Hierarchical
            });

            if (!validationResult.IsValid)
            {
                // Extract detailed error information from the validation result
                ExtractValidationErrors(validationResult, validationErrors, result);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            validationErrors.Add($"Schema validation exception: {ex.Message}");
            _logger.LogErrorWithCorrelation(ex, "Exception during JsonSchema.Net validation");
            return false;
        }
    }

    /// <summary>
    /// Performs full JSON Schema validation using JsonSchema.Net with hierarchical context
    /// </summary>
    private bool ValidateWithJsonSchemaNet(JsonDocument jsonDocument, JsonSchema schema, List<string> validationErrors, SchemaValidationResult result, HierarchicalLoggingContext context)
    {
        try
        {
            var validationResult = schema.Evaluate(jsonDocument.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.Hierarchical
            });

            if (!validationResult.IsValid)
            {
                // Extract detailed error information from the validation result
                ExtractValidationErrors(validationResult, validationErrors, result);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            validationErrors.Add($"Schema validation exception: {ex.Message}");
            _logger.LogErrorWithHierarchy(context, ex, "Exception during JsonSchema.Net validation");
            return false;
        }
    }

    /// <summary>
    /// Extracts validation errors from JsonSchema.Net evaluation result
    /// </summary>
    private void ExtractValidationErrors(EvaluationResults validationResult, List<string> validationErrors, SchemaValidationResult result)
    {
        if (validationResult.Details != null)
        {
            foreach (var detail in validationResult.Details)
            {
                if (!detail.IsValid)
                {
                    var errorPath = detail.InstanceLocation?.ToString() ?? "unknown";
                    var errorMessage = detail.Errors?.FirstOrDefault().Value ?? "Validation failed";
                    
                    validationErrors.Add($"Path: {errorPath}, Error: {errorMessage}");
                    
                    // Set the first error path for the result
                    if (string.IsNullOrEmpty(result.ErrorPath))
                    {
                        result.ErrorPath = errorPath;
                    }
                }
            }
        }
        else
        {
            validationErrors.Add("Schema validation failed with no detailed error information");
        }
    }
}
