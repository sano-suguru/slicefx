namespace Slice.WorkersSample.Services;

/// <summary>
/// Clock abstraction used to keep Workers feature examples easy to test.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
