using Microsoft.AspNetCore.Builder;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using Flow = NetApiFlow.NetApiFlow;

var variant = Environment.GetEnvironmentVariable("COMPARISON_VARIANT") ?? "default";
var reExecutesStatusCodes = variant is "exclude-internal" or "exclude-error";
var reExecutesExceptions = variant is "handler-exclude-internal" or "handler-exclude-error";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddNetApiFlow(options =>
{
    switch (variant)
    {
        case "exclude-internal":
        case "handler-exclude-internal":
            options.ExcludePaths("/internal/**", "/__probe/**");
            break;
        case "exclude-error":
            options.ExcludePaths("/error/**", "/__probe/**");
            break;
        case "handler-exclude-error":
            options.ExcludePaths("/error-handler", "/__probe/**");
            break;
        case "include":
            options.IncludePaths("/orders/**", "/boom", "/control");
            break;
        case "pathbase":
            options.ExcludePaths("/health");
            break;
        default:
            options.ExcludePaths("/__probe/**");
            break;
    }
});
builder.Services.AddNetApiFlowDashboard();

var app = builder.Build();

if (variant == "pathbase")
{
    app.UsePathBase("/api");
}

if (reExecutesStatusCodes)
{
    app.UseStatusCodePagesWithReExecute("/error/{0}");
}

if (reExecutesExceptions)
{
    app.UseExceptionHandler("/error-handler");
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.Value!.EndsWith("/crash", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The request failed before routing.");
        }

        await next();
    });
}

app.UseStaticFiles(new StaticFileOptions { FileProvider = new InMemoryFiles("site.css", "body { color: black; }") });

app.Map("/terminal", branch => branch.Run(async context =>
{
    var gate = Gates.Of("terminal");
    using (Flow.Step("TerminalWork", "Hold"))
    {
        gate.Inside.TrySetResult();
        await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    await context.Response.WriteAsync("done");
}));

app.UseNetApiFlowDashboard();
app.MapControllers();

app.MapGet("/hello", () => "Hello");
app.MapGet("/control", () => "control");
app.MapGet("/health", () => "ok");
app.MapGet("/internal/ping", () => "pong");
app.MapGet("/orders/{id:int}", (int id) => $"order-{id}");
app.MapGet("/boom", string () => throw new InvalidOperationException("The endpoint failed on purpose."));
app.MapGet("/error/{code}", (string code) => $"error {code}");
app.MapGet("/error-handler", () => "error page");

app.MapGet("/__probe/reset/{name}", (string name) =>
{
    Gates.Reset(name);
    return "reset";
});
app.MapGet("/__probe/inside/{name}", async (string name) =>
{
    await Gates.Of(name).Inside.Task.WaitAsync(TimeSpan.FromSeconds(20));
    return "inside";
});
app.MapGet("/__probe/release/{name}", (string name) =>
{
    Gates.Of(name).Release.TrySetResult();
    return "released";
});

app.Run();
