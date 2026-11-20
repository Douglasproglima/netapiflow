namespace NetApiFlow.Testing;

/// <summary>
/// Thrown when an assertion over a recorded trace fails. Left uncaught, every test framework
/// reports it as a failed test, which is why this package needs no assertion library of its own.
/// </summary>
public sealed class FlowAssertionException : Exception
{
    /// <summary>Creates the exception with a message stating what was expected and what was found.</summary>
    public FlowAssertionException(string message)
        : base(message)
    {
    }
}
