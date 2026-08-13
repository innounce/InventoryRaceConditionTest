using Xunit;

// docs/test-plan.md「測試執行順序建議」explicitly runs scenarios A → B → C one
// at a time. Letting xUnit's default cross-class parallelism run them
// simultaneously isn't just a deviation from that — it also makes every
// scenario's own numbers unreliable, since they'd be contending for the same
// PostgreSQL instance's connection slots and CPU at once.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
