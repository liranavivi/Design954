using System.Diagnostics;
using Manager.Processor.Repositories;
using MassTransit;
using Shared.Correlation;
using Shared.Entities;
using Shared.MassTransit.Commands;

namespace Manager.Processor.Consumers;

public class GetProcessorQueryConsumer : IConsumer<GetProcessorQuery>
{
    private readonly IProcessorEntityRepository _repository;
    private readonly ILogger<GetProcessorQueryConsumer> _logger;
    private readonly ICorrelationIdContext _correlationIdContext;

    public GetProcessorQueryConsumer(
        IProcessorEntityRepository repository,
        ILogger<GetProcessorQueryConsumer> logger,
        ICorrelationIdContext correlationIdContext)
    {
        _repository = repository;
        _logger = logger;
        _correlationIdContext = correlationIdContext;
    }

    /// <summary>
    /// Creates a hierarchical logging context for processor query operations.
    /// Layer 2: ProcessorId + CorrelationId (for processor-specific operations)
    /// </summary>
    private HierarchicalLoggingContext CreateHierarchicalContext(ProcessorEntity entity)
    {
        return new HierarchicalLoggingContext
        {
            ProcessorId = entity.Id,
            CorrelationId = _correlationIdContext.Current
        };
    }

    /// <summary>
    /// Creates a hierarchical logging context for general operations.
    /// Layer 1: CorrelationId only (for general operations)
    /// </summary>
    private HierarchicalLoggingContext CreateHierarchicalContext()
    {
        return new HierarchicalLoggingContext
        {
            CorrelationId = _correlationIdContext.Current
        };
    }

    public async Task Consume(ConsumeContext<GetProcessorQuery> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var query = context.Message;

        // Create general hierarchical context for initial processing
        var initialContext = CreateHierarchicalContext();

        // Use hierarchical logging with clean message and structured attributes
        _logger.LogInformationWithHierarchy(initialContext,
            "Processing GetProcessorQuery. Id: {Id}, CompositeKey: {CompositeKey}",
            query.Id?.ToString() ?? "null", query.CompositeKey ?? "null");

        try
        {
            ProcessorEntity? entity = null;

            if (query.Id.HasValue)
            {
                entity = await _repository.GetByIdAsync(query.Id.Value);
            }
            else if (!string.IsNullOrEmpty(query.CompositeKey))
            {
                entity = await _repository.GetByCompositeKeyAsync(query.CompositeKey);
            }

            stopwatch.Stop();

            if (entity != null)
            {
                // Create hierarchical context with ProcessorId for structured logging
                var successContext = CreateHierarchicalContext(entity);

                // Use hierarchical logging with clean message and structured attributes
                _logger.LogInformationWithHierarchy(successContext,
                    "Successfully processed GetProcessorQuery. Duration: {Duration}ms",
                    stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetProcessorQueryResponse
                {
                    Success = true,
                    Entity = entity,
                    Message = "Processor entity found"
                });
            }
            else
            {
                // Create general hierarchical context for not found case
                var notFoundContext = CreateHierarchicalContext();

                // Use hierarchical logging with clean message and structured attributes
                _logger.LogInformationWithHierarchy(notFoundContext,
                    "Processor entity not found. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                    query.Id?.ToString() ?? "null", query.CompositeKey ?? "null", stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetProcessorQueryResponse
                {
                    Success = false,
                    Entity = null,
                    Message = "Processor entity not found"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Create general hierarchical context for error case
            var errorContext = CreateHierarchicalContext();

            // Use hierarchical logging with clean message and structured attributes
            _logger.LogErrorWithHierarchy(errorContext, ex,
                "Error processing GetProcessorQuery. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                query.Id?.ToString() ?? "null", query.CompositeKey ?? "null", stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new GetProcessorQueryResponse
            {
                Success = false,
                Entity = null,
                Message = $"Error retrieving Processor entity: {ex.Message}"
            });
        }
    }
}
