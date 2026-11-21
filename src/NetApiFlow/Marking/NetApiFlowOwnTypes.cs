namespace NetApiFlow.Marking;

internal static class NetApiFlowOwnTypes
{
    private static readonly HashSet<string> PackageAssemblies = new(StringComparer.Ordinal)
    {
        "NetApiFlow",
        "NetApiFlow.Dashboard",
        "NetApiFlow.EntityFrameworkCore",
        "NetApiFlow.MediatR",
        "NetApiFlow.Mcp",
        "NetApiFlow.OpenTelemetry",
        "NetApiFlow.Storage.PostgreSql",
        "NetApiFlow.Storage.SqlServer",
        "NetApiFlow.Testing",
    };

    public static IReadOnlyCollection<string> PackageAssemblyNames => PackageAssemblies;

    public static bool Is(Type type) => type.Assembly.GetName().Name is { } name && PackageAssemblies.Contains(name);
}
