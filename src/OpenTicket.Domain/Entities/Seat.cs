namespace OpenTicket.Domain.Entities;

public class Seat
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Section { get; set; } = string.Empty;
    public string RowLabel { get; set; } = string.Empty;
    public int SeatNumber { get; set; }

    public Venue? Venue { get; set; }
}
