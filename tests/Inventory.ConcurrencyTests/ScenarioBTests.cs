namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 B:負庫存驗證。
// feature/distributed-lock 分支:斷言方向反轉——Redis 分散式鎖保護讀-改-寫,
// 庫存絕不應低於 0,且成功扣庫數不超過初始庫存。
[Trait("Category", "Concurrency")]
public class ScenarioBTests
{
    private const int InitialQuantity = 100;
    private const int RequestCount = 1000;

    [Fact]
    public async Task StockOut_ExceedingInitialQuantity_NoNegativeStock()
    {
        var (factory, schemaName) = await TestSchema.CreateAsync();
        await using var _ = factory;

        using var httpClient = factory.CreateClient();
        var client = new ApiTestClient(httpClient);

        var product = await client.CreateProductAsync($"CT-B-{DateTime.UtcNow:HHmmssfff}", "情境 B 測試商品", InitialQuantity);

        var responses = await ConcurrentBurst.RunAsync(RequestCount, _ => client.StockOutAsync(product.Id, 1));
        var successCount = responses.Count(r => r.IsSuccessStatusCode);

        var finalProduct = await client.GetProductAsync(product.Id);
        var transactions = await client.GetTransactionsAsync(product.Id);
        CsvExport.Write("B", schemaName, finalProduct, transactions);

        var isClean = finalProduct.Quantity >= 0 && successCount <= InitialQuantity
            && finalProduct.Quantity == InitialQuantity - successCount;

        Assert.True(isClean,
            $"Redis 分散式鎖應防止負庫存,但偵測到資料不一致。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, successCount={successCount}, "
            + $"initialQuantity={InitialQuantity}, expectedQuantity={InitialQuantity - successCount}");
    }
}
