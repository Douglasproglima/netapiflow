using Microsoft.EntityFrameworkCore;

namespace SampleApi.Controllers.Data;

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
