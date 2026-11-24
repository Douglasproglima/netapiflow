using System.ComponentModel;
using ModelContextProtocol.Server;

public static class UserServices
{
    public static void Add(IServiceCollection services) =>
        services.AddMcpServer().WithHttpTransport().WithTools<UserTools>();
}

public sealed class UserTools
{
    public const string Name = "user-tool";

    [McpServerTool(Name = Name)]
    [Description("A tool of the user app itself.")]
    public static string UserTool() => "from the user app";
}
