namespace OpenTicket.Api.Contracts;

public record OrderItemResponse(Guid EventSeatId, Guid SeatId, string Section, string RowLabel, int SeatNumber, int PriceCents);

public record OrderResponse(
    Guid Id,
    Guid EventId,
    string Status,
    int TotalAmountCents,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    List<OrderItemResponse> Items);

public record PayOrderResponse(Guid OrderId, string Status);
