using System.Diagnostics;
using System.Text.Json;
using Manager.Orchestrator.Interfaces;
using Shared.Correlation;
using Shared.Entities;
using Shared.Entities.Enums;
using Shared.Models;
using Shared.Services;
using Shared.Services.Interfaces;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Step navigation data containing essential workflow navigation information
/// </summary>
public class StepNavigationData
{
    /// <summary>
    /// List of next step IDs for this step
    /// </summary>
    public List<Guid> NextStepIds { get; set; } = new();

    /// <summary>
    /// Processor ID that executes this step
    /// </summary>
    public Guid ProcessorId { get; set; }

    /// <summary>
    /// Entry condition that determines when this step should execute
    /// </summary>
    public StepEntryCondition EntryCondition { get; set; }
}

/// <summary>
/// HTTP client for communication with other entity managers with resilience patterns
/// </summary>
public class ManagerHttpClient : BaseManagerHttpClient, IManagerHttpClient
{
    private readonly string _orchestratedFlowManagerBaseUrl;
    private readonly string _workflowManagerBaseUrl;
    private readonly string _stepManagerBaseUrl;
    private readonly string _assignmentManagerBaseUrl;
    private readonly string _addressManagerBaseUrl;
    private readonly string _deliveryManagerBaseUrl;
    private readonly string _pluginManagerBaseUrl;
    private readonly string _schemaManagerBaseUrl;

    public ManagerHttpClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ManagerHttpClient> logger,
        ISchemaValidator schemaValidator)
        : base(httpClient, configuration, logger)
    {
        // Get manager URLs from configuration
        _orchestratedFlowManagerBaseUrl = configuration["ManagerUrls:OrchestratedFlow"] ?? "http://localhost:5140";
        _workflowManagerBaseUrl = configuration["ManagerUrls:Workflow"] ?? "http://localhost:5180";
        _stepManagerBaseUrl = configuration["ManagerUrls:Step"] ?? "http://localhost:5170";
        _assignmentManagerBaseUrl = configuration["ManagerUrls:Assignment"] ?? "http://localhost:5130";
        _addressManagerBaseUrl = configuration["ManagerUrls:Address"] ?? "http://localhost:5120";
        _deliveryManagerBaseUrl = configuration["ManagerUrls:Delivery"] ?? "http://localhost:5150";
        _pluginManagerBaseUrl = configuration["ManagerUrls:Plugin"] ?? "http://localhost:5100";
        _schemaManagerBaseUrl = configuration["ManagerUrls:Schema"] ?? "http://localhost:5160";
        // Note: Schema validator is available if needed for future enhancements
    }



    public async Task<OrchestratedFlowEntity?> GetOrchestratedFlowAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context)
    {
        var url = $"{_orchestratedFlowManagerBaseUrl}/api/OrchestratedFlow/{orchestratedFlowId}";
        return await ExecuteAndProcessResponseWithHierarchyAsync<OrchestratedFlowEntity>(url, "orchestrated flow", orchestratedFlowId, context);
    }

    public async Task<(Dictionary<Guid, StepNavigationData> StepEntities, List<Guid> ProcessorIds)> GetStepManagerDataAsync(Guid workflowId, HierarchicalLoggingContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        const string operationName = "step manager data";

        _logger.LogInformationWithHierarchy(context, "Retrieving {OperationName}",
            operationName);

        try
        {
            // Step 1: Get workflow from Workflow Manager to get step IDs
            var workflowUrl = $"{_workflowManagerBaseUrl}/api/Workflow/{workflowId}";

            var workflow = await ExecuteAndProcessResponseWithHierarchyAsync<WorkflowEntity>(workflowUrl, "workflow", workflowId, context);

            if (workflow == null || !workflow.StepIds.Any())
            {
                stopwatch.Stop();
                _logger.LogWarningWithHierarchy(context, "Workflow not found or has no steps. TotalDuration: {TotalDurationMs}ms",
                    stopwatch.ElapsedMilliseconds);
                return (new Dictionary<Guid, StepNavigationData>(), new List<Guid>());
            }

            _logger.LogInformationWithHierarchy(context, "Retrieved workflow with {StepCount} steps. StepIds: {StepIds}",
                workflow.StepIds.Count, string.Join(",", workflow.StepIds));

            // Step 2: Get individual steps from Step Manager using standardized pattern
            var stepTasks = workflow.StepIds.Select(async stepId =>
            {
                var stepUrl = $"{_stepManagerBaseUrl}/api/Step/{stepId}";

                try
                {
                    return await ExecuteAndProcessResponseWithHierarchyAsync<StepEntity>(stepUrl, "step", stepId, context);
                }
                catch (Exception ex)
                {
                    // Create enhanced step-specific context from base context for error logging
                    var stepContext = new HierarchicalLoggingContext
                    {
                        OrchestratedFlowId = context.OrchestratedFlowId,
                        WorkflowId = context.WorkflowId,
                        CorrelationId = context.CorrelationId,
                        StepId = stepId,
                        ProcessorId = context.ProcessorId,
                        PublishId = context.PublishId,
                        ExecutionId = context.ExecutionId
                    };

                    _logger.LogWarningWithHierarchy(stepContext, ex, "Failed to retrieve step");
                    return null;
                }
            });

            var stepResults = await Task.WhenAll(stepTasks);
            var steps = stepResults.Where(s => s != null).Cast<StepEntity>().Distinct().ToList();

            // Step 3: Aggregate into navigation dictionary and processor IDs
            var stepEntities = steps.ToDictionary(
                step => step.Id,
                step => new StepNavigationData
                {
                    NextStepIds = step.NextStepIds.Distinct().ToList(),
                    ProcessorId = step.ProcessorId,
                    EntryCondition = step.EntryCondition
                });

            var processorIds = steps.Select(s => s.ProcessorId).Distinct().ToList();

            stopwatch.Stop();
            _logger.LogInformationWithHierarchy(context, "Successfully retrieved {OperationName}. StepCount: {StepCount}, ProcessorCount: {ProcessorCount}, TotalDuration: {TotalDurationMs}ms",
                operationName, stepEntities.Count, processorIds.Count, stopwatch.ElapsedMilliseconds);

            return (stepEntities, processorIds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithHierarchy(context, ex, "Error retrieving {OperationName}. TotalDuration: {TotalDurationMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<Dictionary<Guid, List<AssignmentModel>>> GetAssignmentManagerDataAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        const string operationName = "assignment manager data";

        _logger.LogInformationWithHierarchy(context, "Retrieving {OperationName}",
            operationName);

        try
        {
            // Step 1: Get orchestrated flow to get assignment IDs
            var orchestratedFlowUrl = $"{_orchestratedFlowManagerBaseUrl}/api/OrchestratedFlow/{orchestratedFlowId}";

            var orchestratedFlow = await ExecuteAndProcessResponseWithHierarchyAsync<OrchestratedFlowEntity>(orchestratedFlowUrl, "orchestrated flow", orchestratedFlowId, context);

            if (orchestratedFlow == null || !orchestratedFlow.AssignmentIds.Any())
            {
                stopwatch.Stop();
                _logger.LogWarningWithHierarchy(context, "Orchestrated flow not found or has no assignments. TotalDuration: {TotalDurationMs}ms",
                    stopwatch.ElapsedMilliseconds);
                return new Dictionary<Guid, List<AssignmentModel>>();
            }

            _logger.LogInformationWithHierarchy(context, "Retrieved orchestrated flow with {AssignmentCount} assignments. AssignmentIds: {AssignmentIds}",
                orchestratedFlow.AssignmentIds.Count, string.Join(",", orchestratedFlow.AssignmentIds));

            // Step 2: Get individual assignments from Assignment Manager using standardized pattern
            var assignmentTasks = orchestratedFlow.AssignmentIds.Select(async assignmentId =>
            {
                var assignmentUrl = $"{_assignmentManagerBaseUrl}/api/Assignment/{assignmentId}";

                try
                {
                    return await ExecuteAndProcessResponseWithHierarchyAsync<AssignmentEntity>(assignmentUrl, "assignment", assignmentId, context);
                }
                catch (Exception ex)
                {
                    // Use base context directly for assignment error logging - assignments don't have specific hierarchy level
                    _logger.LogWarningWithHierarchy(context, ex, "Failed to retrieve assignment. AssignmentId: {AssignmentId}", assignmentId);
                    return null;
                }
            });

            var assignmentResults = await Task.WhenAll(assignmentTasks);
            var assignments = assignmentResults.Where(a => a != null).Cast<AssignmentEntity>().Distinct().ToList();

            // Step 3: Aggregate into stepId → AssignmentModels dictionary with type-specific models
            var assignmentData = new Dictionary<Guid, List<AssignmentModel>>();

            foreach (var assignment in assignments)
            {
                if (!assignmentData.ContainsKey(assignment.StepId))
                {
                    assignmentData[assignment.StepId] = new List<AssignmentModel>();
                }

                // Create type-specific AssignmentModel for each entity ID in this assignment
                foreach (var entityId in assignment.EntityIds.Distinct())
                {
                    var assignmentModel = await CreateAssignmentModelAsync(entityId, assignment, context);
                    if (assignmentModel != null)
                    {
                        assignmentData[assignment.StepId].Add(assignmentModel);
                    }
                }
            }

            stopwatch.Stop();
            var totalAssignmentModelCount = assignmentData.Values.Sum(list => list.Count);
            _logger.LogInformationWithHierarchy(context, "Successfully retrieved {OperationName}. AssignmentEntityCount: {AssignmentEntityCount}, StepCount: {StepCount}, TotalAssignmentModelCount: {TotalAssignmentModelCount}, TotalDuration: {TotalDurationMs}ms",
                operationName, assignments.Count, assignmentData.Count, totalAssignmentModelCount, stopwatch.ElapsedMilliseconds);

            return assignmentData;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithHierarchy(context, ex, "Error retrieving {OperationName}. TotalDuration: {TotalDurationMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
    /// <summary>
    /// Creates type-specific assignment model based on entity type
    /// </summary>
    /// <param name="entityId">The entity ID</param>
    /// <param name="assignment">The assignment entity containing base information</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Type-specific assignment model or null if entity not found</returns>
    private async Task<AssignmentModel?> CreateAssignmentModelAsync(Guid entityId, AssignmentEntity assignment, HierarchicalLoggingContext context)
    {
        try
        {
            // Try to get as address entity first
            var addressEntity = await TryGetAddressEntityAsync(entityId, context);
            if (addressEntity != null)
            {
                return new AddressAssignmentModel
                {
                    EntityId = entityId,
                    Name = addressEntity.Name,
                    Version = addressEntity.Version,
                    Payload = addressEntity.Payload,
                    SchemaId = addressEntity.SchemaId,
                    ConnectionString = addressEntity.ConnectionString
                };
            }

            // Try to get as delivery entity
            var deliveryEntity = await TryGetDeliveryEntityAsync(entityId, context);
            if (deliveryEntity != null)
            {
                return new DeliveryAssignmentModel
                {
                    EntityId = entityId,
                    Name = deliveryEntity.Name,
                    Version = deliveryEntity.Version,
                    Payload = deliveryEntity.Payload,
                    SchemaId = deliveryEntity.SchemaId
                };
            }

            // Try to get as plugin entity
            var pluginEntity = await TryGetPluginEntityAsync(entityId, context);
            if (pluginEntity != null)
            {
                var pluginModel = new PluginAssignmentModel
                {
                    EntityId = entityId,
                    Name = pluginEntity.Name,
                    Version = pluginEntity.Version,
                    InputSchemaId = pluginEntity.InputSchemaId,
                    OutputSchemaId = pluginEntity.OutputSchemaId,
                    EnableInputValidation = pluginEntity.EnableInputValidation,
                    EnableOutputValidation = pluginEntity.EnableOutputValidation,
                    AssemblyBasePath = pluginEntity.AssemblyBasePath,
                    AssemblyName = pluginEntity.AssemblyName,
                    AssemblyVersion = pluginEntity.AssemblyVersion,
                    TypeName = pluginEntity.TypeName,
                    ExecutionTimeoutMs = pluginEntity.ExecutionTimeoutMs,
                    IsStateless = pluginEntity.IsStateless
                };

                // Populate input schema definition if InputSchemaId is not empty
                if (pluginEntity.InputSchemaId != Guid.Empty)
                {
                    try
                    {
                        pluginModel.InputSchemaDefinition = await GetSchemaDefinitionAsync(pluginEntity.InputSchemaId, context);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarningWithHierarchy(context, ex, "Failed to retrieve input schema definition for plugin. EntityId: {EntityId}, InputSchemaId: {InputSchemaId}",
                            entityId, pluginEntity.InputSchemaId);
                    }
                }

                // Populate output schema definition if OutputSchemaId is not empty
                if (pluginEntity.OutputSchemaId != Guid.Empty)
                {
                    try
                    {
                        pluginModel.OutputSchemaDefinition = await GetSchemaDefinitionAsync(pluginEntity.OutputSchemaId, context);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarningWithHierarchy(context, ex, "Failed to retrieve output schema definition for plugin. EntityId: {EntityId}, OutputSchemaId: {OutputSchemaId}",
                            entityId, pluginEntity.OutputSchemaId);
                    }
                }

                return pluginModel;
            }

            _logger.LogWarningWithHierarchy(context, "Entity not found in Address, Delivery, or Plugin managers. EntityId: {EntityId}", entityId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Error creating assignment model. EntityId: {EntityId}", entityId);
            return null;
        }
    }
    /// <summary>
    /// Attempts to retrieve an address entity using standardized template pattern
    /// </summary>
    /// <param name="entityId">The entity ID</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Address entity or null if not found</returns>
    private async Task<AddressEntity?> TryGetAddressEntityAsync(Guid entityId, HierarchicalLoggingContext context)
    {
        try
        {
            var url = $"{_addressManagerBaseUrl}/api/Address/{entityId}";
            return await ExecuteAndProcessResponseAsync<AddressEntity>(url, "address entity", context, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogDebugWithHierarchy(context, ex, "Failed to retrieve entity as address. EntityId: {EntityId}", entityId);
            return null;
        }
    }

    /// <summary>
    /// Attempts to retrieve a delivery entity using standardized template pattern
    /// </summary>
    /// <param name="entityId">The entity ID</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Delivery entity or null if not found</returns>
    private async Task<DeliveryEntity?> TryGetDeliveryEntityAsync(Guid entityId, HierarchicalLoggingContext context)
    {
        try
        {
            var url = $"{_deliveryManagerBaseUrl}/api/Delivery/{entityId}";
            return await ExecuteAndProcessResponseAsync<DeliveryEntity>(url, "delivery entity", context, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogDebugWithHierarchy(context, ex, "Failed to retrieve entity as delivery. EntityId: {EntityId}", entityId);
            return null;
        }
    }

    /// <summary>
    /// Attempts to retrieve a plugin entity using standardized template pattern
    /// </summary>
    /// <param name="entityId">The entity ID</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Plugin entity or null if not found</returns>
    private async Task<PluginEntity?> TryGetPluginEntityAsync(Guid entityId, HierarchicalLoggingContext context)
    {
        try
        {
            var url = $"{_pluginManagerBaseUrl}/api/Plugin/{entityId}";
            return await ExecuteAndProcessResponseAsync<PluginEntity>(url, "plugin entity", context, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogDebugWithHierarchy(context, ex, "Failed to retrieve entity as plugin. EntityId: {EntityId}", entityId);
            return null;
        }
    }

    public async Task<string> GetSchemaDefinitionAsync(Guid schemaId, HierarchicalLoggingContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var url = $"{_schemaManagerBaseUrl}/api/Schema/{schemaId}";
        const string operationName = "schema definition";

        _logger.LogInformationWithHierarchy(context, "Retrieving {OperationName}. SchemaId: {SchemaId}, Url: {Url}",
            operationName, schemaId, url);

        try
        {
            var schemaEntity = await ExecuteAndProcessResponseWithHierarchyAsync<SchemaEntity>(url, operationName, schemaId, context);

            stopwatch.Stop();

            if (schemaEntity != null)
            {
                var definition = schemaEntity.Definition ?? string.Empty;

                // Check if the definition is JSON-escaped (starts with quotes and contains escaped quotes)
                if (!string.IsNullOrEmpty(definition) && definition.StartsWith("\"") && definition.Contains("\\\""))
                {
                    try
                    {
                        // The definition is JSON-escaped, so we need to deserialize it to get the raw JSON
                        var unescapedDefinition = JsonSerializer.Deserialize<string>(definition);
                        _logger.LogInformationWithHierarchy(context, "Successfully retrieved {OperationName} (JSON-escaped). SchemaId: {SchemaId}, OriginalLength: {OriginalLength}, UnescapedLength: {UnescapedLength}, TotalDuration: {TotalDurationMs}ms",
                            operationName, schemaId, definition.Length, unescapedDefinition?.Length ?? 0, stopwatch.ElapsedMilliseconds);
                        return unescapedDefinition ?? string.Empty;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarningWithHierarchy(context, ex, "Failed to unescape JSON-escaped {OperationName}, returning as-is. SchemaId: {SchemaId}, TotalDuration: {TotalDurationMs}ms",
                            operationName, schemaId, stopwatch.ElapsedMilliseconds);
                        return definition;
                    }
                }

                _logger.LogInformationWithHierarchy(context, "Successfully retrieved {OperationName}. SchemaId: {SchemaId}, DefinitionLength: {DefinitionLength}, TotalDuration: {TotalDurationMs}ms",
                    operationName, schemaId, definition.Length, stopwatch.ElapsedMilliseconds);
                return definition;
            }

            _logger.LogInformationWithHierarchy(context, "{OperationName} not found. SchemaId: {SchemaId}, TotalDuration: {TotalDurationMs}ms",
                operationName, schemaId, stopwatch.ElapsedMilliseconds);
            return string.Empty;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithHierarchy(context, ex, "Error retrieving {OperationName}. SchemaId: {SchemaId}, TotalDuration: {TotalDurationMs}ms",
                operationName, schemaId, stopwatch.ElapsedMilliseconds);
            return string.Empty;
        }
    }

    /// <summary>
    /// Executes an HTTP request and processes the response to a specific type with correlation logging
    /// </summary>
    protected virtual async Task<T?> ExecuteAndProcessResponseWithHierarchyAsync<T>(string url, string operationName, Guid? entityId, HierarchicalLoggingContext context, CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await ExecuteHttpRequestAsync(url, operationName, cancellationToken);
            var entity = await ProcessResponseAsync<T>(response, url, operationName, entityId);

            stopwatch.Stop();

            if (entity != null)
            {
                _logger.LogInformationWithCorrelation(
                    "Successfully retrieved {OperationName}. EntityId: {EntityId}, OrchestratedFlowId: {OrchestratedFlowId}, WorkflowId: {WorkflowId}, StepId: {StepId}, ProcessorId: {ProcessorId}, ExecutionId: {ExecutionId}, TotalDuration: {TotalDurationMs}ms",
                    operationName ?? "Unknown", entityId?.ToString() ?? "None", context.OrchestratedFlowId, context.WorkflowId, context.StepId, context.ProcessorId, context.ExecutionId, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformationWithCorrelation(
                    "{OperationName} not found. EntityId: {EntityId}, OrchestratedFlowId: {OrchestratedFlowId}, WorkflowId: {WorkflowId}, StepId: {StepId}, ProcessorId: {ProcessorId}, ExecutionId: {ExecutionId}, TotalDuration: {TotalDurationMs}ms",
                    operationName ?? "Unknown", entityId?.ToString() ?? "None", context.OrchestratedFlowId, context.WorkflowId, context.StepId, context.ProcessorId, context.ExecutionId, stopwatch.ElapsedMilliseconds);
            }

            return entity;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex,
                "Failed to retrieve {OperationName}. EntityId: {EntityId}, OrchestratedFlowId: {OrchestratedFlowId}, WorkflowId: {WorkflowId}, StepId: {StepId}, ProcessorId: {ProcessorId}, ExecutionId: {ExecutionId}, TotalDuration: {TotalDurationMs}ms",
                operationName ?? "Unknown", entityId?.ToString() ?? "None", context.OrchestratedFlowId, context.WorkflowId, context.StepId, context.ProcessorId, context.ExecutionId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
