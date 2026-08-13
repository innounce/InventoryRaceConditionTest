using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Repositories;

public class InventoryTransactionRepository(InventoryDbContext dbContext) : IInventoryTransactionRepository
{
    public async Task AddAsync(InventoryTransaction transaction) =>
        await dbContext.InventoryTransactions.AddAsync(transaction);

    public Task<List<InventoryTransaction>> GetByProductIdAsync(Guid productId) =>
        dbContext.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ProductId == productId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
}
