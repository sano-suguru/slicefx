namespace Slice.WorkersSample.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
