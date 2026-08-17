using Inventory.Api.Dtos;

namespace Inventory.ConcurrencyTests;

// Companion to the kept-around test schema (see TestSchema.cs): a CSV dump of
// the one product's row and all of its transactions, written once at the end
// of each test so you can check what happened without opening a DB client —
// written regardless of whether the assertion passes or fails.
public static class CsvExport
{
    public static string Write(
        string scenarioLabel,
        string schemaName,
        ProductDto product,
        IReadOnlyList<TransactionDto> transactions,
        IReadOnlyList<(bool IsSuccess, TimeSpan Elapsed)> requestStats)
    {
        var dir = Path.Combine(RepoRootReportsDir(), $"{scenarioLabel}-{schemaName}");
        Directory.CreateDirectory(dir);

        File.WriteAllLines(Path.Combine(dir, "product.csv"),
        [
            "id,sku,name,quantity,version,createdAt,updatedAt",
            $"{product.Id},{Escape(product.Sku)},{Escape(product.Name)},{product.Quantity},{product.Version},{product.CreatedAt:o},{product.UpdatedAt:o}"
        ]);

        var transactionLines = new List<string> { "id,productId,changeType,quantity,balanceAfter,createdAt" };
        transactionLines.AddRange(transactions.Select(t =>
            $"{t.Id},{t.ProductId},{t.ChangeType},{t.Quantity},{t.BalanceAfter},{t.CreatedAt:o}"));
        File.WriteAllLines(Path.Combine(dir, "transactions.csv"), transactionLines);

        WriteSummary(dir, scenarioLabel, requestStats);

        return dir;
    }

    private static void WriteSummary(string dir, string scenario, IReadOnlyList<(bool IsSuccess, TimeSpan Elapsed)> stats)
    {
        var total = stats.Count;
        var successCount = stats.Count(s => s.IsSuccess);
        var allMs = stats.Select(s => s.Elapsed.TotalMilliseconds).OrderBy(x => x).ToList();
        var successMs = stats.Where(s => s.IsSuccess).Select(s => s.Elapsed.TotalMilliseconds).OrderBy(x => x).ToList();

        var p50 = Percentile(allMs, 50);
        var p99 = Percentile(allMs, 99);
        var minSuccess = successMs.Count > 0 ? successMs[0] : 0;
        var avgSuccess = successMs.Count > 0 ? successMs.Average() : 0;

        File.WriteAllLines(Path.Combine(dir, "summary.csv"),
        [
            "metric,value",
            $"scenario,{scenario}",
            $"totalRequests,{total}",
            $"successCount,{successCount}",
            $"failureCount,{total - successCount}",
            $"successRatePct,{(total > 0 ? successCount * 100.0 / total : 0):F1}",
            $"p50LatencyMs,{p50:F1}",
            $"p99LatencyMs,{p99:F1}",
            $"minSuccessLatencyMs,{minSuccess:F1}",
            $"avgSuccessLatencyMs,{avgSuccess:F1}",
        ]);
    }

    private static double Percentile(List<double> sorted, int pct)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(pct / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    // dotnet test runs the test host with its working directory set to the
    // build output folder (bin/Debug/net9.0/), not the project or repo root —
    // a bare relative "reports" path would land buried inside bin/ and vanish
    // on the next clean build. Anchor to the repo root instead, computed from
    // this assembly's own location (five levels below repo root, same as
    // Inventory.LoadTestClient's CsvReportWriter).
    private static string RepoRootReportsDir()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "reports");
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
