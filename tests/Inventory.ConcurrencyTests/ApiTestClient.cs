using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Dtos;

namespace Inventory.ConcurrencyTests;

public class ApiTestClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductDto> CreateProductAsync(string sku, string name, int initialQuantity)
    {
        var response = await httpClient.PostAsJsonAsync("products", new CreateProductRequest(sku, name, initialQuantity), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;
    }

    public async Task<HttpResponseMessage> StockOutAsync(Guid productId, int quantity) =>
        await httpClient.PostAsJsonAsync($"products/{productId}/stock-out", new StockChangeRequest(quantity), JsonOptions);

    public async Task<HttpResponseMessage> StockInAsync(Guid productId, int quantity) =>
        await httpClient.PostAsJsonAsync($"products/{productId}/stock-in", new StockChangeRequest(quantity), JsonOptions);

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
}
