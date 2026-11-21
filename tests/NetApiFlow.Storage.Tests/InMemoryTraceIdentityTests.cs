using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.Storage.Tests;

public sealed class InMemoryTraceIdentityTests : TraceIdentityTestsBase
{
    protected override void AddStorage(IServiceCollection services, string tablePrefix)
    {
    }
}
