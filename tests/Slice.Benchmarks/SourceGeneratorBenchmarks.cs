using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Slice.SourceGenerator;

namespace Slice.Benchmarks;

/// <summary>
/// Adds the full JSON exporter so nightly perf workflows can parse measurements
/// and assert against the gate values in <c>tests/Slice.Benchmarks/gates.json</c>.
/// </summary>
internal sealed class JsonReportConfig : ManualConfig
{
    /// <summary>
    /// Initializes the configuration with the full JSON exporter attached.
    /// </summary>
    public JsonReportConfig() => AddExporter(JsonExporter.Full);
}

/// <summary>
/// Benchmarks the Slice incremental source generator against synthetic projects of varying size.
/// Measures cold-run cost and the cost of a no-op edit (cache-reuse path) so regressions in
/// caching effectiveness are visible to nightly CI.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(JsonReportConfig))]
public class SourceGeneratorBenchmarks
{
    private CSharpCompilation _compilation = null!;
    private SyntaxTree _unrelatedV1 = null!;
    private SyntaxTree _unrelatedV2 = null!;
    private GeneratorDriver _warmDriver = null!;

    /// <summary>
    /// Number of synthetic features to include in the compiled project.
    /// </summary>
    [Params(50, 100, 200)]
    public int FeatureCount { get; set; }

    /// <summary>
    /// Builds the synthetic compilation used by every benchmark in this class.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _unrelatedV1 = CSharpSyntaxTree.ParseText("namespace Bench { public static class Unrelated { public static int Answer() => 42; } }");
        _unrelatedV2 = CSharpSyntaxTree.ParseText("namespace Bench { public static class Unrelated { public static int Answer() => 43; } }");

        var featureSource = BuildFeatureSource(FeatureCount);
        _compilation = CreateCompilation("Bench", featureSource, _unrelatedV1);

        var driver = CSharpGeneratorDriver.Create([new SliceFeatureGenerator().AsSourceGenerator()]);
        _warmDriver = driver.RunGenerators(_compilation);
    }

    /// <summary>
    /// Cold run: builds a fresh driver and runs the generator against the full compilation.
    /// </summary>
    /// <returns>The driver post-run, returned to defeat dead-code elimination.</returns>
    [Benchmark]
    public GeneratorDriver ColdRun()
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new SliceFeatureGenerator().AsSourceGenerator()]);
        return driver.RunGenerators(_compilation);
    }

    /// <summary>
    /// Warm run: simulates an unrelated-file edit and re-runs the generator on the already-driven driver.
    /// </summary>
    /// <returns>The driver post-run, returned to defeat dead-code elimination.</returns>
    [Benchmark]
    public GeneratorDriver WarmRun_NoOpEdit()
    {
        var newCompilation = _compilation.ReplaceSyntaxTree(_unrelatedV1, _unrelatedV2);
        return _warmDriver.RunGenerators(newCompilation);
    }

    private static string BuildFeatureSource(int count)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Slice;");
        sb.AppendLine();
        sb.AppendLine("namespace Bench.Features.Items;");
        sb.AppendLine();
        for (var i = 0; i < count; i++)
        {
            var idx = i.ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Feature(\"GET /items/{idx}/{{id:int}}\")]");
            sb.AppendLine(CultureInfo.InvariantCulture, $"public static class GetItem{idx}");
            sb.AppendLine("{");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public static Response Handle(int id) => new(id);");
            sb.AppendLine("    public sealed record Response(int Id);");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source, SyntaxTree unrelated)
    {
        List<SyntaxTree> trees =
        [
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)),
            CSharpSyntaxTree.ParseText(
                "public static class __EntryPoint { public static void Main() { } }",
                new CSharpParseOptions(LanguageVersion.Latest)),
            unrelated,
        ];

        List<MetadataReference> references =
        [
            .. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                ?.Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => MetadataReference.CreateFromFile(p))
                ?? [],
            MetadataReference.CreateFromFile(typeof(FeatureAttribute).Assembly.Location),
        ];

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    }
}
