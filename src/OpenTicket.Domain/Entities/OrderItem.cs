namespace OpenTicket.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid EventSeatId { get; set; }
    public int PriceCents { get; set; }

    public Order? Order { get; set; }
    public EventSeat? EventSeat { get; set; }
}
