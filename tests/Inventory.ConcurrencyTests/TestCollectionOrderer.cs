using Xunit.Abstractions;

namespace Inventory.ConcurrencyTests;

// DisableTestParallelization (see AssemblyInfo.cs) only stops collections from
// running *simultaneously* — xUnit doesn't otherwise guarantee which order
// they run in. Sorting by display name pins it to A → B → C explicitly,
// since ScenarioATests/ScenarioBTests/ScenarioCTests already sort that way
// (same class-name prefix, so the trailing A/B/C decides the order).
public class AlphabeticalTestCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        testCollections.OrderBy(c => c.DisplayName, StringComparer.Ordinal);
}
