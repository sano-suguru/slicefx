namespace SliceFx.TestHost.SliceApp.Services;

public interface IWidgetStore
{
    Widget Add(string name);
}

public sealed record Widget(int Id, string Name);

public sealed class InMemoryWidgetStore : IWidgetStore
{
    private int _nextId;

    public Widget Add(string name)
        => new(Interlocked.Increment(ref _nextId), name);
}
