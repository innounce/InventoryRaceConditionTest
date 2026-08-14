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

    // SELECT ... FOR UPDATE acquires a row-level exclusive lock that is held
    // until the surrounding transaction commits or rolls back, so concurrent
    // requests queue at the DB rather than failing with a concurrency error.
    public Task<Product?> GetByIdForUpdateAsync(Guid id) =>
        dbContext.Products
            .FromSqlInterpolated($"SELECT * FROM \"Product\" WHERE \"Id\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync();

    public Task<IDbContextTransaction> BeginTransactionAsync() =>
        dbContext.Database.BeginTransactionAsync();

    public async Task AddAsync(Product product) =>
        await dbContext.Products.AddAsync(product);

    public void Remove(Product product) =>
        dbContext.Products.Remove(product);

    public Task SaveChangesAsync() => dbContext.SaveChangesAsync();
}
