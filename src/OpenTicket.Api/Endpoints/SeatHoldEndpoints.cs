using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenTicket.Api.Contracts;
using OpenTicket.Domain.Entities;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Persistence;
using OpenTicket.Infrastructure.Redis;

namespace OpenTicket.Api.Endpoints;

public static class SeatHoldEndpoints
{
    private static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(5);

    public static void MapSeatHoldEndpoints(this WebApplication app)
    {
        app.MapPost("/api/events/{id:guid}/seats/hold", HoldSeatsAsync).RequireAuthorization().WithTags("Orders");
        app.MapPost("/api/orders/{id:guid}/release", ReleaseOrderAsync).RequireAuthorization().WithTags("Orders");
    }

    private static async Task<IResult> HoldSeatsAsync(
        Guid id,
        HoldSeatsRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IRedisSeatLockService seatLock)
    {
        if (request.SeatIds.Count == 0)
        {
            return Results.BadRequest("At least one seat must be specified.");
        }

        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var distinctSeatIds = request.SeatIds.Distinct().ToList();

        // Redis is the first gate: losers are rejected here without ever touching Postgres,
        // which is what keeps a flash-sale stampede from exhausting the DB connection pool.
        var holdId = Guid.NewGuid();
        var acquired = await seatLock.TryHoldSeatsAsync(id, distinctSeatIds, userId, holdId, HoldTtl);
        if (!acquired)
        {
            return Results.Conflict("One or more seats are already held or sold.");
        }

        var eventSeats = await db.EventSeats
            .Include(es => es.Event)
            .Where(es => es.EventId == id && distinctSeatIds.Contains(es.SeatId))
            .ToListAsync();

        if (eventSeats.Count != distinctSeatIds.Count || eventSeats.Any(es => es.Event!.Status != EventStatus.OnSale))
        {
            await seatLock.ReleaseSeatsAsync(id, distinctSeatIds, userId, holdId);
            return Results.NotFound("One or more seats do not exist for this event, or the event is not on sale.");
        }

        var holdExpiresAtUtc = DateTime.UtcNow.Add(HoldTtl);
        var eventSeatIds = eventSeats.Select(es => es.Id).ToArray();

        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "EventSeats" SET "Status" = {(int)SeatStatus.Held}, "HoldId" = {holdId}, "HeldByUserId" = {userId}, "HoldExpiresAtUtc" = {holdExpiresAtUtc}
            WHERE "Id" = ANY({eventSeatIds}) AND ("Status" = {(int)SeatStatus.Available} OR ("Status" = {(int)SeatStatus.Held} AND "HoldExpiresAtUtc" < {DateTime.UtcNow}))
            """);

        if (rowsAffected != eventSeatIds.Length)
        {
            await seatLock.ReleaseSeatsAsync(id, distinctSeatIds, userId, holdId);
            return Results.Conflict("Seat state changed concurrently. Please try again.");
        }

        var totalAmountCents = eventSeats.Sum(es => es.PriceCents);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventId = id,
            Status = OrderStatus.PendingPayment,
            TotalAmountCents = totalAmountCents,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var orderItems = eventSeats.Select(es => new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            EventSeatId = es.Id,
            PriceCents = es.PriceCents
        }).ToList();

        db.Orders.Add(order);
        db.OrderItems.AddRange(orderItems);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/orders/{order.Id}",
            new HoldSeatsResponse(order.Id, holdId, holdExpiresAtUtc, totalAmountCents, distinctSeatIds));
    }

    private static async Task<IResult> ReleaseOrderAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IRedisSeatLockService seatLock)
    {
        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var order = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.UserId != userId)
        {
            return Results.Forbid();
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            return Results.Conflict($"Order cannot be released (status: {order.Status}).");
        }

        var holdId = order.Items.First().EventSeat!.HoldId!.Value;
        var seatIds = order.Items.Select(i => i.EventSeat!.SeatId).ToList();
        var eventSeatIds = order.Items.Select(i => i.EventSeatId).ToArray();

        await seatLock.ReleaseSeatsAsync(order.EventId, seatIds, userId, holdId);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "EventSeats" SET "Status" = {(int)SeatStatus.Available}, "HoldId" = NULL, "HeldByUserId" = NULL, "HoldExpiresAtUtc" = NULL
            WHERE "Id" = ANY({eventSeatIds}) AND "HoldId" = {holdId}
            """);

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(new ReleaseOrderResponse(order.Id, order.Status.ToString()));
    }
}
