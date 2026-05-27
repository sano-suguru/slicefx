using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SliceFx.SourceGenerator.Tests;

/// <summary>
/// Verifies that the Slice incremental source generator preserves cache state
/// across no-op edits, so IDE editing remains responsive in large solutions.
/// </summary>
public class IncrementalCacheTests
{
    private const string FeatureSource = """
        using SliceFx;

        namespace CacheTestApp.Features.Items;

        [Feature("GET /items/{id:int}")]
        public static class GetItem
        {
            public static Response Handle(int id) => new(id);

            public sealed record Response(int Id);
        }
        """;

    private const string FeatureSourceTrackedEditV1 = """
        using SliceFx;

        namespace CacheTestApp.Features.Items;

        [Feature("GET /items/{id:int}")]
        public static class GetItem
        {
            public static Response Handle(int id)
            {
                // version 1
                return new(id);
            }

            public sealed record Response(int Id);
        }
        """;

    private const string FeatureSourceTrackedEditV2 = """
        using SliceFx;

        namespace CacheTestApp.Features.Items;

        [Feature("GET /items/{id:int}")]
        public static class GetItem
        {
            public static Response Handle(int id)
            {
                // version 2
                return new(id);
            }

            public sealed record Response(int Id);
        }
        """;

    private const string FeatureSourceWithDiagnosticV1 = """
        using SliceFx;

        namespace CacheTestApp.Items;

        [Feature("GET /items/{id:int}")]
        public static class GetItem
        {
            // version 1
            public static Response Handle(int id) => new(id);

            public sealed record Response(int Id);
        }
        """;

    private const string FeatureSourceWithDiagnosticV2 = """
        using SliceFx;

        namespace CacheTestApp.Items;

        [Feature("GET /items/{id:int}")]
        public static class GetItem
        {
            // version 2
            public static Response Handle(int id) => new(id);

            public sealed record Response(int Id);
        }
        """;

    [Fact]
    public void FeatureModels_step_is_cached_when_unrelated_file_is_edited()
    {
        var unrelatedV1 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 42; } }", cancellationToken: TestContext.Current.CancellationToken);
        var unrelatedV2 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 43; } }", cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CreateCompilation("CacheTestApp", FeatureSource, unrelatedV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var compilationV2 = compilation.ReplaceSyntaxTree(unrelatedV1, unrelatedV2);
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        AssertStepReused(runResult, "SliceFeatureModels");
    }

    [Fact]
    public void ReferencedModules_step_is_cached_when_unrelated_file_is_edited()
    {
        var unrelatedV1 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 42; } }", cancellationToken: TestContext.Current.CancellationToken);
        var unrelatedV2 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 43; } }", cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CreateCompilation("CacheTestApp", FeatureSource, unrelatedV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var compilationV2 = compilation.ReplaceSyntaxTree(unrelatedV1, unrelatedV2);
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        AssertStepReused(runResult, "SliceReferencedModules");
    }

    [Fact]
    public void EmitPlan_step_is_cached_when_unrelated_file_is_edited()
    {
        var unrelatedV1 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 42; } }", cancellationToken: TestContext.Current.CancellationToken);
        var unrelatedV2 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 43; } }", cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CreateCompilation("CacheTestApp", FeatureSource, unrelatedV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var compilationV2 = compilation.ReplaceSyntaxTree(unrelatedV1, unrelatedV2);
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        AssertStepReused(runResult, "SliceEmitPlan");
    }

    [Fact]
    public void EmitPlan_step_is_cached_when_tracked_feature_tree_gets_trivial_edit()
    {
        var compilation = CreateCompilation("CacheTestApp", FeatureSourceTrackedEditV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var featureTree = compilation.SyntaxTrees.First();
        var compilationV2 = compilation.ReplaceSyntaxTree(
            featureTree,
            CSharpSyntaxTree.ParseText(FeatureSourceTrackedEditV2, new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        AssertStepReused(runResult, "SliceFeatureModels");
        AssertStepReused(runResult, "SliceEmitPlan");
    }

    [Fact]
    public void FeatureModels_step_is_cached_when_tracked_feature_tree_with_diagnostic_gets_trivial_edit()
    {
        var compilation = CreateCompilation("CacheTestApp", FeatureSourceWithDiagnosticV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var featureTree = compilation.SyntaxTrees.First();
        var compilationV2 = compilation.ReplaceSyntaxTree(
            featureTree,
            CSharpSyntaxTree.ParseText(FeatureSourceWithDiagnosticV2, new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        AssertStepReused(runResult, "SliceFeatureModels");
        AssertStepReused(runResult, "SliceEmitPlan");
    }

    [Fact]
    public void FeatureModels_step_is_invalidated_when_feature_location_shifts()
    {
        var compilation = CreateCompilation("CacheTestApp", FeatureSourceWithDiagnosticV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var featureTree = compilation.SyntaxTrees.First();
        var compilationV2 = compilation.ReplaceSyntaxTree(
            featureTree,
            CSharpSyntaxTree.ParseText("\n" + FeatureSourceWithDiagnosticV1, new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        AssertStepInvalidated(runResult, "SliceFeatureModels");
    }

    [Fact]
    public void Generated_source_text_is_identical_across_noop_edits()
    {
        var unrelatedV1 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 42; } }", cancellationToken: TestContext.Current.CancellationToken);
        var unrelatedV2 = CSharpSyntaxTree.ParseText("namespace CacheTestApp { public static class Unrelated { public static int Answer() => 43; } }", cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CreateCompilation("CacheTestApp", FeatureSource, unrelatedV1);
        GeneratorDriver driver = CreateTrackingDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var firstRun = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()));

        var compilationV2 = compilation.ReplaceSyntaxTree(unrelatedV1, unrelatedV2);
        driver = driver.RunGenerators(compilationV2, TestContext.Current.CancellationToken);
        var secondRun = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()));

        Assert.Equal(firstRun, secondRun);
    }

    private static CSharpGeneratorDriver CreateTrackingDriver()
        => CSharpGeneratorDriver.Create(
            [new SliceFeatureGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation CreateCompilation(string assemblyName, string source, params SyntaxTree[] extraTrees)
    {
        List<SyntaxTree> trees =
        [
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: TestContext.Current.CancellationToken),
            CSharpSyntaxTree.ParseText(
                "public static class __EntryPoint { public static void Main() { } }",
                new CSharpParseOptions(LanguageVersion.Latest)),
            .. extraTrees,
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

    private static void AssertStepReused(GeneratorDriverRunResult runResult, string stepName)
    {
        var generatorResult = Assert.Single(runResult.Results);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(stepName, out var steps),
            $"Expected tracked step '{stepName}' to be present. Available: {string.Join(", ", generatorResult.TrackedSteps.Keys)}");

        var outputs = steps.SelectMany(s => s.Outputs).ToArray();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Step '{stepName}' had output with reason '{output.Reason}'; expected Cached or Unchanged on a no-op edit. " +
                "This indicates the incremental generator pipeline is re-running work it should reuse."));
    }

    private static void AssertStepInvalidated(GeneratorDriverRunResult runResult, string stepName)
    {
        var generatorResult = Assert.Single(runResult.Results);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(stepName, out var steps),
            $"Expected tracked step '{stepName}' to be present. Available: {string.Join(", ", generatorResult.TrackedSteps.Keys)}");

        var outputs = steps.SelectMany(s => s.Outputs).ToArray();
        Assert.NotEmpty(outputs);
        Assert.Contains(outputs, output =>
            output.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New);
    }
}
