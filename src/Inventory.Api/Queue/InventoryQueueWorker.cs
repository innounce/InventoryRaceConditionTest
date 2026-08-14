using Inventory.Api.Services;

namespace Inventory.Api.Queue;

public sealed class InventoryQueueWorker(
    InventoryChannel channel,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<InventoryService>();
            try
            {
                var result = await item.Work(service);
                item.Completion.SetResult(result);
            }
            catch (Exception ex)
            {
                item.Completion.SetException(ex);
            }
        }
    }
}
