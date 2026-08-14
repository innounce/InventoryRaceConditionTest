using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Repositories;

public class ProductRepository(InventoryDbContext dbContext) : IProductRepository
{
    public Task<List<Product>> GetAllAsync() =>
        dbContext.Products.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync();

    public Task<Product?> GetByIdAsync(Guid id) =>
        dbContext.Products.FirstOrDefaultAsync(p => p.Id == id);

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel) =>
        dbContext.Database.BeginTransactionAsync(isolationLevel);

    public async Task AddAsync(Product product) =>
        await dbContext.Products.AddAsync(product);

    public void Remove(Product product) =>
        dbContext.Products.Remove(product);

    public Task SaveChangesAsync() => dbContext.SaveChangesAsync();
}
