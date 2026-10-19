using Xunit;

// ActivitySource/ActivityListener registration is process-wide static state: running test classes
// in parallel would let one test's listener observe another test's activities.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
