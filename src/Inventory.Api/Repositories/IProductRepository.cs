using Inventory.Api.Models;

namespace Inventory.Api.Repositories;

public interface IProductRepository
{
    Task<List<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(Guid id);
    Task AddAsync(Product product);
    void Remove(Product product);
    Task SaveChangesAsync();
}
