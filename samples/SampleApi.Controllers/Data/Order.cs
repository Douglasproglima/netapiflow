namespace SampleApi.Controllers.Data;

public sealed class Order
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = "";

    public decimal Amount { get; set; }

    public string Status { get; set; } = "Pending";
}
