using Inventory.Api.Dtos;
using Inventory.Api.Queue;

namespace Inventory.Api.Services;

public sealed class QueuedInventoryService(
    InventoryChannel channel,
    InventoryService inner) : IInventoryService
{
    public Task<StockChangeResponse> StockInAsync(Guid productId, StockChangeRequest request)
        => Enqueue(svc => svc.StockInAsync(productId, request));

    public Task<StockChangeResponse> StockOutAsync(Guid productId, StockChangeRequest request)
        => Enqueue(svc => svc.StockOutAsync(productId, request));

    public Task<List<TransactionDto>> GetTransactionsAsync(Guid productId)
        => inner.GetTransactionsAsync(productId);

    private async Task<StockChangeResponse> Enqueue(
        Func<InventoryService, Task<StockChangeResponse>> work)
    {
        var tcs = new TaskCompletionSource<StockChangeResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await channel.Writer.WriteAsync(new StockWorkItem(work, tcs));
        return await tcs.Task;
    }
}
