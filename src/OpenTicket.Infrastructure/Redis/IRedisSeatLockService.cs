namespace OpenTicket.Infrastructure.Redis;

public interface IRedisSeatLockService
{
    Task<bool> TryHoldSeatsAsync(Guid eventId, IReadOnlyList<Guid> seatIds, Guid userId, Guid holdId, TimeSpan ttl);

    Task<long> ReleaseSeatsAsync(Guid eventId, IReadOnlyList<Guid> seatIds, Guid userId, Guid holdId);
}
