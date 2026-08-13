namespace Inventory.Api.Dtos;

public record CreateProductRequest(string Sku, string Name, int InitialQuantity);
