namespace Inventory.Api.Queue;

public sealed record StockWorkItem(
    Func<Services.InventoryService, Task<Dtos.StockChangeResponse>> Work,
    TaskCompletionSource<Dtos.StockChangeResponse> Completion);
