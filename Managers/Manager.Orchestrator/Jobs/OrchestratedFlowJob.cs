using System.Diagnostics;
using Manager.Orchestrator.Interfaces;
using Manager.Orchestrator.Models;
using MassTransit;
using Quartz;
using Shared.Correlation;
using Shared.Entities;
using Shared.MassTransit.Commands;
using Shared.Models;

namespace Manager.Orchestrator.Jobs;

/// <summary>
/// Quartz job that executes orchestrated flow entry points on a scheduled basis.
/// This job is responsible for triggering the execution of orchestrated flows based on cron expressions.
/// </summary>
[DisallowConcurrentExecution] // Prevent multiple instances of the same job running simultaneously
public class OrchestratedFlowJob : IJob
{
    private readonly IOrchestrationCacheService _orchestrationCacheService;
    private readonly IOrchestrationService _orchestrationService;
    private readonly IOrchestrationSchedulerService _schedulerService;
    private readonly IBus _bus;
    private readonly ILogger<OrchestratedFlowJob> _logger;
    private readonly IOrchestratorFlowMetricsService _flowMetricsService;
    
    /// <summary>
    /// Initializes a new instance of the OrchestratedFlowJob class.
    /// </summary>
    /// <param name="orchestrationCacheService">Service for orchestration cache operations</param>
    /// <param name="orchestrationService">Service for orchestration business logic including health validation</param>
    /// <param name="schedulerService">Service for scheduler operations</param>
    /// <param name="bus">MassTransit bus for publishing commands</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="flowMetricsService">Flow metrics service for recording flow metrics</param>
    
    public OrchestratedFlowJob(
        IOrchestrationCacheService orchestrationCacheService,
        IOrchestrationService orchestrationService,
        IOrchestrationSchedulerService schedulerService,
        IBus bus,
        ILogger<OrchestratedFlowJob> logger,
        IOrchestratorFlowMetricsService flowMetricsService
        )
    {
        _orchestrationCacheService = orchestrationCacheService;
        _orchestrationService = orchestrationService;
        _schedulerService = schedulerService;
        _bus = bus;
        _logger = logger;
        _flowMetricsService = flowMetricsService;
    }

    /// <summary>
    /// Executes the orchestrated flow job.
    /// This method is called by Quartz scheduler based on the configured cron expression.
    /// </summary>
    /// <param name="context">Job execution context containing job data</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task Execute(IJobExecutionContext context)
    {
        var jobDataMap = context.JobDetail.JobDataMap;
        var orchestratedFlowId = Guid.Parse(jobDataMap.GetString("OrchestratedFlowId")!);

        // Use stored correlation ID to maintain correlation chain, or generate new one for truly scheduled jobs
        var originalCorrelationIdString = jobDataMap.GetString("OriginalCorrelationId");
        var correlationId = originalCorrelationIdString != null
            ? Guid.Parse(originalCorrelationIdString)
            : Guid.NewGuid();

        var correlationSource = originalCorrelationIdString != null ? "inherited" : "generated";
        CorrelationIdContext.SetCorrelationIdStatic(correlationId);

        // Create Layer 1 hierarchical logging context (orchestration level)
        var orchestrationContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId
        };

        _logger.LogInformationWithHierarchy(orchestrationContext,
            "Starting scheduled execution of orchestrated flow. CorrelationSource: {CorrelationSource}",
            correlationSource);

        try
        {
            // Get orchestration cache data
            var orchestrationData = await _orchestrationCacheService.GetOrchestrationDataAsync(orchestratedFlowId, orchestrationContext);
            if (orchestrationData == null)
            {
                _logger.LogWarningWithHierarchy(orchestrationContext,
                    "Orchestration data not found. Skipping scheduled execution.");
                return;
            }

            // Extract WorkflowId and create Layer 2 context
            var workflowId = orchestrationData.OrchestratedFlow.WorkflowId;
            var workflowContext = new HierarchicalLoggingContext
            {
                OrchestratedFlowId = orchestratedFlowId,
                WorkflowId = workflowId,
                CorrelationId = correlationId
            };

            // Get cached entry points (already calculated and validated during orchestration setup)
            var entryPoints = orchestrationData.EntryPoints;

            if (!entryPoints.Any())
            {
                _logger.LogWarningWithHierarchy(workflowContext,
                    "No entry points found in cached orchestration data. Skipping scheduled execution.");
                return;
            }

            _logger.LogInformationWithHierarchy(workflowContext,
                "Using {EntryPointCount} cached entry points for scheduled execution", entryPoints.Count);

            // Step 6: Check processor health
            _logger.LogDebugWithHierarchy(workflowContext,
                "Validating processor health before scheduled execution");
            var processorIds = orchestrationData.ProcessorIds;
            var isHealthy = await _orchestrationService.ValidateProcessorHealthForExecutionAsync(processorIds, workflowContext);

            if (!isHealthy)
            {
                _logger.LogWarningWithHierarchy(workflowContext,
                    "Processor health validation failed. Skipping execution due to unhealthy processors. ProcessorCount: {ProcessorCount}",
                    processorIds.Count);
                return;
            }

            _logger.LogInformationWithHierarchy(workflowContext,
                "All {ProcessorCount} processors are healthy. Proceeding with scheduled execution",
                processorIds.Count);

            // Execute entry points (using the same logic as manual execution)
            await ExecuteEntryPointsAsync(orchestratedFlowId, workflowId, entryPoints, orchestrationData, correlationId);

            _logger.LogInformationWithHierarchy(workflowContext,
                "Successfully completed scheduled execution of orchestrated flow");

            // Check if this is a one-time execution and stop the scheduler
            await HandleOneTimeExecutionAsync(orchestratedFlowId, orchestrationData.OrchestratedFlow, workflowContext);
        }
        catch (Exception ex)
        {
            // Use orchestration context for error logging (Layer 1)
            _logger.LogErrorWithHierarchy(orchestrationContext, ex,
                "Failed to execute scheduled orchestrated flow");

            // Entry point execution metrics removed for volume optimization

            // Re-throw to let Quartz handle the failure (retry policies, etc.)
            throw;
        }
    }

    /// <summary>
    /// Executes the entry points for the orchestrated flow.
    /// This method contains the core logic for publishing ExecuteActivityCommand for each entry point.
    /// Enhanced with hierarchical logging and WorkflowId support.
    /// </summary>
    /// <param name="orchestratedFlowId">ID of the orchestrated flow</param>
    /// <param name="workflowId">ID of the workflow</param>
    /// <param name="entryPoints">List of entry point step IDs</param>
    /// <param name="orchestrationData">Cached orchestration data</param>
    /// <param name="correlationId">Correlation ID for this execution</param>
    /// <returns>Task representing the asynchronous operation</returns>
    private async Task ExecuteEntryPointsAsync(
        Guid orchestratedFlowId,
        Guid workflowId,
        List<Guid> entryPoints,
        OrchestrationCacheModel orchestrationData,
        Guid correlationId)
    {
        var stopwatch = Stopwatch.StartNew();
        var executionTasks = new List<Task>();
        var publishedCount = 0;

        // Create base workflow context for entry points execution - used throughout the method
        var baseWorkflowContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            WorkflowId = workflowId,
            CorrelationId = correlationId
        };

        foreach (var entryPoint in entryPoints)
        {
            // executionId = Guid.Empty for each entry point, but use orchestration correlation ID
            var executionId = Guid.Empty;

            // Generate publish ID for entry points (declare outside try block for catch access)
            var publishId = Guid.NewGuid();

            // Get processor ID for this step
            var processorId = orchestrationData.StepEntities[entryPoint].ProcessorId;

            // Create Layer 5 hierarchical context for step execution (includes PublishId)
            var stepContext = new HierarchicalLoggingContext
            {
                OrchestratedFlowId = orchestratedFlowId,
                WorkflowId = workflowId,
                CorrelationId = correlationId,
                StepId = entryPoint,
                ProcessorId = processorId,
                PublishId = publishId
            };

            try
            {
                // Get assignments for this entry point step
                var assignmentModels = new List<AssignmentModel>();
                if (orchestrationData.Assignments.TryGetValue(entryPoint, out List<AssignmentModel>? assignments) && assignments != null)
                {
                    assignmentModels.AddRange(assignments);
                }

                // Create ExecuteActivityCommand with WorkflowId
                var command = new ExecuteActivityCommand
                {
                    OrchestratedFlowId = orchestratedFlowId,
                    WorkflowId = workflowId, // ✅ Include WorkflowId in command
                    CorrelationId = correlationId,
                    StepId = entryPoint,
                    ProcessorId = processorId,
                    PublishId = publishId, // Generate publish ID for entry points
                    ExecutionId = executionId,
                    Entities = assignmentModels
                };

                // Layer 4 log - Entry point initiation
                _logger.LogInformationWithHierarchy(stepContext,
                    "Workflow step initiated. WorkflowPhase: {WorkflowPhase}, AssignmentCount: {AssignmentCount}",
                    "EntryPointStart", assignmentModels.Count);

                // Publish command to processor (async)
                var publishTask = _bus.Publish(command);
                executionTasks.Add(publishTask);
                publishedCount++;

                // Record successful event publishing (ExecuteActivityCommand published for entry point)
                _flowMetricsService.RecordEventPublished(success: true, orchestratedFlowId, entryPoint, executionId, publishId);

                // Layer 4 log - Command published
                _logger.LogInformationWithHierarchy(stepContext,
                    "Workflow step command published. WorkflowPhase: {WorkflowPhase}",
                    "CommandPublished");
            }
            catch (Exception ex)
            {
                // Record failed event publishing (ExecuteActivityCommand publishing failed for entry point)
                _flowMetricsService.RecordEventPublished(success: false, orchestratedFlowId, entryPoint, executionId, publishId);

                // Use existing stepContext for error logging (already has Layer 5 with PublishId)
                _logger.LogErrorWithHierarchy(stepContext, ex,
                    "Failed to execute entry point");

                stopwatch.Stop();
                throw;
            }
        }

        // Wait for all publish operations to complete
        if (executionTasks.Any())
        {
            await Task.WhenAll(executionTasks);
            _logger.LogInformationWithHierarchy(baseWorkflowContext,
                "Successfully published {PublishedCount} ExecuteActivityCommands for scheduled execution",
                publishedCount);
        }

        stopwatch.Stop();

        // Entry point execution metrics removed for volume optimization
    }

    /// <summary>
    /// Handles one-time execution logic by stopping the scheduler if the flow is configured for one-time execution
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID</param>
    /// <param name="orchestratedFlow">The orchestrated flow entity</param>
    /// <param name="context">Hierarchical logging context for tracing</param>
    private async Task HandleOneTimeExecutionAsync(Guid orchestratedFlowId, OrchestratedFlowEntity orchestratedFlow, HierarchicalLoggingContext context)
    {
        try
        {
            if (orchestratedFlow.IsOneTimeExecution)
            {
                _logger.LogInformationWithHierarchy(context,
                    "One-time execution completed. Stopping scheduler for orchestrated flow.");

                await _schedulerService.StopSchedulerAsync(orchestratedFlowId);

                _logger.LogInformationWithHierarchy(context,
                    "Successfully stopped scheduler for one-time execution orchestrated flow.");
            }
        }
        catch (Exception ex)
        {
            // Log the scheduler stop failure but don't re-throw
            // The job execution was successful, scheduler cleanup failure shouldn't fail the job
            _logger.LogWarningWithHierarchy(context, ex,
                "Failed to stop scheduler for one-time execution orchestrated flow. Job execution was successful, but scheduler cleanup failed.");
        }
    }
}
