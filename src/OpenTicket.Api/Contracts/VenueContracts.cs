namespace OpenTicket.Api.Contracts;

public record CreateVenueRequest(string Name, string City);

public record VenueResponse(Guid Id, string Name, string City);

public record BulkSeatRow(string Section, string RowLabel, int SeatFrom, int SeatTo);

public record BulkCreateSeatsRequest(List<BulkSeatRow> Rows);

public record SeatResponse(Guid Id, string Section, string RowLabel, int SeatNumber);
