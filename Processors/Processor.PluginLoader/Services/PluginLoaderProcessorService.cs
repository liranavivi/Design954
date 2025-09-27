using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Processor.Base.Services;
using Processor.PluginLoader.Models;
using Shared.Correlation;
using Shared.Entities;
using Shared.Extensions;
using Shared.Models;
using Shared.Services.Interfaces;

namespace Processor.PluginLoader.Services;

/// <summary>
/// Plugin-specific processor service that handles PluginAssignmentModel validation and plugin-specific schemas
/// Inherits from ProcessorService and provides processor-specific validation overrides
/// </summary>
public class PluginLoaderProcessorService : ProcessorService
{
    private readonly PluginLoaderProcessorApplication _pluginLoaderApplication;
    private readonly ILogger<PluginLoaderProcessorService> _logger;

    public PluginLoaderProcessorService(
        ICacheService cacheService,
        ISchemaValidator schemaValidator,
        IBus bus,
        IOptions<ProcessorConfiguration> config,
        IOptions<Shared.Services.Models.SchemaValidationConfiguration> validationConfig,
        IConfiguration configuration,
        ILogger<PluginLoaderProcessorService> logger,
        IResponseProcessingQueue responseProcessingQueue,
        IPerformanceMetricsService? performanceMetricsService,
        IProcessorHealthMetricsService? healthMetricsService,
        PluginLoaderProcessorApplication pluginLoaderApplication,
        IOptions<ProcessorInitializationConfiguration>? initializationConfig = null,
        IOptions<ProcessorActivityDataCacheConfiguration>? activityCacheConfig = null)
        : base(cacheService, schemaValidator, bus, config, validationConfig, configuration,
               logger as ILogger<ProcessorService>, responseProcessingQueue, performanceMetricsService, healthMetricsService,
               initializationConfig, activityCacheConfig)
    {
        _pluginLoaderApplication = pluginLoaderApplication;
        _logger = logger;
    }

    /// <summary>
    /// Implements the abstract ProcessActivityDataAsync method by delegating to PluginLoaderProcessorApplication
    /// </summary>
    protected override async Task<IEnumerable<ProcessedActivityData>> ProcessActivityDataAsync(
        Guid orchestratedFlowId, Guid workflowId, Guid correlationId,
        Guid stepId, Guid processorId, Guid publishId, Guid executionId,
        List<AssignmentModel> entities, object? inputData,
        CancellationToken cancellationToken = default)
    {
        return await _pluginLoaderApplication.ProcessActivityDataAsync(
            orchestratedFlowId, workflowId, correlationId,
            stepId, processorId, publishId, executionId,
            entities, inputData, cancellationToken);
    }

    /// <summary>
    /// Override input validation to handle only PluginAssignmentModel schemas
    /// </summary>
    public override async Task ValidateInputDataAsync(
        List<AssignmentModel> entities,
        string inputData,
        HierarchicalLoggingContext context)
    {
        // For PluginLoader, only validate against PluginAssignmentModel schemas
        // Do NOT call base validation as it would validate against general processor schemas
        await ValidatePluginAssignmentModelAsync(entities, inputData, context);
    }

    /// <summary>
    /// Override output validation to handle only PluginAssignmentModel schemas
    /// </summary>
    public override async Task ValidateOutputDataAsync(
        List<AssignmentModel> entities,
        string outputData,
        HierarchicalLoggingContext context)
    {
        // For PluginLoader, only validate against PluginAssignmentModel schemas
        // Do NOT call base validation as it would validate against general processor schemas
        await ValidatePluginSpecificOutputAsync(entities, outputData, context);
    }

    /// <summary>
    /// Validates PluginAssignmentModel schemas for plugin-specific processing
    /// </summary>
    private async Task ValidatePluginAssignmentModelAsync(
        List<AssignmentModel> entities,
        string inputData,
        HierarchicalLoggingContext context)
    {
        try
        {
            // Check if any entities are PluginAssignmentModel
            var pluginAssignments = entities.OfType<PluginAssignmentModel>().ToList();

            if (!pluginAssignments.Any())
            {
                _logger.LogDebugWithHierarchy(context,
                    "No plugin assignments found, skipping PluginAssignmentModel validation");
                return;
            }

            foreach (var pluginAssignment in pluginAssignments)
            {
                // Validate plugin-specific schema if configured
                if (pluginAssignment.InputSchemaId != Guid.Empty && !string.IsNullOrEmpty(pluginAssignment.InputSchemaDefinition))
                {
                    _logger.LogDebugWithHierarchy(context,
                        "Validating plugin-specific input schema: {SchemaId} for plugin: {PluginId}",
                        pluginAssignment.InputSchemaId, pluginAssignment.EntityId);

                    // Validate using PluginAssignmentModel schema
                    await ValidateInputDataAsync(inputData, pluginAssignment.InputSchemaDefinition,
                        pluginAssignment.EnableInputValidation, context);
                }
            }

            _logger.LogDebugWithHierarchy(context,
                "PluginAssignmentModel validation completed successfully for {Count} plugin assignments",
                pluginAssignments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "PluginAssignmentModel validation failed");
            throw;
        }
    }

    /// <summary>
    /// Validates plugin-specific output schemas
    /// </summary>
    private async Task ValidatePluginSpecificOutputAsync(
        List<AssignmentModel> entities,
        string outputData,
        HierarchicalLoggingContext context)
    {
        try
        {
            // Plugin-specific output validation
            var pluginAssignments = entities.OfType<PluginAssignmentModel>().ToList();

            if (!pluginAssignments.Any())
            {
                return;
            }

            foreach (var pluginAssignment in pluginAssignments)
            {
                if (pluginAssignment.OutputSchemaId != Guid.Empty && !string.IsNullOrEmpty(pluginAssignment.OutputSchemaDefinition))
                {
                    await ValidateOutputDataAsync(outputData, pluginAssignment.OutputSchemaDefinition,
                        pluginAssignment.EnableOutputValidation, context);
                }
            }

            _logger.LogDebugWithHierarchy(context,
                "Plugin-specific output validation completed for output data");
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "Plugin-specific output validation failed");
            throw;
        }
    }
}
