namespace OpenTicket.Api.Contracts;

public record CreateEventRequest(Guid VenueId, string Title, DateTime StartsAtUtc);

public record EventResponse(Guid Id, Guid VenueId, string VenueName, string Title, DateTime StartsAtUtc, string Status);

public record PublishEventRequest(int PriceCents);

public record EventSeatResponse(Guid Id, Guid SeatId, string Section, string RowLabel, int SeatNumber, int PriceCents, string Status);
