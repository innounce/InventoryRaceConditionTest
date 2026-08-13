namespace Inventory.Api.Exceptions;

public class ProductNotFoundException(Guid productId)
    : Exception($"找不到商品 {productId}")
{
    public string ErrorCode => "PRODUCT_NOT_FOUND";
}
