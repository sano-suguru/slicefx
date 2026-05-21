using BenchmarkDotNet.Running;

namespace Slice.Benchmarks;

/// <summary>
/// Entry point for Slice benchmark runs.
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
