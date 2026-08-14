namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 A:Lost Update 驗證。
// feature/serializable-isolation 分支:整個 read-modify-write 包在
// SERIALIZABLE 交易內，PostgreSQL SSI 自動偵測讀寫衝突並 abort 其中一方
// (SQLSTATE 40001)，應用層捕捉後回 409。
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

        var responses = await ConcurrentBurst.RunAsync(RequestCount, _ => client.StockOutAsync(product.Id, 1));
        var successCount = responses.Count(r => r.IsSuccessStatusCode);

        var finalProduct = await client.GetProductAsync(product.Id);
        var transactions = await client.GetTransactionsAsync(product.Id);
        CsvExport.Write("A", schemaName, finalProduct, transactions);
        var hasDuplicateBalance = transactions.GroupBy(t => t.BalanceAfter).Any(g => g.Count() > 1);

        // Serializable isolation 下每筆成功的請求都精確遞減庫存一次：
        //   - Quantity == InitialQuantity - successCount（精確對帳，無 lost update）
        //   - Version == successCount（每次成功 +1，被 abort 的不寫入）
        //   - BalanceAfter 無重複（每筆看到的是序列化後的庫存）
        var isClean = finalProduct.Quantity == InitialQuantity - successCount
            && finalProduct.Version == successCount
            && !hasDuplicateBalance;

        Assert.True(isClean,
            $"Serializable isolation 應確保每筆成功的請求都精確更新庫存，但偵測到不一致。"
            + $"schema={schemaName}, finalQuantity={finalProduct.Quantity}, "
            + $"expectedQuantity={InitialQuantity - successCount}, "
            + $"version={finalProduct.Version}, successCount={successCount}, "
            + $"transactionCount={transactions.Count}, hasDuplicateBalance={hasDuplicateBalance}");
    }
}
