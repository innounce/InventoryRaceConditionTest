using Inventory.Api.Dtos;

namespace Inventory.Api.Services;

public interface IInventoryService
{
    Task<StockChangeResponse> StockInAsync(Guid productId, StockChangeRequest request);
    Task<StockChangeResponse> StockOutAsync(Guid productId, StockChangeRequest request);
    Task<List<TransactionDto>> GetTransactionsAsync(Guid productId);
}
