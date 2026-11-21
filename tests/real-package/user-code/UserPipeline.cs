public static class UserPipeline
{
    private const string ExceptionHandlerVariable = "USER_EXCEPTION_HANDLER";

    public static void Configure(WebApplication app)
    {
        if (Environment.GetEnvironmentVariable(ExceptionHandlerVariable) != "true")
        {
            return;
        }

        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
    }
}
