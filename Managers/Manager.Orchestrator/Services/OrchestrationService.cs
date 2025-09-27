using System.Diagnostics;
using System.Text.Json;
using Manager.Orchestrator.Interfaces;
using Manager.Orchestrator.Models;
using MassTransit;
using Shared.Correlation;
using Shared.Entities;
using Shared.Extensions;
using Shared.Models;
using Shared.Services.Interfaces;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Main orchestration business logic service
/// </summary>
public class OrchestrationService : IOrchestrationService
{
    private readonly IManagerHttpClient _managerHttpClient;
    private readonly IOrchestrationCacheService _cacheService;
    private readonly ICacheService _rawCacheService;
    private readonly IOrchestrationSchedulerService _schedulerService;
    private readonly ISchemaValidator _schemaValidator;
    private readonly ILogger<OrchestrationService> _logger;
    private readonly IOrchestratorHealthMetricsService _metricsService;
    private readonly string _processorHealthMapName;
    private static readonly ActivitySource ActivitySource = new("Manager.Orchestrator.Services");

    public OrchestrationService(
        IManagerHttpClient managerHttpClient,
        IOrchestrationCacheService cacheService,
        ICacheService rawCacheService,
        IOrchestrationSchedulerService schedulerService,
        ISchemaValidator schemaValidator,
        ILogger<OrchestrationService> logger,
        IBus bus,
        IOrchestratorHealthMetricsService metricsService,
        IConfiguration configuration)
    {
        _managerHttpClient = managerHttpClient;
        _cacheService = cacheService;
        _rawCacheService = rawCacheService;
        _schedulerService = schedulerService;
        _schemaValidator = schemaValidator;
        _logger = logger;
        _metricsService = metricsService;
        _processorHealthMapName = configuration["ProcessorHealthMonitor:MapName"] ?? "processor-health";
    }

    public async Task StartOrchestrationAsync(Guid orchestratedFlowId)
    {
        await StopOrchestrationAsync(orchestratedFlowId);

        // ✅ Try to get correlation ID from current context, generate new one if this is truly a new workflow start
        var correlationId = GetCurrentCorrelationIdOrGenerate();
        var initiatedBy = "System"; // TODO: Get from user context

        // Create Layer 1 hierarchical context for orchestration start
        var orchestrationContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId
        };

        using var activity = ActivitySource.StartActivityWithCorrelation("StartOrchestration");
        activity?.SetTag("orchestratedFlowId", orchestratedFlowId.ToString())
                ?.SetTag("correlationId", correlationId.ToString())
                ?.SetTag("initiatedBy", initiatedBy)
                ?.SetTag("operation", "StartOrchestration");

        var stopwatch = Stopwatch.StartNew();
        var publishedCommands = 0;
        var stepCount = 0;
        var assignmentCount = 0;
        var processorCount = 0;
        var entryPointCount = 0;

        // Note: Replaced with hierarchical logging below

        _logger.LogInformationWithHierarchy(orchestrationContext,
            "Starting orchestration. InitiatedBy: {InitiatedBy}",
            initiatedBy);

        try
        {
            // Check if orchestration is already active
            activity?.SetTag("step", "0-CheckExisting");
            if (await _cacheService.ExistsAndValidAsync(orchestratedFlowId, orchestrationContext))
            {
                activity?.SetTag("result", "AlreadyActive");
                _logger.LogInformationWithHierarchy(orchestrationContext,
                    "Orchestration already active. Skipping start operation.");
                return;
            }

            // Step 1: Retrieve orchestrated flow entity
            activity?.SetTag("step", "1-RetrieveOrchestratedFlow");
            var orchestratedFlow = await _managerHttpClient.GetOrchestratedFlowAsync(orchestratedFlowId, orchestrationContext);
            if (orchestratedFlow == null)
            {
                throw new InvalidOperationException($"Orchestrated flow not found: {orchestratedFlowId}");
            }

            // Create Layer 2 workflow context
            var workflowContext = new HierarchicalLoggingContext
            {
                OrchestratedFlowId = orchestratedFlowId,
                WorkflowId = orchestratedFlow.WorkflowId,
                CorrelationId = correlationId
            };

            activity?.SetTag("workflowId", orchestratedFlow.WorkflowId.ToString())
                    ?.SetTag("assignmentIdCount", orchestratedFlow.AssignmentIds.Count);

            _logger.LogInformationWithHierarchy(workflowContext,
                "Retrieved orchestrated flow. AssignmentCount: {AssignmentCount}",
                orchestratedFlow.AssignmentIds.Count);

            // Step 2: Gather all data from managers in parallel
            activity?.SetTag("step", "2-GatherManagerData");
            _logger.LogInformationWithHierarchy(workflowContext,
                "Gathering manager data in parallel");

            var stepManagerTask = _managerHttpClient.GetStepManagerDataAsync(orchestratedFlow.WorkflowId, workflowContext);
            var assignmentManagerTask = _managerHttpClient.GetAssignmentManagerDataAsync(orchestratedFlowId, workflowContext);

            await Task.WhenAll(stepManagerTask,assignmentManagerTask);

            var (stepEntities, processorIds) = await stepManagerTask;
            var assignmentManagerData = await assignmentManagerTask;

            // Collect metrics
            stepCount = stepEntities.Count;
            assignmentCount = assignmentManagerData.Values.Sum(list => list.Count);
            processorCount = processorIds.Count;

            activity?.SetTag("stepCount", stepCount)
                    ?.SetTag("assignmentCount", assignmentCount)
                    ?.SetTag("processorCount", processorCount);

            _logger.LogInformationWithHierarchy(workflowContext,
                "Manager data gathered. StepCount: {StepCount}, AssignmentCount: {AssignmentCount}, ProcessorCount: {ProcessorCount}",
                stepCount, assignmentCount, processorCount);

            // Step 2.5: Validate Assignment Entity Schemas
            activity?.SetTag("step", "2.5-ValidateAssignmentSchemas");
            await ValidateAssignmentSchemas(assignmentManagerData, workflowContext);

            // Step 3: Find and validate entry points
            activity?.SetTag("step", "3-FindAndValidateEntryPoints");
            var entryPoints = FindEntryPoints(stepEntities, workflowContext);
            ValidateEntryPoints(entryPoints, workflowContext);
            entryPointCount = entryPoints.Count;
            activity?.SetTag("entryPointCount", entryPointCount);

            // Step 3.5: Find and validate termination points
            activity?.SetTag("step", "3.5-FindAndValidateTerminationPoints");
            var terminationPoints = FindTerminationPoints(stepEntities, workflowContext);
            ValidateTerminationPoints(terminationPoints, workflowContext);
            var terminationPointCount = terminationPoints.Count;
            activity?.SetTag("terminationPointCount", terminationPointCount);

            // Step 3.6: Validate circular workflow existence
            activity?.SetTag("step", "3.6-ValidateCircularWorkflow");
            ValidateCircularWorkflow(stepEntities, terminationPoints, workflowContext);

            // Step 4: Create complete orchestration cache model
            activity?.SetTag("step", "4-CreateCacheModel");

            var orchestrationData = new OrchestrationCacheModel
            {
                OrchestratedFlowId = orchestratedFlowId,
                OrchestratedFlow = orchestratedFlow,
                StepEntities = stepEntities,
                ProcessorIds = processorIds,
                Assignments = assignmentManagerData,
                EntryPoints = entryPoints, // ✅ Cache the calculated entry points
                CreatedAt = DateTime.UtcNow
            };

            // Step 5: Store in cache
            activity?.SetTag("step", "5-StoreInCache");
            await _cacheService.StoreOrchestrationDataAsync(orchestratedFlowId, orchestrationData, workflowContext);

            // Step 6: Check processor health
            activity?.SetTag("step", "6-ValidateProcessorHealth");
            await ValidateProcessorHealthAsync(processorIds, workflowContext);

            publishedCommands = orchestrationData.EntryPoints.Count;

            // Step 7: Start scheduler if cron expression is provided and enabled
            activity?.SetTag("step", "7-StartScheduler");
            await StartSchedulerIfConfiguredAsync(orchestratedFlowId, orchestratedFlow, workflowContext);

            stopwatch.Stop();
            activity?.SetTag("publishedCommands", publishedCommands)
                    ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                    ?.SetTag("result", "Success")
                    ?.SetStatus(ActivityStatusCode.Ok);


            _logger.LogInformationWithHierarchy(workflowContext,
                "Successfully started orchestration. StepCount: {StepCount}, AssignmentCount: {AssignmentCount}, ProcessorCount: {ProcessorCount}, EntryPoints: {EntryPointCount}, PublishedCommands: {PublishedCommands}, Duration: {Duration}ms",
                stepCount, assignmentCount, processorCount, entryPointCount, publishedCommands, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message)
                    ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                    ?.SetTag("error.type", ex.GetType().Name)
                    ?.SetTag("result", "Error");

            // Record orchestration start exception as critical
            _metricsService.RecordException(ex.GetType().Name, "error", isCritical: true, orchestrationContext.CorrelationId);

            _logger.LogErrorWithHierarchy(orchestrationContext, ex,
                "Error starting orchestration. Duration: {Duration}ms, ErrorType: {ErrorType}",
                stopwatch.ElapsedMilliseconds, ex.GetType().Name);

            // Clean up any partial orchestration data since there's no TTL on orchestration-data map
            try
            {
                _logger.LogInformationWithHierarchy(orchestrationContext, "Cleaning up partial orchestration data due to startup failure");
                await StopOrchestrationAsync(orchestratedFlowId);
            }
            catch (Exception cleanupEx)
            {
                // Log cleanup failure but don't mask the original exception
                _logger.LogWarningWithHierarchy(orchestrationContext, cleanupEx, "Failed to clean up partial orchestration data after startup failure");
            }

            throw;
        }
    }

    public async Task StopOrchestrationAsync(Guid orchestratedFlowId)
    {
        // Create Layer 1 hierarchical context for orchestration stop
        var correlationId = GetCurrentCorrelationIdOrGenerate();
        var orchestrationContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId
        };

        _logger.LogInformationWithHierarchy(orchestrationContext,
            "Stopping orchestration");

        try
        {
            // Check if orchestration exists
            var exists = await _cacheService.ExistsAndValidAsync(orchestratedFlowId, orchestrationContext);
            if (!exists)
            {
                _logger.LogWarningWithHierarchy(orchestrationContext,
                    "Orchestration not found or already expired");

                // Still try to stop scheduler in case it's running
                await StopSchedulerIfRunningAsync(orchestratedFlowId, orchestrationContext);
                return;
            }

            // Stop scheduler if running
            await StopSchedulerIfRunningAsync(orchestratedFlowId, orchestrationContext);

            // Remove from cache
            await _cacheService.RemoveOrchestrationDataAsync(orchestratedFlowId, orchestrationContext);

            _logger.LogInformationWithHierarchy(orchestrationContext,
                "Successfully stopped orchestration");
        }
        catch (Exception ex)
        {
            // Record orchestration stop exception as critical
            _metricsService.RecordException(ex.GetType().Name, "error", isCritical: true, orchestrationContext.CorrelationId);

            _logger.LogErrorWithHierarchy(orchestrationContext, ex,
                "Failed to stop orchestration");
            throw;
        }
    }

    public async Task<OrchestrationStatusModel> GetOrchestrationStatusAsync(Guid orchestratedFlowId)
    {
        // Create Layer 1 hierarchical context for status retrieval - enhanced to Layer 2 when workflow data is available
        var correlationId = GetCurrentCorrelationIdOrGenerate();
        var context = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId
        };

        _logger.LogDebugWithHierarchy(context,
            "Getting orchestration status");

        try
        {
            var orchestrationData = await _cacheService.GetOrchestrationDataAsync(orchestratedFlowId, context);

            if (orchestrationData == null)
            {
                _logger.LogDebugWithHierarchy(context,
                    "Orchestration not found in cache");

                return new OrchestrationStatusModel
                {
                    OrchestratedFlowId = orchestratedFlowId,
                    IsActive = false,
                    StartedAt = null,
                    ExpiresAt = null,
                    StepCount = 0,
                    AssignmentCount = 0
                };
            }

            var totalAssignments = orchestrationData.Assignments.Values.Sum(list => list.Count);

            // Enhance context to Layer 2 with workflow data
            context.WorkflowId = orchestrationData.OrchestratedFlow.WorkflowId;

            var status = new OrchestrationStatusModel
            {
                OrchestratedFlowId = orchestratedFlowId,
                IsActive = true,
                StartedAt = orchestrationData.CreatedAt,
                ExpiresAt = orchestrationData.ExpiresAt,
                StepCount = orchestrationData.StepEntities.Count,
                AssignmentCount = totalAssignments
            };

            _logger.LogDebugWithHierarchy(context,
                "Retrieved orchestration status. IsActive: {IsActive}, StepCount: {StepCount}, AssignmentCount: {AssignmentCount}",
                status.IsActive, status.StepCount, status.AssignmentCount);

            return status;
        }
        catch (Exception ex)
        {
            // Record orchestration status retrieval exception as non-critical
            _metricsService.RecordException(ex.GetType().Name, "error", isCritical: false, context.CorrelationId);

            _logger.LogErrorWithHierarchy(context, ex,
                "Error getting orchestration status");
            throw;
        }
    }

    /// <summary>
    /// Validates the health of all processors required for orchestration
    /// </summary>
    /// <param name="processorIds">Collection of processor IDs to validate</param>
    /// <param name="context">Hierarchical logging context for validation operations</param>
    /// <exception cref="InvalidOperationException">Thrown when one or more processors are unhealthy</exception>
    private async Task ValidateProcessorHealthAsync(List<Guid> processorIds, HierarchicalLoggingContext context)
    {

        var unhealthyProcessors = new List<(Guid ProcessorId, string Reason)>();

        foreach (var processorId in processorIds)
        {
            try
            {
                // Get processor health from cache using configurable map name
                var healthData = await _rawCacheService.GetAsync(_processorHealthMapName, processorId.ToString(), context);

                if (string.IsNullOrEmpty(healthData))
                {
                    unhealthyProcessors.Add((processorId, "No health data found in cache"));
                    _logger.LogWarningWithHierarchy(context, "No health data found for processor {ProcessorId}", processorId);
                    continue;
                }

                // Deserialize health entry
                var healthEntry = System.Text.Json.JsonSerializer.Deserialize<ProcessorHealthCacheEntry>(healthData);

                if (healthEntry == null)
                {
                    unhealthyProcessors.Add((processorId, "Failed to deserialize health data"));
                    _logger.LogWarningWithHierarchy(context, "Failed to deserialize health data for processor {ProcessorId}", processorId);
                    continue;
                }

                // Check if health entry has expired
                if (healthEntry.IsExpired)
                {
                    unhealthyProcessors.Add((processorId, $"Health data expired at {healthEntry.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC"));
                    _logger.LogWarningWithHierarchy(context, "Health data expired for processor {ProcessorId}. ExpiresAt: {ExpiresAt}",
                        processorId, healthEntry.ExpiresAt);
                    continue;
                }

                // Check if health data is still valid based on health check interval
                var nowUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var timeSinceLastUpdate = nowUnixTime - healthEntry.LastUpdated;
                var healthCheckIntervalWithBuffer = healthEntry.HealthCheckInterval * 2; // Allow 2x interval as buffer

                if (timeSinceLastUpdate > healthCheckIntervalWithBuffer)
                {
                    unhealthyProcessors.Add((processorId, $"Health data is stale. Last updated {timeSinceLastUpdate}s ago, threshold: {healthCheckIntervalWithBuffer}s"));
                    _logger.LogWarningWithHierarchy(context, "Health data for processor {ProcessorId} is stale. LastUpdated: {LastUpdated}, " +
                        "TimeSinceLastUpdate: {TimeSinceLastUpdate}s, HealthCheckInterval: {HealthCheckInterval}s, BufferThreshold: {BufferThreshold}s",
                        processorId, healthEntry.LastUpdated, timeSinceLastUpdate, healthEntry.HealthCheckInterval, healthCheckIntervalWithBuffer);
                    continue;
                }

                // Check processor health status
                if (healthEntry.Status != HealthStatus.Healthy)
                {
                    unhealthyProcessors.Add((processorId, $"Processor status: {healthEntry.Status}, Message: {healthEntry.Message}"));
                    _logger.LogWarningWithHierarchy(context, "Processor {ProcessorId} is not healthy. Status: {Status}, Message: {Message}",
                        processorId, healthEntry.Status, healthEntry.Message);
                    continue;
                }

                _logger.LogDebugWithHierarchy(context, "Processor {ProcessorId} is healthy. Status: {Status}, LastUpdated: {LastUpdated}, " +
                    "HealthCheckInterval: {HealthCheckInterval}s, TimeSinceLastUpdate: {TimeSinceLastUpdate}s, IsValid: true",
                    processorId, healthEntry.Status, healthEntry.LastUpdated, healthEntry.HealthCheckInterval, timeSinceLastUpdate);
            }
            catch (Exception ex)
            {
                // Record processor health check exception as non-critical
                _metricsService.RecordException(ex.GetType().Name, "warning", isCritical: false, context.CorrelationId);

                unhealthyProcessors.Add((processorId, $"Error checking health: {ex.Message}"));
                _logger.LogErrorWithHierarchy(context, ex, "Error checking health for processor {ProcessorId}", processorId);
            }
        }

        // If any processors are unhealthy, fail the orchestration
        if (unhealthyProcessors.Count > 0)
        {
            var errorMessage = $"Failed to start orchestration: {unhealthyProcessors.Count} of {processorIds.Count} processors are unhealthy. " +
                              $"Unhealthy processors: {string.Join(", ", unhealthyProcessors.Select(p => $"{p.ProcessorId} ({p.Reason})"))}";

            _logger.LogErrorWithHierarchy(context, "Processor health validation failed. {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformationWithHierarchy(context, "All {ProcessorCount} processors are healthy", processorIds.Count);
    }

    public async Task<bool> ValidateProcessorHealthForExecutionAsync(List<Guid> processorIds, HierarchicalLoggingContext context)
    {
        try
        {
            await ValidateProcessorHealthAsync(processorIds, context);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Health validation failed
            return false;
        }
    }

    public async Task<ProcessorHealthResponse?> GetProcessorHealthAsync(Guid processorId, HierarchicalLoggingContext context)
    {
        using var activity = ActivitySource.StartActivity("GetProcessorHealth");
        activity?.SetTag("processor.id", processorId.ToString());

        _logger.LogDebugWithHierarchy(context, "Getting health status for processor {ProcessorId}", processorId);

        try
        {
            // Get processor health from cache using configurable map name
            var healthData = await _rawCacheService.GetAsync(_processorHealthMapName, processorId.ToString(), context);

            if (string.IsNullOrEmpty(healthData))
            {
                _logger.LogDebugWithHierarchy(context, "No health data found for processor {ProcessorId}", processorId);
                return null;
            }

            var healthEntry = JsonSerializer.Deserialize<ProcessorHealthCacheEntry>(healthData);
            if (healthEntry == null)
            {
                _logger.LogWarningWithHierarchy(context, "Failed to deserialize health data for processor {ProcessorId}", processorId);
                return null;
            }

            // Check if health data is expired
            if (healthEntry.IsExpired)
            {
                _logger.LogDebugWithHierarchy(context, "Health data for processor {ProcessorId} is expired. ExpiresAt: {ExpiresAt}",
                    processorId, healthEntry.ExpiresAt);
                return null;
            }

            // Check if health data is still valid based on health check interval
            var nowUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeSinceLastUpdate = nowUnixTime - healthEntry.LastUpdated;
            var healthCheckIntervalWithBuffer = healthEntry.HealthCheckInterval * 2; // Allow 2x interval as buffer

            if (timeSinceLastUpdate > healthCheckIntervalWithBuffer)
            {
                _logger.LogWarningWithHierarchy(context, "Health data for processor {ProcessorId} is stale. LastUpdated: {LastUpdated}, " +
                    "TimeSinceLastUpdate: {TimeSinceLastUpdate}s, HealthCheckInterval: {HealthCheckInterval}s, BufferThreshold: {BufferThreshold}s",
                    processorId, healthEntry.LastUpdated, timeSinceLastUpdate, healthEntry.HealthCheckInterval, healthCheckIntervalWithBuffer);
                return null;
            }

            var response = new Shared.Models.ProcessorHealthResponse
            {
                CorrelationId = healthEntry.CorrelationId,
                HealthCheckId = healthEntry.HealthCheckId,
                ProcessorId = healthEntry.ProcessorId,
                Status = healthEntry.Status,
                Message = healthEntry.Message,
                LastUpdated = healthEntry.LastUpdated,
                HealthCheckInterval = healthEntry.HealthCheckInterval,
                ExpiresAt = healthEntry.ExpiresAt,
                ReportingPodId = healthEntry.ReportingPodId,
                Uptime = healthEntry.Uptime,
                Metadata = healthEntry.Metadata,
                PerformanceMetrics = healthEntry.PerformanceMetrics,
                HealthChecks = healthEntry.HealthChecks,
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogDebugWithHierarchy(context, "Successfully retrieved health status for processor {ProcessorId}. Status: {Status}, LastUpdated: {LastUpdated}, " +
                "HealthCheckInterval: {HealthCheckInterval}s, TimeSinceLastUpdate: {TimeSinceLastUpdate}s, IsValid: true",
                processorId, healthEntry.Status, healthEntry.LastUpdated, healthEntry.HealthCheckInterval, timeSinceLastUpdate);

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);
            _logger.LogErrorWithHierarchy(context, ex, "Error getting health status for processor {ProcessorId}", processorId);
            throw;
        }
    }

    public async Task<ProcessorHealthResponse?> GetProcessorHealthAsync(Guid processorId)
    {
        using var activity = ActivitySource.StartActivity("GetProcessorHealth");
        activity?.SetTag("processor.id", processorId.ToString());

        _logger.LogDebugWithCorrelation("Getting health status for processor {ProcessorId}", processorId);

        try
        {
            // Get processor health from cache using configurable map name
            var healthData = await _rawCacheService.GetAsync(_processorHealthMapName, processorId.ToString());

            if (string.IsNullOrEmpty(healthData))
            {
                _logger.LogDebugWithCorrelation("No health data found for processor {ProcessorId}", processorId);
                return null;
            }

            var healthEntry = JsonSerializer.Deserialize<ProcessorHealthCacheEntry>(healthData);
            if (healthEntry == null)
            {
                _logger.LogWarningWithCorrelation("Failed to deserialize health data for processor {ProcessorId}", processorId);
                return null;
            }

            // Check if health data is still valid based on health check interval
            var nowUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeSinceLastUpdate = nowUnixTime - healthEntry.LastUpdated;
            var healthCheckIntervalWithBuffer = healthEntry.HealthCheckInterval * 2; // Allow 2x interval as buffer

            if (timeSinceLastUpdate > healthCheckIntervalWithBuffer)
            {
                _logger.LogWarningWithCorrelation("Health data for processor {ProcessorId} is stale. LastUpdated: {LastUpdated}, " +
                    "TimeSinceLastUpdate: {TimeSinceLastUpdate}s, HealthCheckInterval: {HealthCheckInterval}s, BufferThreshold: {BufferThreshold}s",
                    processorId, healthEntry.LastUpdated, timeSinceLastUpdate, healthEntry.HealthCheckInterval, healthCheckIntervalWithBuffer);
                return null;
            }

            var response = new Shared.Models.ProcessorHealthResponse
            {
                CorrelationId = healthEntry.CorrelationId,
                HealthCheckId = healthEntry.HealthCheckId,
                ProcessorId = healthEntry.ProcessorId,
                Status = healthEntry.Status,
                Message = healthEntry.Message,
                LastUpdated = healthEntry.LastUpdated,
                HealthCheckInterval = healthEntry.HealthCheckInterval,
                ExpiresAt = healthEntry.ExpiresAt,
                ReportingPodId = healthEntry.ReportingPodId,
                Uptime = healthEntry.Uptime,
                Metadata = healthEntry.Metadata,
                PerformanceMetrics = healthEntry.PerformanceMetrics,
                HealthChecks = healthEntry.HealthChecks,
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogDebugWithCorrelation("Successfully retrieved health status for processor {ProcessorId}. Status: {Status}, LastUpdated: {LastUpdated}, " +
                "HealthCheckInterval: {HealthCheckInterval}s, TimeSinceLastUpdate: {TimeSinceLastUpdate}s, IsValid: true",
                processorId, healthEntry.Status, healthEntry.LastUpdated, healthEntry.HealthCheckInterval, timeSinceLastUpdate);

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);
            _logger.LogErrorWithCorrelation(ex, "Error getting health status for processor {ProcessorId}", processorId);
            throw;
        }
    }

    public async Task<ProcessorsHealthResponse?> GetProcessorsHealthByOrchestratedFlowAsync(Guid orchestratedFlowId)
    {
        using var activity = ActivitySource.StartActivity("GetProcessorsHealthByOrchestratedFlow");
        activity?.SetTag("orchestrated_flow.id", orchestratedFlowId.ToString());

        // Create basic hierarchical context for processor health retrieval
        var correlationId = GetCurrentCorrelationIdOrGenerate();
        var healthContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId
        };

        try
        {
            // Get orchestration data from cache to retrieve processor IDs
            var orchestrationData = await _cacheService.GetOrchestrationDataAsync(orchestratedFlowId, healthContext);

            if (orchestrationData == null)
            {
                _logger.LogDebugWithHierarchy(healthContext, "Orchestrated flow not found in cache");
                return null;
            }

            if (orchestrationData.IsExpired)
            {
                _logger.LogDebugWithHierarchy(healthContext, "Orchestration data is expired. ExpiresAt: {ExpiresAt}",
                    orchestrationData.ExpiresAt);
                return null;
            }

            var processorIds = orchestrationData.ProcessorIds;
            _logger.LogDebugWithHierarchy(healthContext, "Found {ProcessorCount} processors for orchestrated flow",
                processorIds.Count);

            var processorsHealth = new Dictionary<Guid, Shared.Models.ProcessorHealthResponse>();
            var summary = new Shared.Models.ProcessorsHealthSummary
            {
                TotalProcessors = processorIds.Count
            };

            // Get health status for each processor
            foreach (var processorId in processorIds)
            {
                try
                {
                    var healthResponse = await GetProcessorHealthAsync(processorId, healthContext);

                    if (healthResponse != null)
                    {
                        processorsHealth[processorId] = healthResponse;

                        // Update summary counts
                        switch (healthResponse.Status)
                        {
                            case HealthStatus.Healthy:
                                summary.HealthyProcessors++;
                                break;
                            case HealthStatus.Degraded:
                                summary.DegradedProcessors++;
                                summary.ProblematicProcessors.Add(processorId);
                                break;
                            case HealthStatus.Unhealthy:
                                summary.UnhealthyProcessors++;
                                summary.ProblematicProcessors.Add(processorId);
                                break;
                        }
                    }
                    else
                    {
                        summary.NoHealthDataProcessors++;
                        summary.ProblematicProcessors.Add(processorId);
                        _logger.LogWarningWithHierarchy(healthContext, "No health data found for processor {ProcessorId}",
                            processorId);
                    }
                }
                catch (Exception ex)
                {
                    summary.NoHealthDataProcessors++;
                    summary.ProblematicProcessors.Add(processorId);
                    _logger.LogErrorWithHierarchy(healthContext, ex, "Error getting health for processor {ProcessorId}",
                        processorId);
                }
            }

            // Determine overall status
            if (summary.UnhealthyProcessors > 0 || summary.NoHealthDataProcessors > 0)
            {
                summary.OverallStatus = HealthStatus.Unhealthy;
            }
            else if (summary.DegradedProcessors > 0)
            {
                summary.OverallStatus = HealthStatus.Degraded;
            }
            else
            {
                summary.OverallStatus = HealthStatus.Healthy;
            }

            var response = new Shared.Models.ProcessorsHealthResponse
            {
                OrchestratedFlowId = orchestratedFlowId,
                Processors = processorsHealth,
                Summary = summary,
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogInformationWithHierarchy(healthContext, "Successfully retrieved health status for {ProcessorCount} processors. " +
                                 "Healthy: {HealthyCount}, Degraded: {DegradedCount}, Unhealthy: {UnhealthyCount}, NoData: {NoDataCount}, Overall: {OverallStatus}",
                processorIds.Count, summary.HealthyProcessors, summary.DegradedProcessors,
                summary.UnhealthyProcessors, summary.NoHealthDataProcessors, summary.OverallStatus);

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);
            _logger.LogErrorWithHierarchy(healthContext, ex, "Error getting processors health for orchestrated flow");
            throw;
        }
    }

    /// <summary>
    /// Finds entry points in the workflow by identifying steps that are not referenced as next steps
    /// </summary>
    /// <param name="stepEntities">Dictionary of step entities with step navigation data</param>
    /// <param name="context">Hierarchical logging context for workflow validation</param>
    /// <returns>Collection of step IDs that are entry points</returns>
    private List<Guid> FindEntryPoints(Dictionary<Guid, StepNavigationData> stepEntities, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Finding entry points from {StepCount} steps",
            stepEntities.Count);

        var entryPoints = new List<Guid>();

        // Get all next step IDs referenced across all steps
        var allNextStepIds = stepEntities.Values
            .SelectMany(s => s.NextStepIds)
            .Distinct()
            .ToHashSet();

        // Find steps that are not referenced as next steps (entry points)
        foreach (var stepId in stepEntities.Keys)
        {
            if (!allNextStepIds.Contains(stepId))
            {
                entryPoints.Add(stepId);
                _logger.LogDebugWithHierarchy(context, "Found entry point: {StepId}", stepId);
            }
        }

        _logger.LogInformationWithHierarchy(context, "Found {EntryPointCount} entry points in workflow", entryPoints.Count);
        return entryPoints;
    }

    /// <summary>
    /// Validates that the workflow has valid entry points and is not cyclical
    /// </summary>
    /// <param name="entryPoints">Collection of entry point step IDs</param>
    /// <param name="context">Hierarchical logging context for workflow validation</param>
    /// <exception cref="InvalidOperationException">Thrown when workflow has no entry points (indicating cyclicity)</exception>
    private void ValidateEntryPoints(List<Guid> entryPoints, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Validating {EntryPointCount} entry points", entryPoints.Count);

        if (entryPoints.Count == 0)
        {
            var errorMessage = "Failed to start orchestration: No entry points found in workflow. " +
                              "This indicates that the flow is cyclical, which is not allowed. " +
                              "Every workflow must have at least one step that is not referenced as a next step.";

            _logger.LogErrorWithHierarchy(context, "Entry point validation failed: {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformationWithHierarchy(context, "Entry point validation passed. Found {EntryPointCount} valid entry points: {EntryPoints}",
            entryPoints.Count, string.Join(", ", entryPoints));
    }

    /// <summary>
    /// Finds termination points in the workflow by identifying steps that have no next steps
    /// </summary>
    /// <param name="stepEntities">Dictionary of step entities with step navigation data</param>
    /// <param name="context">Hierarchical logging context for workflow validation</param>
    /// <returns>Collection of step IDs that are termination points</returns>
    private List<Guid> FindTerminationPoints(Dictionary<Guid, StepNavigationData> stepEntities, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Finding termination points from {StepCount} steps",
            stepEntities.Count);

        var terminationPoints = new List<Guid>();

        // Find steps that have no next steps (termination points)
        foreach (var kvp in stepEntities)
        {
            if (kvp.Value.NextStepIds == null || kvp.Value.NextStepIds.Count == 0)
            {
                terminationPoints.Add(kvp.Key);
                _logger.LogDebugWithHierarchy(context, "Found termination point: {StepId}", kvp.Key);
            }
        }

        _logger.LogInformationWithHierarchy(context, "Found {TerminationPointCount} termination points in workflow", terminationPoints.Count);
        return terminationPoints;
    }

    /// <summary>
    /// Validates that the workflow has valid termination points
    /// </summary>
    /// <param name="terminationPoints">Collection of termination point step IDs</param>
    /// <param name="context">Hierarchical logging context for workflow validation</param>
    /// <exception cref="InvalidOperationException">Thrown when workflow has no termination points (indicating endless loop)</exception>
    private void ValidateTerminationPoints(List<Guid> terminationPoints, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Validating {TerminationPointCount} termination points", terminationPoints.Count);

        if (terminationPoints.Count == 0)
        {
            var errorMessage = "Failed to start orchestration: No termination points found in workflow. " +
                              "This indicates that the flow has no end points, which could lead to endless execution. " +
                              "Every workflow must have at least one step that has no next steps.";

            _logger.LogErrorWithHierarchy(context, "Termination point validation failed: {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformationWithHierarchy(context, "Termination point validation passed. Found {TerminationPointCount} valid termination points: {TerminationPoints}",
            terminationPoints.Count, string.Join(", ", terminationPoints));
    }

    /// <summary>
    /// Validates that the workflow does not contain circular references
    /// </summary>
    /// <param name="stepEntities">Dictionary of step entities with step navigation data</param>
    /// <param name="terminationPoints">Collection of termination point step IDs</param>
    /// <param name="context">Hierarchical logging context for workflow validation</param>
    /// <exception cref="InvalidOperationException">Thrown when circular workflow is detected</exception>
    private void ValidateCircularWorkflow(Dictionary<Guid, StepNavigationData> stepEntities, List<Guid> terminationPoints, HierarchicalLoggingContext context)
    {
        _logger.LogDebugWithHierarchy(context, "Validating circular workflow from {StepCount} steps", stepEntities.Count);

        // Step 1: Aggregate all next steps without distinct (to find duplicates)
        var allNextSteps = stepEntities.Values
            .SelectMany(s => s.NextStepIds)
            .ToList();

        _logger.LogDebugWithHierarchy(context, "Found {TotalNextSteps} total next step references", allNextSteps.Count);

        // Step 2: Find steps that appear more than once
        var stepOccurrences = allNextSteps
            .GroupBy(stepId => stepId)
            .Where(group => group.Count() > 1)
            .Select(group => new { StepId = group.Key, Count = group.Count() })
            .ToList();

        if (stepOccurrences.Count == 0)
        {
            _logger.LogInformationWithHierarchy(context, "Circular workflow validation passed. No steps appear multiple times as next steps");
            return;
        }

        _logger.LogDebugWithHierarchy(context, "Found {DuplicateStepCount} steps that appear multiple times as next steps: {DuplicateSteps}",
            stepOccurrences.Count, string.Join(", ", stepOccurrences.Select(s => $"{s.StepId} ({s.Count} times)")));

        // Step 3: Check if any of the duplicate steps are NOT in termination points
        var terminationPointsSet = terminationPoints.ToHashSet();
        var circularSteps = stepOccurrences
            .Where(occurrence => !terminationPointsSet.Contains(occurrence.StepId))
            .ToList();

        if (circularSteps.Count > 0)
        {
            var circularStepDetails = circularSteps.Select(s => $"{s.StepId} (appears {s.Count} times)");
            var errorMessage = $"Failed to start orchestration: Circular workflow detected. " +
                              $"The following steps appear multiple times as next steps but are not termination points: " +
                              $"{string.Join(", ", circularStepDetails)}. " +
                              $"This indicates a circular reference in the workflow, which could lead to infinite loops.";

            _logger.LogErrorWithHierarchy(context, "Circular workflow validation failed: {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformationWithHierarchy(context, "Circular workflow validation passed. All duplicate steps ({DuplicateStepCount}) are termination points",
            stepOccurrences.Count);
    }

    /// <summary>
    /// Gets correlation ID from current context or generates a new one if none exists.
    /// This is appropriate for workflow start operations.
    /// </summary>
    private Guid GetCurrentCorrelationIdOrGenerate()
    {
        // Try to get from Activity baggage first (from HTTP request context)
        var activity = Activity.Current;
        if (activity?.GetBaggageItem("correlation.id") is string baggageValue &&
            Guid.TryParse(baggageValue, out var correlationId))
        {
            return correlationId;
        }

        // Generate new correlation ID for new workflow start
        return Guid.NewGuid();
    }

    /// <summary>
    /// Starts the scheduler for the orchestrated flow if cron expression is configured and enabled
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID</param>
    /// <param name="orchestratedFlow">The orchestrated flow entity</param>
    /// <param name="context">The hierarchical logging context</param>
    private async Task StartSchedulerIfConfiguredAsync(Guid orchestratedFlowId, OrchestratedFlowEntity orchestratedFlow, HierarchicalLoggingContext context)
    {
        try
        {
            // Check if scheduling is configured and enabled
            if (string.IsNullOrWhiteSpace(orchestratedFlow.CronExpression) || !orchestratedFlow.IsScheduleEnabled)
            {
                _logger.LogDebugWithHierarchy(context,
                    "Scheduler not configured or disabled. CronExpression: {CronExpression}, IsScheduleEnabled: {IsScheduleEnabled}",
                    orchestratedFlow.CronExpression ?? "null", orchestratedFlow.IsScheduleEnabled);
                return;
            }

            // Validate cron expression
            if (!_schedulerService.ValidateCronExpression(orchestratedFlow.CronExpression))
            {
                _logger.LogWarningWithHierarchy(context,
                    "Invalid cron expression: {CronExpression}. Skipping scheduler start.",
                    orchestratedFlow.CronExpression);
                return;
            }

            // Log one-time execution information
            if (orchestratedFlow.IsOneTimeExecution)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Starting one-time execution scheduler with cron expression: {CronExpression}. Job will stop after first execution.",
                    orchestratedFlow.CronExpression);
            }

            // Check if scheduler is already running
            var isRunning = await _schedulerService.IsSchedulerRunningAsync(orchestratedFlowId);
            if (isRunning)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Scheduler already running. Updating with new cron expression: {CronExpression}",
                    orchestratedFlow.CronExpression);

                // Update existing scheduler with new cron expression
                await _schedulerService.UpdateSchedulerAsync(orchestratedFlowId, orchestratedFlow.CronExpression);
            }
            else
            {
                _logger.LogInformationWithHierarchy(context,
                    "Starting scheduler with cron expression: {CronExpression}",
                    orchestratedFlow.CronExpression);

                // Start new scheduler
                await _schedulerService.StartSchedulerAsync(orchestratedFlowId, orchestratedFlow.CronExpression);
            }

            // Get next execution time for logging
            var nextExecution = await _schedulerService.GetNextExecutionTimeAsync(orchestratedFlowId);
            _logger.LogInformationWithHierarchy(context,
                "Scheduler configured. Next execution: {NextExecution}",
                nextExecution?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Unknown");
        }
        catch (Exception ex)
        {
            // Record scheduler start exception as non-critical (doesn't prevent orchestration)
            _metricsService.RecordException(ex.GetType().Name, "warning", isCritical: false, context.CorrelationId);

            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to start scheduler. CronExpression: {CronExpression}",
                orchestratedFlow.CronExpression ?? "null");

            // Don't throw - scheduler failure shouldn't prevent orchestration start
            // The orchestration can still be executed manually
        }
    }

    /// <summary>
    /// Stops the scheduler for the orchestrated flow if it's running
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID</param>
    /// <param name="context">Hierarchical logging context</param>
    private async Task StopSchedulerIfRunningAsync(Guid orchestratedFlowId, HierarchicalLoggingContext context)
    {
        try
        {
            // Check if scheduler is running
            var isRunning = await _schedulerService.IsSchedulerRunningAsync(orchestratedFlowId);
            if (!isRunning)
            {
                _logger.LogDebugWithHierarchy(context,
                    "No scheduler running");
                return;
            }

            _logger.LogInformationWithHierarchy(context,
                "Stopping scheduler");

            // Stop the scheduler
            await _schedulerService.StopSchedulerAsync(orchestratedFlowId);

            _logger.LogInformationWithHierarchy(context,
                "Successfully stopped scheduler");
        }
        catch (Exception ex)
        {
            // Record scheduler stop exception as non-critical (doesn't prevent orchestration stop)
            _metricsService.RecordException(ex.GetType().Name, "warning", isCritical: false, context.CorrelationId);

            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to stop scheduler");

            // Don't throw - scheduler failure shouldn't prevent orchestration stop
            // The cache cleanup is more important
        }
    }

    /// <summary>
    /// Validates assignment entity schemas during orchestration start.
    /// Validates AddressAssignmentModel, DeliveryAssignmentModel, and PluginAssignmentModel payloads against their main schemas.
    /// </summary>
    /// <param name="assignments">Dictionary of assignments with stepId as key and list of assignment models as value</param>
    /// <param name="context">Hierarchical logging context for schema validation</param>
    private async Task ValidateAssignmentSchemas(Dictionary<Guid, List<AssignmentModel>> assignments, HierarchicalLoggingContext context)
    {
        var allAssignments = assignments.Values.SelectMany(list => list).ToList();

        if (!allAssignments.Any())
        {
            _logger.LogDebugWithHierarchy(context, "No assignment entities found. Skipping validation.");
            return;
        }

        _logger.LogInformationWithHierarchy(context, "Starting assignment schema validation for {Count} entities.", allAssignments.Count);

        var validationTasks = allAssignments.Select(async assignment =>
        {
            try
            {
                if (assignment is AddressAssignmentModel addressModel)
                {
                    await ValidatePayloadAgainstSchema(addressModel.Payload, addressModel.SchemaId, "Address", addressModel.Name, addressModel.Version, context);
                }
                else if (assignment is DeliveryAssignmentModel deliveryModel)
                {
                    await ValidatePayloadAgainstSchema(deliveryModel.Payload, deliveryModel.SchemaId, "Delivery", deliveryModel.Name, deliveryModel.Version, context);
                }
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithHierarchy(context, ex, "Schema validation failed for assignment. EntityId: {EntityId}, Type: {Type}",
                    assignment.EntityId, assignment.GetType().Name);
                throw;
            }
        });

        await Task.WhenAll(validationTasks);
        _logger.LogInformationWithHierarchy(context, "Assignment schema validation completed successfully for {Count} entities.", allAssignments.Count);
    }
    /// <summary>
    /// Validates entity payload against its schema definition
    /// </summary>
    /// <param name="payload">The payload to validate</param>
    /// <param name="schemaId">The schema ID to validate against</param>
    /// <param name="entityType">Type of entity for logging</param>
    /// <param name="name">Entity name for logging</param>
    /// <param name="version">Entity version for logging</param>
    /// <param name="context">Hierarchical logging context for tracing</param>
    private async Task ValidatePayloadAgainstSchema(string payload, Guid schemaId, string entityType, string name, string version, HierarchicalLoggingContext context)
    {
        // Skip validation if SchemaId is empty (optional validation)
        if (schemaId == Guid.Empty)
        {
            _logger.LogDebugWithHierarchy(context, "Schema validation skipped for {EntityType} entity - SchemaId is empty. Name: {Name}, Version: {Version}",
                entityType, name, version);
            return;
        }

        var schemaDefinition = await _managerHttpClient.GetSchemaDefinitionAsync(schemaId, context);

        if (string.IsNullOrEmpty(schemaDefinition))
        {
            _logger.LogWarningWithHierarchy(context, "Schema definition is missing for {EntityType} entity. SchemaId: {SchemaId}, Name: {Name}, Version: {Version}",
                entityType, schemaId, name, version);
            return;
        }

        var isValid = await _schemaValidator.ValidateAsync(payload, schemaDefinition, context);

        if (!isValid)
        {
            throw new InvalidOperationException($"Schema validation failed for {entityType} entity. Name: {name}, Version: {version}, SchemaId: {schemaId}");
        }

        _logger.LogDebugWithHierarchy(context, "Schema validation passed for {EntityType} entity. Name: {Name}, Version: {Version}",
            entityType, name, version);
    }


}
