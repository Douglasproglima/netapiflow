using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace NetApiFlow.Dashboard.StaticFiles;

/// <summary>Resolves the Angular client's compiled output, embedded in this assembly (ADR-0005).</summary>
internal static class DashboardEmbeddedFiles
{
    private static readonly Assembly Assembly = typeof(DashboardEmbeddedFiles).Assembly;

    /// <summary>Creates a file provider rooted at the embedded <c>wwwroot</c> (the Angular build output).</summary>
    public static IFileProvider CreateFileProvider() => new ManifestEmbeddedFileProvider(Assembly, "wwwroot");
}
