using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.FileWriter.Models;
using Plugin.FileWriter.Services;
using Plugin.Shared.Interfaces;
using Plugin.Shared.Utilities;
using Processor.Base.Models;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Models;

namespace Plugin.FileWriter;

/// <summary>
/// FileWriter plugin implementation that writes extracted files from compressed archives to disk
/// Processes cache data from FileReaderProcessor containing compressed file metadata and extracted files
/// </summary>
public class FileWriterPlugin : IPlugin
{
    private readonly ILogger<FileWriterPlugin> _logger;
    private readonly IFileWriterPluginMetricsService _metricsService;



    /// <summary>
    /// Constructor with dependency injection using standardized plugin pattern
    /// Aligned with FileReaderPlugin architecture
    /// </summary>
    public FileWriterPlugin(
        string pluginCompositeKey,
        ILogger<FileWriterPlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create metrics service with composite key (same pattern as FileReaderPlugin)
        _metricsService = new FileWriterPluginMetricsService(pluginCompositeKey, _logger);

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Plugin implementation of ProcessActivityDataAsync
    /// Enhanced with hierarchical logging support - maintains consistent ID ordering
    /// Processes cache data from FileReaderProcessor and writes extracted files to disk
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
        object? inputData, // Contains deserialized cacheData from FileReaderProcessor
        CancellationToken cancellationToken = default)
    {
        var processingStart = DateTime.UtcNow;

        // Create Layer 6 hierarchical context for FileWriter plugin
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
            "Starting FileWriter plugin processing");

        try
        {
            // 1. Validate entities collection - must have at least one AddressAssignmentModel
            var addressAssignment = entities.OfType<AddressAssignmentModel>().FirstOrDefault();
            if (addressAssignment == null)
            {
                throw new InvalidOperationException("AddressAssignmentModel not found in entities. FileWriterPlugin expects at least one AddressAssignmentModel.");
            }

            _logger.LogInformationWithHierarchy(context,
                "Processing {EntityCount} entities with AddressAssignmentModel: {AddressName} (EntityId: {EntityId})",
                entities.Count, addressAssignment.Name, addressAssignment.EntityId);

            // 1. Extract configuration from AddressAssignmentModel (fresh every time - stateless)
            var config = await ExtractConfigurationFromAddressAssignmentAsync(addressAssignment, context);

            // 2. Validate configuration
            await ValidateConfigurationAsync(config, context);

            // 3. Parse input data (array of compressed file cache data from FileReaderProcessor) - now centrally deserialized
            var cacheDataArray = GetCacheDataArray(inputData, context);

            // 4. Filter cache data items based on SearchPattern and process matching files
            var results = new List<ProcessedActivityData>();
            var filteredItems = new List<JsonElement>();

            // Filter items that match the search pattern
            for (int i = 0; i < cacheDataArray.Length; i++)
            {
                var cacheData = cacheDataArray[i];
                var fileName = PluginHelper.GetFileNameFromCacheData(cacheData);

                if (MatchesSearchPattern(fileName, config.SearchPattern))
                {
                    filteredItems.Add(cacheData);
                    _logger.LogInformationWithHierarchy(context, "Cache data item {ItemNumber} matches search pattern {SearchPattern}: {FileName}",
                        i + 1, config.SearchPattern, fileName);
                }
                else
                {
                    _logger.LogInformationWithHierarchy(context, "Cache data item {ItemNumber} does not match search pattern {SearchPattern}: {FileName} - Skipping",
                        i + 1, config.SearchPattern, fileName);
                }
            }

            _logger.LogInformationWithHierarchy(context, "Filtered {FilteredCount} items out of {TotalCount} total items based on search pattern {SearchPattern}",
                filteredItems.Count, cacheDataArray.Length, config.SearchPattern);

            // Process filtered items
            for (int i = 0; i < filteredItems.Count; i++)
            {
                var cacheData = filteredItems[i];
                var fileName = PluginHelper.GetFileNameFromCacheData(cacheData);
                _logger.LogInformationWithHierarchy(context, "Processing filtered cache data item {ItemNumber} of {TotalCount}: {FileName}",
                    i + 1, filteredItems.Count, fileName);

                var result = await ProcessFileCacheDataWithExceptionHandlingAsync(
                    cacheData, config, context, cancellationToken);

                results.Add(result);
            }

            return results.ToArray();
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
                "FileWriter plugin processing failed. Duration: {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result
            return new[]
            {
                new ProcessedActivityData
                {
                    Result = $"Error in FileWriter plugin processing: {ex.Message}",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "FileWriterProcessor", // Keep same name for compatibility
                    Version = "1.0",
                    ExecutionId = executionId
                }
            };
        }
    }

    /// <summary>
    /// Extract configuration from AddressAssignmentModel
    /// </summary>
    private Task<FileWriterConfiguration> ExtractConfigurationFromAddressAssignmentAsync(
        AddressAssignmentModel addressAssignment, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Extracting configuration from AddressAssignmentModel. EntityId: {EntityId}, Name: {Name}",
            addressAssignment.EntityId, addressAssignment.Name);

        // Parse JSON payload
        JsonElement root;
        try
        {
            var document = JsonDocument.Parse(addressAssignment.Payload);
            root = document.RootElement;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in AddressAssignmentModel payload: {ex.Message}", ex);
        }

        // Extract configuration using shared utilities (the old way)
        var config = new FileWriterConfiguration
        {
            // From ConnectionString (the old way)
            FolderPath = addressAssignment.ConnectionString,

            // From Payload JSON using shared utilities
            SearchPattern = JsonConfigurationExtractor.GetStringValue(root, "searchPattern", "*.{txt,zip,rar,7z,gz,tar}"),
            ProcessingMode = JsonConfigurationExtractor.GetEnumValue<FileProcessingMode>(root, "processingMode", FileProcessingMode.LeaveUnchanged),
            ProcessedExtension = JsonConfigurationExtractor.GetStringValue(root, "processedExtension", ".written"),
            BackupFolder = JsonConfigurationExtractor.GetStringValue(root, "backupFolder", string.Empty),
            MinExtractedContentSize = JsonConfigurationExtractor.GetLongValue(root, "minExtractedContentSize", 0),
            MaxExtractedContentSize = JsonConfigurationExtractor.GetLongValue(root, "maxExtractedContentSize", long.MaxValue)
        };

        _logger.LogInformationWithHierarchy(context,
            "Extracted FileWriter configuration - FolderPath: {FolderPath}, ProcessingMode: {ProcessingMode}, ProcessedExtension: {ProcessedExtension}",
            config.FolderPath, config.ProcessingMode, config.ProcessedExtension);

        return Task.FromResult(config);
    }

    /// <summary>
    /// Validate the extracted configuration
    /// </summary>
    private Task ValidateConfigurationAsync(FileWriterConfiguration config, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Validating configuration");

        if (string.IsNullOrWhiteSpace(config.FolderPath))
        {
            throw new InvalidOperationException("FolderPath cannot be empty");
        }

        // Validate backup folder if needed for backup modes (aligned with other processors)
        var backupModes = new[]
        {
            FileProcessingMode.MoveToBackup,
            FileProcessingMode.CopyToBackup,
            FileProcessingMode.BackupAndMarkProcessed,
            FileProcessingMode.BackupAndDelete
        };

        if (backupModes.Contains(config.ProcessingMode) && string.IsNullOrWhiteSpace(config.BackupFolder))
        {
            throw new InvalidOperationException($"BackupFolder is required for processing mode: {config.ProcessingMode}");
        }

        _logger.LogDebugWithHierarchy(context, "Configuration validation completed successfully");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Extract cache data array from centrally deserialized input data
    /// </summary>
    private JsonElement[] GetCacheDataArray(object? inputData, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Extracting cache data array from deserialized input data");

        if (inputData == null)
        {
            throw new InvalidOperationException("Input data is null - FileWriter expects cache data array from FileReaderProcessor");
        }

        if (inputData is not JsonElement jsonElement)
        {
            throw new InvalidOperationException("Input data is not a JsonElement - FileWriter expects JSON array from FileReaderProcessor");
        }

        if (jsonElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Input data is not a JSON array - FileWriter expects compressed file cache data array from FileReaderProcessor");
        }

        var arrayElements = jsonElement.EnumerateArray().ToArray();
        _logger.LogInformationWithHierarchy(context, "Successfully extracted compressed file cache data array with {ItemCount} items", arrayElements.Length);

        return arrayElements;
    }

    /// <summary>
    /// Process compressed file cache data with exception handling
    /// </summary>
    private async Task<ProcessedActivityData> ProcessFileCacheDataWithExceptionHandlingAsync(
        JsonElement cacheDataItem,
        FileWriterConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        try
        {
            var result = await ProcessFileCacheDataAsync(
                cacheDataItem, config, context, cancellationToken);

            var processingDuration = DateTime.UtcNow - processingStart;
            _logger.LogDebugWithHierarchy(context,
                "Successfully processed compressed file cache data item in {Duration}ms",
                processingDuration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;

            // Record plugin exception for processing failure
            _metricsService.RecordPluginException(
                exceptionType: ex.GetType().Name,
                severity: "error",
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to process compressed file cache data item in {Duration}ms",
                processingDuration.TotalMilliseconds);

            return new ProcessedActivityData
            {
                Result = $"Error processing compressed file cache data: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                Data = new { },
                ProcessorName = "FileWriterProcessor",
                Version = "1.0",
                ExecutionId = context.ExecutionId!.Value
            };
        }
    }

    /// <summary>
    /// Process compressed file cache data and write extracted files to disk
    /// </summary>
    private async Task<ProcessedActivityData> ProcessFileCacheDataAsync(
        JsonElement cacheDataItem,
        FileWriterConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        _logger.LogInformationWithHierarchy(context, "Processing compressed file cache data for writing");

        try
        {
            // Parse the cache data structure
            var cacheDataJson = JsonSerializer.Serialize(cacheDataItem);
            var cacheDataDoc = JsonDocument.Parse(cacheDataJson);
            var root = cacheDataDoc.RootElement;

            // Extract file cache data object
            if (!root.TryGetProperty("fileCacheDataObject", out JsonElement fileCacheDataObject))
            {
                throw new InvalidOperationException("Missing fileCacheDataObject in cache data");
            }

            // Extract extracted files array (can be empty for regular files or empty archives)
            var extractedFiles = new List<ProcessedFileInfo>();
            if (fileCacheDataObject.TryGetProperty("extractedFiles", out JsonElement extractedFilesElement))
            {
                foreach (var fileElement in extractedFilesElement.EnumerateArray())
                {
                    var fileInfo = ParseExtractedFileInfo(fileElement, context);
                    if (fileInfo != null && IsFileSizeValid(fileInfo, config, context))
                    {
                        extractedFiles.Add(fileInfo);
                    }
                }
            }

            _logger.LogInformationWithHierarchy(context,
                "Found {ExtractedFileCount} valid extracted files to write (empty list is valid for regular files or empty archives)",
                extractedFiles.Count);

            // Write the original file first
            var writtenFiles = new List<string>();
            var totalBytesWritten = 0L;

            // Parse and write the original file from fileCacheDataObject
            var originalFileInfo = ParseOriginalFileInfo(fileCacheDataObject, context);
            if (originalFileInfo != null && IsFileSizeValid(originalFileInfo, config, context))
            {
                var originalOutputPath = await WriteExtractedFileAsync(
                    originalFileInfo, config, context, cancellationToken);

                writtenFiles.Add(originalOutputPath);
                totalBytesWritten += originalFileInfo.FileContent?.Length ?? 0;

                _logger.LogInformationWithHierarchy(context,
                    "Successfully wrote original file: {FileName} ({FileSize} bytes)",
                    originalFileInfo.FileName, originalFileInfo.FileContent?.Length ?? 0);
            }

            // Write all extracted files (if any)
            foreach (var fileInfo in extractedFiles)
            {
                var outputFilePath = await WriteExtractedFileAsync(
                    fileInfo, config, context, cancellationToken);

                writtenFiles.Add(outputFilePath);
                totalBytesWritten += fileInfo.FileContent?.Length ?? 0;
            }

            var processingDuration = DateTime.UtcNow - processingStart;
            var totalFilesProcessed = writtenFiles.Count; // Includes original file + extracted files

            // Record content processing metrics
            _metricsService.RecordContentProcessing(
                contentSize: totalBytesWritten,
                contentType: extractedFiles.Count > 0 ? "archive_with_files" : "regular_file_or_empty_archive",
                processingDuration: processingDuration,
                filesWritten: totalFilesProcessed,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Record writing throughput metrics
            var bytesPerSecond = processingDuration.TotalSeconds > 0
                ? (long)(totalBytesWritten / processingDuration.TotalSeconds)
                : totalBytesWritten;
            var filesPerSecond = processingDuration.TotalSeconds > 0
                ? (long)(totalFilesProcessed / processingDuration.TotalSeconds)
                : totalFilesProcessed;
            var recordsPerSecond = filesPerSecond; // For FileWriter, records = files

            _metricsService.RecordWritingThroughput(
                bytesPerSecond: bytesPerSecond,
                filesPerSecond: filesPerSecond,
                recordsPerSecond: recordsPerSecond,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Log appropriate message based on what was written
            if (extractedFiles.Count > 0)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Successfully wrote original file + {ExtractedFileCount} extracted files = {TotalFileCount} total files, Total size: {TotalSize} bytes, Duration: {Duration}ms",
                    extractedFiles.Count, totalFilesProcessed, totalBytesWritten, processingDuration.TotalMilliseconds);
            }
            else
            {
                _logger.LogInformationWithHierarchy(context,
                    "Successfully wrote original file (no extracted files), Total files: {TotalFileCount}, Total size: {TotalSize} bytes, Duration: {Duration}ms",
                    totalFilesProcessed, totalBytesWritten, processingDuration.TotalMilliseconds);
            }

            return new ProcessedActivityData
            {
                Status = ActivityExecutionStatus.Completed,
                Data = new { },
                Result = extractedFiles.Count > 0
                    ? $"Successfully wrote original file + {extractedFiles.Count} extracted files = {writtenFiles.Count} total files ({totalBytesWritten} bytes)"
                    : $"Successfully wrote original file ({totalBytesWritten} bytes)",
                ProcessorName = "FileWriterProcessor",
                Version = "1.0",
                ExecutionId = context.ExecutionId!.Value
            };
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to process compressed file cache data in {Duration}ms",
                processingDuration.TotalMilliseconds);

            // This exception is already recorded in the calling method

            return new ProcessedActivityData
            {
                Result = $"Error processing compressed file cache data: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                Data = new { },
                ProcessorName = "FileWriterProcessor",
                Version = "1.0",
                ExecutionId = context.ExecutionId!.Value
            };
        }
    }

    /// <summary>
    /// Parse original file info from fileCacheDataObject
    /// </summary>
    private ProcessedFileInfo? ParseOriginalFileInfo(JsonElement fileCacheDataObject, HierarchicalLoggingContext context)
    {
        try
        {
            // Extract fileMetadata
            if (!fileCacheDataObject.TryGetProperty("fileMetadata", out JsonElement fileMetadata))
            {
                _logger.LogWarningWithHierarchy(context, "Missing fileMetadata in fileCacheDataObject");
                return null;
            }

            // Extract fileContent
            if (!fileCacheDataObject.TryGetProperty("fileContent", out JsonElement fileContent))
            {
                _logger.LogWarningWithHierarchy(context, "Missing fileContent in fileCacheDataObject");
                return null;
            }

            // Parse all file metadata properties
            var fileName = fileMetadata.TryGetProperty("fileName", out var fileNameElement) ? fileNameElement.GetString() ?? "" : "";
            var filePath = fileMetadata.TryGetProperty("filePath", out var filePathElement) ? filePathElement.GetString() ?? "" : "";
            var fileSize = fileMetadata.TryGetProperty("fileSize", out var fileSizeElement) ? fileSizeElement.GetInt64() : 0;
            var createdDate = fileMetadata.TryGetProperty("createdDate", out var createdElement) ?
                (DateTime.TryParse(createdElement.GetString(), out var created) ? created : DateTime.UtcNow) : DateTime.UtcNow;
            var modifiedDate = fileMetadata.TryGetProperty("modifiedDate", out var modifiedElement) ?
                (DateTime.TryParse(modifiedElement.GetString(), out var modified) ? modified : DateTime.UtcNow) : DateTime.UtcNow;
            var fileExtension = fileMetadata.TryGetProperty("fileExtension", out var extensionElement) ? extensionElement.GetString() ?? "" : "";
            var detectedMimeType = fileMetadata.TryGetProperty("detectedMimeType", out var mimeTypeElement) ? mimeTypeElement.GetString() ?? "" : "";
            var fileType = fileMetadata.TryGetProperty("fileType", out var fileTypeElement) ? fileTypeElement.GetString() ?? "" : "";
            var contentHash = fileMetadata.TryGetProperty("contentHash", out var contentHashElement) ? contentHashElement.GetString() ?? "" : "";

            // Parse file content
            var binaryData = fileContent.TryGetProperty("binaryData", out var binaryElement) ? binaryElement.GetString() ?? "" : "";
            var encoding = fileContent.TryGetProperty("encoding", out var encodingElement) ? encodingElement.GetString() ?? "base64" : "base64";

            // Decode content
            byte[] contentBytes;
            if (encoding == "base64" && !string.IsNullOrEmpty(binaryData))
            {
                contentBytes = Convert.FromBase64String(binaryData);
            }
            else
            {
                _logger.LogWarningWithHierarchy(context, "Unsupported encoding or empty content: {Encoding}", encoding);
                return null;
            }

            // Create ProcessedFileInfo with all properties set
            return new ProcessedFileInfo
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize,
                CreatedDate = createdDate,
                ModifiedDate = modifiedDate,
                FileExtension = fileExtension,
                DetectedMimeType = detectedMimeType,
                FileType = fileType,
                FileContent = contentBytes,
                ContentHash = contentHash,
                ContentEncoding = encoding == "base64" ? "binary" : encoding // Convert to ProcessedFileInfo encoding format
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithHierarchy(context, ex, "Failed to parse original file info");
            return null;
        }
    }

    /// <summary>
    /// Parse extracted file info from JSON element
    /// </summary>
    private ProcessedFileInfo? ParseExtractedFileInfo(JsonElement fileElement, HierarchicalLoggingContext context)
    {
        try
        {
            var fileName = fileElement.GetProperty("fileName").GetString() ?? "";
            var filePath = fileElement.GetProperty("filePath").GetString() ?? "";
            var fileSize = fileElement.GetProperty("fileSize").GetInt64();
            var fileExtension = fileElement.GetProperty("fileExtension").GetString() ?? "";
            var fileType = fileElement.GetProperty("fileType").GetString() ?? "";
            var contentEncoding = fileElement.GetProperty("contentEncoding").GetString() ?? "";
            var detectedMimeType = fileElement.GetProperty("detectedMimeType").GetString() ?? "";
            var contentHash = fileElement.GetProperty("contentHash").GetString() ?? "";

            // Parse dates
            var createdDate = DateTime.TryParse(fileElement.GetProperty("createdDate").GetString(), out var created) ? created : DateTime.UtcNow;
            var modifiedDate = DateTime.TryParse(fileElement.GetProperty("modifiedDate").GetString(), out var modified) ? modified : DateTime.UtcNow;

            // Decode file content from Base64
            var fileContentBase64 = fileElement.GetProperty("fileContent").GetString() ?? "";
            var fileContent = Convert.FromBase64String(fileContentBase64);

            return new ProcessedFileInfo
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize,
                CreatedDate = createdDate,
                ModifiedDate = modifiedDate,
                FileExtension = fileExtension,
                FileContent = fileContent,
                ContentEncoding = contentEncoding,
                DetectedMimeType = detectedMimeType,
                FileType = fileType,
                ContentHash = contentHash
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithHierarchy(context, ex, "Failed to parse extracted file info");
            return null;
        }
    }

    /// <summary>
    /// Check if file size is within configured limits
    /// </summary>
    private bool IsFileSizeValid(ProcessedFileInfo fileInfo, FileWriterConfiguration config, HierarchicalLoggingContext context)
    {
        var fileSize = fileInfo.FileContent?.Length ?? 0;

        if (fileSize < config.MinExtractedContentSize)
        {
            _logger.LogDebugWithHierarchy(context,
                "Skipping file due to size below minimum - File: {FileName}, Size: {Size}, Min: {Min}",
                fileInfo.FileName, fileSize, config.MinExtractedContentSize);
            return false;
        }

        if (fileSize > config.MaxExtractedContentSize)
        {
            _logger.LogDebugWithHierarchy(context,
                "Skipping file due to size above maximum - File: {FileName}, Size: {Size}, Max: {Max}",
                fileInfo.FileName, fileSize, config.MaxExtractedContentSize);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Write extracted file to disk with full processing pipeline
    /// </summary>
    private async Task<string> WriteExtractedFileAsync(
        ProcessedFileInfo fileInfo,
        FileWriterConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        _logger.LogDebugWithHierarchy(context,
            "Writing extracted file - Name: {FileName}, Size: {Size} bytes",
            fileInfo.FileName, fileInfo.FileContent?.Length ?? 0);

        string outputFilePath = string.Empty;
        try
        {
            // Generate output file path preserving original filename with collision handling
            outputFilePath = await GenerateOutputFilePathAsync(fileInfo, config, context);

            // Ensure output directory exists
            var outputDirectory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                _logger.LogDebugWithHierarchy(context, "Created output directory: {Directory}", outputDirectory);
            }

            // Write the file (same pattern as FileWriterProcessor)
            var writeStart = DateTime.UtcNow;
            await WriteFileAsync(outputFilePath, fileInfo, context, cancellationToken);
            var writeDuration = DateTime.UtcNow - writeStart;

            // Record file write metrics
            _metricsService.RecordFileWrite(
                bytesWritten: fileInfo.FileContent?.Length ?? 0,
                filePath: outputFilePath,
                writeDuration: writeDuration,
                fileType: fileInfo.FileType,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Validate written file
            await ValidateWrittenFileAsync(outputFilePath, fileInfo, context);

            // Post-process the file using shared utilities with hierarchical logging
            var postProcessStart = DateTime.UtcNow;
            await FilePostProcessing.PostProcessFileAsync(outputFilePath, config, context, _logger);
            var postProcessDuration = DateTime.UtcNow - postProcessStart;

            // Record data output metrics for post-processing
            _metricsService.RecordDataOutput(
                outputType: $"post_processing_{config.ProcessingMode}",
                recordsProcessed: 1,
                recordsSuccessful: 1,
                outputDuration: postProcessDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            var processingDuration = DateTime.UtcNow - processingStart;
            _logger.LogInformationWithHierarchy(context,
                "Successfully wrote extracted file - Path: {FilePath}, Size: {Size} bytes, Duration: {Duration}ms",
                outputFilePath, fileInfo.FileContent?.Length ?? 0, processingDuration.TotalMilliseconds);

            return outputFilePath;
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;
            // Record file write failure
            _metricsService.RecordFileWriteFailure(
                filePath: outputFilePath ?? fileInfo.FileName,
                failureReason: ex.GetType().Name,
                fileType: fileInfo.FileType,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to write extracted file - Name: {FileName}, Duration: {Duration}ms",
                fileInfo.FileName, processingDuration.TotalMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// Generate output file path preserving original filename with collision handling using shared utilities
    /// </summary>
    private Task<string> GenerateOutputFilePathAsync(
        ProcessedFileInfo fileInfo,
        FileWriterConfiguration config,
        HierarchicalLoggingContext context)
    {
        // Use standardized collision handling from shared utilities
        var outputFilePath = FilePostProcessing.GenerateOutputPath(
            fileInfo.FileName,
            config.FolderPath);

        _logger.LogDebugWithHierarchy(context,
            "Generated output file path - Original: {Original}, Output: {Output}",
            fileInfo.FileName, outputFilePath);

        return Task.FromResult(outputFilePath);
    }

    /// <summary>
    /// Write file content to disk (same pattern as FileWriterProcessor)
    /// </summary>
    private async Task WriteFileAsync(string filePath, ProcessedFileInfo fileInfo, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (fileInfo.FileContent == null)
            {
                throw new InvalidOperationException("File content is null");
            }

            await File.WriteAllBytesAsync(filePath, fileInfo.FileContent, cancellationToken);

            // Preserve original metadata (same pattern as FileWriterProcessor)
            var fileInfo_system = new FileInfo(filePath);
            fileInfo_system.CreationTime = fileInfo.CreatedDate;
            fileInfo_system.LastWriteTime = fileInfo.ModifiedDate;

            _logger.LogDebugWithHierarchy(context,
                "Successfully wrote file - Path: {FilePath}, Size: {Size} bytes",
                filePath, fileInfo.FileContent.Length);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to write file - Path: {FilePath}",
                filePath);
            throw;
        }
    }

    /// <summary>
    /// Validate written file (same pattern as FileWriterProcessor)
    /// </summary>
    private Task ValidateWrittenFileAsync(string filePath, ProcessedFileInfo originalFileInfo, HierarchicalLoggingContext context)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"Written file does not exist: {filePath}");
            }

            var writtenFileInfo = new FileInfo(filePath);
            var expectedSize = originalFileInfo.FileContent?.Length ?? 0;

            if (writtenFileInfo.Length != expectedSize)
            {
                throw new InvalidOperationException(
                    $"File size mismatch - Expected: {expectedSize}, Actual: {writtenFileInfo.Length}");
            }

            _logger.LogDebugWithHierarchy(context,
                "File validation successful - Path: {FilePath}, Size: {Size} bytes",
                filePath, writtenFileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "File validation failed - Path: {FilePath}",
                filePath);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if a file name matches the search pattern
    /// Supports patterns like "*.{txt,zip,rar,7z,gz,tar}" or simple patterns like "*.txt"
    /// </summary>
    private static bool MatchesSearchPattern(string fileName, string searchPattern)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(searchPattern))
            return false;

        // Handle simple patterns like "*.txt"
        if (!searchPattern.Contains('{'))
        {
            return MatchesSimplePattern(fileName, searchPattern);
        }

        // Handle complex patterns like "*.{txt,zip,rar,7z,gz,tar}"
        return MatchesComplexPattern(fileName, searchPattern);
    }

    /// <summary>
    /// Match simple patterns like "*.txt"
    /// </summary>
    private static bool MatchesSimplePattern(string fileName, string pattern)
    {
        if (pattern == "*" || pattern == "*.*")
            return true;

        if (pattern.StartsWith("*."))
        {
            var extension = pattern.Substring(2);
            return fileName.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase);
        }

        // For other patterns, use basic wildcard matching
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Match complex patterns like "*.{txt,zip,rar,7z,gz,tar}"
    /// </summary>
    private static bool MatchesComplexPattern(string fileName, string pattern)
    {
        // Extract the extensions from the pattern
        var startBrace = pattern.IndexOf('{');
        var endBrace = pattern.IndexOf('}');

        if (startBrace == -1 || endBrace == -1 || endBrace <= startBrace)
            return false;

        var prefix = pattern.Substring(0, startBrace);
        var extensionsString = pattern.Substring(startBrace + 1, endBrace - startBrace - 1);
        var extensions = extensionsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(ext => ext.Trim())
                                       .ToArray();

        // Check if the file matches any of the extensions
        foreach (var extension in extensions)
        {
            var fullPattern = prefix + extension;
            if (MatchesSimplePattern(fileName, fullPattern))
                return true;
        }

        return false;
    }
}
