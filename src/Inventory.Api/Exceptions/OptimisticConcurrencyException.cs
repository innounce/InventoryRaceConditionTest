namespace Inventory.Api.Exceptions;

public class OptimisticConcurrencyException()
    : Exception("操作衝突，資料已被其他請求修改，請重試")
{
    public string ErrorCode => "CONCURRENCY_CONFLICT";
}
