namespace Shared.Services.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync<T>(T eventData) where T : class;
}
