namespace NetApiFlow.Tests.Marking.ApplicationNamespace;

public interface IFoo
{
    string Name();
}

public sealed class Foo : IFoo
{
    public string Name() => "foo";
}

public interface IOwnedResource
{
    string Name();
}

public sealed class OwnedResource : IOwnedResource, IDisposable
{
    public bool WasDisposed { get; private set; }

    public string Name() => "owned";

    public void Dispose() => WasDisposed = true;
}
