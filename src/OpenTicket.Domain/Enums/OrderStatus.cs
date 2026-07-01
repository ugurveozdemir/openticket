namespace OpenTicket.Domain.Enums;

public enum OrderStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Failed = 2,
    Expired = 3,
    Cancelled = 4
}
