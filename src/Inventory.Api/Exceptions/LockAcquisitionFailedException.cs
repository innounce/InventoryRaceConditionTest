namespace Inventory.Api.Exceptions;

public class LockAcquisitionFailedException(Guid productId)
    : Exception($"商品 {productId} 正在被其他請求處理中，請稍後重試")
{
    public string ErrorCode => "LOCK_ACQUISITION_FAILED";
}
