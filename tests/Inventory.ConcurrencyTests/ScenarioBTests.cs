namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 B:負庫存驗證。同樣是 baseline 階段,斷言方向是
// 「斷言弄髒了才算通過」。
[Trait("Category", "Concurrency")]
public class ScenarioBTests
{
    private const int InitialQuantity = 100;
    private const int RequestCount = 1000;

    [Fact]
    public async Task StockOut_ExceedingInitialQuantity_ReproducesNegativeStock()
    {
        var (factory, schemaName) = await TestSchema.CreateAsync();
        await using var _ = factory;

        using var httpClient = factory.CreateClient();
        var client = new ApiTestClient(httpClient);

        var product = await client.CreateProductAsync($"CT-B-{DateTime.UtcNow:HHmmssfff}", "情境 B 測試商品", InitialQuantity);

        var responses = await ConcurrentBurst.RunAsync(RequestCount, _ => client.StockOutAsync(product.Id, 1));
        var successCount = responses.Count(r => r.IsSuccessStatusCode);

        var finalProduct = await client.GetProductAsync(product.Id);

        var isDirty = finalProduct.Quantity < 0 || successCount > InitialQuantity;

        Assert.True(isDirty,
            $"預期在沒有併發控制的 baseline 上重現「庫存不可為負數」規則失守,但沒有偵測到。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, successCount={successCount}, "
            + $"initialQuantity={InitialQuantity}");
    }
}
