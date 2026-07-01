using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTicket.Domain.Entities;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Messaging;
using OpenTicket.Infrastructure.Messaging.Contracts;
using OpenTicket.Infrastructure.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenTicket.Worker;

public class PaymentProcessingConsumer(
    IRabbitMqConnectionProvider connectionProvider,
    IRabbitMqPublisher publisher,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentSimulationOptions> options,
    ILogger<PaymentProcessingConsumer> logger) : BackgroundService
{
    // Bounded concurrency: each simulated payment blocks for 1-5s, so a burst of holds
    // (flash sale) would otherwise drain at one message at a time. A single shared channel
    // is not safe for concurrent ack/nack, so those calls go through _channelLock.
    private const ushort MaxConcurrency = 20;

    private readonly PaymentSimulationOptions _options = options.Value;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrency, MaxConcurrency);
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync();
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await Topology.DeclareAllAsync(channel);
        await channel.BasicQosAsync(0, MaxConcurrency, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            _ = ProcessDeliveryAsync(channel, ea, stoppingToken);
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(Topology.PaymentRequestedQueue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation(
            "PaymentProcessingConsumer listening on {Queue} (max concurrency: {Concurrency})",
            Topology.PaymentRequestedQueue, MaxConcurrency);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        await _concurrencyLimiter.WaitAsync(stoppingToken);
        try
        {
            await HandleMessageAsync(ea, stoppingToken);
            await AckAsync(channel, ea.DeliveryTag, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process payment request, dead-lettering");
            await NackAsync(channel, ea.DeliveryTag, stoppingToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private async Task AckAsync(IChannel channel, ulong deliveryTag, CancellationToken ct)
    {
        await _channelLock.WaitAsync(ct);
        try
        {
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    private async Task NackAsync(IChannel channel, ulong deliveryTag, CancellationToken ct)
    {
        await _channelLock.WaitAsync(ct);
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: ct);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<PaymentRequestedMessage>(ea.Body.Span)
            ?? throw new InvalidOperationException("Invalid PaymentRequestedMessage payload.");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var alreadyProcessed = await db.Payments.AnyAsync(p => p.OrderId == message.OrderId, ct);
        if (alreadyProcessed)
        {
            logger.LogInformation("Order {OrderId} already has a payment record, skipping (redelivery)", message.OrderId);
            return;
        }

        var delaySeconds = Random.Shared.Next(_options.MinDelaySeconds, _options.MaxDelaySeconds + 1);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);

        var succeeded = Random.Shared.NextDouble() >= _options.FailureRate;

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = message.OrderId,
            Status = succeeded ? PaymentStatus.Succeeded : PaymentStatus.Failed,
            Provider = "simulated",
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            FailureReason = succeeded ? null : "Simulated payment decline"
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        if (succeeded)
        {
            await publisher.PublishAsync(
                Topology.PaymentSucceededRoutingKey,
                new PaymentSucceededMessage(message.OrderId, payment.Id, DateTime.UtcNow),
                ct);
        }
        else
        {
            await publisher.PublishAsync(
                Topology.PaymentFailedRoutingKey,
                new PaymentFailedMessage(message.OrderId, payment.Id, payment.FailureReason!, DateTime.UtcNow),
                ct);
        }

        logger.LogInformation("Processed payment for order {OrderId}: {Status}", message.OrderId, payment.Status);
    }
}
