namespace NetApiFlow.Marking;

internal static class FlowConventions
{
    private static readonly (string Suffix, ComponentType Component)[] BySuffix =
    [
        ("Service", ComponentType.Service),
        ("Repository", ComponentType.Repository),
        ("Handler", ComponentType.UseCase),
    ];

    public static ComponentType? ComponentTypeOf(Type type)
    {
        foreach (var (suffix, component) in BySuffix)
        {
            if (type.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return component;
            }
        }

        return null;
    }

    public static IEnumerable<ComponentType> Components => BySuffix.Select(entry => entry.Component);

    public static bool IsMediatRService(Type serviceType) =>
        serviceType.Namespace is { } serviceNamespace
        && (serviceNamespace.Equals("MediatR", StringComparison.Ordinal) || serviceNamespace.StartsWith("MediatR.", StringComparison.Ordinal));
}
