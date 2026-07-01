using OpenTicket.Infrastructure.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace OpenTicket.Tests;

public class RedisSeatLockServiceTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7").Build();
    private IRedisSeatLockService _seatLock = null!;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        var redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _seatLock = new RedisSeatLockService(redis);
    }

    public Task DisposeAsync() => _redisContainer.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentHolds_OnSameSeat_OnlyOneWinner()
    {
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        const int concurrency = 50;

        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            _seatLock.TryHoldSeatsAsync(eventId, [seatId], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30)));

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task Hold_AllOrNothing_WhenOneSeatAlreadyHeld()
    {
        var eventId = Guid.NewGuid();
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();

        var firstUserHeld = await _seatLock.TryHoldSeatsAsync(eventId, [seatB], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30));
        Assert.True(firstUserHeld);

        var secondAttempt = await _seatLock.TryHoldSeatsAsync(eventId, [seatA, seatB], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30));
        Assert.False(secondAttempt);

        var seatAStillFree = await _seatLock.TryHoldSeatsAsync(eventId, [seatA], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30));
        Assert.True(seatAStillFree);
    }

    [Fact]
    public async Task Release_ThenReHold_Succeeds()
    {
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var holdId = Guid.NewGuid();

        Assert.True(await _seatLock.TryHoldSeatsAsync(eventId, [seatId], userId, holdId, TimeSpan.FromSeconds(30)));

        var released = await _seatLock.ReleaseSeatsAsync(eventId, [seatId], userId, holdId);
        Assert.Equal(1, released);

        Assert.True(await _seatLock.TryHoldSeatsAsync(eventId, [seatId], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Release_WithWrongToken_DoesNotRelease()
    {
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var holdId = Guid.NewGuid();

        await _seatLock.TryHoldSeatsAsync(eventId, [seatId], userId, holdId, TimeSpan.FromSeconds(30));

        var releasedByWrongOwner = await _seatLock.ReleaseSeatsAsync(eventId, [seatId], Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(0, releasedByWrongOwner);

        var stillHeld = await _seatLock.TryHoldSeatsAsync(eventId, [seatId], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30));
        Assert.False(stillHeld);
    }

    [Fact]
    public async Task Hold_AfterTtlExpires_CanBeReacquired()
    {
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        Assert.True(await _seatLock.TryHoldSeatsAsync(eventId, [seatId], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(1)));

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.True(await _seatLock.TryHoldSeatsAsync(eventId, [seatId], Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(30)));
    }
}
