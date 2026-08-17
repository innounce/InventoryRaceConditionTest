namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 A:Lost Update 驗證。
// feature/pessimistic-lock 分支:SELECT ... FOR UPDATE 在 DB 層取得 row-level
// exclusive lock，並發請求在 PostgreSQL 排隊等待，不會彼此覆蓋計算結果。
// 斷言方向與 master baseline 相反:成功的請求必須精確對帳。
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

        // 悲觀鎖下每筆成功的請求都序列化執行，精確遞減庫存一次：
        //   - Quantity == InitialQuantity - successCount（精確對帳，無 lost update）
        //   - Version == successCount（每次成功 +1）
        //   - BalanceAfter 無重複（每筆鎖定時看到的是前一筆已提交的庫存）
        var isClean = finalProduct.Quantity == InitialQuantity - successCount
            && finalProduct.Version == successCount
            && !hasDuplicateBalance;

        Assert.True(isClean,
            $"悲觀鎖應確保每筆成功的請求都精確更新庫存，但偵測到不一致。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, "
            + $"expectedQuantity={InitialQuantity - successCount}, "
            + $"version={finalProduct.Version}, successCount={successCount}, "
            + $"transactionCount={transactions.Count}, hasDuplicateBalance={hasDuplicateBalance}");
    }
}
