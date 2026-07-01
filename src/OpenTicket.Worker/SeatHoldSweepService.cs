using Microsoft.EntityFrameworkCore;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Persistence;

namespace OpenTicket.Worker;

public class SeatHoldSweepService(IServiceScopeFactory scopeFactory, ILogger<SeatHoldSweepService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepExpiredHoldsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seat hold sweep failed");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task SweepExpiredHoldsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "EventSeats" SET "Status" = {(int)SeatStatus.Available}, "HoldId" = NULL, "HeldByUserId" = NULL, "HoldExpiresAtUtc" = NULL
            WHERE "Status" = {(int)SeatStatus.Held} AND "HoldExpiresAtUtc" < {now}
            """, ct);

        if (rowsAffected > 0)
        {
            logger.LogInformation("Swept {Count} expired seat hold(s)", rowsAffected);
        }
    }
}
