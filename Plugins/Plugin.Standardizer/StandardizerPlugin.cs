using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.Shared.Interfaces;
using Plugin.Shared.Utilities;
using Plugin.Standardizer.Interfaces;
using Plugin.Standardizer.Models;
using Plugin.Standardizer.Services;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Models;

namespace Plugin.Standardizer;

/// <summary>
/// Standardizer plugin for processing audio and information content pairs from compressed files
/// Expects exactly 1 compressed file containing 2 extracted files: one audio file and one information content file
///
/// Processing:
/// - Compressed file: Extracts audio and information content files from extractedFileCacheDataObject
/// - Information content: Standardized to metadata format using MetadataImplementationType
/// - Audio files: Not processed (passed through unchanged)
///
/// Configuration:
/// - MetadataImplementationType: Mandatory - specifies the custom implementation to use (from current assembly)
/// </summary>
public class StandardizerPlugin : IPlugin
{
    private readonly ILogger<StandardizerPlugin> _logger;
    private readonly IStandardizerPluginMetricsService _metricsService;

    // Cached implementation
    private IMetadataStandardizationImplementation? _metadataImplementation;

    /// <summary>
    /// Constructor with dependency injection using standardized plugin pattern
    /// Aligned with PreFileReaderPlugin architecture
    /// </summary>
    public StandardizerPlugin(
        string pluginCompositeKey,
        ILogger<StandardizerPlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create metrics service with composite key (same pattern as PreFileReaderPlugin)
        _metricsService = new StandardizerPluginMetricsService(pluginCompositeKey, _logger);

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Process activity data - standardize cache data from FileReader
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

        // Create Layer 6 hierarchical context for Standardizer plugin
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
            "Starting Standardizer plugin processing");

        try
        {
            // 1. Extract configuration from DeliveryAssignmentModel
            var deliveryAssignment = entities.OfType<DeliveryAssignmentModel>().FirstOrDefault() ??
                throw new InvalidOperationException("DeliveryAssignmentModel not found in entities. StandardizerPlugin expects a DeliveryAssignmentModel for configuration.");

            var config = await ExtractConfigurationFromDeliveryAssignmentAsync(deliveryAssignment, context);

            // 2. Validate entities collection - must have at least one AssignmentModel
            var assignment = entities.FirstOrDefault() ??
                throw new InvalidOperationException("AssignmentModel not found in entities. StandardizerPlugin expects at least one AssignmentModel.");

            _logger.LogInformationWithHierarchy(context,
                "Processing {EntityCount} entities with AssignmentModel: {AssignmentName} (EntityId: {EntityId})",
                entities.Count, assignment.Name, assignment.EntityId);

            // 3. Parse input data (array of cache objects from FileReader) - now centrally deserialized
            var cacheDataArray = GetCacheDataArray(inputData, context);

            // 4. Validate that we have exactly one compressed file with extracted content
            if (cacheDataArray.Length != 1)
            {
                throw new InvalidOperationException($"StandardizerPlugin expects exactly 1 compressed file with extracted content, but received {cacheDataArray.Length} files");
            }

            // 5. Extract the compressed file data and validate extracted content
            var compressedFileData = cacheDataArray[0];
            if (!compressedFileData.TryGetProperty("extractedFileCacheDataObject", out var extractedFilesElement))
            {
                throw new InvalidOperationException("Compressed file data must contain 'extractedFileCacheDataObject' property");
            }

            var extractedFiles = extractedFilesElement.EnumerateArray().ToArray();
            if (extractedFiles.Length != 2)
            {
                throw new InvalidOperationException($"StandardizerPlugin expects exactly 2 extracted files (audio + information content), but found {extractedFiles.Length} extracted files");
            }

            _logger.LogInformationWithHierarchy(context,
                "Successfully extracted {ExtractedFileCount} files from compressed archive for processing",
                extractedFiles.Length);

            // 6. Process the extracted files from compressed archive
            var result = await ProcessExtractedFilesAsync(
                extractedFiles, config, context, cancellationToken);

            return new[] { result };
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;

            // Record plugin exception
            _metricsService.RecordPluginException(
                exceptionType: ex.GetType().Name,
                severity: "error",
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            _logger.LogErrorWithHierarchy(context, ex,
                "Standardizer plugin processing failed. Duration: {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result
            return new[] { new ProcessedActivityData
            {
                Result = $"Error in Standardizer plugin processing: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                ExecutionId = context.ExecutionId!.Value,
                ProcessorName = "StandardizerPlugin",
                Version = "1.0",
                Data = new { } // Empty object for errors
            } };
        }
    }

    /// <summary>
    /// Extract standardization configuration from DeliveryAssignmentModel payload
    /// </summary>
    private Task<StandardizationConfiguration> ExtractConfigurationFromDeliveryAssignmentAsync(
        DeliveryAssignmentModel deliveryAssignment,
        HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Extracting configuration from DeliveryAssignmentModel. EntityId: {EntityId}, Name: {Name}",
            deliveryAssignment.EntityId, deliveryAssignment.Name);

        if (string.IsNullOrEmpty(deliveryAssignment.Payload))
        {
            throw new InvalidOperationException("DeliveryAssignmentModel.Payload (configuration JSON) cannot be empty");
        }

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
        var config = new StandardizationConfiguration
        {
            MetadataImplementationType = JsonConfigurationExtractor.GetStringValue(root, "metadataImplementationType", "")
        };

        _logger.LogInformationWithHierarchy(context,
            "Extracted standardization configuration from DeliveryAssignmentModel - MetadataImplementationType: {MetadataImplementationType}",
            config.MetadataImplementationType ?? "default");

        return Task.FromResult(config);
    }

    /// <summary>
    /// Load metadata implementation dynamically based on configuration
    /// </summary>
    private IMetadataStandardizationImplementation LoadMetadataImplementation(StandardizationConfiguration config, HierarchicalLoggingContext context)
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
                _logger.LogInformationWithHierarchy(context, "No MetadataImplementationType specified, using default ExampleXmlMetadataStandardizer");
                _metadataImplementation = new Examples.ExampleXmlMetadataStandardizer();
            }
            else
            {
                _logger.LogInformationWithHierarchy(context, "Loading metadata implementation: {ImplementationType}", implementationType);
                var implementationLoader = new Services.ImplementationLoader(_logger);
                _metadataImplementation = implementationLoader.LoadImplementation<IMetadataStandardizationImplementation>(implementationType, context);
            }

            _logger.LogInformationWithHierarchy(context, "Successfully loaded metadata implementation: {ImplementationName}", _metadataImplementation.GetType().Name);
            return _metadataImplementation;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to load metadata implementation: {MetadataImplementationType}. Using default implementation.", config.MetadataImplementationType ?? "null");
            _metadataImplementation = new Examples.ExampleXmlMetadataStandardizer();
            return _metadataImplementation;
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
            throw new InvalidOperationException("Input data is null - Standardizer expects compressed file cache data array from FileReader");
        }

        if (inputData is not JsonElement jsonElement)
        {
            throw new InvalidOperationException("Input data is not a JsonElement - Standardizer expects JSON array from FileReader");
        }

        if (jsonElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Input data is not a JSON array - Standardizer expects compressed file cache data array from FileReader");
        }

        var arrayElements = jsonElement.EnumerateArray().ToArray();
        _logger.LogInformationWithHierarchy(context, "Successfully extracted compressed file cache data array with {ItemCount} items", arrayElements.Length);

        return arrayElements;
    }

    /// <summary>
    /// Process extracted files from compressed archive with specialized handling
    /// </summary>
    private async Task<ProcessedActivityData> ProcessExtractedFilesAsync(
        JsonElement[] cacheDataArray,
        StandardizationConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        try
        {
            _logger.LogInformationWithHierarchy(context, "Processing extracted files for standardization");

            // 1. Load metadata implementation dynamically to get mandatory file extension
            var metadataImplementation = LoadMetadataImplementation(config, context);
            var mandatoryExtension = metadataImplementation.MandatoryFileExtension.ToLowerInvariant();

            _logger.LogInformationWithHierarchy(context, "Looking for files with mandatory extension: {MandatoryExtension}", mandatoryExtension);

            // 2. Process all files, standardizing those with mandatory extension
            var resultData = new List<object>();
            var standardizedCount = 0;

            foreach (var cacheData in cacheDataArray)
            {
                var fileName = PluginHelper.GetFileNameFromCacheData(cacheData);

                if (fileName.EndsWith(mandatoryExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // Standardize this file (already includes extractedFileCacheDataObject from ProcessInformationContentStandardizationAsync)
                    _logger.LogInformationWithHierarchy(context, "Standardizing file: {FileName}", fileName);
                    var standardizedCacheData = await ProcessInformationContentStandardizationAsync(
                        cacheData, fileName, config, context);

                    resultData.Add(standardizedCacheData);
                    standardizedCount++;
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

            // Record standardization metrics
            _metricsService.RecordDataStandardization(
                recordsProcessed: cacheDataArray.Length,
                recordsSuccessful: standardizedCount,
                standardizationDuration: processingDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Log results
            if (standardizedCount > 0)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Successfully standardized {StandardizedCount} file(s) to XML - Duration: {Duration}ms",
                    standardizedCount, processingDuration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarningWithHierarchy(context,
                    "No files with mandatory extension {MandatoryExtension} found for standardization",
                    mandatoryExtension);
            }

            return new ProcessedActivityData
            {
                Result = $"Successfully processed {cacheDataArray.Length} files, standardized {standardizedCount} files",
                Status = ActivityExecutionStatus.Completed,
                ExecutionId = context.ExecutionId!.Value,
                ProcessorName = "StandardizerPlugin",
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
                ProcessorName = "StandardizerPlugin",
                Version = "1.0",
                Data = new { }
            };
        }
    }

    /// <summary>
    /// Process information content standardization - convert content to XML metadata
    /// Virtual method to allow customization in derived classes
    /// Throws exceptions on failure instead of returning null for better error handling
    /// </summary>
    protected virtual async Task<object> ProcessInformationContentStandardizationAsync(
        JsonElement informationFile,
        string fileName,
        StandardizationConfiguration config,
        HierarchicalLoggingContext context)
    {
        var standardizationStart = DateTime.UtcNow;

        _logger.LogDebugWithHierarchy(context, "Starting information content standardization for: {FileName}", fileName);

        // Use the already loaded metadata implementation
        if (_metadataImplementation == null)
        {
            throw new InvalidOperationException("Metadata implementation not loaded. This should not happen.");
        }

        _logger.LogInformationWithHierarchy(context, "Using metadata implementation: {ImplementationName}", _metadataImplementation.GetType().Name);

        // Extract information content as string
        var informationContent = ExtractInformationContentAsString(informationFile);

        // Get complete file cache data object with XML content from implementation
        var xmlFileCacheDataObject = await _metadataImplementation.StandardizeToMetadataAsync(
            informationContent, fileName, config, context, _logger);

        var processingDuration = DateTime.UtcNow - standardizationStart;
        _logger.LogInformationWithHierarchy(context, "Successfully standardized information content for: {FileName} - Duration: {Duration}ms", fileName, processingDuration.TotalMilliseconds);

        // Return the complete file cache data object directly
        return xmlFileCacheDataObject;
    }

    /// <summary>
    /// Extract information content as string from cache data
    /// </summary>
    private string ExtractInformationContentAsString(JsonElement informationFile)
    {
        try
        {
            var content = PluginHelper.ExtractFileContent(informationFile);
            if (content == null)
            {
                return string.Empty;
            }

            // If content is already a string, return it
            if (content is string stringContent)
            {
                return stringContent;
            }

            // If content is a JsonElement, try to extract text from it
            if (content is JsonElement jsonElement)
            {
                // Try to get text content from binary data
                if (jsonElement.TryGetProperty("binaryData", out var binaryDataElement) &&
                    jsonElement.TryGetProperty("encoding", out var encodingElement))
                {
                    var binaryData = binaryDataElement.GetString();
                    var encoding = encodingElement.GetString();

                    if (!string.IsNullOrEmpty(binaryData) && encoding?.ToLowerInvariant() == "base64")
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(binaryData);
                            return System.Text.Encoding.UTF8.GetString(bytes);
                        }
                        catch
                        {
                            // Fall back to JSON serialization
                        }
                    }
                }
            }

            // Fall back to JSON serialization
            return JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return string.Empty;
        }
    }

}
