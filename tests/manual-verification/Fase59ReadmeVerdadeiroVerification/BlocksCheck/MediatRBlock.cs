using NetApiFlow.MediatR;

public static class MediatRBlock
{
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddNetApiFlowMediatR();
    }
}
