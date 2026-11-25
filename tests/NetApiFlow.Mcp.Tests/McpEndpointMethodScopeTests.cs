using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpEndpointMethodScopeTests
{
    private const int MethodNotFound = -32601;
    private const string NotAvailable = "is not available";

    private static readonly Dictionary<string, object?> MethodsOfTheApp = new()
    {
        ["resources/list"] = null,
        ["resources/templates/list"] = null,
        ["resources/read"] = new { uri = AppResources.SecretUri },
        ["resources/subscribe"] = new { uri = AppResources.SecretUri },
        ["resources/unsubscribe"] = new { uri = AppResources.SecretUri },
        ["prompts/list"] = null,
        ["prompts/get"] = new { name = AppPrompts.Name },
        ["completion/complete"] = new { @ref = new { type = "ref/prompt", name = AppPrompts.Name }, argument = new { name = "x", value = "a" } },
        [InventedMethod] = new { },
    };

    public static TheoryData<HttpServerSessionMode, string> ModesAndMethodsOfTheApp()
    {
        var data = new TheoryData<HttpServerSessionMode, string>();
        foreach (var mode in McpSessionModes.All)
        {
            foreach (var method in MethodsOfTheApp.Keys)
            {
                data.Add(mode, method);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModesAndMethodsOfTheApp))]
    public async Task NetApiFlowEndpoint_AnyMethodButItsTools_AnswersMethodNotFound(HttpServerSessionMode mode, string method)
    {
        await using var host = await McpAppHost.StartAsync(AppWithPromptsAndResources(mode));
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        var answer = await host.SendAsync("/flow/mcp", session, method, MethodsOfTheApp[method]);

        answer.ErrorCode.ShouldBe(MethodNotFound);
        answer.ErrorMessage.ShouldNotBeNull().ShouldContain(NotAvailable);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task AppEndpoint_StillServesItsPromptsResourcesAndCompletions(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(AppWithPromptsAndResources(mode));
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.SendAsync("/mcp", session, "resources/list", null)).Raw.ShouldContain(AppResources.SecretUri);
        (await host.SendAsync("/mcp", session, "resources/read", MethodsOfTheApp["resources/read"])).Raw.ShouldContain(AppResources.Secret);
        (await host.SendAsync("/mcp", session, "prompts/get", MethodsOfTheApp["prompts/get"])).Raw.ShouldContain(AppPrompts.Text);
        (await host.SendAsync("/mcp", session, "completion/complete", MethodsOfTheApp["completion/complete"])).Raw.ShouldContain(AppCompletion);
        (await host.SendAsync("/mcp", session, "resources/subscribe", MethodsOfTheApp["resources/subscribe"])).ErrorCode.ShouldBeNull();
        (await host.SendAsync("/mcp", session, InventedMethod, MethodsOfTheApp[InventedMethod])).Raw.ShouldContain(InventedAnswer);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task NetApiFlowEndpoint_StillAnswersPingAndLoggingLevel(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(AppWithPromptsAndResources(mode));
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        (await host.SendAsync("/flow/mcp", session, "ping", null)).ErrorCode.ShouldBeNull();
        (await host.SendAsync("/flow/mcp", session, "logging/setLevel", new { level = "warning" })).ErrorCode.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task WithTheOfficialClient_NetApiFlowEndpoint_RefusesThePromptsAndResourcesOfTheApp(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(AppWithPromptsAndResources(mode));
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");
        await using var app = await host.ConnectAsync("/mcp");

        (await RefusalAsync(() => netApiFlow.ListResourcesAsync(cancellationToken: CancellationToken.None).AsTask())).ShouldContain(NotAvailable);
        (await RefusalAsync(() => netApiFlow.ReadResourceAsync(AppResources.SecretUri, cancellationToken: CancellationToken.None).AsTask())).ShouldContain(NotAvailable);
        (await RefusalAsync(() => netApiFlow.ListPromptsAsync(cancellationToken: CancellationToken.None).AsTask())).ShouldContain(NotAvailable);
        (await RefusalAsync(() => netApiFlow.GetPromptAsync(AppPrompts.Name, cancellationToken: CancellationToken.None).AsTask())).ShouldContain(NotAvailable);
        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(netApiFlow)).ShouldBe(McpAppHost.NetApiFlowTools);
        (await app.ListResourcesAsync(cancellationToken: CancellationToken.None)).Select(resource => resource.Uri).ShouldContain(AppResources.SecretUri);
        (await app.ListPromptsAsync(cancellationToken: CancellationToken.None)).Select(prompt => prompt.Name).ShouldContain(AppPrompts.Name);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task SubscriptionsListenOnTheNewestRevision_IsRefusedOnTheNetApiFlowEndpointAndServedOnTheAppEndpoint(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(AppWithPromptsAndResources(mode));
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");
        await using var app = await host.ConnectAsync("/mcp");

        (await RefusalAsync(() => ListenAsync(netApiFlow))).ShouldContain(NotAvailable);
        await ListenAsync(app);
    }

    private const string AppCompletion = "APP-COMPLETION";
    private const string InventedMethod = "app/invented";
    private const string InventedAnswer = "APP-INVENTED";

    private static McpAppSetup AppWithPromptsAndResources(HttpServerSessionMode mode) => new()
    {
        SessionMode = mode,
        NetApiFlowRegisteredFirst = true,
        ConfigureAppServer = mcp => mcp
            .WithResources<AppResources>()
            .WithPrompts<AppPrompts>()
            .WithSubscribeToResourcesHandler((_, _) => ValueTask.FromResult(new EmptyResult()))
            .WithUnsubscribeFromResourcesHandler((_, _) => ValueTask.FromResult(new EmptyResult()))
            .WithSubscriptionsListenHandler((_, _) => ValueTask.FromResult(new EmptyResult()))
            .WithCompleteHandler((_, _) => ValueTask.FromResult(new CompleteResult { Completion = new Completion { Values = [AppCompletion] } }))
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest { Method: InventedMethod } request)
                {
                    await context.Server.SendMessageAsync(
                        new JsonRpcResponse { Id = request.Id, Result = new JsonObject { ["answer"] = InventedAnswer } },
                        cancellationToken);
                    return;
                }

                await next(context, cancellationToken);
            })),
    };

    private static Task<JsonRpcResponse> ListenAsync(McpClient client) =>
        client.SendRequestAsync(
            new JsonRpcRequest { Method = "subscriptions/listen", Params = JsonSerializer.SerializeToNode(new { notifications = new { } }) },
            CancellationToken.None);

    private static async Task<string> RefusalAsync(Func<Task> call) => (await Should.ThrowAsync<Exception>(call)).Message;
}
