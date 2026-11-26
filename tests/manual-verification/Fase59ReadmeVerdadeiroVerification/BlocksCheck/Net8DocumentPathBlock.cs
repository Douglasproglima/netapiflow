using NetApiFlow;

public static class Net8DocumentPathBlock
{
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddNetApiFlow(options => options.OpenApiDocumentPath = "/swagger/v1/swagger.json");
    }
}
