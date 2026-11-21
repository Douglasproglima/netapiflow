using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetApiFlow.TestSupport;

public enum MatrixOutcome
{
    Open,
    Refused404,
    Refused403,
    Refused401,
}

public enum MatrixCaller
{
    HasTheClaim,
    LacksTheClaim,
    NotSignedIn,
}

public enum MatrixConvention
{
    None,
    RequireAuthorization,
    AllowAnonymous,
    RequireThenAllowAnonymous,
    AllowAnonymousThenRequire,
    AllowAnonymousAttributeThenRequire,
    AllowAnonymousOnTheGroupThenRequire,
}

public enum MatrixFilter
{
    None,
    Allows,
    Denies,
}

public sealed record AuthorizationMatrixCase(
    string Name,
    string Environment,
    MatrixConvention Convention,
    MatrixCaller Caller,
    MatrixFilter Filter,
    MatrixOutcome Expected);

public sealed record MatrixRequest(string Method, string Path, bool HasRouteParameters);

public sealed record MatrixAnswer(MatrixRequest Request, int Status);

public static class AuthorizationMatrix
{
    public const string Development = "Development";
    public const string Production = "Production";

    private const string SchemeName = "MatrixScheme";
    private const string Policy = "RequiresAdmin";

    public static readonly IReadOnlyList<AuthorizationMatrixCase> Cases =
    [
        new("development, nothing configured", Development, MatrixConvention.None, MatrixCaller.NotSignedIn, MatrixFilter.None, MatrixOutcome.Open),
        new("production, nothing configured", Production, MatrixConvention.None, MatrixCaller.NotSignedIn, MatrixFilter.None, MatrixOutcome.Refused404),
        new("production, RequireAuthorization, caller has the claim", Production, MatrixConvention.RequireAuthorization, MatrixCaller.HasTheClaim, MatrixFilter.None, MatrixOutcome.Open),
        new("production, RequireAuthorization, caller lacks the claim", Production, MatrixConvention.RequireAuthorization, MatrixCaller.LacksTheClaim, MatrixFilter.None, MatrixOutcome.Refused403),
        new("production, RequireAuthorization, caller not signed in", Production, MatrixConvention.RequireAuthorization, MatrixCaller.NotSignedIn, MatrixFilter.None, MatrixOutcome.Refused401),
        new("development, RequireAuthorization, caller lacks the claim", Development, MatrixConvention.RequireAuthorization, MatrixCaller.LacksTheClaim, MatrixFilter.None, MatrixOutcome.Refused403),
        new("production, AllowAnonymous alone", Production, MatrixConvention.AllowAnonymous, MatrixCaller.NotSignedIn, MatrixFilter.None, MatrixOutcome.Refused404),
        new("production, RequireAuthorization then AllowAnonymous", Production, MatrixConvention.RequireThenAllowAnonymous, MatrixCaller.HasTheClaim, MatrixFilter.None, MatrixOutcome.Refused404),
        new("production, AllowAnonymous then RequireAuthorization", Production, MatrixConvention.AllowAnonymousThenRequire, MatrixCaller.HasTheClaim, MatrixFilter.None, MatrixOutcome.Refused404),
        new("production, AllowAnonymous attribute then RequireAuthorization", Production, MatrixConvention.AllowAnonymousAttributeThenRequire, MatrixCaller.HasTheClaim, MatrixFilter.None, MatrixOutcome.Refused404),
        new("production, AllowAnonymous on the group then RequireAuthorization", Production, MatrixConvention.AllowAnonymousOnTheGroupThenRequire, MatrixCaller.HasTheClaim, MatrixFilter.None, MatrixOutcome.Refused404),
        new("production, access filter allows", Production, MatrixConvention.None, MatrixCaller.NotSignedIn, MatrixFilter.Allows, MatrixOutcome.Open),
        new("production, access filter denies", Production, MatrixConvention.None, MatrixCaller.NotSignedIn, MatrixFilter.Denies, MatrixOutcome.Refused403),
        new("development, access filter denies", Development, MatrixConvention.None, MatrixCaller.NotSignedIn, MatrixFilter.Denies, MatrixOutcome.Refused403),
        new("production, AllowAnonymous and an access filter that allows", Production, MatrixConvention.AllowAnonymous, MatrixCaller.NotSignedIn, MatrixFilter.Allows, MatrixOutcome.Open),
        new("production, AllowAnonymous and an access filter that denies", Production, MatrixConvention.AllowAnonymous, MatrixCaller.NotSignedIn, MatrixFilter.Denies, MatrixOutcome.Refused403),
    ];

    public static IEnumerable<object[]> Names => Cases.Select(matrixCase => new object[] { matrixCase.Name });

    public static AuthorizationMatrixCase ByName(string name) => Cases.Single(matrixCase => matrixCase.Name == name);

    public static void ConfigureServices(IServiceCollection services, AuthorizationMatrixCase matrixCase)
    {
        services.AddSingleton(new MatrixCallerSelection(matrixCase.Caller));
        services.AddAuthentication(SchemeName).AddScheme<AuthenticationSchemeOptions, MatrixAuthenticationHandler>(SchemeName, _ => { });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy.RequireClaim("role", "admin")));
        if (matrixCase.Filter != MatrixFilter.None)
        {
            services.AddSingleton<IFlowAccessFilter>(new FixedAccessFilter(matrixCase.Filter == MatrixFilter.Allows));
        }
    }

    public static void Map(
        IEndpointRouteBuilder endpoints,
        AuthorizationMatrixCase matrixCase,
        Func<IEndpointRouteBuilder, IEndpointConventionBuilder> mount)
    {
        IEndpointRouteBuilder target = matrixCase.Convention == MatrixConvention.AllowAnonymousOnTheGroupThenRequire
            ? endpoints.MapGroup(string.Empty).AllowAnonymous()
            : endpoints;
        var builder = mount(target);

        switch (matrixCase.Convention)
        {
            case MatrixConvention.RequireAuthorization:
            case MatrixConvention.AllowAnonymousOnTheGroupThenRequire:
                builder.RequireAuthorization(Policy);
                break;
            case MatrixConvention.AllowAnonymous:
                builder.AllowAnonymous();
                break;
            case MatrixConvention.RequireThenAllowAnonymous:
                builder.RequireAuthorization(Policy).AllowAnonymous();
                break;
            case MatrixConvention.AllowAnonymousThenRequire:
                builder.AllowAnonymous().RequireAuthorization(Policy);
                break;
            case MatrixConvention.AllowAnonymousAttributeThenRequire:
                builder.WithMetadata(new AllowAnonymousAttribute()).RequireAuthorization(Policy);
                break;
        }
    }

    public static IReadOnlyList<MatrixRequest> RequestsUnder(EndpointDataSource source, string basePath)
    {
        var requests = new List<MatrixRequest>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var template = endpoint.RoutePattern.RawText ?? string.Empty;
            if (!template.Equals(basePath, StringComparison.OrdinalIgnoreCase)
                && !template.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            var hasParameters = template.Contains('{', StringComparison.Ordinal);
            foreach (var path in PathsFor(template))
            {
                foreach (var method in methods)
                {
                    requests.Add(new MatrixRequest(method, path, hasParameters));
                }
            }
        }

        return requests;
    }

    public static string? Violation(AuthorizationMatrixCase matrixCase, MatrixRequest request, int status, int baselineStatus) =>
        matrixCase.Expected switch
        {
            MatrixOutcome.Open => status == baselineStatus
                ? null
                : $"{request.Method} {request.Path} answered {status}, the open Development answer was {baselineStatus}",
            MatrixOutcome.Refused404 => status == StatusCodes.Status404NotFound ? null : $"{request.Method} {request.Path} answered {status}, expected 404",
            MatrixOutcome.Refused403 => status == StatusCodes.Status403Forbidden ? null : $"{request.Method} {request.Path} answered {status}, expected 403",
            MatrixOutcome.Refused401 => status == StatusCodes.Status401Unauthorized ? null : $"{request.Method} {request.Path} answered {status}, expected 401",
            _ => $"{request.Method} {request.Path} has no expectation",
        };

    public static async Task<IReadOnlyList<MatrixAnswer>> RunAsync(
        AuthorizationMatrixCase matrixCase,
        string basePath,
        Action<IServiceCollection> addPackage,
        Func<IEndpointRouteBuilder, IEndpointConventionBuilder> mount,
        Action<IEndpointRouteBuilder>? mapExtra = null)
    {
        IEndpointRouteBuilder? mapped = null;
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(matrixCase.Environment);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    addPackage(services);
                    ConfigureServices(services, matrixCase);
                });
                webBuilder.Configure(app =>
                {
                    app.UseAuthentication();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        Map(endpoints, matrixCase, mount);
                        mapExtra?.Invoke(endpoints);
                        mapped = endpoints;
                    });
                });
            })
            .StartAsync();

        var requests = RequestsUnder(new CompositeEndpointDataSource(mapped!.DataSources), basePath);
        var client = host.GetTestServer().CreateClient();
        var answers = new List<MatrixAnswer>();
        foreach (var request in requests)
        {
            answers.Add(new MatrixAnswer(request, await StatusOfAsync(client, request)));
        }

        return answers;
    }

    public static IReadOnlyList<string> Violations(
        AuthorizationMatrixCase matrixCase,
        IReadOnlyList<MatrixAnswer> answers,
        IReadOnlyList<MatrixAnswer> openDevelopmentAnswers) =>
        answers
            .Select((answer, index) => Violation(matrixCase, answer.Request, answer.Status, openDevelopmentAnswers[index].Status))
            .OfType<string>()
            .ToList();

    private static async Task<int> StatusOfAsync(HttpClient client, MatrixRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Path);
        if (request.Method is "POST" or "PUT" or "PATCH")
        {
            message.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return (int)response.StatusCode;
        }
        catch (BadHttpRequestException exception)
        {
            return exception.StatusCode;
        }
    }

    private static IEnumerable<string> PathsFor(string template)
    {
        var concrete = Regex.Replace(template, @"\{[^}]*:int[^}]*\}", "1");
        concrete = Regex.Replace(concrete, @"\{[^}]+\}", "x");
        yield return concrete;

        if (template.Contains("**", StringComparison.Ordinal))
        {
            var root = Regex.Replace(template, @"\{\*\*[^}]+\}", string.Empty);
            yield return root;
            yield return root + "index.html";
        }
    }

    private sealed record MatrixCallerSelection(MatrixCaller Caller);

    private sealed class FixedAccessFilter(bool allows) : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(HttpContext context) => new(allows);
    }

    private sealed class MatrixAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MatrixCallerSelection selection) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (selection.Caller == MatrixCaller.NotSignedIn)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(SchemeName);
            if (selection.Caller == MatrixCaller.HasTheClaim)
            {
                identity.AddClaim(new Claim("role", "admin"));
            }

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
