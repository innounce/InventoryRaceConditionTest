namespace Inventory.LoadTestClient;

// Parameters and flow for each scenario mirror docs/test-plan.md exactly.
public static class Scenarios
{
    public static async Task RunScenarioA(ApiClient client)
    {
        const int initialQuantity = 1000;
        const int requestCount = 1000;

        var product = await client.CreateProductAsync($"LOAD-TEST-A-{DateTime.UtcNow:HHmmssfff}", "情境 A 測試商品", initialQuantity);
        Console.WriteLine($"建立商品 {product.Id},初始庫存 {initialQuantity}");

        var results = await ConcurrentDispatcher.RunBurstAsync(requestCount, _ => client.StockOutAsync(product.Id, 1));

        await ReportAsync(client, "A - Lost Update 驗證", product.Id, initialQuantity, results);
    }

    public static async Task RunScenarioB(ApiClient client)
    {
        const int initialQuantity = 100;
        const int requestCount = 1000;

        var product = await client.CreateProductAsync($"LOAD-TEST-B-{DateTime.UtcNow:HHmmssfff}", "情境 B 測試商品", initialQuantity);
        Console.WriteLine($"建立商品 {product.Id},初始庫存 {initialQuantity}");

        var results = await ConcurrentDispatcher.RunBurstAsync(requestCount, _ => client.StockOutAsync(product.Id, 1));

        await ReportAsync(client, "B - 負庫存驗證", product.Id, initialQuantity, results);
    }

    public static async Task RunScenarioC(ApiClient client)
    {
        const int initialQuantity = 500;
        var duration = TimeSpan.FromSeconds(60);
        const int concurrency = 100;

        var product = await client.CreateProductAsync($"LOAD-TEST-C-{DateTime.UtcNow:HHmmssfff}", "情境 C 測試商品", initialQuantity);
        Console.WriteLine($"建立商品 {product.Id},初始庫存 {initialQuantity},開始跑 {duration.TotalSeconds} 秒混合壓力測試...");

        var random = new Random();
        var results = await ConcurrentDispatcher.RunSustainedAsync(duration, concurrency, () =>
        {
            var quantity = random.Next(1, 6);
            return random.NextDouble() < 0.6
                ? client.StockOutAsync(product.Id, quantity)
                : client.StockInAsync(product.Id, quantity);
        });

        await ReportAsync(client, "C - 長時間混合壓力測試", product.Id, initialQuantity, results);
    }

    private static async Task ReportAsync(
        ApiClient client, string scenarioName, Guid productId, int initialQuantity,
        List<ApiResult<StockChangeResponse>> results)
    {
        var finalProduct = await client.GetProductAsync(productId);
        var transactions = await client.GetTransactionsAsync(productId);
        ScenarioReport.Print(scenarioName, initialQuantity, results, finalProduct, transactions);

        var reportDir = CsvReportWriter.Write(scenarioName, finalProduct, transactions);
        Console.WriteLine($"CSV 報告已輸出到:{reportDir}");
    }
}
