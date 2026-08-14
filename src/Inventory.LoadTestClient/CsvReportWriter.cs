namespace Inventory.LoadTestClient;

// The console report (ScenarioReport) is for eyeballing right after a run;
// this is the durable artifact — a full dump of the one product's row and
// all of its transactions, written once at the end instead of leaving
// per-request SQL trace noise in the log.
public static class CsvReportWriter
{
    public static string Write(string scenarioLabel, ProductDto product, IReadOnlyList<TransactionDto> transactions)
    {
        var safeLabel = scenarioLabel.Split(' ')[0];
        var dir = Path.Combine(RepoRootReportsDir(), $"{safeLabel}-{DateTime.UtcNow:yyyyMMddHHmmss}");
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

        return dir;
    }

    // A bare relative "reports" path isn't reliable — it depends on whatever
    // directory the process happened to be launched from. Anchor to the repo
    // root instead, computed from the running assembly's own location
    // (bin/Debug/net9.0/ under this project, five levels below repo root).
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
