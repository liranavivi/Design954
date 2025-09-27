using System.Diagnostics;
using Manager.Plugin.Repositories;
using MassTransit;
using Shared.Correlation;
using Shared.Entities;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;

namespace Manager.Plugin.Consumers;

public class UpdatePluginCommandConsumer : IConsumer<UpdatePluginCommand>
{
    private readonly IPluginEntityRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<UpdatePluginCommandConsumer> _logger;

    public UpdatePluginCommandConsumer(
        IPluginEntityRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<UpdatePluginCommandConsumer> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UpdatePluginCommand> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = context.Message;

        _logger.LogInformationWithCorrelation("Processing UpdatePluginCommand. Id: {Id}, Version: {Version}, Name: {Name}, Payload: {Payload}, RequestedBy: {RequestedBy}",
            command.Id, command.Version, command.Name, command.Payload, command.RequestedBy);

        try
        {
            var existingEntity = await _repository.GetByIdAsync(command.Id);
            if (existingEntity == null)
            {
                _logger.LogWarningWithCorrelation("Plugin entity not found for update. Id: {Id}", command.Id);
                await context.RespondAsync(new UpdatePluginCommandResponse
                {
                    Success = false,
                    Message = $"Plugin entity with ID {command.Id} not found"
                });
                return;
            }

            var entity = new PluginEntity
            {
                Id = command.Id,
                Version = command.Version,
                Name = command.Name,
                Description = command.Description,

                InputSchemaId = command.InputSchemaId,
                OutputSchemaId = command.OutputSchemaId,
                EnableInputValidation = command.EnableInputValidation,
                EnableOutputValidation = command.EnableOutputValidation,
                AssemblyBasePath = command.AssemblyBasePath,
                AssemblyName = command.AssemblyName,
                TypeName = command.TypeName,
                ExecutionTimeoutMs = command.ExecutionTimeoutMs,
                UpdatedBy = command.RequestedBy,
                CreatedAt = existingEntity.CreatedAt,
                CreatedBy = existingEntity.CreatedBy
            };

            var updated = await _repository.UpdateAsync(entity);

            await _publishEndpoint.Publish(new PluginUpdatedEvent
            {
                Id = updated.Id,
                Version = updated.Version,
                Name = updated.Name,
                Description = updated.Description,

                InputSchemaId = updated.InputSchemaId,
                OutputSchemaId = updated.OutputSchemaId,
                EnableInputValidation = updated.EnableInputValidation,
                EnableOutputValidation = updated.EnableOutputValidation,
                AssemblyBasePath = updated.AssemblyBasePath,
                AssemblyName = updated.AssemblyName,
                TypeName = updated.TypeName,
                ExecutionTimeoutMs = updated.ExecutionTimeoutMs,
                UpdatedAt = updated.UpdatedAt,
                UpdatedBy = updated.UpdatedBy
            });

            stopwatch.Stop();
            _logger.LogInformationWithCorrelation("Successfully processed UpdatePluginCommand. Id: {Id}, Duration: {Duration}ms",
                updated.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new UpdatePluginCommandResponse
            {
                Success = true,
                Message = "Plugin entity updated successfully"
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex, "Error processing UpdatePluginCommand. Id: {Id}, Duration: {Duration}ms",
                command.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new UpdatePluginCommandResponse
            {
                Success = false,
                Message = $"Failed to update Plugin entity: {ex.Message}"
            });
        }
    }
}

public class UpdatePluginCommandResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
