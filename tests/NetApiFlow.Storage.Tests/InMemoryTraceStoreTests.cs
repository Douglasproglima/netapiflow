namespace NetApiFlow.Storage.Tests;

public sealed class InMemoryTraceStoreTests : TraceStoreTestsBase
{
    protected override ITraceStore CreateStore() => new InMemoryTraceStore();
}
