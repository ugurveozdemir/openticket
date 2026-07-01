namespace OpenTicket.Infrastructure.Messaging.Contracts;

public record PaymentRequestedMessage(Guid OrderId, Guid UserId, int AmountCents, DateTime RequestedAtUtc);

public record PaymentSucceededMessage(Guid OrderId, Guid PaymentId, DateTime CompletedAtUtc);

public record PaymentFailedMessage(Guid OrderId, Guid PaymentId, string Reason, DateTime CompletedAtUtc);
