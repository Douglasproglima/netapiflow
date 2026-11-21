using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace NetApiFlow.TestSupport;

public class HostConfigurationWatchersTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ADefaultHostBuiltByTheSuite_WatchesNoConfigurationFile(string environment)
    {
        using var host = Host.CreateDefaultBuilder()
            .UseEnvironment(environment)
            .Build();

        WatchedFileProviders(host.Services.GetRequiredService<IConfiguration>()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void AWebApplicationBuiltByTheSuite_WatchesNoConfigurationFile(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });

        using var app = builder.Build();

        WatchedFileProviders(app.Configuration).ShouldBeEmpty();
    }

    private static List<string> WatchedFileProviders(IConfiguration configuration) =>
        ((IConfigurationRoot)configuration).Providers
            .OfType<FileConfigurationProvider>()
            .Where(provider => provider.Source.ReloadOnChange)
            .Select(provider => provider.Source.Path ?? provider.GetType().Name)
            .ToList();
}
