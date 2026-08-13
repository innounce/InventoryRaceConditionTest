using Inventory.Api.Dtos;
using Inventory.Api.Exceptions;
using Inventory.Api.Models;
using Inventory.Api.Repositories;

namespace Inventory.Api.Services;

public class ProductService(IProductRepository productRepository) : IProductService
{
    public async Task<List<ProductDto>> GetAllAsync() =>
        (await productRepository.GetAllAsync()).Select(ToDto).ToList();

    public async Task<ProductDto> GetByIdAsync(Guid id)
    {
        var product = await productRepository.GetByIdAsync(id)
            ?? throw new ProductNotFoundException(id);
        return ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Sku = request.Sku,
            Name = request.Name,
            Quantity = request.InitialQuantity,
            Version = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        await productRepository.AddAsync(product);
        await productRepository.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request)
    {
        var product = await productRepository.GetByIdAsync(id)
            ?? throw new ProductNotFoundException(id);

        product.Sku = request.Sku;
        product.Name = request.Name;
        product.UpdatedAt = DateTime.UtcNow;

        await productRepository.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await productRepository.GetByIdAsync(id)
            ?? throw new ProductNotFoundException(id);

        productRepository.Remove(product);
        await productRepository.SaveChangesAsync();
    }

    private static ProductDto ToDto(Product product) => new(
        product.Id, product.Sku, product.Name, product.Quantity,
        product.Version, product.CreatedAt, product.UpdatedAt);
}
