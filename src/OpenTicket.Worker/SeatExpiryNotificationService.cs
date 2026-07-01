using Microsoft.EntityFrameworkCore;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Persistence;
using StackExchange.Redis;

namespace OpenTicket.Worker;

public class SeatExpiryNotificationService(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<SeatExpiryNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Pattern("__keyevent@0__:expired"), async (_, key) =>
        {
            try
            {
                await HandleExpiredKeyAsync(key.ToString(), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reconcile expired seat hold for key {Key}", key);
            }
        });

        logger.LogInformation("Subscribed to Redis keyspace expiry notifications");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleExpiredKeyAsync(string key, CancellationToken ct)
    {
        var parts = key.Split(':');
        if (parts.Length != 4 || parts[0] != "seat" || parts[1] != "hold")
        {
            return;
        }

        if (!Guid.TryParse(parts[2], out var eventId) || !Guid.TryParse(parts[3], out var seatId))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "EventSeats" SET "Status" = {(int)SeatStatus.Available}, "HoldId" = NULL, "HeldByUserId" = NULL, "HoldExpiresAtUtc" = NULL
            WHERE "EventId" = {eventId} AND "SeatId" = {seatId} AND "Status" = {(int)SeatStatus.Held} AND "HoldExpiresAtUtc" < {now}
            """, ct);

        if (rowsAffected > 0)
        {
            logger.LogInformation("Reconciled expired hold for event {EventId} seat {SeatId}", eventId, seatId);
        }
    }
}
