using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Processor.Base;
using Processor.Base.Interfaces;
using Processor.PluginLoader.Interfaces;
using Processor.PluginLoader.Models;
using Processor.PluginLoader.Services;
using Shared.Correlation;
using Shared.Models;

namespace Processor.PluginLoader;

/// <summary>
/// PluginLoader processor application that dynamically loads and executes plugins
/// based on configuration provided in each ProcessActivityDataAsync call.
/// Features two-level caching:
/// - Level 1: PluginManager instances are always cached by AssemblyBasePath
/// - Level 2: Plugin instances are cached based on IsStateless configuration flag
/// </summary>
public class PluginLoaderProcessorApplication : BaseProcessorApplication
{
    /// <summary>
    /// Configure processor-specific services
    /// </summary>
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Call base implementation
        base.ConfigureServices(services, configuration);

        // Register stateful plugin registry configuration
        services.Configure<StatefulPluginRegistryConfiguration>(
            configuration.GetSection("StatefulPluginRegistry"));

        // Register stateful plugin registry service
        services.AddSingleton<IStatefulPluginRegistryService, StatefulPluginRegistryService>();

        // Register processor-specific services
        // Note: PluginLoaderProcessorMetricsService removed - metrics moved to individual plugins
    }

    /// <summary>
    /// Override to register the concrete PluginLoaderProcessorService
    /// </summary>
    protected override void RegisterProcessorService(IServiceCollection services)
    {
        services.AddSingleton<IProcessorService, Services.PluginLoaderProcessorService>();
    }

    /// <summary>
    /// Initialize custom metrics services
    /// </summary>
    protected override async Task InitializeCustomMetricsServicesAsync()
    {
        await base.InitializeCustomMetricsServicesAsync();
    }

    /// <summary>
    /// Initialize processor-specific services including stateful plugin preloading
    /// </summary>
    protected override async Task InitializeProcessorSpecificServicesAsync()
    {
        // Create application-level hierarchical context for preloading
        var appContext = new HierarchicalLoggingContext
        {
            CorrelationId = CorrelationIdContext.GetCurrentCorrelationIdStatic()
        };

        try
        {
            // Preload stateful plugins from registry
            var registryService = ServiceProvider.GetRequiredService<IStatefulPluginRegistryService>();
            await registryService.PreloadStatefulPluginsAsync(appContext);

            var logger = ServiceProvider.GetRequiredService<ILogger<PluginLoaderProcessorApplication>>();
            logger.LogInformationWithHierarchy(appContext, "✅ Stateful plugin preloading completed successfully");
        }
        catch (Exception ex)
        {
            var logger = ServiceProvider.GetRequiredService<ILogger<PluginLoaderProcessorApplication>>();
            logger.LogErrorWithHierarchy(appContext, ex, "❌ Failed to preload stateful plugins");
            throw;
        }
    }

    /// <summary>
    /// Concrete implementation of the activity processing logic for file adapter pipe processing
    /// This processor acts as a pipe - it receives cache data, processes it, and passes it through
    /// while recording metrics and performing any necessary adaptations
    /// Enhanced with hierarchical logging support - maintains consistent ID ordering
    /// </summary>
    public override async Task<IEnumerable<ProcessedActivityData>> ProcessActivityDataAsync(
        Guid orchestratedFlowId,
        Guid workflowId,
        Guid correlationId,
        Guid stepId,
        Guid processorId,
        Guid publishId,
        Guid executionId,
        List<AssignmentModel> entities,
        object? inputData, // Contains deserialized cacheData from previous processor
        CancellationToken cancellationToken = default)
    {
        var logger = ServiceProvider.GetRequiredService<ILogger<PluginLoaderProcessorApplication>>();
        var processingStart = DateTime.UtcNow;

        // Create Layer 5 hierarchical context for plugin loader
        var pluginLoaderContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            WorkflowId = workflowId,
            CorrelationId = correlationId,
            StepId = stepId,
            ProcessorId = processorId,
            PublishId = publishId,
            ExecutionId = executionId
        };

        logger.LogInformationWithHierarchy(pluginLoaderContext,
            "Starting plugin processing");

        try
        {
            // 1. Validate entities collection - expect at least two entities with one PluginAssignmentModel
            if (entities.Count < 2)
            {
                throw new InvalidOperationException($"PluginLoaderProcessor expects at least two entities, but received {entities.Count}. At least one must be a PluginAssignmentModel.");
            }

            var pluginAssignment = entities.OfType<PluginAssignmentModel>().FirstOrDefault();
            if (pluginAssignment == null)
            {
                throw new InvalidOperationException("No PluginAssignmentModel found in entities collection. PluginLoaderProcessor expects at least one PluginAssignmentModel among the provided entities.");
            }

            logger.LogInformationWithHierarchy(pluginLoaderContext,
                "Processing {EntityCount} entities with PluginAssignmentModel: {PluginName} (EntityId: {EntityId})",
                entities.Count, pluginAssignment.Name, pluginAssignment.EntityId);

            var config = await ExtractPluginConfigurationFromPluginAssignmentAsync(entities, logger, pluginLoaderContext);

            // 2. Validate plugin configuration
            await ValidatePluginConfigurationAsync(config, logger, pluginLoaderContext);

            // 3. Get PluginManager from factory (Level 1 cache - always cached)
            var pluginManager = PluginManagerFactory.GetPluginManager(config.AssemblyBasePath, ServiceProvider, pluginLoaderContext);

            // 4. Get plugin instance with conditional caching (Level 2 cache - based on IsStateless)
            var pluginVersion = Version.Parse(config.Version);
            var plugin = await pluginManager.GetPluginInstanceAsync(
                config.AssemblyName, pluginVersion, config.TypeName, config.IsStateless, pluginLoaderContext);

            logger.LogInformationWithHierarchy(pluginLoaderContext,
                "Successfully loaded plugin instance: {TypeName} from {AssemblyName} v{Version} (IsStateless: {IsStateless})",
                config.TypeName, config.AssemblyName, config.Version, config.IsStateless);

            // 5. Execute plugin with timeout if configured
            IEnumerable<ProcessedActivityData> result;
            if (config.ExecutionTimeoutMs > 0 && config.ExecutionTimeoutMs != 300000) // Only apply timeout if different from default
            {
                using var timeoutCts = new CancellationTokenSource(config.ExecutionTimeoutMs);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                result = await plugin.ProcessActivityDataAsync(
                    orchestratedFlowId, workflowId, correlationId, stepId,
                    processorId, publishId, executionId, entities, inputData,
                    combinedCts.Token);
            }
            else
            {
                result = await plugin.ProcessActivityDataAsync(
                    orchestratedFlowId, workflowId, correlationId, stepId,
                    processorId, publishId, executionId, entities, inputData,
                    cancellationToken);
            }

            var processingDuration = DateTime.UtcNow - processingStart;
            logger.LogInformationWithHierarchy(pluginLoaderContext,
                "Completed plugin processing in {Duration}ms",
                processingDuration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var processingDuration = DateTime.UtcNow - processingStart;
            logger.LogErrorWithHierarchy(pluginLoaderContext, ex,
                "Error in plugin processing after {Duration}ms",
                processingDuration.TotalMilliseconds);

            // Return error result
            return new[]
            {
                new ProcessedActivityData
                {
                    Result = $"Error in plugin processing: {ex.Message}",
                    Status = ActivityExecutionStatus.Failed,
                    Data = new { }, // Empty data on error
                    ProcessorName = "PluginLoaderProcessor",
                    Version = "1.0",
                    ExecutionId = executionId
                }
            };
        }
    }

    /// <summary>
    /// Extract plugin configuration from PluginAssignmentModel within the entities collection
    /// </summary>
    private async Task<PluginLoaderConfiguration> ExtractPluginConfigurationFromPluginAssignmentAsync(List<AssignmentModel> entities, ILogger logger, HierarchicalLoggingContext context)
    {
        logger.LogDebugWithHierarchy(context, "Extracting plugin configuration from {EntityCount} entities", entities.Count);

        // Find the PluginAssignmentModel (validation already ensures it exists)
        var pluginAssignment = entities.OfType<PluginAssignmentModel>().First();

        logger.LogDebugWithHierarchy(context, "Found PluginAssignmentModel. EntityId: {EntityId}, Name: {Name}",
            pluginAssignment.EntityId, pluginAssignment.Name);

        // Extract plugin configuration directly from PluginAssignmentModel properties
        var config = new PluginLoaderConfiguration
        {
            AssemblyBasePath = pluginAssignment.AssemblyBasePath,
            AssemblyName = pluginAssignment.AssemblyName,
            Version = pluginAssignment.AssemblyVersion, // Use AssemblyVersion instead of entity Version
            TypeName = pluginAssignment.TypeName,
            ExecutionTimeoutMs = pluginAssignment.ExecutionTimeoutMs,
            IsStateless = pluginAssignment.IsStateless
        };

        logger.LogInformationWithHierarchy(context,
            "Extracted plugin configuration from PluginAssignmentModel - AssemblyBasePath: {AssemblyBasePath}, AssemblyName: {AssemblyName}, AssemblyVersion: {AssemblyVersion}, TypeName: {TypeName}, ExecutionTimeoutMs: {ExecutionTimeoutMs}, IsStateless: {IsStateless}",
            config.AssemblyBasePath, config.AssemblyName, config.Version, config.TypeName, config.ExecutionTimeoutMs, config.IsStateless);

        return await Task.FromResult(config);
    }

    /// <summary>
    /// Validate the extracted plugin configuration
    /// </summary>
    private async Task ValidatePluginConfigurationAsync(PluginLoaderConfiguration config, ILogger logger, HierarchicalLoggingContext context)
    {
        logger.LogDebugWithHierarchy(context, "Validating plugin configuration");

        if (string.IsNullOrWhiteSpace(config.AssemblyBasePath))
        {
            throw new InvalidOperationException("AssemblyBasePath cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(config.AssemblyName))
        {
            throw new InvalidOperationException("AssemblyName cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(config.Version))
        {
            throw new InvalidOperationException("Version cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(config.TypeName))
        {
            throw new InvalidOperationException("TypeName cannot be empty");
        }

        // Validate version format
        if (!Version.TryParse(config.Version, out _))
        {
            throw new InvalidOperationException($"Invalid version format: {config.Version}");
        }

        // Validate assembly base path exists
        if (!Directory.Exists(config.AssemblyBasePath))
        {
            throw new InvalidOperationException($"Assembly base path does not exist: {config.AssemblyBasePath}");
        }

        // Validate execution timeout
        if (config.ExecutionTimeoutMs <= 0)
        {
            throw new InvalidOperationException("ExecutionTimeoutMs must be greater than 0");
        }

        logger.LogDebugWithHierarchy(context, "Plugin configuration validation completed successfully");
        await Task.CompletedTask;
    }
}
