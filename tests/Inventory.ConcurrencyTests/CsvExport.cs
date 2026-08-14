using Inventory.Api.Dtos;

namespace Inventory.ConcurrencyTests;

// Companion to the kept-around test schema (see TestSchema.cs): a CSV dump of
// the one product's row and all of its transactions, written once at the end
// of each test so you can check what happened without opening a DB client —
// written regardless of whether the assertion passes or fails.
public static class CsvExport
{
    public static string Write(string scenarioLabel, string schemaName, ProductDto product, IReadOnlyList<TransactionDto> transactions)
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

        return dir;
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
