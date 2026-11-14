namespace NetApiFlow.Storage.Tests;

public sealed class InMemoryDeployAnnotationStoreTests : DeployAnnotationStoreTestsBase
{
    protected override IDeployAnnotationStore CreateStore() => new InMemoryDeployAnnotationStore();
}
