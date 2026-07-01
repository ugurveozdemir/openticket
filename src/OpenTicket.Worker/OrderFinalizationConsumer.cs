using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Messaging;
using OpenTicket.Infrastructure.Messaging.Contracts;
using OpenTicket.Infrastructure.Persistence;
using OpenTicket.Infrastructure.Redis;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenTicket.Worker;

public class OrderFinalizationConsumer(
    IRabbitMqConnectionProvider connectionProvider,
    IRedisSeatLockService seatLock,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderFinalizationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync();
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await Topology.DeclareAllAsync(channel);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        await ConsumeAsync(channel, Topology.PaymentSucceededQueue, isSuccessQueue: true, stoppingToken);
        await ConsumeAsync(channel, Topology.PaymentFailedQueue, isSuccessQueue: false, stoppingToken);

        logger.LogInformation(
            "OrderFinalizationConsumer listening on {Succeeded} and {Failed}",
            Topology.PaymentSucceededQueue, Topology.PaymentFailedQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ConsumeAsync(IChannel channel, string queue, bool isSuccessQueue, CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                Guid orderId;
                if (isSuccessQueue)
                {
                    var message = JsonSerializer.Deserialize<PaymentSucceededMessage>(ea.Body.Span)
                        ?? throw new InvalidOperationException("Invalid PaymentSucceededMessage payload.");
                    orderId = message.OrderId;
                }
                else
                {
                    var message = JsonSerializer.Deserialize<PaymentFailedMessage>(ea.Body.Span)
                        ?? throw new InvalidOperationException("Invalid PaymentFailedMessage payload.");
                    orderId = message.OrderId;
                }

                await FinalizeAsync(orderId, paymentSucceeded: isSuccessQueue, stoppingToken);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to finalize order from {Queue}, dead-lettering", queue);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
    }

    private async Task FinalizeAsync(Guid orderId, bool paymentSucceeded, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found during finalization", orderId);
            return;
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            logger.LogInformation(
                "Order {OrderId} already finalized (status: {Status}), skipping (redelivery)", orderId, order.Status);
            return;
        }

        var eventSeatIds = order.Items.Select(i => i.EventSeatId).ToArray();
        var holdId = order.Items.First().EventSeat?.HoldId;
        var seatIds = order.Items.Select(i => i.EventSeat!.SeatId).ToList();

        var finalOrderStatus = OrderStatus.Failed;

        if (paymentSucceeded)
        {
            var soldRows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "EventSeats" SET "Status" = {(int)SeatStatus.Sold}, "SoldAtUtc" = {DateTime.UtcNow}
                WHERE "Id" = ANY({eventSeatIds}) AND "Status" = {(int)SeatStatus.Held}
                """, ct);

            if (soldRows == eventSeatIds.Length)
            {
                finalOrderStatus = OrderStatus.Confirmed;
            }
            else
            {
                logger.LogWarning(
                    "Order {OrderId} payment succeeded but only {Sold}/{Total} seat holds were still valid; marking order Failed",
                    orderId, soldRows, eventSeatIds.Length);
            }
        }

        if (finalOrderStatus == OrderStatus.Failed)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "EventSeats" SET "Status" = {(int)SeatStatus.Available}, "HoldId" = NULL, "HeldByUserId" = NULL, "HoldExpiresAtUtc" = NULL
                WHERE "Id" = ANY({eventSeatIds}) AND "Status" = {(int)SeatStatus.Held}
                """, ct);
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Orders" SET "Status" = {(int)finalOrderStatus}, "UpdatedAtUtc" = {DateTime.UtcNow}
            WHERE "Id" = {orderId} AND "Status" = {(int)OrderStatus.PendingPayment}
            """, ct);

        if (holdId.HasValue)
        {
            await seatLock.ReleaseSeatsAsync(order.EventId, seatIds, order.UserId, holdId.Value);
        }

        logger.LogInformation("Finalized order {OrderId} as {Status}", orderId, finalOrderStatus);
    }
}
