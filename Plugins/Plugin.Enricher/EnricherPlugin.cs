using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.Enricher.Interfaces;
using Plugin.Enricher.Models;
using Plugin.Enricher.Services;
using Plugin.Shared.Interfaces;
using Plugin.Shared.Utilities;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Models;

namespace Plugin.Enricher;

/// <summary>
/// Enricher plugin implementation that enriches information files with additional analysis from compressed files
/// Processes compressed file cache data from StandardizerPlugin and outputs enriched cache objects with same schema
/// Expects exactly 1 compressed file containing 2 extracted files: one audio file and one information file (XML, JSON, TXT, etc.)
/// Demonstrates configurable metadata enrichment with IMetadataStandardizationImplementation pattern
///
/// Processing:
/// - Compressed file: Extracts audio and information files from extractedFileCacheDataObject
/// - Information files: Enriched using configurable implementation with StandardizationConfiguration
/// - Audio files: Passed through unchanged (no processing needed)
/// - Output: Returns array of 2 individual file cache objects (information enriched, audio unchanged)
///
/// Extensibility Features:
/// - IMetadataStandardizationImplementation interface for configurable enrichment logic
/// - StandardizationConfiguration support for runtime configuration
/// - Non-blocking enrichment failures for robust processing
/// </summary>
public class EnricherPlugin : IPlugin
{
    private readonly ILogger<EnricherPlugin> _logger;
    private readonly IEnricherPluginMetricsService _metricsService;

    // Cached implementation
    private IMetadataEnrichmentImplementation? _metadataImplementation;

    /// <summary>
    /// Constructor with dependency injection using standardized plugin pattern
    /// Aligned with StandardizerPlugin architecture
    /// </summary>
    public EnricherPlugin(
        string pluginCompositeKey,
        ILogger<EnricherPlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create metrics service with composite key (same pattern as StandardizerPlugin)
        _metricsService = new EnricherPluginMetricsService(pluginCompositeKey, _logger);

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Process activity data - enrich cache data from StandardizerPlugin
    /// Enhanced with hierarchical logging support - maintains consistent ID ordering
    /// </summary>
    public async Task<IEnumerable<ProcessedActivityData>> ProcessActivityDataAsync(
        // ✅ Consistent order: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId
        Guid orchestratedFlowId,
        Guid workflowId,
        Guid correlationId,
        Guid stepId,
        Guid processorId,
        Guid publishId,
        Guid executionId,
        List<AssignmentModel> entities,
        object? inputData,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        // Create Layer 6 hierarchical context for Enricher plugin
        var context = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            WorkflowId = workflowId,
            CorrelationId = correlationId,
            StepId = stepId,
            ProcessorId = processorId,
            PublishId = publishId,
            ExecutionId = executionId
        };

        _logger.LogInformationWithHierarchy(context,
            "Starting Enricher plugin processing");

        try
        {
            // 1. Extract assignment model and configuration
            var deliveryAssignment = entities.OfType<DeliveryAssignmentModel>().FirstOrDefault() ??
                throw new InvalidOperationException("DeliveryAssignmentModel not found in entities. EnricherPlugin expects at least one DeliveryAssignmentModel.");

            _logger.LogInformationWithHierarchy(context,
                "Processing {EntityCount} entities with DeliveryAssignmentModel: {AssignmentName} (EntityId: {EntityId})",
                entities.Count, deliveryAssignment.Name, deliveryAssignment.EntityId);

            // 2. Extract configuration from DeliveryAssignmentModel (fresh every time - stateless)
            var config = await ExtractConfigurationFromDeliveryAssignmentAsync(deliveryAssignment, context);

            // 3. Validate configuration
            await ValidateConfigurationAsync(config, context);

            // 4. Parse input data (array of individual files from StandardizerPlugin) - now centrally deserialized
            var cacheDataArray = GetCacheDataArray(inputData, context);

            // 5. Validate that we have exactly 2 individual files (information + audio)
            if (cacheDataArray.Length != 2)
            {
                throw new InvalidOperationException($"EnricherPlugin expects exactly 2 individual files (information + audio), but received {cacheDataArray.Length} files");
            }

            _logger.LogInformationWithHierarchy(context,
                "Successfully received {FileCount} individual files for enrichment",
                cacheDataArray.Length);

            // 6. Process the extracted files (information + audio)
            var result = await ProcessExtractedFilesAsync(
                cacheDataArray, config, context, cancellationToken);

            return new[] { result };
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;

            // Record plugin exception
            _metricsService.RecordPluginException(
                exceptionType: ex.GetType().Name,
                severity: "error",
                correlationId: correlationId.ToString(),
                orchestratedFlowId: orchestratedFlowId,
                stepId: stepId,
                executionId: executionId,
                context: context);

            _logger.LogErrorWithHierarchy(context, ex,
                "Enricher plugin processing failed - Duration: {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result
            return new List<ProcessedActivityData>
            {
                new ProcessedActivityData
                {
                    Result = $"Error in Enricher plugin processing: {ex.Message}",
                    Status = ActivityExecutionStatus.Failed,
                    ExecutionId = executionId,
                    ProcessorName = "EnricherPlugin",
                    Version = "1.0",
                    Data = new { } // Empty object for errors
                }
            };
        }
    }

    /// <summary>
    /// Extract cache data array from centrally deserialized input data
    /// </summary>
    private JsonElement[] GetCacheDataArray(object? inputData, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Extracting cache data array from deserialized input data");

        if (inputData == null)
        {
            throw new InvalidOperationException("Input data is null - Enricher expects compressed file cache data array from StandardizerPlugin");
        }

        if (inputData is not JsonElement jsonElement)
        {
            throw new InvalidOperationException("Input data is not a JsonElement - Enricher expects JSON array from StandardizerPlugin");
        }

        if (jsonElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Input data is not a JSON array - Enricher expects compressed file cache data array from StandardizerPlugin");
        }

        var arrayElements = jsonElement.EnumerateArray().ToArray();
        _logger.LogInformationWithHierarchy(context, "Successfully extracted cache data array with {ItemCount} items", arrayElements.Length);

        return arrayElements;
    }

    /// <summary>
    /// Process extracted files from StandardizerPlugin with specialized handling
    /// </summary>
    private async Task<ProcessedActivityData> ProcessExtractedFilesAsync(
        JsonElement[] cacheDataArray,
        EnrichmentConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        try
        {
            _logger.LogInformationWithHierarchy(context, "Processing extracted files for enrichment");

            // 1. Load metadata implementation dynamically to get mandatory file extension
            var metadataImplementation = LoadMetadataImplementation(config, context);
            var mandatoryExtension = metadataImplementation.MandatoryFileExtension.ToLowerInvariant();

            _logger.LogInformationWithHierarchy(context, "Looking for files with mandatory extension: {MandatoryExtension}", mandatoryExtension);

            // 2. Process all files, enriching those with mandatory extension
            var resultData = new List<object>();
            var enrichedCount = 0;

            foreach (var cacheData in cacheDataArray)
            {
                var fileName = PluginHelper.GetFileNameFromCacheData(cacheData);

                if (fileName.EndsWith(mandatoryExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // Enrich this file (already includes extractedFileCacheDataObject from ProcessInformationFileEnrichmentAsync)
                    _logger.LogInformationWithHierarchy(context, "Enriching file: {FileName}", fileName);
                    var enrichedCacheData = await ProcessInformationFileEnrichmentAsync(
                        cacheData, fileName, config, context);

                    resultData.Add(enrichedCacheData);
                    enrichedCount++;
                }
                else
                {
                    // Keep all other files unchanged but add extractedFileCacheDataObject
                    _logger.LogInformationWithHierarchy(context, "Keeping file unchanged: {FileName}", fileName);

                    // Create schema-compliant file object with extractedFileCacheDataObject for non-mandatory files
                    var fileWithExtracted = new
                    {
                        fileCacheDataObject = cacheData.GetProperty("fileCacheDataObject"),
                        extractedFileCacheDataObject = new object[0] // Empty array as required
                    };

                    resultData.Add(fileWithExtracted);
                }
            }

            var processingDuration = DateTime.UtcNow - processingStart;

            // Record enrichment metrics
            _metricsService.RecordDataEnrichment(
                recordsProcessed: cacheDataArray.Length,
                recordsSuccessful: enrichedCount,
                enrichmentDuration: processingDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Log results
            if (enrichedCount > 0)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Successfully enriched {EnrichedCount} file(s) with metadata - Duration: {Duration}ms",
                    enrichedCount, processingDuration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarningWithHierarchy(context,
                    "No files with mandatory extension {MandatoryExtension} found for enrichment",
                    mandatoryExtension);
            }

            return new ProcessedActivityData
            {
                Result = $"Successfully processed {cacheDataArray.Length} files, enriched {enrichedCount} files",
                Status = ActivityExecutionStatus.Completed,
                ExecutionId = context.ExecutionId!.Value,
                ProcessorName = "EnricherPlugin",
                Version = "1.0",
                Data = resultData.ToArray()
            };
        }
        catch (Exception ex)
        {
            // Record plugin exception for processing failure
            _metricsService.RecordPluginException(
                exceptionType: ex.GetType().Name,
                severity: "error",
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            _logger.LogErrorWithHierarchy(context, ex, "Failed to process audio and information content pair");

            return new ProcessedActivityData
            {
                Result = $"Audio and information content pair processing error: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                ExecutionId = context.ExecutionId!.Value,
                ProcessorName = "EnricherPlugin",
                Version = "1.0",
                Data = new { }
            };
        }
    }

    /// <summary>
    /// Process information file enrichment using configurable implementation
    /// Throws exceptions on failure instead of returning null for better error handling
    /// </summary>
    protected virtual async Task<object> ProcessInformationFileEnrichmentAsync(
        JsonElement informationFile,
        string fileName,
        EnrichmentConfiguration config,
        HierarchicalLoggingContext context)
    {
        var enrichmentStart = DateTime.UtcNow;

        _logger.LogDebugWithHierarchy(context, "Starting information file enrichment for: {FileName}", fileName);

        // Use the already loaded metadata implementation
        if (_metadataImplementation == null)
        {
            throw new InvalidOperationException("Metadata implementation not loaded. This should not happen.");
        }

        _logger.LogInformationWithHierarchy(context, "Using metadata implementation: {ImplementationType}", _metadataImplementation.GetType().Name);

        // Extract information content as string
        var informationContent = ExtractInformationContentAsString(informationFile);

        // Get complete file cache data object with enriched content from implementation
        var enrichedFileCacheDataObject = await _metadataImplementation.EnrichToMetadataAsync(
            informationContent, fileName, config, context, _logger);

        var processingDuration = DateTime.UtcNow - enrichmentStart;
        _logger.LogInformationWithHierarchy(context, "Successfully enriched information content for: {FileName} - Duration: {Duration}ms", fileName, processingDuration.TotalMilliseconds);

        // Return the complete file cache data object directly
        return enrichedFileCacheDataObject;
    }

    /// <summary>
    /// Extract information content as string from file content object
    /// </summary>
    private static string ExtractInformationContentAsString(JsonElement informationFile)
    {
        try
        {
            if (informationFile.TryGetProperty("fileCacheDataObject", out var fileCacheObj) &&
                fileCacheObj.TryGetProperty("fileContent", out var content))
            {
                // Try to get standardized text content first
                if (content.TryGetProperty("standardizedTextContent", out var standardizedContent))
                {
                    return standardizedContent.GetString() ?? "";
                }

                // Fallback to binary data if available
                if (content.TryGetProperty("binaryData", out var binaryData))
                {
                    var binaryDataString = binaryData.GetString();
                    if (!string.IsNullOrEmpty(binaryDataString))
                    {
                        var bytes = Convert.FromBase64String(binaryDataString);
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore parsing errors and return empty string
        }

        return "";
    }

    /// <summary>
    /// Extract enrichment configuration from DeliveryAssignmentModel payload
    /// </summary>
    private Task<EnrichmentConfiguration> ExtractConfigurationFromDeliveryAssignmentAsync(
        DeliveryAssignmentModel deliveryAssignment,
        HierarchicalLoggingContext context)
    {
        _logger.LogInformationWithHierarchy(context, "Extracting enrichment configuration from DeliveryAssignmentModel");

        // Parse JSON using consistent JsonElement pattern
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(deliveryAssignment.Payload ?? "{}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in DeliveryAssignmentModel payload: {ex.Message}", ex);
        }

        // Extract configuration using shared utilities
        var config = new EnrichmentConfiguration
        {
            MetadataImplementationType = JsonConfigurationExtractor.GetStringValue(root, "metadataImplementationType", "")
        };

        _logger.LogInformationWithHierarchy(context,
            "Extracted enrichment configuration from DeliveryAssignmentModel - MetadataImplementationType: {MetadataImplementationType}",
            config.MetadataImplementationType ?? "default");

        return Task.FromResult(config);
    }

    /// <summary>
    /// Load metadata implementation dynamically based on configuration
    /// </summary>
    private IMetadataEnrichmentImplementation LoadMetadataImplementation(EnrichmentConfiguration config, HierarchicalLoggingContext context)
    {
        if (_metadataImplementation != null)
        {
            return _metadataImplementation;
        }

        try
        {
            // Use configured implementation type or default
            var implementationType = config.MetadataImplementationType;

            if (string.IsNullOrEmpty(implementationType))
            {
                _logger.LogInformationWithHierarchy(context, "No MetadataImplementationType specified, using default ExampleXmlMetadataEnricher");
                _metadataImplementation = new Examples.ExampleXmlMetadataEnricher();
            }
            else
            {
                _logger.LogInformationWithHierarchy(context, "Loading metadata implementation: {ImplementationType}", implementationType);
                var implementationLoader = new Services.ImplementationLoader(_logger);
                _metadataImplementation = implementationLoader.LoadImplementation<IMetadataEnrichmentImplementation>(implementationType, context);
            }

            _logger.LogInformationWithHierarchy(context, "Successfully loaded metadata implementation: {ImplementationType}", _metadataImplementation.GetType().Name);
            return _metadataImplementation;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to load metadata implementation: {ImplementationType}. Using default implementation.", config.MetadataImplementationType ?? "null");
            _metadataImplementation = new Examples.ExampleXmlMetadataEnricher();
            return _metadataImplementation;
        }
    }

    /// <summary>
    /// Validate enrichment configuration
    /// </summary>
    private Task ValidateConfigurationAsync(EnrichmentConfiguration config, HierarchicalLoggingContext context)
    {
        _logger.LogInformationWithHierarchy(context, "Validating enrichment configuration");

        // Configuration validation is optional - plugin can work with default implementation
        if (string.IsNullOrEmpty(config.MetadataImplementationType))
        {
            _logger.LogInformationWithHierarchy(context, "No specific metadata implementation type specified, using default enrichment");
        }
        else
        {
            _logger.LogInformationWithHierarchy(context, "Using metadata implementation type: {ImplementationType}", config.MetadataImplementationType);
        }

        return Task.CompletedTask;
    }
}
