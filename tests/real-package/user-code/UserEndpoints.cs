public static class UserEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/orders/{id:int}", async (int id, IOrderService orders) => await orders.GetAsync(id));
        app.MapGet("/boom", string () => throw new InvalidOperationException("The order endpoint failed on purpose."));
        LiveProbeEndpoints.Map(app);
    }
}
