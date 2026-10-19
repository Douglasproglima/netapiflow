using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests;

public class NetApiFlowServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNetApiFlow_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddNetApiFlow();
        services.AddNetApiFlow();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Count().ShouldBe(3);
        provider.GetServices<IStartupFilter>().Count().ShouldBe(1);
        provider.GetRequiredService<FlowRecorder>().ShouldNotBeNull();
    }
}
