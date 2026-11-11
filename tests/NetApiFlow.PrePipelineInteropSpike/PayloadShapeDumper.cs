using System.Reflection;

namespace NetApiFlow.PrePipelineInteropSpike;

internal static class PayloadShapeDumper
{
    public static string Describe(object? payload)
    {
        if (payload is null)
        {
            return "<null>";
        }

        var type = payload.GetType();
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property =>
                $"{(property.GetMethod?.IsPublic == true ? "public" : "non-public")} {property.PropertyType.Name} {property.Name}");
        var interfaces = type.GetInterfaces().OrderBy(i => i.Name, StringComparer.Ordinal).Select(i => i.Name);

        return $"{type.FullName} (isPublic={type.IsPublic}, interfaces=[{string.Join(", ", interfaces)}]) " +
            $"{{ {string.Join(", ", properties)} }}";
    }
}
