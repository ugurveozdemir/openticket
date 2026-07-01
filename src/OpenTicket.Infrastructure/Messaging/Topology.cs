using RabbitMQ.Client;

namespace OpenTicket.Infrastructure.Messaging;

public static class Topology
{
    public const string DirectExchange = "tickets.direct";
    public const string DeadLetterExchange = "tickets.dlx";

    public const string PaymentRequestedQueue = "q.payment.requested";
    public const string PaymentSucceededQueue = "q.payment.succeeded";
    public const string PaymentFailedQueue = "q.payment.failed";
    public const string DeadLetterQueue = "q.tickets.dead-letter";

    public const string PaymentRequestedRoutingKey = "payment.requested";
    public const string PaymentSucceededRoutingKey = "payment.succeeded";
    public const string PaymentFailedRoutingKey = "payment.failed";

    public static async Task DeclareAllAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(DirectExchange, ExchangeType.Direct, durable: true);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true);

        var mainQueueArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange };

        await channel.QueueDeclareAsync(PaymentRequestedQueue, durable: true, exclusive: false, autoDelete: false, arguments: mainQueueArgs);
        await channel.QueueDeclareAsync(PaymentSucceededQueue, durable: true, exclusive: false, autoDelete: false, arguments: mainQueueArgs);
        await channel.QueueDeclareAsync(PaymentFailedQueue, durable: true, exclusive: false, autoDelete: false, arguments: mainQueueArgs);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);

        await channel.QueueBindAsync(PaymentRequestedQueue, DirectExchange, PaymentRequestedRoutingKey);
        await channel.QueueBindAsync(PaymentSucceededQueue, DirectExchange, PaymentSucceededRoutingKey);
        await channel.QueueBindAsync(PaymentFailedQueue, DirectExchange, PaymentFailedRoutingKey);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: string.Empty);
    }
}
