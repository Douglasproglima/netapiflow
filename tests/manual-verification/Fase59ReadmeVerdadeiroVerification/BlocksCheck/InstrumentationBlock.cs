using OpenTelemetry.Trace;

public static class InstrumentationBlock
{
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.StartsWithSegments("/flow")));
    }
}
