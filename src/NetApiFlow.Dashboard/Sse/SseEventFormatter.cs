using System.Text.Json;
using NetApiFlow.Dashboard.Contracts;
using NetApiFlow.Dashboard.Json;
using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Sse;

/// <summary>Builds the wire representation of a <see cref="FlowEvent"/>, shared by the net8.0 and net10.0 <c>GET /stream</c> writers.</summary>
internal static class SseEventFormatter
{
    public static string EventName(FlowEvent flowEvent) => flowEvent.Type.ToString();

    public static string SerializeData(FlowEvent flowEvent) =>
        JsonSerializer.Serialize(FlowEventDto.From(flowEvent), DashboardJsonOptions.Sse);
}
