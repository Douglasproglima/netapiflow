using System.Runtime.CompilerServices;

namespace NetApiFlow.TestSupport;

internal static class DisableConfigurationFileWatchers
{
    private const string ReloadConfigOnChangeVariable = "DOTNET_hostBuilder__reloadConfigOnChange";

    [ModuleInitializer]
    internal static void Run() =>
        Environment.SetEnvironmentVariable(ReloadConfigOnChangeVariable, "false");
}
