using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Correlation;
using Shared.Extensions;
using Shared.MassTransit.Events;
using Shared.Models;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using System.Diagnostics;

namespace Processor.Base.Services;

/// <summary>
/// Background service that processes activities from the queue and publishes events
/// </summary>
public class ActivityProcessingService : BackgroundService
{
    private readonly ActivityProcessingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActivityProcessingService> _logger;
    private readonly int _workerCount;
    private static readonly ActivitySource ActivitySource = new(ActivitySources.Services);

    public ActivityProcessingService(
        ActivityProcessingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ActivityProcessingService> logger,
        IOptions<ProcessorConfiguration> config)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _workerCount = config.Value.BackgroundWorkerCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Note: Service-level logging will be updated when hierarchical context is available during service startup

        // Create multiple worker tasks
        var workerTasks = Enumerable.Range(0, _workerCount)
            .Select(workerId => StartWorker(workerId, stoppingToken))
            .ToArray();

        // Wait for all workers to complete
        await Task.WhenAll(workerTasks);

        // Note: Service-level logging will be updated when hierarchical context is available during service shutdown
    }

    private async Task StartWorker(int workerId, CancellationToken stoppingToken)
    {
        // Note: Worker-level logging will be updated when hierarchical context is available during worker startup

        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessRequestAsync(request, workerId, stoppingToken);
                }
                finally
                {
                    // Decrement queue depth counter
                    _queue.DecrementQueueDepth();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
            // Note: Worker cancellation logging will be updated when hierarchical context is available
        }
        catch (Exception)
        {
            // Note: Worker error logging will be updated when hierarchical context is available
            throw; // Re-throw to fail the service
        }
        finally
        {
            // Note: Worker shutdown logging will be updated when hierarchical context is available
        }
    }

    private async Task ProcessRequestAsync(ProcessingRequest request, int workerId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        using var activity = ActivitySource.StartActivityWithCorrelation("ProcessActivityFromQueue");
        
        var stopwatch = Stopwatch.StartNew();
        var command = request.OriginalCommand;
        var correlationId = request.CorrelationId;

        // Set correlation context for this processing thread
        var correlationIdContext = scope.ServiceProvider.GetRequiredService<ICorrelationIdContext>();
        correlationIdContext.Set(correlationId);

        // Set activity tags
        activity?.SetTag("correlation.id", correlationId.ToString())
                ?.SetBaggage("correlation.id", correlationId.ToString())
                ?.SetActivityExecutionTagsWithCorrelation(
                    command.OrchestratedFlowId,
                    command.StepId,
                    command.ExecutionId)
                ?.SetEntityTags(command.Entities.Count);

        // Create Layer 5 hierarchical context for activity processing - used throughout the method
        var processingContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = command.OrchestratedFlowId,
            WorkflowId = command.WorkflowId,
            CorrelationId = correlationId,
            StepId = command.StepId,
            ProcessorId = command.ProcessorId,
            PublishId = command.PublishId,
            ExecutionId = command.ExecutionId
        };

        try
        {
            // Get required services
            var processorService = scope.ServiceProvider.GetRequiredService<IProcessorService>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var config = scope.ServiceProvider.GetRequiredService<IOptions<ProcessorConfiguration>>().Value;
            var flowMetricsService = scope.ServiceProvider.GetService<IProcessorFlowMetricsService>();

            _logger.LogInformationWithHierarchy(processingContext,
                "Processing activity from queue. WorkerId: {WorkerId}, QueueTime: {QueueTime}ms",
                workerId, (DateTime.UtcNow - request.ReceivedAt).TotalMilliseconds);

            // Process the activity
            var responses = await processorService.ProcessActivityAsync(request.ActivityMessage);

            stopwatch.Stop();

            // Publish events for each response
            foreach (var response in responses)
            {
                activity?.SetTag($"ActivityStatus_{response.ExecutionId}", response.Status.ToString());

                // Temporarily enhance processing context with specific response ExecutionId for logging
                processingContext.ExecutionId = response.ExecutionId;

                _logger.LogInformationWithHierarchy(processingContext,
                    "Activity item completed from queue. WorkerId: {WorkerId}, OriginalExecutionId: {OriginalExecutionId}, Status: {Status}, Duration: {Duration}ms",
                    workerId, command.ExecutionId, response.Status, stopwatch.ElapsedMilliseconds);

                if (response.Status == ActivityExecutionStatus.Completed)
                {
                    await publishEndpoint.Publish(new ActivityExecutedEvent
                    {
                        OrchestratedFlowId = command.OrchestratedFlowId,
                        WorkflowId = command.WorkflowId, // ✅ Include WorkflowId in event
                        CorrelationId = correlationId,
                        StepId = command.StepId,
                        ProcessorId = command.ProcessorId,
                        PublishId = command.PublishId,
                        ExecutionId = response.ExecutionId,
                        Duration = response.Duration,
                        Status = response.Status,
                        EntitiesProcessed = command.Entities.Count,
                    }, cancellationToken);

                    flowMetricsService?.RecordEventPublished(true, command.OrchestratedFlowId, command.StepId, command.ExecutionId, correlationId);
                }
                else
                {
                    await publishEndpoint.Publish(new ActivityFailedEvent
                    {
                        OrchestratedFlowId = command.OrchestratedFlowId,
                        WorkflowId = command.WorkflowId, // ✅ Include WorkflowId in failed event
                        CorrelationId = correlationId,
                        StepId = command.StepId,
                        ProcessorId = command.ProcessorId,
                        PublishId = command.PublishId,
                        ExecutionId = response.ExecutionId,
                        Duration = response.Duration,
                        ErrorMessage = response.ErrorMessage ?? "Unknown error",
                        EntitiesBeingProcessed = command.Entities.Count,
                    }, cancellationToken);

                    flowMetricsService?.RecordEventPublished(true, command.OrchestratedFlowId, command.StepId, command.ExecutionId, correlationId);
                }
            }

            activity?.SetTag(ActivityTags.ActivityDuration, stopwatch.ElapsedMilliseconds)
                    ?.SetTag("ResponseCount", responses.Count());

            _logger.LogInformationWithHierarchy(processingContext,
                "Activity collection completed from queue. WorkerId: {WorkerId}, OriginalExecutionId: {OriginalExecutionId}, ResponseCount: {ResponseCount}, Duration: {Duration}ms",
                workerId, command.ExecutionId, responses.Count(), stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await HandleProcessingErrorAsync(request, ex, scope, workerId, processingContext, cancellationToken);
        }
    }

    private async Task HandleProcessingErrorAsync(ProcessingRequest request, Exception ex, IServiceScope scope, int workerId, HierarchicalLoggingContext context, CancellationToken cancellationToken)
    {
        var command = request.OriginalCommand;
        var correlationId = request.CorrelationId;

        // Get required services for error handling
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var config = scope.ServiceProvider.GetRequiredService<IOptions<ProcessorConfiguration>>().Value;
        var flowMetricsService = scope.ServiceProvider.GetService<IProcessorFlowMetricsService>();
        var healthMetricsService = scope.ServiceProvider.GetService<IProcessorHealthMetricsService>();

        // Record metrics
        flowMetricsService?.RecordCommandConsumed(false, command.OrchestratedFlowId, command.StepId, command.ExecutionId, correlationId);
        healthMetricsService?.RecordException(ex.GetType().Name, "critical", correlationId);

        // Use passed context directly for error logging
        _logger.LogErrorWithHierarchy(context, ex,
            "Failed to process activity from queue. WorkerId: {WorkerId}, RetryCount: {RetryCount}",
            workerId, request.RetryCount);

        // Publish failure event
        await publishEndpoint.Publish(new ActivityFailedEvent
        {
            ProcessorId = command.ProcessorId,
            OrchestratedFlowId = command.OrchestratedFlowId,
            StepId = command.StepId,
            ExecutionId = command.ExecutionId,
            CorrelationId = correlationId,
            PublishId = command.PublishId,
            Duration = TimeSpan.Zero,
            ErrorMessage = ex.Message,
            ExceptionType = ex.GetType().Name,
            StackTrace = ex.StackTrace,
            EntitiesBeingProcessed = command.Entities.Count,
        }, cancellationToken);

        flowMetricsService?.RecordEventPublished(true, command.OrchestratedFlowId, command.StepId, command.ExecutionId, correlationId);
    }
}
