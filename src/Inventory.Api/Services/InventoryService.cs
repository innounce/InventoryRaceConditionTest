using System.Data;
using Inventory.Api.Dtos;
using Inventory.Api.Exceptions;
using Inventory.Api.Models;
using Inventory.Api.Repositories;
using Npgsql;

namespace Inventory.Api.Services;

public class InventoryService(
    IProductRepository productRepository,
    IInventoryTransactionRepository transactionRepository) : IInventoryService
{
    public async Task<StockChangeResponse> StockInAsync(Guid productId, StockChangeRequest request)
    {
        if (request.Quantity <= 0)
            throw new InvalidQuantityException();

        await using var tx = await productRepository.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var product = await productRepository.GetByIdAsync(productId)
                ?? throw new ProductNotFoundException(productId);

            product.Quantity += request.Quantity;
            var result = await ApplyChangeAsync(product, ChangeType.In, request.Quantity);
            await tx.CommitAsync();
            return result;
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            throw new SerializationFailureException();
        }
    }

    public async Task<StockChangeResponse> StockOutAsync(Guid productId, StockChangeRequest request)
    {
        if (request.Quantity <= 0)
            throw new InvalidQuantityException();

        await using var tx = await productRepository.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var product = await productRepository.GetByIdAsync(productId)
                ?? throw new ProductNotFoundException(productId);

            var newQuantity = product.Quantity - request.Quantity;
            if (newQuantity < 0)
                throw new InsufficientStockException(product.Quantity, request.Quantity);

            product.Quantity = newQuantity;
            var result = await ApplyChangeAsync(product, ChangeType.Out, request.Quantity);
            await tx.CommitAsync();
            return result;
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            throw new SerializationFailureException();
        }
    }

    public async Task<List<TransactionDto>> GetTransactionsAsync(Guid productId)
    {
        _ = await productRepository.GetByIdAsync(productId)
            ?? throw new ProductNotFoundException(productId);

        var transactions = await transactionRepository.GetByProductIdAsync(productId);
        return transactions.Select(ToDto).ToList();
    }

    // PostgreSQL SSI aborts conflicting transactions with SQLSTATE 40001.
    // Npgsql's execution strategy wraps it: PostgresException → DbUpdateException
    // → InvalidOperationException, so we walk the full InnerException chain.
    private static bool IsSerializationFailure(Exception ex)
    {
        var current = (Exception?)ex;
        while (current is not null)
        {
            if (current is PostgresException pg && pg.SqlState == "40001")
                return true;
            current = current.InnerException;
        }
        return false;
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
