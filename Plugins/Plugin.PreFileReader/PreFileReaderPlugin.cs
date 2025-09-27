using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plugin.PreFileReader.Interfaces;
using Plugin.PreFileReader.Models;
using Plugin.PreFileReader.Services;
using Plugin.PreFileReader.Utilities;
using Plugin.Shared.Interfaces;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Models;
using Shared.Services.Interfaces;

namespace Plugin.PreFileReader;

/// <summary>
/// PreFileReader plugin implementation that handles compressed archives (ZIP, RAR, 7-Zip, GZIP, TAR)
/// Implements IPlugin interface for dynamic loading by PluginLoaderProcessor
/// </summary>
public class PreFileReaderPlugin : IPlugin
{
    private readonly ILogger<PreFileReaderPlugin> _logger;
    private readonly IPreFileReaderPluginMetricsService _metricsService;
    private readonly IFileRegistrationService _fileRegistrationService;



    /// <summary>
    /// Constructor with dependency injection using standardized plugin pattern
    /// </summary>
    public PreFileReaderPlugin(
        string pluginCompositeKey,
        ILogger<PreFileReaderPlugin> logger,
        ICacheService cacheService)
    {
        // Store host-provided services with null check
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Validate plugin composite key
        if (string.IsNullOrWhiteSpace(pluginCompositeKey))
            throw new ArgumentException("Plugin composite key cannot be null or empty", nameof(pluginCompositeKey));

        // Create metrics service with DI-provided composite key
        _metricsService = new PreFileReaderPluginMetricsService(pluginCompositeKey, _logger);

        // Create file registration service with plugin logger
        var cacheConfig = Options.Create(new FileRegistrationCacheConfiguration());
        _fileRegistrationService = new FileRegistrationService(cacheService, _logger, cacheConfig);
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

        // Supporting parameters
        List<AssignmentModel> entities,
        object? inputData, // Discovery: null | Processing: JsonElement with cached file path
        CancellationToken cancellationToken = default)
    {
        var processingStart = DateTime.UtcNow;

        // Create Layer 6 hierarchical logging context (full execution context)
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

        // Layer 6 log - Full execution context
        _logger.LogInformationWithHierarchy(context,
            "Starting PreFileReader plugin processing. EntityCount: {EntityCount}",
            entities.Count);

        try
        {
            // 1. Validate entities collection - must have at least one AddressAssignmentModel
            var addressAssignment = entities.OfType<AddressAssignmentModel>().FirstOrDefault();
            if (addressAssignment == null)
            {
                _logger.LogErrorWithHierarchy(context,
                    "AddressAssignmentModel not found in entities. PreFileReaderPlugin expects at least one AddressAssignmentModel.");
                throw new InvalidOperationException("AddressAssignmentModel not found in entities. PreFileReaderPlugin expects at least one AddressAssignmentModel.");
            }

            // Layer 6 log - Entity validation success
            _logger.LogInformationWithHierarchy(context,
                "Processing {EntityCount} entities with AddressAssignmentModel: {AddressName} (EntityId: {EntityId})",
                entities.Count, addressAssignment.Name, addressAssignment.EntityId);

            // 1. Extract configuration from AddressAssignmentModel (fresh every time - stateless)
            var config = await ExtractConfigurationFromAddressAssignmentAsync(addressAssignment, context);

            // 2. Validate configuration
            await ValidateConfigurationAsync(config, context);

            return await ProcessFileDiscoveryAsync(
                    config, entities, context, cancellationToken);
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
                stepId: context.StepId ?? Guid.Empty,
                executionId: context.ExecutionId ?? Guid.Empty,
                context: context);

            // Layer 6 error log with full context
            _logger.LogErrorWithHierarchy(context, ex,
                "PreFileReader plugin processing failed. Duration: {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result
            return new[]
            {
                new ProcessedActivityData
                {
                    Result = $"Error in PreFileReader plugin processing: {ex.Message}",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "PreFileReaderProcessor", // Keep same name for compatibility
                    Version = "1.0",
                    ExecutionId = context.ExecutionId ?? Guid.NewGuid()
                }
            };
        }
    }

    /// <summary>
    /// Process file discovery phase - discover files and cache them for individual processing
    /// Enhanced with hierarchical logging support
    /// </summary>
    private async Task<IEnumerable<ProcessedActivityData>> ProcessFileDiscoveryAsync(
        PreFileReaderConfiguration config,
        List<AssignmentModel> entities,
        HierarchicalLoggingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformationWithHierarchy(context,
            "Using cache-based approach for file discovery and processing");

        try
        {
            // Use local discovery and return ProcessedActivityData for each discovered file
            var processedActivityDataList = await DiscoverAndRegisterFilesAsync(
                config,
                _fileRegistrationService,
                context,
                entities);

            _logger.LogInformationWithHierarchy(context,
                "File discovery phase completed - Discovered {DiscoveredFiles} files for processing",
                processedActivityDataList.Count);

            // Return ProcessedActivityData list for immediate processing
            return processedActivityDataList;
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - DateTime.UtcNow; // Will be calculated properly in calling method

            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to complete file discovery phase. Duration: {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result for discovery phase
            return new[]
            {
                new ProcessedActivityData
                {
                    Result = $"Error in file discovery phase: {ex.Message}",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { },
                    ProcessorName = "PreFileReaderProcessor",
                    Version = "1.0",
                    ExecutionId = context.ExecutionId ?? Guid.NewGuid()
                }
            };
        }
    }

    private Task<PreFileReaderConfiguration> ExtractConfigurationFromAddressAssignmentAsync(
        AddressAssignmentModel addressAssignment,
        HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context,
            "Extracting configuration from AddressAssignmentModel. EntityId: {EntityId}, Name: {Name}",
            addressAssignment.EntityId, addressAssignment.Name);

        if (string.IsNullOrEmpty(addressAssignment.Payload))
        {
            throw new InvalidOperationException("AddressAssignmentModel.Payload (configuration JSON) cannot be empty");
        }

        // Parse JSON using consistent JsonElement pattern
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(addressAssignment.Payload ?? "{}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in AddressAssignmentModel.Payload", ex);
        }

        // Extract configuration using shared utilities
        var config = new PreFileReaderConfiguration
        {
            FolderPath = addressAssignment.ConnectionString,
            SearchPattern = JsonConfigurationExtractor.GetStringValue(root, "searchPattern", "*.{zip,rar,7z,gz,tar}"),
            MaxFilesToProcess = JsonConfigurationExtractor.GetIntValue(root, "maxFilesToProcess", 50)
        };

        _logger.LogInformationWithHierarchy(context,
            "Extracted PreFileReader configuration from AddressAssignmentModel - FolderPath: {FolderPath}, SearchPattern: {SearchPattern}, MaxFiles: {MaxFiles}",
            config.FolderPath, config.SearchPattern, config.MaxFilesToProcess);

        return Task.FromResult(config);
    }

    private Task ValidateConfigurationAsync(PreFileReaderConfiguration config, HierarchicalLoggingContext context)
    {
        _logger.LogInformationWithHierarchy(context, "Validating PreFileReader configuration");

        if (string.IsNullOrWhiteSpace(config.FolderPath))
        {
            throw new InvalidOperationException("FolderPath cannot be empty");
        }

        if (!Directory.Exists(config.FolderPath))
        {
            throw new DirectoryNotFoundException($"Folder does not exist: {config.FolderPath}");
        }

        if (config.MaxFilesToProcess <= 0)
        {
            throw new InvalidOperationException($"MaxFilesToProcess must be greater than 0, but was {config.MaxFilesToProcess}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Discovers and registers files for processing and returns ProcessedActivityData for each file
    /// Each file gets a unique executionId generated during registration
    /// Enhanced with hierarchical logging support
    /// </summary>
    private async Task<List<ProcessedActivityData>> DiscoverAndRegisterFilesAsync(
        PreFileReaderConfiguration config,
        IFileRegistrationService fileRegistrationService,
        HierarchicalLoggingContext context,
        List<AssignmentModel> entities)
    {
        var scanStart = DateTime.UtcNow;
        List<string> allFiles;

        try
        {
            // Single file enumeration
            allFiles = FilePatternExpander.EnumerateFiles(config.FolderPath, config.SearchPattern).ToList();

            var scanDuration = DateTime.UtcNow - scanStart;

            // Layer 6 log - File discovery performance
            _logger.LogInformationWithHierarchy(context,
                "Directory scan completed. FolderPath: {FolderPath}, FilesFound: {FilesFound}, ScanDuration: {ScanDuration}ms",
                config.FolderPath, allFiles.Count, scanDuration.TotalMilliseconds);

            // Record successful directory scan metrics
            _metricsService.RecordDirectoryScan(
                success: true,
                directoryPath: config.FolderPath,
                duration: scanDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId ?? Guid.Empty,
                executionId: context.ExecutionId ?? Guid.Empty,
                context: context);

            // Record file discovery metrics
            _metricsService.RecordFileDiscovery(
                filesFound: allFiles.Count,
                directoryPath: config.FolderPath,
                scanDuration: scanDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId ?? Guid.Empty,
                executionId: context.ExecutionId ?? Guid.Empty,
                context: context);
        }
        catch (Exception ex)
        {
            var scanDuration = DateTime.UtcNow - scanStart;

            // Record failed directory scan metrics
            _metricsService.RecordDirectoryScan(
                success: false,
                directoryPath: config.FolderPath,
                duration: scanDuration,
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            // Record plugin exception for directory scan failure
            _metricsService.RecordPluginException(
                exceptionType: ex.GetType().Name,
                severity: "error",
                correlationId: context.CorrelationId.ToString(),
                orchestratedFlowId: context.OrchestratedFlowId,
                stepId: context.StepId!.Value,
                executionId: context.ExecutionId!.Value,
                context: context);

            throw; // Re-throw the exception
        }

        // Apply MaxFilesToProcess limit if specified
        if (config.MaxFilesToProcess > 0 && allFiles.Count > config.MaxFilesToProcess)
        {
            allFiles = allFiles.Take(config.MaxFilesToProcess).ToList();
            _logger.LogInformationWithHierarchy(context,
                "Limited file discovery to {MaxFiles} files (found {TotalFiles} total)",
                config.MaxFilesToProcess, allFiles.Count);
        }

        _logger.LogInformationWithHierarchy(context,
            "Discovered {FileCount} files for processing", allFiles.Count);

        // Registration and ProcessedActivityData creation loop
        var processedActivityDataList = new List<ProcessedActivityData>();
        foreach (var filePath in allFiles)
        {
            // Generate unique executionId for each file processing
            var fileExecutionId = Guid.NewGuid();

            // Create child context for individual file processing (Layer 6 with unique ExecutionId)
            var fileContext = new HierarchicalLoggingContext
            {
                OrchestratedFlowId = context.OrchestratedFlowId,
                WorkflowId = context.WorkflowId,
                CorrelationId = context.CorrelationId,
                StepId = context.StepId,
                ProcessorId = context.ProcessorId,
                PublishId = context.PublishId,
                ExecutionId = fileExecutionId
            };

            // Layer 6 log - Individual file processing context
            _logger.LogDebugWithHierarchy(fileContext,
                "Processing discovered file: {FilePath}", filePath);

            // Atomically try to register the file - returns true if successfully added, false if already registered
            var wasAdded = await fileRegistrationService.TryToAddAsync(
                filePath,
                context.ProcessorId ?? Guid.Empty,
                fileExecutionId,
                context.CorrelationId,
                fileContext);

            if (wasAdded)
            {
                // Create ProcessedActivityData for this file
                var processedActivityData = new ProcessedActivityData
                {
                    ExecutionId = fileExecutionId,
                    Data = filePath,
                    Status = ActivityExecutionStatus.Completed,
                    Result = $"File discovered and registered: {filePath}",
                    ProcessorName = "PreFileReaderProcessor",
                    Version = "1.0"
                };

                processedActivityDataList.Add(processedActivityData);

                // Layer 6 log - File registration success
                _logger.LogDebugWithHierarchy(fileContext,
                    "Registered file and created ProcessedActivityData for: {FilePath}",
                    filePath);
            }
            else
            {
                // Layer 6 log - File already registered (not an error)
                _logger.LogDebugWithHierarchy(fileContext,
                    "File already registered, skipping: {FilePath}", filePath);
            }
        }

        _logger.LogInformationWithHierarchy(context,
            "Registered {RegisteredFiles} new files out of {TotalFiles} discovered",
            processedActivityDataList.Count, allFiles.Count);



        return processedActivityDataList;
    }


}
