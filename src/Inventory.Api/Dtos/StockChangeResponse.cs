namespace Inventory.Api.Dtos;

public record StockChangeResponse(ProductDto Product, TransactionDto Transaction);
