namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 B:負庫存驗證。
// feature/serializable-isolation 分支:斷言方向與 master baseline 相反，
// 驗證 Serializable isolation 能防止庫存低於 0 以及成功數超過初始庫存。
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

        // Serializable isolation 下衝突交易被 abort（409），業務邏輯正確拒絕超額扣庫：
        //   - Quantity >= 0（庫存永不為負）
        //   - successCount <= InitialQuantity（成功數不能超過可用庫存）
        //   - Version == successCount（每次成功 +1）
        var isClean = finalProduct.Quantity >= 0
            && successCount <= InitialQuantity
            && finalProduct.Version == successCount;

        Assert.True(isClean,
            $"Serializable isolation 應防止庫存為負且成功數不超過初始庫存，但偵測到異常。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, "
            + $"successCount={successCount}, version={finalProduct.Version}, "
            + $"initialQuantity={InitialQuantity}");
    }
}
