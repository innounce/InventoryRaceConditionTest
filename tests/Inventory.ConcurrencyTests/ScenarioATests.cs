namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 A:Lost Update 驗證。
// feature/queue-based-serialization 分支:斷言方向反轉——單一 consumer 保證
// 所有寫入嚴格序列化,不應再出現 lost update。
[Trait("Category", "Concurrency")]
public class ScenarioATests
{
    private const int InitialQuantity = 1000;
    private const int RequestCount = 1000;

    [Fact]
    public async Task StockOut_UnderHighConcurrency_NoLostUpdate()
    {
        var (factory, schemaName) = await TestSchema.CreateAsync();
        await using var _ = factory;

        using var httpClient = factory.CreateClient();
        var client = new ApiTestClient(httpClient);

        var product = await client.CreateProductAsync($"CT-A-{DateTime.UtcNow:HHmmssfff}", "情境 A 測試商品", InitialQuantity);

        var results = await ConcurrentBurst.RunAsync(RequestCount, _ => client.StockOutAsync(product.Id, 1));
        var successCount = results.Count(r => r.Value.IsSuccessStatusCode);
        var requestStats = results.Select(r => (r.Value.IsSuccessStatusCode, r.Elapsed)).ToList();

        var finalProduct = await client.GetProductAsync(product.Id);
        var transactions = await client.GetTransactionsAsync(product.Id);
        CsvExport.Write("A", schemaName, finalProduct, transactions, requestStats);
        var hasDuplicateBalance = transactions.GroupBy(t => t.BalanceAfter).Any(g => g.Count() > 1);

        var isClean = finalProduct.Quantity == InitialQuantity - successCount
            && finalProduct.Version == successCount
            && !hasDuplicateBalance;

        Assert.True(isClean,
            $"Queue 序列化應防止 lost update,但偵測到資料不一致。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, expectedQuantity={InitialQuantity - successCount}, "
            + $"version={finalProduct.Version}, successCount={successCount}, "
            + $"transactionCount={transactions.Count}, hasDuplicateBalance={hasDuplicateBalance}");
    }
}
