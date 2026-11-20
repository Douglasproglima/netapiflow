using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NetApiFlow.Routing;

internal readonly record struct EndpointStepName(string Component, string Operation)
{
    private const string AnonymousHandlerComponent = "Minimal API";
    private const string UnknownEndpointOperation = "Endpoint";

    public static EndpointStepName For(Endpoint? endpoint)
    {
        if (endpoint is not RouteEndpoint routeEndpoint)
        {
            return new EndpointStepName(AnonymousHandlerComponent, endpoint?.DisplayName ?? UnknownEndpointOperation);
        }

        var namedMethod = NamedHandlerMethod(routeEndpoint);
        if (namedMethod is not null)
        {
            return new EndpointStepName(namedMethod.DeclaringType!.Name, namedMethod.Name);
        }

        var endpointName = routeEndpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        return new EndpointStepName(AnonymousHandlerComponent, endpointName ?? VerbsAndRoute(routeEndpoint));
    }

    private static MethodInfo? NamedHandlerMethod(Endpoint endpoint)
    {
        var method = endpoint.Metadata.GetMetadata<MethodInfo>();
        var declaringTypeName = method?.DeclaringType?.Name;
        if (method is null || declaringTypeName is null)
        {
            return null;
        }

        return HasCompilerMark(declaringTypeName) || HasCompilerMark(method.Name) ? null : method;
    }

    private static bool HasCompilerMark(string name) => name.AsSpan().IndexOfAny('<', '>') >= 0;

    private static string VerbsAndRoute(RouteEndpoint endpoint)
    {
        var route = endpoint.RoutePattern.RawText is { } rawText
            ? RouteTemplateNormalizer.Normalize(rawText)
            : endpoint.DisplayName ?? UnknownEndpointOperation;
        var verbs = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return verbs is { Count: > 0 } ? $"{string.Join(", ", verbs)} {route}" : route;
    }
}
