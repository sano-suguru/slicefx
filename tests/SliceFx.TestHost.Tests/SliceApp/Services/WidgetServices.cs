namespace SliceFx.TestHost.SliceApp.Services;

public sealed class AuditRecorder
{
    private string _latest = "";

    public void Record(string message) => _latest = message;

    public string GetLatestEntry() => _latest;
}

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
