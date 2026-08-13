namespace Inventory.LoadTestClient;

public record ApiResult<T>(int StatusCode, T? Body, ErrorResponse? Error, TimeSpan Latency, DateTime SentAt);
