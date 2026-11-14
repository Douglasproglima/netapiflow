using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

/// <summary>Verifies each storage package replaces the default in-memory <see cref="ITraceStore"/> and <see cref="IDeployAnnotationStore"/>, without needing a live database.</summary>
public class StorageRegistrationTests
{
    [Fact]
    public void AddNetApiFlowPostgreSqlStorage_ReplacesTheInMemoryDefault()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlow();
        services.AddNetApiFlowPostgreSqlStorage("Host=localhost;Database=x;Username=x;Password=x");

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ITraceStore>();
        var deployAnnotationStore = provider.GetRequiredService<IDeployAnnotationStore>();

        store.GetType().Namespace.ShouldBe("NetApiFlow.Storage.PostgreSql");
        deployAnnotationStore.GetType().Namespace.ShouldBe("NetApiFlow.Storage.PostgreSql");
    }

    [Fact]
    public void AddNetApiFlowSqlServerStorage_ReplacesTheInMemoryDefault()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlow();
        services.AddNetApiFlowSqlServerStorage("Server=localhost;Database=x;User Id=x;Password=x;Encrypt=false;TrustServerCertificate=true");

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ITraceStore>();
        var deployAnnotationStore = provider.GetRequiredService<IDeployAnnotationStore>();

        store.GetType().Namespace.ShouldBe("NetApiFlow.Storage.SqlServer");
        deployAnnotationStore.GetType().Namespace.ShouldBe("NetApiFlow.Storage.SqlServer");
    }
}
