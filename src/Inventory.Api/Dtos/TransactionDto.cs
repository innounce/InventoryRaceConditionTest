namespace Inventory.Api.Dtos;

public record TransactionDto(
    Guid Id,
    Guid ProductId,
    string ChangeType,
    int Quantity,
    int BalanceAfter,
    DateTime CreatedAt);
