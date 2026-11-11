using Xunit;

// DiagnosticListener.AllListeners is process-wide static state: running test classes in
// parallel lets one test's host (its own "Microsoft.AspNetCore" DiagnosticListener instance)
// leak events into a recorder subscribed by a different, concurrently running test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
