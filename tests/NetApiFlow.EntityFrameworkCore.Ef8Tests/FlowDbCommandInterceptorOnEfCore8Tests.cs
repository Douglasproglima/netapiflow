using System.Net;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetApiFlow.Domain;
using NetApiFlow.Testing;
using Shouldly;
using Xunit;

namespace NetApiFlow.EntityFrameworkCore.Ef8Tests;

public class FlowDbCommandInterceptorOnEfCore8Tests
{
    private const string DeclaredEfCoreFloor = "8.0.10";

    [Fact]
    public void ResolvedEfCore_InThisProject_IsTheDeclaredFloor()
    {
        var resolved = typeof(DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        resolved.ShouldStartWith(DeclaredEfCoreFloor);
    }

    [Fact]
    public async Task Query_OnTheFloorEfCore_RecordsOneEntityFrameworkCoreDatabaseStep()
    {
        using var host = new EfCore8Host();

        var response = await host.Client.GetAsync("/query");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var databaseSteps = DatabaseStepsOf(response, host);
        databaseSteps.Count.ShouldBe(1);
        databaseSteps[0].Component.ShouldBe("EntityFrameworkCore");
        databaseSteps[0].Operation.ShouldStartWith("SELECT");
        databaseSteps[0].Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public async Task SaveChanges_OnTheFloorEfCore_RecordsTheInsertAsAnEntityFrameworkCoreDatabaseStep()
    {
        using var host = new EfCore8Host();

        var response = await host.Client.PostAsync("/insert", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var databaseSteps = DatabaseStepsOf(response, host);
        databaseSteps.Count.ShouldBe(1);
        databaseSteps[0].Component.ShouldBe("EntityFrameworkCore");
        databaseSteps[0].Operation.ShouldStartWith("INSERT");
        databaseSteps[0].Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public async Task Query_WithTheInterceptorAttachedTwice_RecordsOneDatabaseStepPerCommand()
    {
        using var host = new EfCore8Host();

        var response = await host.Client.GetAsync("/query-double-attached");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var databaseSteps = DatabaseStepsOf(response, host);
        databaseSteps.Count.ShouldBe(1);
        databaseSteps[0].Status.ShouldBe(FlowStatus.Success);
    }

    private static IReadOnlyList<FlowSpan> DatabaseStepsOf(HttpResponseMessage response, EfCore8Host host) =>
        [.. response.GetFlowTrace(host.Services).Spans.Where(span => span.Type == ComponentType.Database)];
}
