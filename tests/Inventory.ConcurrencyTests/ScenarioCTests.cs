using Inventory.Api.Dtos;

namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 C:長時間混合壓力測試。手動測試(Inventory.LoadTestClient)
// 用 60 秒模擬,這裡為了讓自動化測試能常態重複執行,把時長縮短到 5 秒、併發度降到
// 50——時間拉長不會改變這條斷言驗證的東西(對帳是否一致),只是讓重現機率更高,
// 縮短是刻意的取捨,不是規格變動。
//
// feature/queue-based-serialization 分支:斷言方向反轉——單一 consumer 序列化所有
// 寫入，Product.Quantity 必須與 InventoryTransaction 記錄的總和完全吻合。
[Trait("Category", "Concurrency")]
public class ScenarioCTests
{
    private const int InitialQuantity = 500;
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private const int Concurrency = 50;

    [Fact]
    public async Task MixedStockInOut_UnderSustainedLoad_ReconciliationMatches()
    {
        var (factory, schemaName) = await TestSchema.CreateAsync();
        await using var _ = factory;

        using var httpClient = factory.CreateClient();
        var client = new ApiTestClient(httpClient);

        var product = await client.CreateProductAsync($"CT-C-{DateTime.UtcNow:HHmmssfff}", "情境 C 測試商品", InitialQuantity);

        var random = new Random();
        await ConcurrentBurst.RunSustainedAsync(Duration, Concurrency, () =>
        {
            var quantity = random.Next(1, 6);
            return random.NextDouble() < 0.6
                ? client.StockOutAsync(product.Id, quantity)
                : client.StockInAsync(product.Id, quantity);
        });

        var finalProduct = await client.GetProductAsync(product.Id);
        var transactions = await client.GetTransactionsAsync(product.Id);
        CsvExport.Write("C", schemaName, finalProduct, transactions);

        var expectedQuantity = InitialQuantity + transactions.Sum(SignedQuantity);
        var isClean = expectedQuantity == finalProduct.Quantity;

        Assert.True(isClean,
            $"Queue 序列化應確保對帳一致,但偵測到不符。"
            + $"初始庫存 {InitialQuantity} ± 交易紀錄總和 = {expectedQuantity},目前 Quantity = {finalProduct.Quantity}。"
            + $"schema={schemaName}, transactionCount={transactions.Count}");
    }

    private static int SignedQuantity(TransactionDto transaction) =>
        transaction.ChangeType == "IN" ? transaction.Quantity : -transaction.Quantity;
}
