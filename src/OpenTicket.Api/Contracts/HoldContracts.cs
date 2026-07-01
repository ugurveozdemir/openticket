namespace OpenTicket.Api.Contracts;

public record HoldSeatsRequest(List<Guid> SeatIds);

public record HoldSeatsResponse(Guid OrderId, Guid HoldId, DateTime HoldExpiresAtUtc, int TotalAmountCents, List<Guid> SeatIds);

public record ReleaseOrderResponse(Guid OrderId, string Status);
