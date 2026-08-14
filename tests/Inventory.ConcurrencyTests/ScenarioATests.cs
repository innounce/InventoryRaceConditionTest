namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 A:Lost Update 驗證。
// Baseline 階段的斷言方向是「斷言弄髒了才算通過」——這裡驗證的是 API 目前
// 還沒有任何併發控制,所以理論上應該要重現 lost update。等第 3 步做完某種鎖
// 機制後,同一套測試骨架的斷言方向要反過來(見 docs/test-plan.md「跨分支重複使用測試」)。
[Trait("Category", "Concurrency")]
public class ScenarioATests
{
    private const int InitialQuantity = 1000;
    private const int RequestCount = 1000;

    [Fact]
    public async Task StockOut_UnderHighConcurrency_ReproducesLostUpdate()
    {
        var (factory, schemaName) = await TestSchema.CreateAsync();
        await using var _ = factory;

        using var httpClient = factory.CreateClient();
        var client = new ApiTestClient(httpClient);

        var product = await client.CreateProductAsync($"CT-A-{DateTime.UtcNow:HHmmssfff}", "情境 A 測試商品", InitialQuantity);

        var responses = await ConcurrentBurst.RunAsync(RequestCount, _ => client.StockOutAsync(product.Id, 1));
        var successCount = responses.Count(r => r.IsSuccessStatusCode);

        var finalProduct = await client.GetProductAsync(product.Id);
        var transactions = await client.GetTransactionsAsync(product.Id);
        CsvExport.Write("A", schemaName, finalProduct, transactions);
        var hasDuplicateBalance = transactions.GroupBy(t => t.BalanceAfter).Any(g => g.Count() > 1);

        var isDirty = finalProduct.Quantity != 0
            || finalProduct.Version < successCount
            || hasDuplicateBalance;

        Assert.True(isDirty,
            $"預期在沒有併發控制的 baseline 上重現 lost update,但沒有偵測到。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, version={finalProduct.Version}, "
            + $"successCount={successCount}, transactionCount={transactions.Count}, hasDuplicateBalance={hasDuplicateBalance}");
    }
}
