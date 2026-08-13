namespace Inventory.LoadTestClient;

// Releases every request at the same instant via an async gate — see
// docs/test-plan.md "共通測試環境準備" on why Task.WhenAll alone isn't enough to
// reliably reproduce a race window. A blocking Barrier looks like the obvious
// tool for this, but Barrier.SignalAndWait() blocks its thread; parking
// hundreds of pool threads at once runs into the thread pool's slow ramp-up
// (it only grows by ~1 thread/~500ms after the initial burst), so the
// "simultaneous" release actually trickles out over tens of seconds. A
// TaskCompletionSource never blocks a thread while waiting, so every
// participant can be queued immediately and released together.
public static class ConcurrentDispatcher
{
    public static async Task<List<ApiResult<StockChangeResponse>>> RunBurstAsync(
        int count, Func<int, Task<ApiResult<StockChangeResponse>>> action)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var tasks = new Task<ApiResult<StockChangeResponse>>[count];

        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks[index] = Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyCount) == count)
                    gate.TrySetResult();
                await gate.Task;
                return await action(index);
            });
        }

        return (await Task.WhenAll(tasks)).ToList();
    }

    // Sustained load: keeps up to `concurrency` requests in flight for `duration`,
    // used by scenario C to simulate ongoing traffic rather than a single spike.
    public static async Task<List<ApiResult<StockChangeResponse>>> RunSustainedAsync(
        TimeSpan duration, int concurrency, Func<Task<ApiResult<StockChangeResponse>>> action)
    {
        using var semaphore = new SemaphoreSlim(concurrency);
        var results = new List<ApiResult<StockChangeResponse>>();
        var resultsLock = new object();
        var inFlight = new List<Task>();
        var stopAt = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < stopAt)
        {
            await semaphore.WaitAsync();
            var task = Task.Run(async () =>
            {
                try
                {
                    var result = await action();
                    lock (resultsLock) results.Add(result);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            inFlight.Add(task);
        }

        await Task.WhenAll(inFlight);
        return results;
    }
}
