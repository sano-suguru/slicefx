using BenchmarkDotNet.Running;

namespace SliceFx.Benchmarks.Runtime;

/// <summary>
/// Entry point for SliceFx runtime benchmark runs.
/// </summary>
public static class Program
{
    /// <summary>
    /// Launches the BenchmarkDotNet switcher against this assembly's benchmark classes.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to BenchmarkSwitcher.</param>
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
