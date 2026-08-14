namespace Inventory.Api.Exceptions;

public class SerializationFailureException()
    : Exception("交易發生序列化衝突，請重試")
{
    public string ErrorCode => "SERIALIZATION_FAILURE";
}
