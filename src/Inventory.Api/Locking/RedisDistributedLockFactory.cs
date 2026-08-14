using StackExchange.Redis;

namespace Inventory.Api.Locking;

public class RedisDistributedLockFactory(IConnectionMultiplexer redis) : IDistributedLockFactory
{
    // Release script: only delete the key if the stored token matches ours,
    // preventing a slow holder from releasing a lock that already expired and
    // was re-acquired by another request.
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare("""
        if redis.call("get", @key) == @token then
            return redis.call("del", @key)
        else
            return 0
        end
        """);

    public async Task<IDistributedLock?> TryAcquireAsync(string key, TimeSpan expiry, TimeSpan timeout = default)
    {
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + timeout;

        do
        {
            // SET key token NX PX expiry — atomic acquire
            var acquired = await db.StringSetAsync(key, token, expiry, When.NotExists);
            if (acquired)
                return new RedisLock(db, key, token);

            if (timeout == default || DateTime.UtcNow >= deadline)
                return null;

            await Task.Delay(5);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private sealed class RedisLock(IDatabase db, string key, string token) : IDistributedLock
    {
        public async ValueTask DisposeAsync()
        {
            await db.ScriptEvaluateAsync(ReleaseScript, new { key = (RedisKey)key, token });
        }
    }
}
