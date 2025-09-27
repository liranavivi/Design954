using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Shared.Correlation;
using Shared.Extensions;
using Shared.MassTransit.Events;
using Shared.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Processor.Base.Services;

/// <summary>
/// Background service for processing response items concurrently
/// Follows the same pattern as RequestProcessingService with multiple concurrent workers
/// Thread-safe design with isolated worker processing
/// </summary>
public class ResponseProcessingService : BackgroundService
{
    private readonly IResponseProcessingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ResponseProcessingService> _logger;
    private readonly int _workerCount;
    private readonly ActivitySource _activitySource;

    public ResponseProcessingService(
        IResponseProcessingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ResponseProcessingService> logger,
        IOptions<ProcessorConfiguration> config)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _workerCount = config.Value.BackgroundWorkerCount;
        _activitySource = new ActivitySource(ActivitySources.Services);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Note: Service-level logging will be updated when hierarchical context is available during service startup

        // Create multiple worker tasks (same pattern as ActivityProcessingService)
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
            await foreach (var responseItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessResponseItemAsync(responseItem, workerId, stoppingToken);
                }
                finally
                {
                    // Decrement queue depth counter (thread-safe operation)
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

    private async Task ProcessResponseItemAsync(ProcessedResponseItem responseItem, int workerId, CancellationToken cancellationToken)
    {
        // Each worker gets its own service scope for thread safety
        using var scope = _serviceProvider.CreateScope();
        using var activity = _activitySource.StartActivityWithCorrelation("ProcessResponseFromQueue");
        
        var processingContext = responseItem.ProcessingContext;

        // Set activity tags
        activity?.SetTag("correlation.id", processingContext.CorrelationId.ToString())
                ?.SetBaggage("correlation.id", processingContext.CorrelationId.ToString())
                ?.SetActivityExecutionTagsWithCorrelation(
                    processingContext.OrchestratedFlowId,
                    processingContext.StepId ?? Guid.Empty,
                    processingContext.ExecutionId ?? Guid.Empty);

        _logger.LogInformationWithHierarchy(processingContext,
            "Processing response item from queue. WorkerId: {WorkerId}, QueueTime: {QueueTime}ms",
            workerId, (DateTime.UtcNow - responseItem.QueuedAt).TotalMilliseconds);

        try
        {
            // Step 1: Handle serialization with proper error handling
            string? serializedData = null;
            if (responseItem.ProcessedData.Data != null)
            {
                try
                {
                    serializedData = JsonSerializer.Serialize(responseItem.ProcessedData.Data, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    });
                }
                catch (Exception serializationEx)
                {
                    _logger.LogErrorWithHierarchy(processingContext, serializationEx,
                        "Failed to serialize processed data, treating as failed execution");
                    
                    // Safe to modify since we're working with cloned data
                    responseItem.ProcessedData.Status = ActivityExecutionStatus.Failed;
                    responseItem.ProcessedData.Result = $"Serialization failed: {serializationEx.Message}";
                    serializedData = null;
                }
            }

            // Step 2: Virtual output validation (after serialization)
            if (!string.IsNullOrWhiteSpace(serializedData))
            {
                var processorService = scope.ServiceProvider.GetRequiredService<IProcessorService>();
                await processorService.ValidateOutputDataAsync(responseItem.OriginalMessage.Entities, serializedData, processingContext);
            }

            // Step 3: Save to cache if data is not empty (regardless of status)
            if (!string.IsNullOrWhiteSpace(serializedData))
            {
                if (responseItem.ProcessedData.ExecutionId != Guid.Empty)
                {
                    var processorService = scope.ServiceProvider.GetRequiredService<IProcessorService>();
                    await processorService.SaveCachedDataAsync(responseItem.OriginalMessage.OrchestratedFlowId, 
                        responseItem.OriginalMessage.CorrelationId,
                        responseItem.ProcessedData.ExecutionId, 
                        responseItem.OriginalMessage.StepId, 
                        responseItem.OriginalMessage.PublishId, 
                        serializedData, 
                        responseItem.OriginalMessage.ProcessorId);
                }
                else
                {
                    _logger.LogWarningWithHierarchy(processingContext,
                        "ExecutionId is empty - skipping cache save. OriginalExecutionId: {OriginalExecutionId}",
                        responseItem.OriginalMessage.ExecutionId);
                }
            }
            else
            {
                _logger.LogInformationWithHierarchy(processingContext,
                    "Skipping cache save - no data to serialize");
            }

            // Step 4: Publish success event
            await PublishActivityExecutedEventAsync(scope, responseItem, processingContext, cancellationToken);

            _logger.LogInformationWithHierarchy(processingContext,
                "Successfully processed response item from queue. WorkerId: {WorkerId}", workerId);
        }
        catch (Exception itemEx)
        {
            _logger.LogErrorWithHierarchy(processingContext, itemEx,
                "Error processing response item. WorkerId: {WorkerId}", workerId);

            // Publish failure event
            await PublishActivityFailedEventAsync(scope, responseItem, itemEx, processingContext, cancellationToken);

            // Could implement retry logic here if needed
            // For now, we log the error and continue processing other items
        }
    }

    private async Task PublishActivityExecutedEventAsync(IServiceScope scope, ProcessedResponseItem responseItem,
        HierarchicalLoggingContext processingContext, CancellationToken cancellationToken)
    {
        try
        {
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var flowMetricsService = scope.ServiceProvider.GetService<IProcessorFlowMetricsService>();

            var executedEvent = new ActivityExecutedEvent
            {
                ProcessorId = responseItem.OriginalMessage.ProcessorId,
                OrchestratedFlowId = responseItem.OriginalMessage.OrchestratedFlowId,
                WorkflowId = responseItem.OriginalMessage.WorkflowId,
                StepId = responseItem.OriginalMessage.StepId,
                ExecutionId = responseItem.ProcessedData.ExecutionId,
                CorrelationId = responseItem.OriginalMessage.CorrelationId,
                PublishId = responseItem.OriginalMessage.PublishId,
                Duration = responseItem.ProcessingStopwatch.Elapsed,
                Status = responseItem.ProcessedData.Status ?? ActivityExecutionStatus.Failed,
                ResultDataSize = responseItem.ProcessedData.Data?.ToString()?.Length ?? 0,
                EntitiesProcessed = responseItem.OriginalMessage.Entities.Count
            };

            await publishEndpoint.Publish(executedEvent, cancellationToken);

            flowMetricsService?.RecordEventPublished(false, responseItem.OriginalMessage.OrchestratedFlowId,
                responseItem.OriginalMessage.StepId, responseItem.ProcessedData.ExecutionId,
                responseItem.OriginalMessage.CorrelationId);

            _logger.LogDebugWithHierarchy(processingContext,
                "Published ActivityExecutedEvent for ExecutionId: {ExecutionId}", responseItem.ProcessedData.ExecutionId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(processingContext, ex,
                "Failed to publish ActivityExecutedEvent for ExecutionId: {ExecutionId}", responseItem.ProcessedData.ExecutionId);
        }
    }

    private async Task PublishActivityFailedEventAsync(IServiceScope scope, ProcessedResponseItem responseItem,
        Exception exception, HierarchicalLoggingContext processingContext, CancellationToken cancellationToken)
    {
        try
        {
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var flowMetricsService = scope.ServiceProvider.GetService<IProcessorFlowMetricsService>();

            var failedEvent = new ActivityFailedEvent
            {
                ProcessorId = responseItem.OriginalMessage.ProcessorId,
                OrchestratedFlowId = responseItem.OriginalMessage.OrchestratedFlowId,
                WorkflowId = responseItem.OriginalMessage.WorkflowId,
                StepId = responseItem.OriginalMessage.StepId,
                ExecutionId = responseItem.ProcessedData.ExecutionId,
                CorrelationId = responseItem.OriginalMessage.CorrelationId,
                PublishId = responseItem.OriginalMessage.PublishId,
                Duration = responseItem.ProcessingStopwatch.Elapsed,
                ErrorMessage = exception.Message,
                ExceptionType = exception.GetType().Name,
                StackTrace = exception.StackTrace,
                EntitiesBeingProcessed = responseItem.OriginalMessage.Entities.Count,
                IsValidationFailure = false // This is a processing failure, not validation
            };

            await publishEndpoint.Publish(failedEvent, cancellationToken);

            flowMetricsService?.RecordEventPublished(true, responseItem.OriginalMessage.OrchestratedFlowId,
                responseItem.OriginalMessage.StepId, responseItem.ProcessedData.ExecutionId,
                responseItem.OriginalMessage.CorrelationId);

            _logger.LogDebugWithHierarchy(processingContext,
                "Published ActivityFailedEvent for ExecutionId: {ExecutionId}", responseItem.ProcessedData.ExecutionId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(processingContext, ex,
                "Failed to publish ActivityFailedEvent for ExecutionId: {ExecutionId}", responseItem.ProcessedData.ExecutionId);
        }
    }
}
