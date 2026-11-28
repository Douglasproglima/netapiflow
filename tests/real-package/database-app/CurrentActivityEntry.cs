using System.Diagnostics;

public sealed record CurrentActivityEntry(string Label, string? Id, bool? IsStopped)
{
    public static CurrentActivityEntry Snapshot(string label) =>
        new(label, Activity.Current?.Id, Activity.Current?.IsStopped);
}
