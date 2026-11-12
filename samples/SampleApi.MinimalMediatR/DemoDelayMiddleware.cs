namespace SampleApi.MinimalMediatR;

public sealed class DemoDelayMiddleware(RequestDelegate next)
{
    private const string DelayHeaderName = "X-NetApiFlow-Demo-Delay-Ms";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(DelayHeaderName, out var rawValue)
            && int.TryParse(rawValue, out var delayMs)
            && delayMs > 0)
        {
            await Task.Delay(delayMs, context.RequestAborted);
        }

        await next(context);
    }
}
