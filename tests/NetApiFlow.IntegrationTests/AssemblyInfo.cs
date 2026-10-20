using Xunit;

// FlowEventPublisher's event id counter and ActivitySource/ActivityListener registration are
// process-wide static state: running test classes in parallel lets one test's events/activities
// interleave with another's, breaking assertions on exact or relative event ids (see
// NetApiFlow.Tests/AssemblyInfo.cs for the same reasoning applied there first).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
