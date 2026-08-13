using System.Diagnostics;
using Inventory.LoadTestClient;

var scenario = GetArgValue(args, "--scenario")?.ToUpperInvariant();
var baseUrl = GetArgValue(args, "--base-url") ?? "http://localhost:5279";

if (scenario is not ("A" or "B" or "C"))
{
    Console.WriteLine("用法:dotnet run -- --scenario A|B|C [--base-url http://localhost:5080]");
    return 1;
}

using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
var apiClient = new ApiClient(httpClient);

await (scenario switch
{
    "A" => Scenarios.RunScenarioA(apiClient),
    "B" => Scenarios.RunScenarioB(apiClient),
    "C" => Scenarios.RunScenarioC(apiClient),
    _ => throw new UnreachableException()
});

return 0;

static string? GetArgValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
