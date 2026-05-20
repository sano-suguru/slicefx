using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Slice.SourceGenerator.Tests;

public class SourceGeneratorCompileTests
{
    [Fact]
    public void Generator_emits_compilable_registrations_with_sanitized_names_and_portability_vocabulary()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using Slice;

            namespace GeneratedApp.Features.Products;

            [Feature("GET /products/{id:int}", Summary = "Get a product")]
            [Filter<AuditFilter>]
            public static partial class GetProduct
            {
                public static Response Handle(int id) => new(id);

                public sealed record Response(int Id);
            }

            [Feature("DELETE /products/{id:int}")]
            public static class DeleteProduct
            {
                public static IResult Handle(int id) => Results.NoContent();
            }

            public sealed class AuditFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateCompilation("123.My-App", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("public static class _123_My_App_SliceRegistrations", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public static class _123_My_App_SliceRouteManifest", generatedSource, StringComparison.Ordinal);
        Assert.Contains("string Portability", generatedSource, StringComparison.Ordinal);
        Assert.Contains("\"partial\"", generatedSource, StringComparison.Ordinal);
        Assert.Contains("\"aspnet-only\"", generatedSource, StringComparison.Ordinal);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList()
            ?? [];

        references.Add(MetadataReference.CreateFromFile(typeof(FeatureAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
