using Inventory.Api.Dtos;

namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 C:長時間混合壓力測試。
// feature/pessimistic-lock 分支:斷言方向與 master baseline 相反，
// 驗證對帳一致——SELECT ... FOR UPDATE 確保每筆寫入都序列化，
// 無 lost update，故交易紀錄總和必然等於最終庫存。
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
            $"悲觀鎖應確保對帳一致，但偵測到不一致。"
            + $"初始庫存 {InitialQuantity} ± 交易紀錄總和 = {expectedQuantity}，目前 Quantity = {finalProduct.Quantity}。"
            + $"schema={schemaName}, transactionCount={transactions.Count}");
    }

    private static int SignedQuantity(TransactionDto transaction) =>
        transaction.ChangeType == "IN" ? transaction.Quantity : -transaction.Quantity;
}
