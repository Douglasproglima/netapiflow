using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class OpeningTagsTests
{
    [Fact]
    public async Task Trace_WhenTheTenantResolverThrows_IsStillRecordedWithTheW3CTraceIdAndWithoutTheTenant()
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Services = services => services.AddSingleton<IFlowTenantResolver, ThrowingTenantResolver>(),
            Map = endpoints => endpoints.MapGet("/orders", () => "ok"),
        });
        var w3cTraceId = ActivityTraceId.CreateRandom().ToHexString();

        var response = await app.GetWithTraceIdAsync("/orders", w3cTraceId);
        var stored = await app.StoredAsync(response);

        response.EnsureSuccessStatusCode();
        stored.Trace.Tags["w3c.trace_id"].ShouldBe(w3cTraceId);
        stored.Trace.Tags.ContainsKey("tenant").ShouldBeFalse();
    }

    private sealed class ThrowingTenantResolver : IFlowTenantResolver
    {
        public string? Resolve(HttpContext context) => throw new InvalidOperationException("the tenant resolver of the user failed");
    }
}
