using Inventory.LoadTestClient;

var scenario = GetArgValue(args, "--scenario")?.ToUpperInvariant();
var baseUrl = GetArgValue(args, "--base-url") ?? "http://localhost:5279";

if (scenario is not ("A" or "B" or "C" or "ALL"))
{
    Console.WriteLine("用法:dotnet run -- --scenario A|B|C|ALL [--base-url http://localhost:5279]");
    Console.WriteLine("ALL 會依序跑完 A → B → C,確認前一個完全結束(含 CSV 寫檔)才開始下一個,避免三個情境互搶連線。");
    return 1;
}

using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
var apiClient = new ApiClient(httpClient);

if (scenario == "ALL")
{
    // 刻意用單一 await 鏈依序執行,不是 Task.WhenAll——每個情境都會建立自己的
    // 商品、打滿併發、寫完報告之後,才輪到下一個情境起跑,避免三個情境同時佔用
    // 同一個 Npgsql 連線池,把訊號弄髒(見 docs/test-plan.md「共通測試環境準備」)。
    await RunWithHeader("A", () => Scenarios.RunScenarioA(apiClient));
    await RunWithHeader("B", () => Scenarios.RunScenarioB(apiClient));
    await RunWithHeader("C", () => Scenarios.RunScenarioC(apiClient));
    return 0;
}

await (scenario switch
{
    "A" => Scenarios.RunScenarioA(apiClient),
    "B" => Scenarios.RunScenarioB(apiClient),
    "C" => Scenarios.RunScenarioC(apiClient),
    _ => throw new InvalidOperationException()
});

return 0;

static async Task RunWithHeader(string label, Func<Task> run)
{
    Console.WriteLine();
    Console.WriteLine($"########## 開始情境 {label} ##########");
    await run();
    Console.WriteLine($"########## 情境 {label} 結束 ##########");
}

static string? GetArgValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
