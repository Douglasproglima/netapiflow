using System.Reflection;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests;

public class ArchitectureTests
{
    private static readonly string[] ForbiddenAssemblyNamePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "MediatR",
        "Npgsql",
        "Microsoft.Data.SqlClient",
        "OpenTelemetry",
        "Dapper",
    ];

    [Fact]
    public void CoreAssembly_NeverReferences_ForbiddenDependencies()
    {
        var referenced = typeof(Domain.FlowTrace).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToArray();

        foreach (var forbiddenPrefix in ForbiddenAssemblyNamePrefixes)
        {
            referenced.ShouldNotContain(name => name!.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The whole point of this package is that asserting on a flow costs the user's test project
    /// nothing but NetApiFlow itself. A reference to any assertion library, Shouldly included,
    /// would impose it on every consumer.
    /// </summary>
    [Fact]
    public void TestingAssembly_ReferencesOnlyTheCoreAndTheFramework()
    {
        var referenced = typeof(global::NetApiFlow.Testing.FlowTraceAssertions).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToArray();

        foreach (var forbiddenPrefix in ForbiddenAssemblyNamePrefixes.Append("Shouldly").Append("xunit").Append("NSubstitute"))
        {
            referenced.ShouldNotContain(name => name!.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
        }

        referenced.ShouldContain("NetApiFlow");
    }

    [Theory]
    [InlineData(typeof(Domain.FlowTrace))]
    [InlineData(typeof(Domain.FlowSpan))]
    public void DomainType_HasNoFrameworkOrDatabaseDependencyInItsOwnAssembly(Type domainType)
    {
        var forbiddenNamespacePrefixes = new[]
        {
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "MediatR",
            "Npgsql",
            "Microsoft.Data.SqlClient",
        };

        var memberTypeNamespaces = domainType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType.Namespace)
            .Where(ns => ns is not null)
            .ToArray();

        foreach (var forbiddenNamespace in forbiddenNamespacePrefixes)
        {
            memberTypeNamespaces.ShouldNotContain(ns => ns!.StartsWith(forbiddenNamespace, StringComparison.Ordinal));
        }
    }
}
