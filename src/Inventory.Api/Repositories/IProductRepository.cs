using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Repositories;

public interface IProductRepository
{
    Task<List<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(Guid id);
    Task<Product?> GetByIdForUpdateAsync(Guid id);
    Task<IDbContextTransaction> BeginTransactionAsync();
    Task AddAsync(Product product);
    void Remove(Product product);
    Task SaveChangesAsync();
}
