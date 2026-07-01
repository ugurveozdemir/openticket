namespace OpenTicket.Infrastructure.Messaging;

public interface IRabbitMqPublisher
{
    Task PublishAsync<T>(string routingKey, T message, CancellationToken ct = default);
}
