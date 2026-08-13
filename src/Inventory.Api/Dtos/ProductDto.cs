namespace Inventory.Api.Dtos;

public record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    int Quantity,
    int Version,
    DateTime CreatedAt,
    DateTime UpdatedAt);
