using StackExchange.Redis;

namespace OpenTicket.Infrastructure.Redis;

public class RedisSeatLockService : IRedisSeatLockService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private byte[]? _holdScriptSha;
    private byte[]? _releaseScriptSha;

    public RedisSeatLockService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> TryHoldSeatsAsync(Guid eventId, IReadOnlyList<Guid> seatIds, Guid userId, Guid holdId, TimeSpan ttl)
    {
        var keys = ToKeys(eventId, seatIds);
        var values = new RedisValue[] { BuildToken(userId, holdId), (long)ttl.TotalMilliseconds };

        var result = await EvaluateAsync(isHold: true, keys, values);
        return (int)result == 1;
    }

    public async Task<long> ReleaseSeatsAsync(Guid eventId, IReadOnlyList<Guid> seatIds, Guid userId, Guid holdId)
    {
        var keys = ToKeys(eventId, seatIds);
        var values = new RedisValue[] { BuildToken(userId, holdId) };

        var result = await EvaluateAsync(isHold: false, keys, values);
        return (long)result;
    }

    private async Task<RedisResult> EvaluateAsync(bool isHold, RedisKey[] keys, RedisValue[] values)
    {
        await EnsureScriptsLoadedAsync();
        var db = _redis.GetDatabase();
        var sha = isHold ? _holdScriptSha! : _releaseScriptSha!;

        try
        {
            return await db.ScriptEvaluateAsync(sha, keys, values);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("NOSCRIPT"))
        {
            await ReloadScriptsAsync();
            sha = isHold ? _holdScriptSha! : _releaseScriptSha!;
            return await db.ScriptEvaluateAsync(sha, keys, values);
        }
    }

    private async Task EnsureScriptsLoadedAsync()
    {
        if (_holdScriptSha is not null && _releaseScriptSha is not null)
        {
            return;
        }

        await _loadLock.WaitAsync();
        try
        {
            if (_holdScriptSha is null || _releaseScriptSha is null)
            {
                await ReloadScriptsAsync();
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task ReloadScriptsAsync()
    {
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var holdScript = await File.ReadAllTextAsync(ScriptPath("hold_seats.lua"));
        var releaseScript = await File.ReadAllTextAsync(ScriptPath("release_if_owner.lua"));

        _holdScriptSha = await server.ScriptLoadAsync(holdScript);
        _releaseScriptSha = await server.ScriptLoadAsync(releaseScript);
    }

    private static string ScriptPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Redis", "Scripts", fileName);

    private static RedisKey[] ToKeys(Guid eventId, IReadOnlyList<Guid> seatIds) =>
        seatIds.Select(seatId => (RedisKey)$"seat:hold:{eventId}:{seatId}").ToArray();

    private static string BuildToken(Guid userId, Guid holdId) => $"{userId}:{holdId}";
}
