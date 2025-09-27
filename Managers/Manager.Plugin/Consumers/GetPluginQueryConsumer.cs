using System.Diagnostics;
using Manager.Plugin.Repositories;
using MassTransit;
using Shared.Correlation;
using Shared.Entities;
using Shared.MassTransit.Commands;

namespace Manager.Plugin.Consumers;

public class GetPluginQueryConsumer : IConsumer<GetPluginQuery>
{
    private readonly IPluginEntityRepository _repository;
    private readonly ILogger<GetPluginQueryConsumer> _logger;

    public GetPluginQueryConsumer(
        IPluginEntityRepository repository,
        ILogger<GetPluginQueryConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GetPluginQuery> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var query = context.Message;

        _logger.LogInformationWithCorrelation("Processing GetPluginQuery. Id: {Id}, CompositeKey: {CompositeKey}",
            query.Id, query.CompositeKey);

        try
        {
            PluginEntity? entity = null;

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
                _logger.LogInformationWithCorrelation("Successfully processed GetPluginQuery. Found entity Id: {Id}, Duration: {Duration}ms",
                    entity.Id, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetPluginQueryResponse
                {
                    Success = true,
                    Entity = entity,
                    Message = "Plugin entity found"
                });
            }
            else
            {
                _logger.LogInformationWithCorrelation("Plugin entity not found. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                    query.Id, query.CompositeKey, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetPluginQueryResponse
                {
                    Success = false,
                    Entity = null,
                    Message = "Plugin entity not found"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex, "Error processing GetPluginQuery. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                query.Id, query.CompositeKey, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new GetPluginQueryResponse
            {
                Success = false,
                Entity = null,
                Message = $"Error retrieving Plugin entity: {ex.Message}"
            });
        }
    }
}

public class GetPluginQueryResponse
{
    public bool Success { get; set; }
    public PluginEntity? Entity { get; set; }
    public string Message { get; set; } = string.Empty;
}
