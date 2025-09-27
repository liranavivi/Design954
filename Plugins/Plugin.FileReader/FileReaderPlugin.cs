using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.FileReader.Interfaces;
using Plugin.FileReader.Models;
using Plugin.FileReader.Services;
using Plugin.FileReader.Utilities;
using Plugin.Shared.Interfaces;
using Plugin.Shared.Utilities;
using Processor.Base.Models;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Models;
using Shared.Services.Interfaces;
using SharpCompress.Archives;

namespace Plugin.FileReader;

/// <summary>
/// FileReader plugin implementation that handles compressed archives (ZIP, RAR, 7-Zip, GZIP, TAR)
/// Implements IPlugin interface for dynamic loading by PluginLoaderProcessor
/// </summary>
public class FileReaderPlugin : IPlugin
{
    private readonly ILogger<FileReaderPlugin> _logger;
    private readonly IFileReaderPluginMetricsService _metricsService;
    

    /// <summary>
    /// Constructor with dependency injection using standardized plugin pattern
    /// Aligned with PreFileReaderPlugin architecture
    /// </summary>
    public FileReaderPlugin(
        string pluginCompositeKey,
        ILogger<FileReaderPlugin> logger,
        ICacheService cacheService)
    {
        // Store host-provided services with null check
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create metrics service with composite key (same pattern as PreFileReaderPlugin)
        _metricsService = new FileReaderPluginMetricsService(pluginCompositeKey, _logger);

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Plugin implementation of ProcessActivityDataAsync
    /// Enhanced with hierarchical logging support - maintains consistent ID ordering
    /// Handles both discovery phase (executionId empty) and processing phase (executionId populated)
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
        object? inputData, // Discovery: null | Processing: JsonElement with cached file path
        CancellationToken cancellationToken = default)
    {
        var processingStart = DateTime.UtcNow;

        // Create Layer 6 hierarchical context for FileReader plugin
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
            "Starting FileReader plugin processing");

        try
        {
            // 1. Validate entities collection - must have at least one DeliveryAssignmentModel
            var deliveryAssignment = entities.OfType<DeliveryAssignmentModel>().FirstOrDefault();
            if (deliveryAssignment == null)
            {
                throw new InvalidOperationException("DeliveryAssignmentModel not found in entities. FileReaderPlugin expects at least one DeliveryAssignmentModel.");
            }

            _logger.LogInformationWithHierarchy(context,
                "Processing {EntityCount} entities with DeliveryAssignmentModel: {DeliveryName} (EntityId: {EntityId})",
                entities.Count, deliveryAssignment.Name, deliveryAssignment.EntityId);

            // 1. Extract configuration from DeliveryAssignmentModel (fresh every time - stateless)
            var config = await ExtractConfigurationFromDeliveryAssignmentAsync(deliveryAssignment, context);

            // 2. Validate configuration
            await ValidateConfigurationAsync(config, context);

            // Processing phase: process individual file from cache
            _logger.LogInformationWithHierarchy(context,
                "Starting individual file processing phase");
            var result = await ProcessIndividualFileAsync(
                config, inputData, context, cancellationToken);
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
                "FileReader plugin processing failed. Duration: {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result
            return new[]
            {
                new ProcessedActivityData
                {
                    Result = $"Error in FileReader plugin processing: {ex.Message}",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "FileReaderProcessor", // Keep same name for compatibility
                    Version = "1.0",
                    ExecutionId = executionId
                }
            };
        }
    }

    
    private Task<FileReaderConfiguration> ExtractConfigurationFromDeliveryAssignmentAsync(
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
            throw new InvalidOperationException($"Invalid JSON in DeliveryAssignmentModel.Payload", ex);
        }

        // Extract configuration using shared utilities
        var config = new FileReaderConfiguration
        {
            ProcessingMode = JsonConfigurationExtractor.GetEnumValue<FileProcessingMode>(root, "processingMode", FileProcessingMode.LeaveUnchanged),
            ProcessedExtension = JsonConfigurationExtractor.GetStringValue(root, "processedExtension", ".processed"),
            BackupFolder = JsonConfigurationExtractor.GetStringValue(root, "backupFolder", ""),

            // File size filters
            MinFileSize = JsonConfigurationExtractor.GetLongValue(root, "minFileSize", 0),
            MaxFileSize = JsonConfigurationExtractor.GetLongValue(root, "maxFileSize", 100 * 1024 * 1024), // 100MB
            MinExtractedSize = JsonConfigurationExtractor.GetLongValue(root, "minExtractedSize", 0),
            MaxExtractedSize = JsonConfigurationExtractor.GetLongValue(root, "maxExtractedSize", 50 * 1024 * 1024), // 50MB

            // Archive analysis
            MinEntriesToList = JsonConfigurationExtractor.GetIntValue(root, "minEntriesToList", 1),
            MaxEntriesToList = JsonConfigurationExtractor.GetIntValue(root, "maxEntriesToList", 100)
        };

        _logger.LogInformationWithHierarchy(context,
            "Extracted FileReader configuration from DeliveryAssignmentModel - MinFileSize: {MinFileSize}, MaxFileSize: {MaxFileSize}, MinEntries: {MinEntries}, MaxEntries: {MaxEntries}",
            config.MinFileSize, config.MaxFileSize, config.MinEntriesToList, config.MaxEntriesToList);

        return Task.FromResult(config);
    }

    private Task ValidateConfigurationAsync(FileReaderConfiguration config, HierarchicalLoggingContext context)
    {
        _logger.LogInformationWithHierarchy(context, "Validating FileReader configuration");

        if (config.MaxFileSize <= 0)
        {
            throw new InvalidOperationException($"MaxFileSize must be greater than 0, but was {config.MaxFileSize}");
        }

        if (config.MinFileSize < 0)
        {
            throw new InvalidOperationException($"MinFileSize must be greater than or equal to 0, but was {config.MinFileSize}");
        }

        if (config.MinFileSize > config.MaxFileSize)
        {
            throw new InvalidOperationException($"MinFileSize ({config.MinFileSize}) cannot be greater than MaxFileSize ({config.MaxFileSize})");
        }

        if (config.MaxExtractedSize <= 0)
        {
            throw new InvalidOperationException($"MaxExtractedSize must be greater than 0, but was {config.MaxExtractedSize}");
        }

        if (config.MinExtractedSize < 0)
        {
            throw new InvalidOperationException($"MinExtractedSize must be greater than or equal to 0, but was {config.MinExtractedSize}");
        }

        if (config.MinExtractedSize > config.MaxExtractedSize)
        {
            throw new InvalidOperationException($"MinExtractedSize ({config.MinExtractedSize}) cannot be greater than MaxExtractedSize ({config.MaxExtractedSize})");
        }

        if (config.MinEntriesToList < 0)
        {
            throw new InvalidOperationException($"MinEntriesToList must be greater than or equal to 0, but was {config.MinEntriesToList}");
        }

        if (config.MaxEntriesToList <= 0)
        {
            throw new InvalidOperationException($"MaxEntriesToList must be greater than 0, but was {config.MaxEntriesToList}");
        }

        if (config.MinEntriesToList > config.MaxEntriesToList)
        {
            throw new InvalidOperationException($"MinEntriesToList ({config.MinEntriesToList}) cannot be greater than MaxEntriesToList ({config.MaxEntriesToList})");
        }

        return Task.CompletedTask;
    }

    
    /// <summary>
    /// Process individual file from cache with post-processing after successful file reading and content processing
    /// </summary>
    private async Task<ProcessedActivityData> ProcessIndividualFileAsync(
        FileReaderConfiguration config,
        object? inputData,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        // Extract file path from deserialized input data
        string filePath;
        if (inputData is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
        {
            filePath = jsonElement.GetString() ?? throw new InvalidOperationException("File path is null in input data");
        }
        else if (inputData is string stringData)
        {
            filePath = stringData.Trim('"');
        }
        else
        {
            throw new InvalidOperationException("Input data must contain a file path string");
        }

        _logger.LogInformationWithHierarchy(context,
            "Processing individual file: {FilePath}",
            filePath);

        try
        {
            // 1. File reading and validation FIRST
            if (!File.Exists(filePath))
            {
                _logger.LogWarningWithHierarchy(context, "File not found: {FilePath}", filePath);
                return new ProcessedActivityData
                {
                    Result = $"Error in individual file processing: File not found: {filePath}",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "FileReaderProcessor",
                    Version = "1.0",
                    ExecutionId = context.ExecutionId!.Value
                };
            }

            var fileInfo = new FileInfo(filePath);
            var fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);

            _logger.LogDebugWithHierarchy(context, "Read file: {FilePath}, Size: {Size} bytes", filePath, fileContent.Length);

            // Check file size against configuration limits
            if (fileContent.Length < config.MinFileSize)
            {
                _logger.LogWarningWithHierarchy(context, "File size {Size} is below minimum size {MinSize}",
                    fileContent.Length, config.MinFileSize);
                return new ProcessedActivityData
                {
                    Result = $"File size {fileContent.Length} bytes is below minimum size {config.MinFileSize} bytes",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "FileReaderProcessor",
                    Version = "1.0",
                    ExecutionId = context.ExecutionId!.Value
                };
            }

            if (fileContent.Length > config.MaxFileSize)
            {
                _logger.LogWarningWithHierarchy(context, "File size {Size} exceeds maximum size {MaxSize}",
                    fileContent.Length, config.MaxFileSize);
                return new ProcessedActivityData
                {
                    Result = $"File size {fileContent.Length} bytes exceeds maximum size {config.MaxFileSize} bytes",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "FileReaderProcessor",
                    Version = "1.0",
                    ExecutionId = context.ExecutionId!.Value
                };
            }

            // 2. Process file content (MIME detection, extraction, cache data creation)
            var result = await ProcessFileContentAsync(
                fileContent, filePath, fileInfo, config, context, cancellationToken);

            // 3. FilePostProcessing AFTER successful processing with hierarchical logging
            _logger.LogDebugWithHierarchy(context, "Starting post-processing for file: {FilePath}", filePath);
            await FilePostProcessing.PostProcessFileAsync<FileReaderConfiguration>(filePath, config, context, _logger);
            _logger.LogDebugWithHierarchy(context, "Post-processing completed for file: {FilePath}", filePath);

            _logger.LogInformationWithHierarchy(context, "Successfully completed processing for file: {FilePath}", filePath);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to process individual file: {FilePath}",
                filePath);

            return new ProcessedActivityData
            {
                Result = $"Error in individual file processing: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                Data = new { },
                ProcessorName = "FileReaderProcessor",
                Version = "1.0",
                ExecutionId = context.ExecutionId!.Value
            };
        }
    }

    /// <summary>
    /// Process file content including MIME detection, extraction, and cache data creation
    /// </summary>
    private async Task<ProcessedActivityData> ProcessFileContentAsync(
        byte[] fileContent,
        string filePath,
        FileInfo fileInfo,
        FileReaderConfiguration config,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        _logger.LogDebugWithHierarchy(context, "Processing file content: {FilePath}, Size: {Size} bytes", filePath, fileContent.Length);

        try
        {
            // 1. MIME type detection using local method
            var detectedMimeType = DetectMimeType(fileContent, extension);

            // 2. Determine file type and process accordingly
            var fileType = DetermineFileType(extension);
            var extractedFiles = new List<ProcessedFileInfo>();

            // 3. Archive extraction if applicable
            if (IsArchiveFile(extension))
            {
                try
                {
                    extractedFiles = await ExtractArchiveAsync(filePath, fileContent, extension, config, context, cancellationToken);
                    _logger.LogInformationWithHierarchy(context, "Extracted {Count} files from archive: {FilePath}", extractedFiles.Count, filePath);

                    // Check extracted files count against configuration limits
                    if (extractedFiles.Count < config.MinEntriesToList)
                    {
                        _logger.LogWarningWithHierarchy(context, "Archive {FilePath} extracted {Count} files, below minimum {MinCount}",
                            filePath, extractedFiles.Count, config.MinEntriesToList);
                        return new ProcessedActivityData
                        {
                            Result = $"Archive extracted {extractedFiles.Count} files, below minimum {config.MinEntriesToList}",
                            Status = ActivityExecutionStatus.Failed,
                            Data = new { },
                            ProcessorName = "FileReaderProcessor",
                            Version = "1.0",
                            ExecutionId = context.ExecutionId!.Value
                        };
                    }

                    if (extractedFiles.Count > config.MaxEntriesToList)
                    {
                        _logger.LogWarningWithHierarchy(context, "Archive {FilePath} extracted {Count} files, above maximum {MaxCount}",
                            filePath, extractedFiles.Count, config.MaxEntriesToList);
                        return new ProcessedActivityData
                        {
                            Result = $"Archive extracted {extractedFiles.Count} files, above maximum {config.MaxEntriesToList}",
                            Status = ActivityExecutionStatus.Failed,
                            Data = new { },
                            ProcessorName = "FileReaderProcessor",
                            Version = "1.0",
                            ExecutionId = context.ExecutionId!.Value
                        };
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("is outside allowed range"))
                {
                    // Handle extracted file size validation failures
                    _logger.LogWarningWithHierarchy(context, "Archive extraction failed due to size constraints: {Message}", ex.Message);
                    return new ProcessedActivityData
                    {
                        Result = $"Archive extraction failed: {ex.Message}",
                        Status = ActivityExecutionStatus.Failed,
                        Data = new { },
                        ProcessorName = "FileReaderProcessor",
                        Version = "1.0",
                        ExecutionId = context.ExecutionId!.Value
                    };
                }
            }

            // 4. Create file cache data object
            var fileCacheData = CreateFileCacheDataObject(filePath, fileContent, detectedMimeType, fileType, extractedFiles, fileInfo);

            var processingDuration = DateTime.UtcNow - processingStart;

            // 5. Record metrics (aligned with PreFileReaderPlugin architecture)

            // Record file read operation
            _metricsService.RecordFileRead(
                bytesRead: fileContent.Length,
                filePath: filePath,
                readDuration: processingDuration,
                fileType: fileType,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Record content processing
            _metricsService.RecordContentProcessing(
                contentSize: fileContent.Length,
                contentType: detectedMimeType,
                processingDuration: processingDuration,
                recordsExtracted: extractedFiles.Count,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Record data extraction if files were extracted
            if (extractedFiles.Count > 0)
            {
                _metricsService.RecordDataExtraction(
                    extractionType: fileType,
                    recordsProcessed: extractedFiles.Count,
                    recordsSuccessful: extractedFiles.Count, // All extracted files are considered successful
                    extractionDuration: processingDuration,
                    correlationId: context.CorrelationId.ToString(),
                    orchestratedFlowId: context.OrchestratedFlowId,
                    stepId: context.StepId!.Value,
                    executionId: context.ExecutionId!.Value,
                    context: context);
            }

            // Record reading throughput metrics
            var bytesPerSecond = processingDuration.TotalSeconds > 0
                ? (long)(fileContent.Length / processingDuration.TotalSeconds)
                : fileContent.Length;
            var filesPerSecond = processingDuration.TotalSeconds > 0
                ? (long)(1 / processingDuration.TotalSeconds)
                : 1;
            var recordsPerSecond = processingDuration.TotalSeconds > 0
                ? (long)(extractedFiles.Count / processingDuration.TotalSeconds)
                : extractedFiles.Count;

            _metricsService.RecordReadingThroughput(
                bytesPerSecond: bytesPerSecond,
                filesPerSecond: filesPerSecond,
                recordsPerSecond: recordsPerSecond,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            _logger.LogInformationWithHierarchy(context, "Successfully processed file content: {FilePath} in {Duration}ms", filePath, processingDuration.TotalMilliseconds);

            return new ProcessedActivityData
            {
                Status = ActivityExecutionStatus.Completed,
                Data = fileCacheData,
                Result = $"Successfully processed {fileType} file: {Path.GetFileName(filePath)} ({extractedFiles.Count} extracted files)",
                ProcessorName = "FileReaderProcessor",
                Version = "1.0",
                ExecutionId = context.ExecutionId!.Value
            };
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;

            // Record file read failure
            _metricsService.RecordFileReadFailure(
                filePath: filePath,
                failureReason: ex.GetType().Name,
                fileType: DetermineFileType(Path.GetExtension(filePath).ToLowerInvariant()),
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

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
                "Failed to process file content: {FilePath} in {Duration}ms",
                filePath, processingDuration.TotalMilliseconds);

            return new ProcessedActivityData
            {
                Result = $"Error processing file content: {ex.Message}",
                Status = ActivityExecutionStatus.Failed,
                Data = new { },
                ProcessorName = "FileReaderProcessor",
                Version = "1.0",
                ExecutionId = context.ExecutionId!.Value
            };
        }
    }

    /// <summary>
    /// Detect MIME type from file content and extension
    /// </summary>
    private string DetectMimeType(byte[] fileContent, string fileExtension)
    {
        // Basic MIME type detection based on file signatures
        if (fileContent.Length >= 4)
        {
            var signature = fileContent.Take(4).ToArray();

            // PDF
            if (signature.Take(4).SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 }))
                return "application/pdf";

            // PNG
            if (signature.Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }))
                return "image/png";

            // JPEG
            if (signature.Take(2).SequenceEqual(new byte[] { 0xFF, 0xD8 }))
                return "image/jpeg";

            // ZIP
            if (signature.Take(2).SequenceEqual(new byte[] { 0x50, 0x4B }))
                return "application/zip";

            // RAR (Rar! signature)
            if (signature.Take(4).SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21 }))
                return "application/x-rar-compressed";

            // 7-Zip
            if (fileContent.Length >= 6 && fileContent.Take(6).SequenceEqual(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))
                return "application/x-7z-compressed";

            // GIF
            if (signature.Take(3).SequenceEqual(new byte[] { 0x47, 0x49, 0x46 }))
                return "image/gif";

            // BMP
            if (signature.Take(2).SequenceEqual(new byte[] { 0x42, 0x4D }))
                return "image/bmp";
        }

        // Fallback to extension-based detection
        return fileExtension.ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".doc" => "application/msword",
            ".xls" => "application/vnd.ms-excel",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".rtf" => "application/rtf",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Determine file type from extension
    /// </summary>
    private static string DetermineFileType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".zip" => "ZIP Archive",
            ".rar" => "RAR Archive",
            ".7z" => "7-Zip Archive",
            ".tar" => "TAR Archive",
            ".gz" => "GZIP Archive",
            ".pdf" => "PDF Document",
            ".txt" => "Text File",
            ".json" => "JSON File",
            ".xml" => "XML File",
            ".csv" => "CSV File",
            ".docx" => "Word Document",
            ".xlsx" => "Excel Spreadsheet",
            ".pptx" => "PowerPoint Presentation",
            _ => "Unknown File"
        };
    }

    /// <summary>
    /// Check if file is an archive that can be extracted
    /// </summary>
    private static bool IsArchiveFile(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => true,
            _ => false
        };
    }

    /// <summary>
    /// Extract files from archive with size filtering
    /// </summary>
    private async Task<List<ProcessedFileInfo>> ExtractArchiveAsync(string filePath, byte[] fileContent, string extension, FileReaderConfiguration config, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var extractedFiles = new List<ProcessedFileInfo>();

        try
        {
            switch (extension.ToLowerInvariant())
            {
                case ".zip":
                    extractedFiles = await ExtractZipArchiveAsync(fileContent, config, context, cancellationToken);
                    break;
                case ".gz":
                    extractedFiles = await ExtractGzipArchiveAsync(fileContent, Path.GetFileNameWithoutExtension(filePath), config, context, cancellationToken);
                    break;
                case ".rar":
                    extractedFiles = await ExtractRarArchiveAsync(fileContent, config, context, cancellationToken);
                    break;
                case ".7z":
                    extractedFiles = await ExtractSevenZipArchiveAsync(fileContent, config, context, cancellationToken);
                    break;
                case ".tar":
                    extractedFiles = await ExtractTarArchiveAsync(fileContent, config, context, cancellationToken);
                    break;
                default:
                    _logger.LogWarningWithHierarchy(context, "Archive type {Extension} not yet implemented for extraction", extension);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to extract archive: {FilePath}", filePath);
            // Return empty list on extraction failure
        }

        return extractedFiles;
    }

    /// <summary>
    /// Extract ZIP archive with size filtering
    /// </summary>
    private async Task<List<ProcessedFileInfo>> ExtractZipArchiveAsync(byte[] zipContent, FileReaderConfiguration config, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var extractedFiles = new List<ProcessedFileInfo>();

        using var memoryStream = new MemoryStream(zipContent);
        using var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0 || entry.FullName.EndsWith('/')) continue; // Skip directories

            try
            {
                using var entryStream = entry.Open();
                using var contentStream = new MemoryStream();
                await entryStream.CopyToAsync(contentStream, cancellationToken);

                var content = contentStream.ToArray();
                var extension = Path.GetExtension(entry.Name);
                var contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(content));

                // Check size criteria - if any file doesn't meet criteria, fail entire processing
                if (content.Length < config.MinExtractedSize || content.Length > config.MaxExtractedSize)
                {
                    _logger.LogWarningWithHierarchy(context, "Extracted file {FileName} with size {Size} is outside range {MinSize}-{MaxSize}, failing entire archive processing",
                        entry.Name, content.Length, config.MinExtractedSize, config.MaxExtractedSize);
                    throw new InvalidOperationException($"Extracted file {entry.Name} size {content.Length} is outside allowed range {config.MinExtractedSize}-{config.MaxExtractedSize}");
                }

                extractedFiles.Add(new ProcessedFileInfo
                {
                    FileName = entry.Name,
                    FilePath = entry.FullName,
                    FileSize = content.Length,
                    CreatedDate = entry.LastWriteTime.DateTime,
                    ModifiedDate = entry.LastWriteTime.DateTime,
                    FileExtension = extension,
                    FileContent = content,
                    ContentEncoding = "binary",
                    DetectedMimeType = DetectMimeType(content, extension),
                    FileType = DetermineFileType(extension),
                    ContentHash = contentHash
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarningWithHierarchy(context, ex, "Failed to extract ZIP entry: {EntryName}", entry.FullName);
            }
        }

        return extractedFiles;
    }

    /// <summary>
    /// Extract GZIP archive with size filtering
    /// </summary>
    private async Task<List<ProcessedFileInfo>> ExtractGzipArchiveAsync(byte[] gzipContent, string originalFileName, FileReaderConfiguration config, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var extractedFiles = new List<ProcessedFileInfo>();

        try
        {
            using var compressedStream = new MemoryStream(gzipContent);
            using var gzipStream = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();

            await gzipStream.CopyToAsync(decompressedStream, cancellationToken);
            var content = decompressedStream.ToArray();
            var extension = Path.GetExtension(originalFileName);
            var contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(content));

            // Check size criteria - if file doesn't meet criteria, fail entire processing
            if (content.Length < config.MinExtractedSize || content.Length > config.MaxExtractedSize)
            {
                _logger.LogWarningWithHierarchy(context, "Extracted GZIP file {FileName} with size {Size} is outside range {MinSize}-{MaxSize}, failing entire archive processing",
                    originalFileName, content.Length, config.MinExtractedSize, config.MaxExtractedSize);
                throw new InvalidOperationException($"Extracted GZIP file {originalFileName} size {content.Length} is outside allowed range {config.MinExtractedSize}-{config.MaxExtractedSize}");
            }

            extractedFiles.Add(new ProcessedFileInfo
            {
                FileName = originalFileName,
                FilePath = originalFileName,
                FileSize = content.Length,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                FileExtension = extension,
                FileContent = content,
                ContentEncoding = "binary",
                DetectedMimeType = DetectMimeType(content, extension),
                FileType = DetermineFileType(extension),
                ContentHash = contentHash
            });
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to extract GZIP content");
        }

        return extractedFiles;
    }

    /// <summary>
    /// Extract RAR archive using SharpCompress with size filtering
    /// </summary>
    private async Task<List<ProcessedFileInfo>> ExtractRarArchiveAsync(byte[] rarContent, FileReaderConfiguration config, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var extractedFiles = new List<ProcessedFileInfo>();

        try
        {
            using var memoryStream = new MemoryStream(rarContent);
            using var archive = ArchiveFactory.Open(memoryStream);

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || entry.Size == 0) continue; // Skip directories and empty files

                try
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var contentStream = new MemoryStream();
                    await entryStream.CopyToAsync(contentStream, cancellationToken);

                    var content = contentStream.ToArray();
                    var extension = Path.GetExtension(entry.Key);
                    var contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(content));

                    // Check size criteria - if any file doesn't meet criteria, fail entire processing
                    if (content.Length < config.MinExtractedSize || content.Length > config.MaxExtractedSize)
                    {
                        _logger.LogWarningWithHierarchy(context, "Extracted RAR file {FileName} with size {Size} is outside range {MinSize}-{MaxSize}, failing entire archive processing",
                            entry.Key ?? "unknown", content.Length, config.MinExtractedSize, config.MaxExtractedSize);
                        throw new InvalidOperationException($"Extracted RAR file {entry.Key} size {content.Length} is outside allowed range {config.MinExtractedSize}-{config.MaxExtractedSize}");
                    }

                    extractedFiles.Add(new ProcessedFileInfo
                    {
                        FileName = Path.GetFileName(entry.Key) ?? string.Empty,
                        FilePath = entry.Key ?? string.Empty,
                        FileSize = content.Length,
                        CreatedDate = entry.CreatedTime ?? DateTime.UtcNow,
                        ModifiedDate = entry.LastModifiedTime ?? DateTime.UtcNow,
                        FileExtension = extension ?? string.Empty,
                        FileContent = content,
                        ContentEncoding = "binary",
                        DetectedMimeType = DetectMimeType(content, extension ?? string.Empty),
                        FileType = DetermineFileType(extension ?? string.Empty),
                        ContentHash = contentHash
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarningWithHierarchy(context, ex, "Failed to extract entry {EntryName} from RAR archive", entry.Key ?? "unknown");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to extract RAR archive");
        }

        return extractedFiles;
    }

    /// <summary>
    /// Extract 7-Zip archive using SharpCompress with size filtering
    /// </summary>
    private async Task<List<ProcessedFileInfo>> ExtractSevenZipArchiveAsync(byte[] sevenZipContent, FileReaderConfiguration config, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var extractedFiles = new List<ProcessedFileInfo>();

        try
        {
            using var memoryStream = new MemoryStream(sevenZipContent);
            using var archive = ArchiveFactory.Open(memoryStream);

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || entry.Size == 0) continue; // Skip directories and empty files

                try
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var contentStream = new MemoryStream();
                    await entryStream.CopyToAsync(contentStream, cancellationToken);

                    var content = contentStream.ToArray();
                    var extension = Path.GetExtension(entry.Key);
                    var contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(content));

                    // Check size criteria - if any file doesn't meet criteria, fail entire processing
                    if (content.Length < config.MinExtractedSize || content.Length > config.MaxExtractedSize)
                    {
                        _logger.LogWarningWithHierarchy(context, "Extracted 7-Zip file {FileName} with size {Size} is outside range {MinSize}-{MaxSize}, failing entire archive processing",
                            entry.Key ?? "unknown", content.Length, config.MinExtractedSize, config.MaxExtractedSize);
                        throw new InvalidOperationException($"Extracted 7-Zip file {entry.Key} size {content.Length} is outside allowed range {config.MinExtractedSize}-{config.MaxExtractedSize}");
                    }

                    extractedFiles.Add(new ProcessedFileInfo
                    {
                        FileName = Path.GetFileName(entry.Key) ?? string.Empty,
                        FilePath = entry.Key ?? string.Empty,
                        FileSize = content.Length,
                        CreatedDate = entry.CreatedTime ?? DateTime.UtcNow,
                        ModifiedDate = entry.LastModifiedTime ?? DateTime.UtcNow,
                        FileExtension = extension ?? string.Empty,
                        FileContent = content,
                        ContentEncoding = "binary",
                        DetectedMimeType = DetectMimeType(content, extension ?? string.Empty),
                        FileType = DetermineFileType(extension ?? string.Empty),
                        ContentHash = contentHash
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarningWithHierarchy(context, ex, "Failed to extract entry {EntryName} from 7-Zip archive", entry.Key ?? "unknown");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to extract 7-Zip archive");
        }

        return extractedFiles;
    }

    /// <summary>
    /// Extract TAR archive using SharpCompress with size filtering
    /// </summary>
    private async Task<List<ProcessedFileInfo>> ExtractTarArchiveAsync(byte[] tarContent, FileReaderConfiguration config, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var extractedFiles = new List<ProcessedFileInfo>();

        try
        {
            using var memoryStream = new MemoryStream(tarContent);
            using var archive = ArchiveFactory.Open(memoryStream);

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || entry.Size == 0) continue; // Skip directories and empty files

                try
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var contentStream = new MemoryStream();
                    await entryStream.CopyToAsync(contentStream, cancellationToken);

                    var content = contentStream.ToArray();
                    var extension = Path.GetExtension(entry.Key);
                    var contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(content));

                    // Check size criteria - if any file doesn't meet criteria, fail entire processing
                    if (content.Length < config.MinExtractedSize || content.Length > config.MaxExtractedSize)
                    {
                        _logger.LogWarningWithHierarchy(context, "Extracted TAR file {FileName} with size {Size} is outside range {MinSize}-{MaxSize}, failing entire archive processing",
                            entry.Key ?? "unknown", content.Length, config.MinExtractedSize, config.MaxExtractedSize);
                        throw new InvalidOperationException($"Extracted TAR file {entry.Key} size {content.Length} is outside allowed range {config.MinExtractedSize}-{config.MaxExtractedSize}");
                    }

                    extractedFiles.Add(new ProcessedFileInfo
                    {
                        FileName = Path.GetFileName(entry.Key) ?? string.Empty,
                        FilePath = entry.Key ?? string.Empty,
                        FileSize = content.Length,
                        CreatedDate = entry.CreatedTime ?? DateTime.UtcNow,
                        ModifiedDate = entry.LastModifiedTime ?? DateTime.UtcNow,
                        FileExtension = extension ?? string.Empty,
                        FileContent = content,
                        ContentEncoding = "binary",
                        DetectedMimeType = DetectMimeType(content, extension ?? string.Empty),
                        FileType = DetermineFileType(extension ?? string.Empty),
                        ContentHash = contentHash
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarningWithHierarchy(context, ex, "Failed to extract entry {EntryName} from TAR archive", entry.Key ?? "unknown");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Failed to extract TAR archive");
        }

        return extractedFiles;
    }

    /// <summary>
    /// Create file cache data object for serialization
    /// Returns array of cache objects (always array format)
    /// </summary>
    private object CreateFileCacheDataObject(string filePath, byte[] fileContent, string detectedMimeType, string fileType, List<ProcessedFileInfo> extractedFiles, FileInfo fileInfo)
    {
        var contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(fileContent));

        // Always return array format - even for single files
        return new[]
        {
            new
            {
                fileCacheDataObject = new
                {
                    fileMetadata = new
                    {
                        fileName = Path.GetFileName(filePath),
                        filePath = filePath,
                        fileSize = fileContent.Length,
                        createdDate = fileInfo.CreationTime,
                        modifiedDate = fileInfo.LastWriteTime,
                        fileExtension = Path.GetExtension(filePath),
                        detectedMimeType = detectedMimeType,
                        fileType = fileType,
                        contentHash = contentHash
                    },
                    fileContent = new
                    {
                        binaryData = Convert.ToBase64String(fileContent),
                        encoding = "base64"
                    }
                },
                ExtractedFileCacheDataObject = extractedFiles.Select(f =>
                    CacheDataFactory.CreateFileCacheDataObject(f)
                ).ToArray()
            }
        };
    }
}
