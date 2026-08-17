using System.Diagnostics;

namespace Inventory.ConcurrencyTests;

public record RequestResult<T>(T Value, TimeSpan Elapsed);

// Same async-gate release approach as Inventory.LoadTestClient's
// ConcurrentDispatcher (see that file for why a blocking Barrier is wrong
// here — it starves the thread pool instead of releasing everyone at once) —
// kept as a small local duplicate rather than a project reference, since this
// test project already depends on Inventory.Api and shouldn't also pull in
// the console client.
public static class ConcurrentBurst
{
    public static async Task<List<RequestResult<T>>> RunAsync<T>(int count, Func<int, Task<T>> action)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var tasks = new Task<RequestResult<T>>[count];

        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks[index] = Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyCount) == count)
                    gate.TrySetResult();
                await gate.Task;
                var sw = Stopwatch.StartNew();
                var result = await action(index);
                return new RequestResult<T>(result, sw.Elapsed);
            });
        }

        return (await Task.WhenAll(tasks)).ToList();
    }

    public static async Task<List<RequestResult<T>>> RunSustainedAsync<T>(TimeSpan duration, int concurrency, Func<Task<T>> action)
    {
        using var semaphore = new SemaphoreSlim(concurrency);
        var results = new List<RequestResult<T>>();
        var resultsLock = new object();
        var inFlight = new List<Task>();
        var stopAt = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < stopAt)
        {
            await semaphore.WaitAsync();
            inFlight.Add(Task.Run(async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    var result = await action();
                    var elapsed = sw.Elapsed;
                    lock (resultsLock) results.Add(new RequestResult<T>(result, elapsed));
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(inFlight);
        return results;
    }
}
