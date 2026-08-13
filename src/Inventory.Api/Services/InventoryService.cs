using Inventory.Api.Dtos;
using Inventory.Api.Exceptions;
using Inventory.Api.Models;
using Inventory.Api.Repositories;

namespace Inventory.Api.Services;

public class InventoryService(
    IProductRepository productRepository,
    IInventoryTransactionRepository transactionRepository) : IInventoryService
{
    // Deliberately naive read-modify-write: no SELECT ... FOR UPDATE, no explicit
    // isolation level change, no retry. This is the baseline described in
    // docs/db-schema.md and README.md — the race condition is the point, not a bug
    // to be fixed here. Do not add locking to this method.
    public async Task<StockChangeResponse> StockInAsync(Guid productId, StockChangeRequest request)
    {
        if (request.Quantity <= 0)
            throw new InvalidQuantityException();

        var product = await productRepository.GetByIdAsync(productId)
            ?? throw new ProductNotFoundException(productId);

        product.Quantity += request.Quantity;
        return await ApplyChangeAsync(product, ChangeType.In, request.Quantity);
    }

    public async Task<StockChangeResponse> StockOutAsync(Guid productId, StockChangeRequest request)
    {
        if (request.Quantity <= 0)
            throw new InvalidQuantityException();

        var product = await productRepository.GetByIdAsync(productId)
            ?? throw new ProductNotFoundException(productId);

        var newQuantity = product.Quantity - request.Quantity;
        if (newQuantity < 0)
            throw new InsufficientStockException(product.Quantity, request.Quantity);

        product.Quantity = newQuantity;
        return await ApplyChangeAsync(product, ChangeType.Out, request.Quantity);
    }

    public async Task<List<TransactionDto>> GetTransactionsAsync(Guid productId)
    {
        _ = await productRepository.GetByIdAsync(productId)
            ?? throw new ProductNotFoundException(productId);

        var transactions = await transactionRepository.GetByProductIdAsync(productId);
        return transactions.Select(ToDto).ToList();
    }

    private async Task<StockChangeResponse> ApplyChangeAsync(Product product, ChangeType changeType, int quantity)
    {
        product.Version += 1;
        product.UpdatedAt = DateTime.UtcNow;

        var transaction = new InventoryTransaction
        {
            ProductId = product.Id,
            ChangeType = changeType,
            Quantity = quantity,
            BalanceAfter = product.Quantity,
            CreatedAt = product.UpdatedAt
        };
        await transactionRepository.AddAsync(transaction);
        await productRepository.SaveChangesAsync();

        var productDto = new ProductDto(
            product.Id, product.Sku, product.Name, product.Quantity,
            product.Version, product.CreatedAt, product.UpdatedAt);
        return new StockChangeResponse(productDto, ToDto(transaction));
    }

    private static TransactionDto ToDto(InventoryTransaction transaction) => new(
        transaction.Id, transaction.ProductId,
        transaction.ChangeType == ChangeType.In ? "IN" : "OUT",
        transaction.Quantity, transaction.BalanceAfter, transaction.CreatedAt);
}
