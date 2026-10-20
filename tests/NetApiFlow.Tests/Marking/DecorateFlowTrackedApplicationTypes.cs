namespace NetApiFlow.Tests.Marking.ApplicationNamespace;

public interface IFoo
{
    string Name();
}

public sealed class Foo : IFoo
{
    public string Name() => "foo";
}
