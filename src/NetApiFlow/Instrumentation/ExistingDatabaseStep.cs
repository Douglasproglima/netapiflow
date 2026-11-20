using System.Diagnostics;
using NetApiFlow.Domain;

namespace NetApiFlow.Instrumentation;

internal static class ExistingDatabaseStep
{
    public static bool Covers(Activity? enclosing) =>
        enclosing is not null
        && enclosing.Source.Name == FlowActivitySource.Name
        && enclosing.GetTagItem(FlowTagNames.ComponentType) as string == nameof(ComponentType.Database);
}
