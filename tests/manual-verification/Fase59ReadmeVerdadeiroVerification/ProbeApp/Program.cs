using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Marking;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using ProbeApp;

var builder = WebApplication.CreateBuilder(args);
var probe = builder.Configuration.GetSection("Probe");
var port = probe.GetValue("Port", 5059);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddOutputCache();
builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticatedHandler>("probe", null);
builder.Services.AddAuthorization();

#if NET10_0_OR_GREATER
if (probe.GetValue("OpenApi", false))
{
    builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>
        {
            ["bearerAuth"] = new Microsoft.OpenApi.OpenApiSecurityScheme { Type = Microsoft.OpenApi.SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" },
        };
        document.Security =
        [
            new Microsoft.OpenApi.OpenApiSecurityRequirement { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearerAuth", document)] = [] },
        ];
        return Task.CompletedTask;
    }));
}
#endif

builder.Services.AddNetApiFlow(options =>
{
    options.Enabled = probe.GetValue("Enabled", true);
    options.CapturePrePipelineSteps = probe.GetValue("CapturePrePipelineSteps", false);
    if (probe["ExcludePath"] is { Length: > 0 } excludedPath)
    {
        options.ExcludePaths(excludedPath);
    }

    if (probe["ExternalSources"] is { Length: > 0 } externalSources)
    {
        options.ExternalSources = Enum.Parse<ExternalActivitySource>(externalSources);
    }

    if (probe["Retention:MaxAgeSeconds"] is { Length: > 0 } maxAgeSeconds)
    {
        options.Retention.MaxAge = TimeSpan.FromSeconds(int.Parse(maxAgeSeconds));
    }
});
builder.Services.AddNetApiFlowDashboard();
if (probe.GetValue("Tenant", false))
{
    builder.Services.AddSingleton<IFlowTenantResolver, HeaderTenantResolver>();
}

builder.Services.AddNetApiFlowMcp();

if (probe.GetValue("HostSamplerOff", false))
{
    builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.SetSampler(new AlwaysOffSampler()));
}

#if PROBE_INSTRUMENTATION
if (probe.GetValue("AspNetCoreInstrumentation", false))
{
    builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(options =>
    {
        if (probe.GetValue("InstrumentationFilter", false))
        {
            options.Filter = context => !context.Request.Path.StartsWithSegments("/flow");
        }
    }));
}
#endif

if (probe.GetValue("Otlp", false))
{
    builder.Services.AddNetApiFlowOtlpExport(options =>
    {
        options.IncludeHttpClientSource = probe.GetValue("IncludeHttpClientSource", false);
        options.ConfigureOtlpExporter = exporter =>
        {
            exporter.Endpoint = new Uri(probe["OtlpEndpoint"] ?? "http://127.0.0.1:4318/v1/traces");
            exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
            exporter.ExportProcessorType = ExportProcessorType.Simple;
        };
    });
}

builder.Services.AddFlowTracked<IItemFeed, ItemFeed>(ComponentType.Service);
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
if (probe["Decorate"] is "predicate")
{
    builder.Services.DecorateFlowTracked(
        serviceType => ProbeDecoration.Observe(serviceType),
        ComponentType.Repository);
}

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

#if NET10_0_OR_GREATER
if (probe.GetValue("OpenApi", false))
{
    app.MapOpenApi();
}
#endif

app.MapNetApiFlowDashboard();
app.MapNetApiFlowMcp();
app.MapControllers();

app.MapGet("/hello", () => "Hello");
app.MapGet("/cached-minimal", () => DateTime.UtcNow.ToString("O")).CacheOutput();
app.MapGet("/target", () => "target");

app.MapGet("/outbound", async (IHttpClientFactory clients, HttpContext context) =>
{
    context.Response.Headers["X-Probe-TraceId"] = Flow.CurrentTraceId;
    using var client = clients.CreateClient();
    using var response = await client.GetAsync($"http://127.0.0.1:{port}/target?token=SECRET-QUERY-123#fragment");
    return (int)response.StatusCode;
});

app.MapGet("/suppress", async () =>
{
    Task suppressedWork;
    using (ExecutionContext.SuppressFlow())
    {
        suppressedWork = Task.Run(() =>
        {
            using var step = Flow.Step("Suppressed", "Inside");
        });
    }

    await suppressedWork;
    return "done";
});

app.MapGet("/manual-throw", () =>
{
    using var step = Flow.Step("Manual", "Throws");
    throw new InvalidOperationException("manual-step-threw");
});

app.MapGet("/iae", (IItemFeed feed) => feed.StreamAsync());

app.MapPost("/body", async (HttpContext context) =>
{
    var body = await context.Request.ReadFromJsonAsync<Dictionary<string, string>>();
    return int.Parse(body!["quantity"]);
});

app.MapGet("/trace-of", (IServiceProvider services, string id) =>
{
    var trace = Flow.GetRecentTrace(services, id);
    return trace is null ? Results.NotFound() : Results.Json(trace);
});

app.MapGet("/orders/{id:int}", (int id, IOrderService orders) => orders.Describe(id));

app.Run();

namespace ProbeApp
{
    public sealed class AlwaysAuthenticatedHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new System.Security.Claims.ClaimsIdentity("probe");
            var ticket = new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), "probe");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CustomAuthorizationFilterAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
        }
    }

    [ApiController]
    [Route("pre")]
    public sealed class PreController : ControllerBase
    {
        [HttpGet("plain")]
        public string Plain() => "plain";

        [Authorize(AuthenticationSchemes = "probe")]
        [HttpGet("authorize")]
        public string Authorized() => "authorized";

        [CustomAuthorizationFilter]
        [HttpGet("custom")]
        public string Custom() => "custom";

        [OutputCache(Duration = 60)]
        [HttpGet("cached")]
        public string Cached() => DateTime.UtcNow.ToString("O");
    }

    public sealed class HeaderTenantResolver : IFlowTenantResolver
    {
        public string? Resolve(HttpContext context) => context.Request.Headers["X-Tenant"].FirstOrDefault();
    }

    public interface IItemFeed
    {
        IAsyncEnumerable<int> StreamAsync();
    }

    public sealed class ItemFeed : IItemFeed
    {
        public async IAsyncEnumerable<int> StreamAsync()
        {
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("enumeration-fails");
        }
    }

    public interface IOrderRepository
    {
        string Find(int id);
    }

    public sealed class OrderRepository : IOrderRepository
    {
        public string Find(int id) => $"order-{id}";
    }

    public interface IOrderService
    {
        string Describe(int id);
    }

    public sealed class OrderService(IOrderRepository repository) : IOrderService
    {
        public string Describe(int id) => repository.Find(id);
    }

    public static class ProbeDecoration
    {
        public static bool Observe(Type serviceType)
        {
            Console.WriteLine($"DECORATE-PREDICATE serviceType={serviceType.FullName}");
            return serviceType.Name.EndsWith("Repository", StringComparison.Ordinal);
        }
    }
}
