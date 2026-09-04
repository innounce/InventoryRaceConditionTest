using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Inventory.ConcurrencyTests;

// Every test gets its own schema (test_yyyyMMddHHmmssfff_xxxxxxxx) inside the
// *same* real PostgreSQL database the API normally runs against — no
// containers, and the schema is deliberately left behind after the test for
// manual inspection. See docs/test-plan.md "自動化測試腳本設計" for why.
//
// The random suffix matters: xUnit runs separate test classes in parallel by
// default, so two schemas created in the same millisecond are not just
// theoretically possible but routinely observed — a bare millisecond
// timestamp collided in practice during development.
public static class TestSchema
{
    public static async Task<(WebApplicationFactory<Program> Factory, string SchemaName)> CreateAsync()
    {
        var baseConnectionString = BuildBaseConnectionString();

        var schemaName = $"test_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(baseConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schemaName}\";";
            await command.ExecuteNonQueryAsync();
        }

        var scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        }.ConnectionString;

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Both descriptors have to go — removing only DbContextOptions<T>
                // leaves the original InventoryDbContext registration in place,
                // and it still builds its own options internally rather than
                // resolving DbContextOptions<T> from DI, so the override was
                // silently ignored (confirmed by watching tables land in
                // "public" instead of the test schema during development).
                services.RemoveAll<DbContextOptions<InventoryDbContext>>();
                services.RemoveAll<InventoryDbContext>();
                services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(scopedConnectionString));
            });
        });

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            // EnsureCreated() decides whether to create tables by checking if the
            // *database* has any tables at all, ignoring schema/search_path — since
            // the API's own "public" schema already has Product/InventoryTransaction,
            // EnsureCreated() silently no-ops for every new test schema too. Migrate()
            // doesn't have that shortcut: it tracks applied migrations via
            // __EFMigrationsHistory, which (being an unqualified table name, same as
            // Product/InventoryTransaction) gets created fresh inside whichever schema
            // the connection's search_path points to.
            await dbContext.Database.MigrateAsync();
        }

        return (factory, schemaName);
    }

    // Reads Inventory.Api's own appsettings.json/user-secrets directly, without
    // spinning up a throwaway WebApplicationFactory just to read configuration
    // — an earlier version did that, and disposing the throwaway factory (even
    // though it was a *separate* instance from the one returned to the test)
    // tore down the TestServer the real factory ended up depending on.
    private static string BuildBaseConnectionString()
    {
        var apiAssembly = typeof(Program).Assembly;
        var apiProjectDirectory = FindApiProjectDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(apiAssembly)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("找不到 ConnectionStrings:Default,請確認 appsettings/user-secrets 是否已設定");
    }

    private static string FindApiProjectDirectory()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Inventory.Api");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("找不到 src/Inventory.Api 目錄");
    }
}
