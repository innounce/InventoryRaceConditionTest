using Inventory.Api.Models;

namespace Inventory.Api.Repositories;

public interface IInventoryTransactionRepository
{
    Task AddAsync(InventoryTransaction transaction);
    Task<List<InventoryTransaction>> GetByProductIdAsync(Guid productId);
}
