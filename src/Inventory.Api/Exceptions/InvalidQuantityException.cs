namespace Inventory.Api.Exceptions;

public class InvalidQuantityException()
    : Exception("quantity 必須大於 0")
{
    public string ErrorCode => "INVALID_QUANTITY";
}
