namespace Inventory.Api.Locking;

public interface IDistributedLockFactory
{
    // Returns null if the lock could not be acquired within the timeout.
    Task<IDistributedLock?> TryAcquireAsync(string key, TimeSpan expiry, TimeSpan timeout = default);
}
