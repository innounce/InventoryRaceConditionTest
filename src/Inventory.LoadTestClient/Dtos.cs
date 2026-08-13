namespace Inventory.LoadTestClient;

// Minimal DTOs kept local to this project on purpose — the client is not allowed
// to share types with Inventory.Api (see README.md "架構設計:前後端分離").
public record ProductDto(Guid Id, string Sku, string Name, int Quantity, int Version, DateTime CreatedAt, DateTime UpdatedAt);

public record TransactionDto(Guid Id, Guid ProductId, string ChangeType, int Quantity, int BalanceAfter, DateTime CreatedAt);

public record StockChangeResponse(ProductDto Product, TransactionDto Transaction);

public record ErrorResponse(string Error, string Message);

public record CreateProductRequest(string Sku, string Name, int InitialQuantity);

public record StockChangeRequest(int Quantity);
