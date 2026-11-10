using System.Collections.Concurrent;
using System.Diagnostics;

namespace NetApiFlow.NpgsqlInteropSpike;

internal sealed record ActivitySnapshot(
    string SourceName,
    string OperationName,
    string ActivityId,
    string TraceId,
    string? ParentId,
    string? ParentSourceName,
    string? ParentOperationName,
    IReadOnlyList<KeyValuePair<string, string?>> Tags)
{
    public string? Tag(string key) =>
        Tags.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.Ordinal)).Value;

    public string Describe() =>
        $"source={SourceName} name={OperationName} traceId={TraceId} parentId={ParentId ?? "<null>"} " +
        $"parentSource={ParentSourceName ?? "<null>"} parentName={ParentOperationName ?? "<null>"}";

    public string DescribeTags() =>
        Tags.Count == 0
            ? "    <no tags>"
            : string.Join(Environment.NewLine, Tags.Select(pair => $"    {pair.Key} = {pair.Value ?? "<null>"}"));
}

internal sealed class ActivitySnapshotRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<ActivitySnapshot> _stopped = new();

    public ActivitySnapshotRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _stopped.Enqueue(Snapshot(activity)),
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<ActivitySnapshot> All => [.. _stopped];

    public IReadOnlyList<ActivitySnapshot> From(string sourceName) =>
        [.. _stopped.Where(snapshot => string.Equals(snapshot.SourceName, sourceName, StringComparison.Ordinal))];

    public IReadOnlyList<string> SourceNames =>
        [.. _stopped.Select(snapshot => snapshot.SourceName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public void Clear() => _stopped.Clear();

    public void Dispose() => _listener.Dispose();

    private static ActivitySnapshot Snapshot(Activity activity) => new(
        activity.Source.Name,
        activity.OperationName,
        activity.Id ?? "<null>",
        activity.TraceId.ToString(),
        activity.ParentId,
        activity.Parent?.Source.Name,
        activity.Parent?.OperationName,
        [.. activity.TagObjects.Select(tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString()))]);
}
