using System.Diagnostics;
using Manager.Plugin.Repositories;
using MassTransit;
using Shared.Correlation;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;

namespace Manager.Plugin.Consumers;

public class DeletePluginCommandConsumer : IConsumer<DeletePluginCommand>
{
    private readonly IPluginEntityRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<DeletePluginCommandConsumer> _logger;

    public DeletePluginCommandConsumer(
        IPluginEntityRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<DeletePluginCommandConsumer> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeletePluginCommand> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = context.Message;

        _logger.LogInformationWithCorrelation("Processing DeletePluginCommand. Id: {Id}, RequestedBy: {RequestedBy}",
            command.Id, command.RequestedBy);

        try
        {
            var existingEntity = await _repository.GetByIdAsync(command.Id);
            if (existingEntity == null)
            {
                _logger.LogWarningWithCorrelation("Plugin entity not found for deletion. Id: {Id}", command.Id);
                await context.RespondAsync(new DeletePluginCommandResponse
                {
                    Success = false,
                    Message = $"Plugin entity with ID {command.Id} not found"
                });
                return;
            }

            var success = await _repository.DeleteAsync(command.Id);

            if (success)
            {
                await _publishEndpoint.Publish(new PluginDeletedEvent
                {
                    Id = command.Id,
                    DeletedAt = DateTime.UtcNow,
                    DeletedBy = command.RequestedBy
                });

                stopwatch.Stop();
                _logger.LogInformationWithCorrelation("Successfully processed DeletePluginCommand. Id: {Id}, Duration: {Duration}ms",
                    command.Id, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new DeletePluginCommandResponse
                {
                    Success = true,
                    Message = "Plugin entity deleted successfully"
                });
            }
            else
            {
                stopwatch.Stop();
                _logger.LogWarningWithCorrelation("Failed to delete Plugin entity. Id: {Id}, Duration: {Duration}ms",
                    command.Id, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new DeletePluginCommandResponse
                {
                    Success = false,
                    Message = "Failed to delete Plugin entity"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex, "Error processing DeletePluginCommand. Id: {Id}, Duration: {Duration}ms",
                command.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new DeletePluginCommandResponse
            {
                Success = false,
                Message = $"Failed to delete Plugin entity: {ex.Message}"
            });
        }
    }
}

public class DeletePluginCommandResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
