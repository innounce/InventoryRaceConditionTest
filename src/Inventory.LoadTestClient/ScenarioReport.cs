namespace Inventory.LoadTestClient;

// Prints the "共通分析方法" checklist from docs/test-plan.md after every scenario run.
public static class ScenarioReport
{
    public static void Print(
        string scenarioName,
        int initialQuantity,
        IReadOnlyList<ApiResult<StockChangeResponse>> results,
        ProductDto finalProduct,
        IReadOnlyList<TransactionDto> transactions)
    {
        Console.WriteLine();
        Console.WriteLine($"===== 情境 {scenarioName} 報告 =====");

        var successResults = results.Where(r => r.StatusCode == 200).ToList();
        var failureGroups = results.Where(r => r.StatusCode != 200)
            .GroupBy(r => r.Error?.Error ?? $"HTTP {results.First().StatusCode}")
            .ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine($"總請求數:{results.Count}");
        Console.WriteLine($"成功請求數:{successResults.Count}");
        Console.WriteLine($"失敗請求數:{results.Count - successResults.Count}");
        foreach (var (errorCode, count) in failureGroups)
            Console.WriteLine($"  失敗原因 {errorCode}:{count} 次");

        Console.WriteLine($"最終 Quantity:{finalProduct.Quantity}");
        Console.WriteLine($"最終 Version:{finalProduct.Version}(成功請求數 {successResults.Count})");
        if (finalProduct.Version < successResults.Count)
            Console.WriteLine("  ⚠ Version 小於成功請求數,代表有寫入被覆蓋(lost update)");

        var orderedTransactions = transactions.OrderBy(t => t.CreatedAt).ToList();
        Console.WriteLine($"InventoryTransaction 筆數:{orderedTransactions.Count}(成功請求數 {successResults.Count})");
        if (orderedTransactions.Count != successResults.Count)
            Console.WriteLine("  ⚠ 交易紀錄筆數跟成功請求數對不上,可能有漏寫");

        var duplicateBalances = orderedTransactions
            .GroupBy(t => t.BalanceAfter)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateBalances.Count > 0)
            Console.WriteLine($"  ⚠ balanceAfter 出現重複值:{string.Join(", ", duplicateBalances)}(兩個以上請求讀到同一個舊庫存值去計算)");

        var lastBalance = orderedTransactions.LastOrDefault()?.BalanceAfter;
        if (lastBalance is not null && lastBalance != finalProduct.Quantity)
            Console.WriteLine($"  ⚠ 最後一筆 balanceAfter({lastBalance}) 跟目前 Quantity({finalProduct.Quantity}) 不一致");

        var expectedQuantity = initialQuantity + orderedTransactions.Sum(t => t.ChangeType == "IN" ? t.Quantity : -t.Quantity);
        Console.WriteLine($"對帳:初始庫存 {initialQuantity} ± 交易紀錄總和 = {expectedQuantity},目前 Quantity = {finalProduct.Quantity}"
            + (expectedQuantity == finalProduct.Quantity ? "(一致)" : "  ⚠ 對帳不一致"));

        if (results.Count > 0)
        {
            var latencies = results.Select(r => r.Latency.TotalMilliseconds).OrderBy(x => x).ToList();
            var durationSeconds = (results.Max(r => r.SentAt) - results.Min(r => r.SentAt)).TotalSeconds;
            var throughput = durationSeconds > 0 ? results.Count / durationSeconds : results.Count;
            Console.WriteLine($"Throughput:約 {throughput:F1} req/s");
            Console.WriteLine($"Latency p50/p95/p99(ms):{Percentile(latencies, 0.50):F1} / {Percentile(latencies, 0.95):F1} / {Percentile(latencies, 0.99):F1}");
        }
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}
