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
