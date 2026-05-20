namespace Slice.Workers;

/// <summary>
/// Entry point for creating Slice Workers applications.
/// </summary>
public static class WorkerHost
{
    /// <summary>
    /// Creates a new <see cref="WorkerHostBuilder"/> for registering services and Worker routes.
    /// </summary>
    /// <returns>A new Worker host builder.</returns>
    public static WorkerHostBuilder CreateBuilder() => new();
}
