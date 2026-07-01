using System.Text.Json;
using RabbitMQ.Client;

namespace OpenTicket.Infrastructure.Messaging;

public class RabbitMqPublisher(IRabbitMqConnectionProvider connectionProvider) : IRabbitMqPublisher
{
    public async Task PublishAsync<T>(string routingKey, T message, CancellationToken ct = default)
    {
        var connection = await connectionProvider.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);

        await Topology.DeclareAllAsync(channel);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json"
        };

        await channel.BasicPublishAsync(
            exchange: Topology.DirectExchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }
}
