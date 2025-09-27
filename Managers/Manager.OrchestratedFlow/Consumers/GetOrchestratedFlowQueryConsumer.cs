using System.Diagnostics;
using Manager.OrchestratedFlow.Repositories;
using MassTransit;
using Shared.Correlation;
using Shared.Entities;
using Shared.MassTransit.Commands;

namespace Manager.OrchestratedFlow.Consumers;

public class GetOrchestratedFlowQueryConsumer : IConsumer<GetOrchestratedFlowQuery>
{
    private readonly IOrchestratedFlowEntityRepository _repository;
    private readonly ILogger<GetOrchestratedFlowQueryConsumer> _logger;
    private readonly ICorrelationIdContext _correlationIdContext;

    public GetOrchestratedFlowQueryConsumer(
        IOrchestratedFlowEntityRepository repository,
        ILogger<GetOrchestratedFlowQueryConsumer> logger,
        ICorrelationIdContext correlationIdContext)
    {
        _repository = repository;
        _logger = logger;
        _correlationIdContext = correlationIdContext;
    }

    /// <summary>
    /// Creates a hierarchical logging context for operations with an OrchestratedFlow entity
    /// </summary>
    private HierarchicalLoggingContext CreateHierarchicalContext(OrchestratedFlowEntity entity)
    {
        return new HierarchicalLoggingContext
        {
            OrchestratedFlowId = entity.Id,
            WorkflowId = entity.WorkflowId,
            CorrelationId = _correlationIdContext.Current
        };
    }

    public async Task Consume(ConsumeContext<GetOrchestratedFlowQuery> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var query = context.Message;

        _logger.LogInformationWithCorrelation("Processing GetOrchestratedFlowQuery. Id: {Id}, CompositeKey: {CompositeKey}",
            query.Id, query.CompositeKey);

        try
        {
            OrchestratedFlowEntity? entity = null;

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
                // Create hierarchical context for the found entity
                var hierarchicalContext = CreateHierarchicalContext(entity);

                // Keep existing correlation logging for backward compatibility
                _logger.LogInformationWithCorrelation("Successfully processed GetOrchestratedFlowQuery. Found entity Id: {Id}, Duration: {Duration}ms",
                    entity.Id, stopwatch.ElapsedMilliseconds);

                // Add hierarchical logging with clean message (Option 1: Method Overloads)
                _logger.LogInformationWithHierarchy(hierarchicalContext,
                    "Successfully processed GetOrchestratedFlowQuery. Duration: {Duration}ms",
                    stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetOrchestratedFlowQueryResponse
                {
                    Success = true,
                    Entity = entity,
                    Message = "OrchestratedFlow entity found"
                });
            }
            else
            {
                _logger.LogInformationWithCorrelation("OrchestratedFlow entity not found. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                    query.Id, query.CompositeKey, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetOrchestratedFlowQueryResponse
                {
                    Success = false,
                    Entity = null,
                    Message = "OrchestratedFlow entity not found"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex, "Error processing GetOrchestratedFlowQuery. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                query.Id, query.CompositeKey, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new GetOrchestratedFlowQueryResponse
            {
                Success = false,
                Entity = null,
                Message = $"Error retrieving OrchestratedFlow entity: {ex.Message}"
            });
        }
    }
}

public class GetOrchestratedFlowQueryResponse
{
    public bool Success { get; set; }
    public OrchestratedFlowEntity? Entity { get; set; }
    public string Message { get; set; } = string.Empty;
}
