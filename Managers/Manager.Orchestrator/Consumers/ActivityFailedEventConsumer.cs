using System.Diagnostics;
using Manager.Orchestrator.Interfaces;
using Manager.Orchestrator.Models;
using MassTransit;
using Shared.Correlation;
using Shared.Entities.Enums;
using Shared.Extensions;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;
using Shared.Models;
using Shared.Services.Interfaces;

namespace Manager.Orchestrator.Consumers;

/// <summary>
/// Consumer for ActivityFailedEvent that handles workflow progression for failed activities
/// Manages the transition from failed steps to their next steps based on entry conditions
/// </summary>
public class ActivityFailedEventConsumer : IConsumer<ActivityFailedEvent>
{
    private readonly IOrchestrationCacheService _orchestrationCacheService;
    private readonly ICacheService _rawCacheService;
    private readonly ILogger<ActivityFailedEventConsumer> _logger;
    private readonly IBus _bus;
    private readonly IOrchestratorHealthMetricsService _metricsService;
    private readonly IOrchestratorFlowMetricsService _flowMetricsService;
    private readonly IConfiguration _configuration;
    private readonly string _processorActivityMapName;
    private static readonly ActivitySource ActivitySource = new("Manager.Orchestrator.Consumers");

    public ActivityFailedEventConsumer(
        IOrchestrationCacheService orchestrationCacheService,
        ICacheService rawCacheService,
        ILogger<ActivityFailedEventConsumer> logger,
        IBus bus,
        IOrchestratorHealthMetricsService metricsService,
        IOrchestratorFlowMetricsService flowMetricsService,
        IConfiguration configuration)
    {
        _orchestrationCacheService = orchestrationCacheService;
        _rawCacheService = rawCacheService;
        _logger = logger;
        _bus = bus;
        _metricsService = metricsService;
        _flowMetricsService = flowMetricsService;
        _configuration = configuration;
        _processorActivityMapName = _configuration["ProcessorActivityDataCache:MapName"] ?? "processor-activity";
    }

    public async Task Consume(ConsumeContext<ActivityFailedEvent> context)
    {
        var activityEvent = context.Message;
        // ✅ Use the correlation ID from the event message instead of generating new one
        var correlationId = activityEvent.CorrelationId;

        using var activity = ActivitySource.StartActivityWithCorrelation("ProcessActivityFailedEvent");
        activity?.SetTag("orchestratedFlowId", activityEvent.OrchestratedFlowId.ToString())
                ?.SetTag("stepId", activityEvent.StepId.ToString())
                ?.SetTag("processorId", activityEvent.ProcessorId.ToString())
                ?.SetTag("executionId", activityEvent.ExecutionId.ToString())
                ?.SetTag("correlationId", correlationId.ToString());

        var stopwatch = Stopwatch.StartNew();
        var publishedCommands = 0;

        // Create Layer 5 hierarchical context for event processing
        var eventContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = activityEvent.OrchestratedFlowId,
            WorkflowId = activityEvent.WorkflowId,
            CorrelationId = correlationId,
            StepId = activityEvent.StepId,
            ProcessorId = activityEvent.ProcessorId,
            PublishId = activityEvent.PublishId,
            ExecutionId = activityEvent.ExecutionId
        };

        _logger.LogInformationWithHierarchy(eventContext,
            "Processing ActivityFailedEvent. ErrorMessage: {ErrorMessage}",
            activityEvent.ErrorMessage);

        try
        {
            // Record successful command consumption (ActivityFailedEvent consumed)
            _flowMetricsService.RecordCommandConsumed(success: true, activityEvent.OrchestratedFlowId, activityEvent.StepId, activityEvent.ExecutionId, Guid.Empty);

            // Step 1: Read OrchestrationCacheModel from cache
            activity?.SetTag("step", "1-ReadOrchestrationCache");
            var orchestrationData = await _orchestrationCacheService.GetOrchestrationDataAsync(activityEvent.OrchestratedFlowId, eventContext);
            if (orchestrationData == null)
            {
                throw new InvalidOperationException($"Orchestration data not found in cache for OrchestratedFlowId: {activityEvent.OrchestratedFlowId}");
            }

            // Step 2: Get the nextSteps collection from StepEntities
            activity?.SetTag("step", "2-GetNextSteps");
            if (!orchestrationData.StepEntities.TryGetValue(activityEvent.StepId, out var currentStepEntity))
            {
                throw new InvalidOperationException($"Step entity not found for StepId: {activityEvent.StepId}");
            }

            var nextSteps = currentStepEntity.NextStepIds.ToList();
            activity?.SetTag("nextStepCount", nextSteps.Count);

            // Step 3: Check if nextSteps collection is empty (flow branch termination)
            if (nextSteps.Count == 0)
            {
                activity?.SetTag("step", "3-FlowTermination");
                await HandleFlowBranchTerminationAsync(activityEvent);
                
                stopwatch.Stop();
                activity?.SetTag("result", "FlowTerminated")
                        ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                        ?.SetStatus(ActivityStatusCode.Ok);

                _logger.LogInformationWithHierarchy(eventContext,
                    "Flow branch termination detected and processed for failed activity. Duration: {Duration}ms",
                    stopwatch.ElapsedMilliseconds);
                return;
            }

            // Step 4: Process each next step
            activity?.SetTag("step", "4-ProcessNextSteps");
            await ProcessNextStepsAsync(activityEvent, nextSteps, orchestrationData);
            publishedCommands = nextSteps.Count;

            stopwatch.Stop();
            activity?.SetTag("publishedCommands", publishedCommands)
                    ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                    ?.SetTag("result", "Success")
                    ?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformationWithHierarchy(eventContext,
                "Successfully processed ActivityFailedEvent. NextSteps: {NextStepCount}, PublishedCommands: {PublishedCommands}, Duration: {Duration}ms",
                nextSteps.Count, publishedCommands, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message)
                    ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                    ?.SetTag("error.type", ex.GetType().Name)
                    ?.SetTag("result", "Error");

            // Record failed command consumption (ActivityFailedEvent processing failed)
            _flowMetricsService.RecordCommandConsumed(success: false, activityEvent.OrchestratedFlowId, activityEvent.StepId, activityEvent.ExecutionId, Guid.Empty);

            // Record activity failed event processing exception as critical
            _metricsService.RecordException(ex.GetType().Name, "error", isCritical: true, Guid.Empty);

            _logger.LogErrorWithHierarchy(eventContext, ex,
                "Error processing ActivityFailedEvent. Duration: {Duration}ms, ErrorType: {ErrorType}",
                stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Handles flow branch termination by deleting cache processor data
    /// </summary>
    /// <param name="activityEvent">The activity failed event</param>
    private async Task HandleFlowBranchTerminationAsync(ActivityFailedEvent activityEvent)
    {
        using var activity = ActivitySource.StartActivity("HandleFlowBranchTermination");
        activity?.SetTag("processorId", activityEvent.ProcessorId.ToString())
                ?.SetTag("orchestratedFlowId", activityEvent.OrchestratedFlowId.ToString())
                ?.SetTag("stepId", activityEvent.StepId.ToString());

        // Create hierarchical context for flow branch termination
        var terminationContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = activityEvent.OrchestratedFlowId,
            WorkflowId = activityEvent.WorkflowId,
            CorrelationId = activityEvent.CorrelationId,
            StepId = activityEvent.StepId,
            ProcessorId = activityEvent.ProcessorId,
            PublishId = activityEvent.PublishId,
            ExecutionId = activityEvent.ExecutionId
        };

        try
        {
            await DeleteSourceCacheDataAsync(activityEvent);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // Record cache deletion exception as non-critical (cleanup operation)
            _metricsService.RecordException(ex.GetType().Name, "warning", isCritical: false, Guid.Empty);

            _logger.LogErrorWithHierarchy(terminationContext, ex,
                "Failed to delete cache processor data for flow branch termination");
            throw;
        }
    }

    /// <summary>
    /// Processes all next steps by copying cache data and publishing ExecuteActivityCommand
    /// </summary>
    /// <param name="activityEvent">The activity failed event</param>
    /// <param name="nextSteps">Collection of next step IDs</param>
    /// <param name="orchestrationData">Orchestration cache data</param>
    private async Task ProcessNextStepsAsync(ActivityFailedEvent activityEvent, List<Guid> nextSteps, OrchestrationCacheModel orchestrationData)
    {
        using var activity = ActivitySource.StartActivity("ProcessNextSteps");
        activity?.SetTag("nextStepCount", nextSteps.Count);

        var tasks = new List<Task>();

        foreach (var nextStepId in nextSteps)
        {
            var task = ProcessSingleNextStepAsync(activityEvent, nextStepId, orchestrationData);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Processes a single next step by copying cache data and publishing ExecuteActivityCommand
    /// </summary>
    /// <param name="activityEvent">The activity failed event</param>
    /// <param name="nextStepId">The next step ID to process</param>
    /// <param name="orchestrationData">Orchestration cache data</param>
    private async Task ProcessSingleNextStepAsync(ActivityFailedEvent activityEvent, Guid nextStepId, OrchestrationCacheModel orchestrationData)
    {
        using var activity = ActivitySource.StartActivity("ProcessSingleNextStep");
        activity?.SetTag("nextStepId", nextStepId.ToString());

        // Create hierarchical context for next step processing
        var nextStepContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = activityEvent.OrchestratedFlowId,
            WorkflowId = activityEvent.WorkflowId,
            CorrelationId = activityEvent.CorrelationId,
            StepId = nextStepId,
            ProcessorId = activityEvent.ProcessorId, // Will be updated when we get the next step entity
            PublishId = activityEvent.PublishId,
            ExecutionId = activityEvent.ExecutionId
        };

        try
        {
            // Get next step entity
            if (!orchestrationData.StepEntities.TryGetValue(nextStepId, out var nextStepEntity))
            {
                throw new InvalidOperationException($"Next step entity not found for StepId: {nextStepId}");
            }

            // Update context with next step's processor ID
            nextStepContext.ProcessorId = nextStepEntity.ProcessorId;

            // Handle entry conditions
            var shouldExecuteStep = ShouldExecuteStep(nextStepEntity.EntryCondition, activityEvent);
            activity?.SetTag("entryCondition", nextStepEntity.EntryCondition.ToString())
                    ?.SetTag("shouldExecuteStep", shouldExecuteStep)
                    ?.SetTag("activityStatus", "Failed"); // ActivityFailedEvent always represents Failed status

            if (!shouldExecuteStep)
            {
                _logger.LogInformationWithHierarchy(nextStepContext,
                    "Skipping step due to entry condition. EntryCondition: {EntryCondition}, ActivityStatus: {ActivityStatus}",
                    nextStepEntity.EntryCondition, "Failed");
                return;
            }

            // Step 4.1: Copy cache processor data from source to destination
            await CopyCacheProcessorDataAsync(activityEvent, nextStepId, nextStepEntity.ProcessorId);

            // Step 4.2: Get assignments for next step
            var assignmentModels = new List<AssignmentModel>();
            if (orchestrationData.Assignments.TryGetValue(nextStepId, out var assignments))
            {
                assignmentModels.AddRange(assignments);
            }

            // Step 4.3: Compose and publish ExecuteActivityCommand
            var command = new ExecuteActivityCommand
            {
                ProcessorId = nextStepEntity.ProcessorId,
                OrchestratedFlowId = activityEvent.OrchestratedFlowId,
                StepId = nextStepId,
                ExecutionId = activityEvent.ExecutionId,
                Entities = assignmentModels,
                CorrelationId = activityEvent.CorrelationId,
                PublishId = Guid.NewGuid() // Generate new publishId for command publication
            };

            await _bus.Publish(command);

            // Record successful event publishing (ExecuteActivityCommand published after failure)
            _flowMetricsService.RecordEventPublished(success: true, activityEvent.OrchestratedFlowId, nextStepId, activityEvent.ExecutionId, Guid.Empty);

            _logger.LogDebugWithHierarchy(nextStepContext,
                "Published ExecuteActivityCommand for next step after failure. AssignmentCount: {AssignmentCount}",
                assignmentModels.Count);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // Record failed event publishing (ExecuteActivityCommand publishing failed after failure)
            _flowMetricsService.RecordEventPublished(success: false, activityEvent.OrchestratedFlowId, Guid.Empty, activityEvent.ExecutionId, Guid.Empty);

            // Record step processing after failure exception as critical
            _metricsService.RecordException(ex.GetType().Name, "error", isCritical: true, Guid.Empty);

            _logger.LogErrorWithHierarchy(nextStepContext, ex, "Failed to process next step after failure");
            throw;
        }
        finally
        {
            // Delete source cache data in all cases (success, failure, or skip)
            await DeleteSourceCacheDataAsync(activityEvent);
        }
    }

    /// <summary>
    /// Copies cache processor data from source processor to destination processor
    /// </summary>
    /// <param name="activityEvent">The activity failed event</param>
    /// <param name="nextStepId">The next step ID</param>
    /// <param name="destinationProcessorId">The destination processor ID</param>
    private async Task CopyCacheProcessorDataAsync(ActivityFailedEvent activityEvent, Guid nextStepId, Guid destinationProcessorId)
    {
        using var activity = ActivitySource.StartActivity("CopyCacheProcessorData");
        activity?.SetTag("sourceProcessorId", activityEvent.ProcessorId.ToString())
                ?.SetTag("destinationProcessorId", destinationProcessorId.ToString())
                ?.SetTag("nextStepId", nextStepId.ToString());

        // Create hierarchical context for cache copy operation
        var copyContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = activityEvent.OrchestratedFlowId,
            WorkflowId = activityEvent.WorkflowId,
            CorrelationId = activityEvent.CorrelationId,
            StepId = nextStepId,
            ProcessorId = destinationProcessorId,
            PublishId = activityEvent.PublishId,
            ExecutionId = activityEvent.ExecutionId
        };

        try
        {
            // Source cache location
            var sourceMapName = _processorActivityMapName;
            var sourceKey = _rawCacheService.GetProcessorCacheKey(activityEvent.ProcessorId, activityEvent.OrchestratedFlowId, activityEvent.CorrelationId, activityEvent.ExecutionId, activityEvent.StepId, activityEvent.PublishId);

            // Destination cache location (generate new publishId for failed activity retry)
            var destinationPublishId = Guid.NewGuid();
            var destinationMapName = _processorActivityMapName;
            var destinationKey = _rawCacheService.GetProcessorCacheKey(destinationProcessorId, activityEvent.OrchestratedFlowId, activityEvent.CorrelationId, activityEvent.ExecutionId, nextStepId, destinationPublishId);

            // Copy data from source to destination
            var sourceData = await _rawCacheService.GetAsync(sourceMapName, sourceKey, copyContext);
            if (!string.IsNullOrEmpty(sourceData))
            {
                await _rawCacheService.SetAsync(destinationMapName, destinationKey, sourceData, copyContext);

                // Log enriched success message with all the givens
                _logger.LogInformationWithHierarchy(copyContext,
                    "Saved data to cache. MapName: {MapName}, Key: {Key}, ExecutionId: {ExecutionId}, PublishId: {PublishId}, DataLength: {DataLength}",
                    destinationMapName, destinationKey, activityEvent.ExecutionId, destinationPublishId, sourceData.Length);

                _logger.LogDebugWithHierarchy(copyContext,
                    "Copied cache processor data after failure. Source: {SourceMapName}:{SourceKey} -> Destination: {DestinationMapName}:{DestinationKey}",
                    sourceMapName, sourceKey, destinationMapName, destinationKey);
            }
            else
            {
                _logger.LogWarningWithHierarchy(copyContext,
                    "No source data found to copy after failure. Source: {SourceMapName}:{SourceKey}",
                    sourceMapName, sourceKey);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithHierarchy(copyContext, ex,
                "Failed to copy cache processor data after failure. SourceProcessorId: {SourceProcessorId}, DestinationProcessorId: {DestinationProcessorId}",
                activityEvent.ProcessorId, destinationProcessorId);
            throw;
        }
    }

    /// <summary>
    /// Determines if a step should be executed based on its entry condition and activity execution status
    /// </summary>
    /// <param name="entryCondition">The entry condition of the step</param>
    /// <param name="activityEvent">The activity event from the previous step</param>
    /// <returns>True if the step should be executed, false otherwise</returns>
    private bool ShouldExecuteStep(StepEntryCondition entryCondition, ActivityFailedEvent activityEvent)
    {
        // ActivityFailedEvent always represents Failed status
        const ActivityExecutionStatus status = ActivityExecutionStatus.Failed;

        return entryCondition switch
        {
            StepEntryCondition.PreviousProcessing => status == ActivityExecutionStatus.Processing,
            StepEntryCondition.PreviousCompleted => status == ActivityExecutionStatus.Completed,
            StepEntryCondition.PreviousFailed => status == ActivityExecutionStatus.Failed,
            StepEntryCondition.PreviousCancelled => status == ActivityExecutionStatus.Cancelled,
            StepEntryCondition.Always => true,
            StepEntryCondition.Never => false,
            _ => false // Default to not execute for unknown conditions
        };
    }

    /// <summary>
    /// Deletes source cache data after processing
    /// </summary>
    /// <param name="activityEvent">The activity failed event</param>
    private async Task DeleteSourceCacheDataAsync(ActivityFailedEvent activityEvent)
    {
        // Create hierarchical context for cache deletion operation
        var deleteContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = activityEvent.OrchestratedFlowId,
            WorkflowId = activityEvent.WorkflowId,
            CorrelationId = activityEvent.CorrelationId,
            StepId = activityEvent.StepId,
            ProcessorId = activityEvent.ProcessorId,
            PublishId = activityEvent.PublishId,
            ExecutionId = activityEvent.ExecutionId
        };

        try
        {
            var sourceMapName = _processorActivityMapName;
            var sourceKey = _rawCacheService.GetProcessorCacheKey(activityEvent.ProcessorId, activityEvent.OrchestratedFlowId, activityEvent.CorrelationId, activityEvent.ExecutionId, activityEvent.StepId, activityEvent.PublishId);

            await _rawCacheService.RemoveAsync(sourceMapName, sourceKey);

            _logger.LogDebugWithHierarchy(deleteContext,
                "Deleted source cache data after failure. Source: {SourceMapName}:{SourceKey}",
                sourceMapName, sourceKey);
        }
        catch (Exception ex)
        {
            // Record cache cleanup exception as non-critical (cleanup operation)
            _metricsService.RecordException(ex.GetType().Name, "warning", isCritical: false, Guid.Empty);

            _logger.LogErrorWithHierarchy(deleteContext, ex,
                "Failed to delete source cache data after failure");
            // Don't throw here as this is cleanup - log the error but continue
        }
    }
}
