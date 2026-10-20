using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetApiFlow.Dashboard.Json;

/// <summary>Writes UTC timestamps as <c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c> (microsecond precision), per the frozen REST contract.</summary>
internal sealed class DashboardDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
