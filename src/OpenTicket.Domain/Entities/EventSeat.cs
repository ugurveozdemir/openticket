using OpenTicket.Domain.Enums;

namespace OpenTicket.Domain.Entities;

public class EventSeat
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid SeatId { get; set; }
    public int PriceCents { get; set; }
    public SeatStatus Status { get; set; }
    public Guid? HoldId { get; set; }
    public Guid? HeldByUserId { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public DateTime? SoldAtUtc { get; set; }

    public uint Version { get; set; }

    public Event? Event { get; set; }
    public Seat? Seat { get; set; }
}
