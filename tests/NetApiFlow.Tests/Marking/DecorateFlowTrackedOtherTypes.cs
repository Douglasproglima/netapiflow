namespace NetApiFlow.Tests.Marking.OtherNamespace;

public interface IBar
{
    string Name();
}

public sealed class Bar : IBar
{
    public string Name() => "bar";
}
