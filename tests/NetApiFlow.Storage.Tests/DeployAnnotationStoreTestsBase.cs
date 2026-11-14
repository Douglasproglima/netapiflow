using Shouldly;

namespace NetApiFlow.Storage.Tests;

/// <summary>Exercises the full <see cref="IDeployAnnotationStore"/> contract against a real database.</summary>
public abstract class DeployAnnotationStoreTestsBase
{
    protected abstract IDeployAnnotationStore CreateStore();

    private static DeployAnnotation MakeAnnotation(string id, DateTime timestamp, string label = "v1.0.0") => new()
    {
        Id = id,
        Timestamp = timestamp,
        Label = label,
    };

    [Fact]
    public async Task SaveAsync_ThenQueryAsync_RoundTripsTheAnnotation()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeAnnotation("deploy-1", now, "v1.4.2"), CancellationToken.None);

        var annotations = await store.QueryAsync(now.AddMinutes(-1), now.AddMinutes(1), CancellationToken.None);

        var annotation = annotations.ShouldHaveSingleItem();
        annotation.Id.ShouldBe("deploy-1");
        annotation.Label.ShouldBe("v1.4.2");
        annotation.Timestamp.ShouldBe(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task QueryAsync_FiltersByWindow_ExcludingAnnotationsOutsideFromAndTo()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeAnnotation("deploy-old", now.AddDays(-1)), CancellationToken.None);
        await store.SaveAsync(MakeAnnotation("deploy-inside", now), CancellationToken.None);
        await store.SaveAsync(MakeAnnotation("deploy-future", now.AddDays(1)), CancellationToken.None);

        var annotations = await store.QueryAsync(now.AddHours(-1), now.AddHours(1), CancellationToken.None);

        var annotation = annotations.ShouldHaveSingleItem();
        annotation.Id.ShouldBe("deploy-inside");
    }

    [Fact]
    public async Task QueryAsync_OrdersResultsByTimestampAscending()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeAnnotation("deploy-last", now), CancellationToken.None);
        await store.SaveAsync(MakeAnnotation("deploy-first", now.AddMinutes(-10)), CancellationToken.None);
        await store.SaveAsync(MakeAnnotation("deploy-middle", now.AddMinutes(-5)), CancellationToken.None);

        var annotations = await store.QueryAsync(now.AddHours(-1), now.AddHours(1), CancellationToken.None);

        annotations.Select(a => a.Id).ShouldBe(["deploy-first", "deploy-middle", "deploy-last"]);
    }

    [Fact]
    public async Task QueryAsync_NoAnnotationsInWindow_ReturnsEmpty()
    {
        var store = CreateStore();

        var annotations = await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);

        annotations.ShouldBeEmpty();
    }
}
