using Microsoft.EntityFrameworkCore;

namespace NetApiFlow.EntityFrameworkCore.Ef8Tests;

internal sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

internal sealed class SingleAttachedDbContext(DbContextOptions<SingleAttachedDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}

internal sealed class DoubleAttachedDbContext(DbContextOptions<DoubleAttachedDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}
