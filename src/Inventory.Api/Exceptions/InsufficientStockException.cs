namespace Inventory.Api.Exceptions;

public class InsufficientStockException(int currentQuantity, int requestedQuantity)
    : Exception($"庫存不足，目前庫存為 {currentQuantity}，無法扣除 {requestedQuantity}")
{
    public string ErrorCode => "INSUFFICIENT_STOCK";
}
