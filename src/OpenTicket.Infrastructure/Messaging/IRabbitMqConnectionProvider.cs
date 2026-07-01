using RabbitMQ.Client;

namespace OpenTicket.Infrastructure.Messaging;

public interface IRabbitMqConnectionProvider
{
    Task<IConnection> GetConnectionAsync();
}
