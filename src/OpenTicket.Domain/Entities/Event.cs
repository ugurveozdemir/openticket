using OpenTicket.Domain.Enums;

namespace OpenTicket.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public EventStatus Status { get; set; }

    public Venue? Venue { get; set; }
    public ICollection<EventSeat> EventSeats { get; set; } = new List<EventSeat>();
}
