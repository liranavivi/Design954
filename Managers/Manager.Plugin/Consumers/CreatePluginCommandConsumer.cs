using System.Diagnostics;
using Manager.Plugin.Repositories;
using MassTransit;
using Shared.Correlation;
using Shared.Entities;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;

namespace Manager.Plugin.Consumers;

public class CreatePluginCommandConsumer : IConsumer<CreatePluginCommand>
{
    private readonly IPluginEntityRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CreatePluginCommandConsumer> _logger;

    public CreatePluginCommandConsumer(
        IPluginEntityRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<CreatePluginCommandConsumer> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CreatePluginCommand> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = context.Message;

        _logger.LogInformationWithCorrelation("Processing CreatePluginCommand. Version: {Version}, Name: {Name}, Payload: {Payload}, RequestedBy: {RequestedBy}",
            command.Version, command.Name, command.Payload, command.RequestedBy);

        try
        {
            var entity = new PluginEntity
            {
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
                CreatedBy = command.RequestedBy
            };

            var created = await _repository.CreateAsync(entity);

            await _publishEndpoint.Publish(new PluginCreatedEvent
            {
                Id = created.Id,
                Version = created.Version,
                Name = created.Name,
                Description = created.Description,

                InputSchemaId = created.InputSchemaId,
                OutputSchemaId = created.OutputSchemaId,
                EnableInputValidation = created.EnableInputValidation,
                EnableOutputValidation = created.EnableOutputValidation,
                AssemblyBasePath = created.AssemblyBasePath,
                AssemblyName = created.AssemblyName,
                AssemblyVersion = created.AssemblyVersion,
                TypeName = created.TypeName,
                ExecutionTimeoutMs = created.ExecutionTimeoutMs,
                CreatedAt = created.CreatedAt,
                CreatedBy = created.CreatedBy
            });

            stopwatch.Stop();
            _logger.LogInformationWithCorrelation("Successfully processed CreatePluginCommand. Id: {Id}, Duration: {Duration}ms",
                created.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new CreatePluginCommandResponse
            {
                Success = true,
                Id = created.Id,
                Message = "Plugin entity created successfully"
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex, "Error processing CreatePluginCommand. Version: {Version}, Name: {Name}, Payload: {Payload}, Duration: {Duration}ms",
                command.Version, command.Name, command.Payload, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new CreatePluginCommandResponse
            {
                Success = false,
                Id = Guid.Empty,
                Message = $"Failed to create Plugin entity: {ex.Message}"
            });
        }
    }
}

public class CreatePluginCommandResponse
{
    public bool Success { get; set; }
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
}
