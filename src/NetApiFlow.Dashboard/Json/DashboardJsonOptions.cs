using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetApiFlow.Dashboard.Json;

/// <summary>JSON options for the dashboard REST API and SSE stream, independent of the host app's own configuration.</summary>
internal static class DashboardJsonOptions
{
    /// <summary>REST response bodies: nulls stay explicit (contract shows them, e.g. <c>"errorMessage": null</c>).</summary>
    public static readonly JsonSerializerOptions Rest = Create(ignoreNulls: false);

    /// <summary>SSE event bodies: null <c>trace</c>/<c>span</c> keys are omitted entirely, per the frozen contract.</summary>
    public static readonly JsonSerializerOptions Sse = Create(ignoreNulls: true);

    private static JsonSerializerOptions Create(bool ignoreNulls)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = ignoreNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new DashboardDateTimeConverter());
        return options;
    }
}
