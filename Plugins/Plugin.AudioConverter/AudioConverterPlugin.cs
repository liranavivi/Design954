using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.AudioConverter.Interfaces;
using Plugin.AudioConverter.Models;
using Plugin.AudioConverter.Services;
using Plugin.AudioConverter.Utilities;
using Plugin.Shared.Interfaces;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Models;

namespace Plugin.AudioConverter;

/// <summary>
/// Audio converter plugin for processing individual audio-information file pairs
/// Expects exactly 2 individual files: one audio file and one information file (XML, JSON, TXT, etc.)
///
/// Processing:
/// - Individual files: Receives 2 separate file cache objects with extractedFileCacheDataObject: []
/// - Audio files: Converted using FFmpeg with configured arguments
/// - Information files: Passed through unchanged (no processing needed)
/// - Output: Returns array of 2 individual file cache objects (information unchanged, audio converted)
///
/// Extensibility:
/// - Virtual PerformConversionAsync method for custom logic
/// - Virtual ProcessAudioFileConversionAsync method for audio processing
/// </summary>
public class AudioConverterPlugin : IPlugin
{
    private readonly ILogger<AudioConverterPlugin> _logger;
    private readonly IAudioConverterPluginMetricsService _metricsService;

    // Cached FFmpeg service implementation
    private IFFmpegService? _ffmpegService;

    // Cached audio conversion implementation
    private IAudioConversionImplementation? _audioImplementation;

    /// <summary>
    /// Constructor with dependency injection using standardized plugin pattern
    /// Aligned with StandardizerPlugin architecture
    /// </summary>
    public AudioConverterPlugin(
        string pluginCompositeKey,
        ILogger<AudioConverterPlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create metrics service with composite key (same pattern as StandardizerPlugin)
        _metricsService = new AudioConverterPluginMetricsService(pluginCompositeKey, _logger);

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Process activity data with audio conversion
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
        CancellationToken cancellationToken = default)
    {
        var processingStart = DateTime.UtcNow;

        // Create Layer 6 hierarchical context for AudioConverter plugin
        var pluginContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            WorkflowId = workflowId,
            CorrelationId = correlationId,
            StepId = stepId,
            ProcessorId = processorId,
            PublishId = publishId,
            ExecutionId = executionId
        };

        _logger.LogInformationWithHierarchy(pluginContext,
            "Starting AudioConverter plugin processing");

        try
        {
            // 1. Validate entities collection - must have at least one DeliveryAssignmentModel
            var deliveryAssignment = entities.OfType<DeliveryAssignmentModel>().FirstOrDefault();
            if (deliveryAssignment == null)
            {
                throw new InvalidOperationException("DeliveryAssignmentModel not found in entities. AudioConverterPlugin expects at least one DeliveryAssignmentModel.");
            }

            _logger.LogInformationWithHierarchy(pluginContext,
                "Processing {EntityCount} entities with DeliveryAssignmentModel: {DeliveryName} (EntityId: {EntityId})",
                entities.Count, deliveryAssignment.Name, deliveryAssignment.EntityId);

            // 1. Extract configuration from DeliveryAssignmentModel (fresh every time - stateless)
            var config = await ExtractConfigurationFromDeliveryAssignmentAsync(deliveryAssignment, pluginContext);

            // 2. Validate configuration
            await ValidateConfigurationAsync(config, pluginContext);

            // 3. Parse input data (array of individual files from EnricherPlugin) - now centrally deserialized
            var cacheDataArray = GetCacheDataArray(inputData, pluginContext);

            // 4. Validate that we have exactly 2 individual files (information + audio)
            if (cacheDataArray.Length != 2)
            {
                throw new InvalidOperationException($"AudioConverterPlugin expects exactly 2 individual files (information + audio), but received {cacheDataArray.Length} files");
            }

            _logger.LogInformationWithHierarchy(pluginContext,
                "Successfully received {FileCount} individual files for audio conversion",
                cacheDataArray.Length);

            // 5. Process the extracted files (information + audio)
            var result = await ProcessExtractedFilesAsync(
                cacheDataArray, config, pluginContext, cancellationToken);

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
                context: pluginContext);

            _logger.LogErrorWithHierarchy(pluginContext, ex, "Failed to process entities in AudioConverter plugin");

            // Return error result
            return new[] { new ProcessedActivityData
            {
                Result = $"Error in AudioConverter plugin processing: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                ExecutionId = executionId,
                ProcessorName = "AudioConverterPlugin",
                Version = "1.0",
                Data = new { } // Empty object for errors
            } };
        }
    }

    private Task<AudioConverterConfiguration> ExtractConfigurationFromDeliveryAssignmentAsync(
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
        var config = new AudioConverterConfiguration
        {
            FFmpegConversionArguments = JsonConfigurationExtractor.GetStringValue(root, "ffmpegConversionArguments", "-acodec libmp3lame -ab 320k -ar 44100 -ac 2"),
            FFmpegPath = JsonConfigurationExtractor.GetStringValue(root, "ffmpegPath", null!),
            MetadataImplementationType = JsonConfigurationExtractor.GetStringValue(root, "metadataImplementationType", null!)
        };

        _logger.LogInformationWithHierarchy(context,
            "Extracted AudioConverter configuration - FFmpegConversionArguments: {FFmpegConversionArguments}, FFmpegPath: {FFmpegPath}",
            config.FFmpegConversionArguments, config.FFmpegPath ?? "system PATH");

        return Task.FromResult(config);
    }

    private Task ValidateConfigurationAsync(AudioConverterConfiguration configuration, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Validating AudioConverter configuration");

        if (string.IsNullOrWhiteSpace(configuration.FFmpegConversionArguments))
        {
            throw new InvalidOperationException("FFmpeg conversion arguments are required");
        }

        // Create FFmpeg service if not already created
        _ffmpegService ??= new FFmpegService(_logger);

        // Validate FFmpeg availability at configured path
        if (!_ffmpegService.IsFFmpegAvailable(context, configuration.FFmpegPath))
        {
            var pathInfo = configuration.FFmpegPath ?? "system PATH";
            throw new InvalidOperationException($"FFmpeg is not available at: {pathInfo}");
        }

        _logger.LogInformationWithHierarchy(context, "AudioConverter configuration validation completed successfully");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Load audio implementation dynamically based on configuration
    /// </summary>
    private IAudioConversionImplementation LoadAudioImplementation(AudioConverterConfiguration config, HierarchicalLoggingContext context)
    {
        if (_audioImplementation != null)
        {
            return _audioImplementation;
        }

        try
        {
            // Use configured implementation type or default
            var implementationType = config.MetadataImplementationType;

            if (string.IsNullOrEmpty(implementationType))
            {
                _logger.LogInformationWithHierarchy(context, "No MetadataImplementationType specified, using default ExampleAudioConverter");
                _audioImplementation = new Examples.ExampleAudioConverter();
            }
            else
            {
                _logger.LogInformationWithHierarchy(context, "Loading audio implementation: {ImplementationType}", implementationType);
                var implementationLoader = new Services.ImplementationLoader(_logger);
                _audioImplementation = implementationLoader.LoadImplementation<IAudioConversionImplementation>(implementationType, context);
            }

            _logger.LogInformationWithHierarchy(context, "Successfully loaded audio implementation: {ImplementationName}", _audioImplementation.GetType().Name);
            return _audioImplementation;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to load audio implementation: {ImplementationType}. Using default implementation.", config.MetadataImplementationType ?? "null");
            _audioImplementation = new Examples.ExampleAudioConverter();
            return _audioImplementation;
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
            throw new InvalidOperationException("Input data is null - AudioConverter expects compressed file cache data array from EnricherPlugin");
        }

        if (inputData is not JsonElement jsonElement)
        {
            throw new InvalidOperationException("Input data is not a JsonElement - AudioConverter expects JSON array from EnricherPlugin");
        }

        if (jsonElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Input data is not a JSON array - AudioConverter expects compressed file cache data array from EnricherPlugin");
        }

        var arrayElements = jsonElement.EnumerateArray().ToArray();
        _logger.LogInformationWithHierarchy(context, "Successfully extracted {ItemCount} compressed file cache data items", arrayElements.Length);

        return arrayElements;
    }

    /// <summary>
    /// Process extracted files from EnricherPlugin with specialized handling
    /// </summary>
    private async Task<ProcessedActivityData> ProcessExtractedFilesAsync(
        JsonElement[] cacheDataArray,
        AudioConverterConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        try
        {
            _logger.LogInformationWithHierarchy(context, "Processing extracted files for audio conversion");

            // 1. Load audio implementation dynamically to get mandatory file extension
            var audioImplementation = LoadAudioImplementation(config, context);
            var mandatoryExtension = audioImplementation.MandatoryFileExtension.ToLowerInvariant();

            _logger.LogInformationWithHierarchy(context, "Looking for files with mandatory extension: {MandatoryExtension}", mandatoryExtension);

            // 2. Process all files, converting those with mandatory extension
            var resultData = new List<object>();
            var convertedCount = 0;

            foreach (var cacheData in cacheDataArray)
            {
                var fileName = AudioConverterHelper.GetFileNameFromCacheData(cacheData);

                if (fileName.EndsWith(mandatoryExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // Convert this file (already includes extractedFileCacheDataObject from ProcessAudioFileConversionAsync)
                    _logger.LogInformationWithHierarchy(context, "Converting audio file: {FileName}", fileName);
                    var convertedCacheData = await ProcessAudioFileConversionAsync(cacheData, fileName, config, context);
                    resultData.Add(convertedCacheData);
                    convertedCount++;
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

            // Record conversion metrics
            _metricsService.RecordDataConversion(
                recordsProcessed: cacheDataArray.Length,
                recordsSuccessful: convertedCount,
                conversionDuration: processingDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Log results
            if (convertedCount > 0)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Successfully converted {ConvertedCount} audio file(s) to target format - Duration: {Duration}ms",
                    convertedCount, processingDuration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarningWithHierarchy(context,
                    "No files with mandatory extension {MandatoryExtension} found for conversion",
                    mandatoryExtension);
            }

            return new ProcessedActivityData
            {
                Result = $"Successfully processed {cacheDataArray.Length} files, converted {convertedCount} files",
                Status = ActivityExecutionStatus.Completed,
                ExecutionId = context.ExecutionId!.Value,
                ProcessorName = "AudioConverterPlugin",
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
                Result = $"Audio and information content pair conversion error: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                ExecutionId = context.ExecutionId!.Value,
                ProcessorName = "AudioConverterPlugin",
                Version = "1.0",
                Data = new { }
            };
        }
    }

    /// <summary>
    /// Process audio file conversion using FFmpeg
    /// Virtual method to allow customization in derived classes
    /// Throws exceptions on failure instead of returning null for better error handling
    /// </summary>
    protected virtual async Task<object> ProcessAudioFileConversionAsync(
        JsonElement audioFile,
        string fileName,
        AudioConverterConfiguration config,
        HierarchicalLoggingContext context)
    {
        var conversionStart = DateTime.UtcNow;

        _logger.LogDebugWithHierarchy(context, "Starting audio file conversion for: {FileName}", fileName);

        // Extract audio binary data from the file
        var audioData = AudioConverterHelper.ExtractAudioBinaryData(audioFile) ??
            throw new InvalidOperationException("Failed to extract audio binary data from file");

        // Use the already loaded audio implementation
        if (_audioImplementation == null)
        {
            throw new InvalidOperationException("Audio implementation not loaded. This should not happen.");
        }

        _logger.LogInformationWithHierarchy(context, "Using audio implementation: {ImplementationName}", _audioImplementation.GetType().Name);

        // Perform conversion using the loaded implementation
        var convertedAudioCacheData = await _audioImplementation.ConvertAudioAsync(audioData, fileName, config, context, _logger);

        var processingDuration = DateTime.UtcNow - conversionStart;

        _logger.LogInformationWithHierarchy(context,
            "Successfully converted audio file: {FileName} - Original: {OriginalSize} bytes, Duration: {Duration}ms",
            fileName, audioData.Length, processingDuration.TotalMilliseconds);

        // Return the complete file cache data object directly
        return convertedAudioCacheData;
    }

    }
