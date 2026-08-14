using Inventory.Api.Dtos;

namespace Inventory.ConcurrencyTests;

// docs/test-plan.md 情境 C:長時間混合壓力測試。手動測試(Inventory.LoadTestClient)
// 用 60 秒模擬,這裡為了讓自動化測試能常態重複執行,把時長縮短到 5 秒、併發度降到
// 50——時間拉長不會改變這條斷言驗證的東西(對帳是否一致),只是讓重現機率更高,
// 縮短是刻意的取捨,不是規格變動。
//
// Baseline 階段的斷言方向跟情境 A/B 一致:斷言對帳「不一致」才算通過,因為
// lost update 天生就會讓 Product.Quantity 跟 InventoryTransaction 記錄的總和對不
// 上——每筆交易紀錄都誠實記下了「這次異動了多少」,但實際庫存值可能被併發的另一
// 個請求覆蓋掉,所以兩者必然對不上。等第 3 步做完某種鎖機制後,斷言要反過來(改
// 成斷言對帳「一致」)。
[Trait("Category", "Concurrency")]
public class ScenarioCTests
{
    private const int InitialQuantity = 500;
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private const int Concurrency = 50;

    [Fact]
    public async Task MixedStockInOut_UnderSustainedLoad_ReproducesReconciliationMismatch()
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
        var isDirty = expectedQuantity != finalProduct.Quantity;

        Assert.True(isDirty,
            $"預期在沒有併發控制的 baseline 上重現對帳不一致,但沒有偵測到。"
            + $"初始庫存 {InitialQuantity} ± 交易紀錄總和 = {expectedQuantity},目前 Quantity = {finalProduct.Quantity}。"
            + $"schema={schemaName}, transactionCount={transactions.Count}");
    }

    private static int SignedQuantity(TransactionDto transaction) =>
        transaction.ChangeType == "IN" ? transaction.Quantity : -transaction.Quantity;
}
