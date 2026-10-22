using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Configuration;

/// <summary>Binds <see cref="NetApiFlowOptions"/> from the <c>NetApiFlow</c> configuration section.</summary>
internal sealed class NetApiFlowOptionsSetup(IConfiguration configuration) : IConfigureOptions<NetApiFlowOptions>
{
    private const string SectionName = "NetApiFlow";

    public void Configure(NetApiFlowOptions options)
    {
        var section = configuration.GetSection(SectionName);
        section.Bind(options);

        var includePaths = section.GetSection("IncludePaths").Get<string[]>();
        if (includePaths is { Length: > 0 })
        {
            options.IncludePaths(includePaths);
        }

        var excludePaths = section.GetSection("ExcludePaths").Get<string[]>();
        if (excludePaths is { Length: > 0 })
        {
            options.ExcludePaths(excludePaths);
        }
    }
}
