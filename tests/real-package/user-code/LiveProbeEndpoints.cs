public static class LiveProbeEndpoints
{
    private static TaskCompletionSource _gate = NewGate();

    public static void Map(WebApplication app)
    {
        app.MapGet("/live-probe/reset", () =>
        {
            _gate = NewGate();
            return "reset";
        });

        app.MapGet("/live-probe/release", () =>
        {
            _gate.TrySetResult();
            return "released";
        });

        app.MapGet("/live-probe/orders/{id:int}", async (int id, bool cached, bool hold, IOrderService orders) =>
        {
            using (NetApiFlow.NetApiFlow.Step("OrderCache", "TryGet"))
            {
                await Task.Delay(1);
            }

            if (hold)
            {
                await _gate.Task.WaitAsync(TimeSpan.FromSeconds(60));
            }

            return cached ? "cached" : await orders.GetAsync(id);
        });
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
