using System.Reflection;

namespace NetApiFlow.Marking;

/// <summary>Scope of <see cref="FlowTrackedServiceCollectionExtensions.TrackFlowByConvention"/>.</summary>
public sealed class FlowConventionOptions
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<string> _namespacePrefixes = [];

    /// <summary>Adds an assembly to the scope. Any call replaces the default scope, the entry assembly.</summary>
    public FlowConventionOptions IncludeAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Adds a namespace prefix to the scope. Any call replaces the default scope, the entry assembly.</summary>
    public FlowConventionOptions IncludeNamespacePrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _namespacePrefixes.Add(prefix.TrimEnd('.'));
        return this;
    }

    internal bool Includes(Type type, Assembly? entryAssembly)
    {
        if (_assemblies.Count == 0 && _namespacePrefixes.Count == 0)
        {
            return entryAssembly is not null && type.Assembly == entryAssembly;
        }

        return _assemblies.Contains(type.Assembly) || _namespacePrefixes.Any(prefix => InNamespace(type, prefix));
    }

    private static bool InNamespace(Type type, string prefix)
    {
        var typeNamespace = type.Namespace;
        return typeNamespace is not null
            && (typeNamespace.Equals(prefix, StringComparison.Ordinal)
                || typeNamespace.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
