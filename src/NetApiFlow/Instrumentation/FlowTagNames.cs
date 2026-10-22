namespace NetApiFlow.Instrumentation;

/// <summary>Reserved <see cref="System.Diagnostics.Activity"/> tag names used to carry structural span data.</summary>
internal static class FlowTagNames
{
    public const string Component = "netapiflow.component";
    public const string Operation = "netapiflow.operation";
    public const string ComponentType = "netapiflow.type";
    public const string ErrorType = "netapiflow.error.type";
    public const string ErrorMessage = "netapiflow.error.message";
    public const string ErrorOrigin = "netapiflow.error.origin";
}
