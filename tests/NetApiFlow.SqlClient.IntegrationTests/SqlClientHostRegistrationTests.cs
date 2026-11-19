using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;

namespace NetApiFlow.SqlClient.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class SqlClientHostRegistrationTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Development_WithNoOptionsStated_RecordsTheRawSqlCommand()
    {
        using var host = await StartHostAsync(Environments.Development);

        var spans = await RunRawCommandUnderATraceAsync(host);

        spans.ShouldHaveSingleItem().Component.ShouldBe("SqlClient");
    }

    [Fact]
    public async Task Production_WithSqlClientOnlyInConfiguration_RecordsTheRawSqlCommand()
    {
        using var host = await StartHostAsync(
            Environments.Production,
            new Dictionary<string, string?> { ["NetApiFlow:ExternalSources"] = "SqlClient" });

        var spans = await RunRawCommandUnderATraceAsync(host);

        spans.ShouldHaveSingleItem().Component.ShouldBe("SqlClient");
    }

    [Fact]
    public async Task Development_WithNoneStatedInCode_NeitherSubscribesNorRecords()
    {
        using var host = await StartHostAsync(
            Environments.Development,
            configureOptions: options => options.ExternalSources = ExternalActivitySource.None);

        host.Services.GetRequiredService<SqlClientCommandListener>().IsSubscribed.ShouldBeFalse();
        (await RunRawCommandUnderATraceAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Development_WithNoneStatedInConfiguration_NeitherSubscribesNorRecords()
    {
        using var host = await StartHostAsync(
            Environments.Development,
            new Dictionary<string, string?> { ["NetApiFlow:ExternalSources"] = "None" });

        host.Services.GetRequiredService<SqlClientCommandListener>().IsSubscribed.ShouldBeFalse();
        (await RunRawCommandUnderATraceAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Production_WithNothingStated_NeitherSubscribesNorRecords()
    {
        using var host = await StartHostAsync(Environments.Production);

        host.Services.GetRequiredService<SqlClientCommandListener>().IsSubscribed.ShouldBeFalse();
        (await RunRawCommandUnderATraceAsync(host)).ShouldBeEmpty();
    }

    private static async Task<IHost> StartHostAsync(
        string environmentName,
        Dictionary<string, string?>? configuration = null,
        Action<NetApiFlowOptions>? configureOptions = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environmentName });
        builder.Configuration.AddInMemoryCollection(configuration ?? []);
        builder.Services.AddNetApiFlow(configureOptions);

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private async Task<List<FlowSpan>> RunRawCommandUnderATraceAsync(IHost host)
    {
        var recorder = host.Services.GetRequiredService<FlowRecorder>();

        using var root = new Activity("SqlClientHostRegistrationRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Id FROM Users", connection);
        await command.ExecuteScalarAsync();

        return [.. recorder.GetActiveTrace(traceId)!.Spans.Where(span => span.Type == ComponentType.Database)];
    }
}
