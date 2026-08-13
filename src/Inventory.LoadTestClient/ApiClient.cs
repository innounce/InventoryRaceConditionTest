using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.LoadTestClient;

public class ApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductDto> CreateProductAsync(string sku, string name, int initialQuantity)
    {
        var response = await httpClient.PostAsJsonAsync("products", new CreateProductRequest(sku, name, initialQuantity), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;
    }

    public Task<ApiResult<StockChangeResponse>> StockInAsync(Guid productId, int quantity) =>
        PostStockChangeAsync($"products/{productId}/stock-in", quantity);

    public Task<ApiResult<StockChangeResponse>> StockOutAsync(Guid productId, int quantity) =>
        PostStockChangeAsync($"products/{productId}/stock-out", quantity);

    public async Task<ProductDto> GetProductAsync(Guid productId)
    {
        var response = await httpClient.GetAsync($"products/{productId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;
    }

    public async Task<List<TransactionDto>> GetTransactionsAsync(Guid productId)
    {
        var response = await httpClient.GetAsync($"products/{productId}/transactions");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>(JsonOptions))!;
    }

    private async Task<ApiResult<StockChangeResponse>> PostStockChangeAsync(string path, int quantity)
    {
        var sentAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var response = await httpClient.PostAsJsonAsync(path, new StockChangeRequest(quantity), JsonOptions);
        stopwatch.Stop();

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<StockChangeResponse>(JsonOptions);
            return new ApiResult<StockChangeResponse>((int)response.StatusCode, body, null, stopwatch.Elapsed, sentAt);
        }

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        return new ApiResult<StockChangeResponse>((int)response.StatusCode, null, error, stopwatch.Elapsed, sentAt);
    }
}
