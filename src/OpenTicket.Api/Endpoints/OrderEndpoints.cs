using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenTicket.Api.Contracts;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Messaging;
using OpenTicket.Infrastructure.Messaging.Contracts;
using OpenTicket.Infrastructure.Persistence;

namespace OpenTicket.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders").RequireAuthorization().WithTags("Orders");

        group.MapPost("/{id:guid}/pay", PayOrderAsync);
        group.MapGet("/{id:guid}", GetOrderAsync);
        group.MapGet("", ListOrdersAsync);
    }

    private static async Task<IResult> PayOrderAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IRabbitMqPublisher publisher,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
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
            return Results.Conflict($"Order cannot be paid (status: {order.Status}).");
        }

        var eventSeatIds = order.Items.Select(i => i.EventSeatId).ToArray();
        var stillHeldCount = await db.EventSeats.CountAsync(
            es => eventSeatIds.Contains(es.Id) && es.Status == SeatStatus.Held && es.HoldExpiresAtUtc > DateTime.UtcNow, ct);

        if (stillHeldCount != eventSeatIds.Length)
        {
            order.Status = OrderStatus.Expired;
            order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Conflict("Seat hold expired before payment could be processed.");
        }

        var message = new PaymentRequestedMessage(order.Id, userId, order.TotalAmountCents, DateTime.UtcNow);
        await publisher.PublishAsync(Topology.PaymentRequestedRoutingKey, message, ct);

        return Results.Accepted($"/api/orders/{order.Id}", new PayOrderResponse(order.Id, order.Status.ToString()));
    }

    private static async Task<IResult> GetOrderAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var order = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.EventSeat).ThenInclude(es => es!.Seat)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.UserId != userId)
        {
            return Results.Forbid();
        }

        return Results.Ok(ToResponse(order));
    }

    private static async Task<IResult> ListOrdersAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var orders = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.EventSeat).ThenInclude(es => es!.Seat)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(orders.Select(ToResponse));
    }

    private static OrderResponse ToResponse(Domain.Entities.Order order) => new(
        order.Id,
        order.EventId,
        order.Status.ToString(),
        order.TotalAmountCents,
        order.CreatedAtUtc,
        order.UpdatedAtUtc,
        order.Items.Select(i => new OrderItemResponse(
            i.EventSeatId,
            i.EventSeat!.SeatId,
            i.EventSeat.Seat!.Section,
            i.EventSeat.Seat.RowLabel,
            i.EventSeat.Seat.SeatNumber,
            i.PriceCents)).ToList());
}
