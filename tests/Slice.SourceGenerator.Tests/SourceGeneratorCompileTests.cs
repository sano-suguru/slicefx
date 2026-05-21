using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Slice.Workers;

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

        var compilation = CreateHostCompilation("123.My-App", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("public static class _123_My_App_SliceRegistrations", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public static class _123_My_App_SliceRouteManifest", generatedSource, StringComparison.Ordinal);
        Assert.Contains("namespace Slice;", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Slice.Generated;", generatedSource, StringComparison.Ordinal);
        Assert.Contains("SliceFeatureModuleAttribute", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IServiceCollection AddSliceServices(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IEndpointRouteBuilder MapSliceRoutes(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IServiceCollection AddSlice(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IEndpointRouteBuilder MapSlices(", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceGenerated", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MapSlicesGenerated", generatedSource, StringComparison.Ordinal);
        Assert.Contains("string Portability", generatedSource, StringComparison.Ordinal);
        Assert.Contains("\"partial\"", generatedSource, StringComparison.Ordinal);
        Assert.Contains("\"aspnet-only\"", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_empty_host_registrations_and_manifest_when_no_features_exist()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using Slice;

            namespace EmptyHost;

            public static class Startup
            {
                public static void Configure()
                {
                    var builder = WebApplication.CreateSlimBuilder();
                    builder.Services.AddSlice();
                    using var app = builder.Build();
                    app.MapSlices();
                }
            }
            """;

        var compilation = CreateHostCompilation("EmptyHost", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("public static class EmptyHost_SliceRegistrations", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IServiceCollection AddSlice(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IEndpointRouteBuilder MapSlices(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("private static readonly SliceRouteDescriptor[] s_routes =", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SliceFeatureModuleAttribute", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_workers_AddSlice_extension_in_host_compilations()
    {
        var source = """
            using Slice;
            using Slice.Workers;

            namespace WorkerHostApp.Features.Health
            {
                [Feature("GET /health")]
                public static class GetHealth
                {
                    public static string Handle() => "ok";
                }
            }

            namespace WorkerHostApp
            {
                public static class Startup
                {
                    public static void Configure()
                    {
                        var builder = WorkerHost.CreateBuilder();
                        builder.AddSlice();
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("WorkerHostApp", source, includeWorkersReference: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("namespace Slice;", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WorkerHostBuilder AddSliceWorkerRoutes(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WorkerHostBuilder AddSlice(", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceGenerated", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_escapes_control_characters_in_workers_string_literals()
    {
        var source = """
            using Slice;

            namespace WorkerEscapingApp.Features.Diagnostics;

            [Feature("GET /control\0")]
            public static class GetControl
            {
                public static string Handle() => "ok";
            }
            """;

        var compilation = CreateHostCompilation("WorkerEscapingApp", source, includeWorkersReference: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("/control", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', generatedSource);
    }

    [Fact]
    public void Generator_classifies_workers_route_parameters_case_insensitively()
    {
        var source = """
            using System;
            using Slice;

            namespace WorkerRouteApp.Features.Users;

            [Feature("GET /users/{Id:guid}")]
            public static class GetUser
            {
                public static string Handle(Guid id) => id.ToString();
            }
            """;

        var compilation = CreateHostCompilation("WorkerRouteApp", source, includeWorkersReference: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var workersSource = GetGeneratedSource(driver, "SliceWorkersRegistrations.g.cs");

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var id)", workersSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".TryGetFromQuery<global::System.Guid>(ctx, \"id\", out var id)", workersSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_excludes_aspnet_typed_results_from_workers_routes_and_manifest()
    {
        var source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Http.HttpResults;
            using Slice;

            namespace WorkerTypedResultsApp.Features.Results;

            [Feature("GET /ok")]
            public static class GetOk
            {
                public static Ok<Response> Handle() => TypedResults.Ok(new Response("ok"));

                public sealed record Response(string Message);
            }

            [Feature("GET /union")]
            public static class GetUnion
            {
                public static Results<Ok<Response>, NotFound> Handle(bool missing)
                {
                    if (missing)
                    {
                        return TypedResults.NotFound();
                    }

                    return TypedResults.Ok(new Response("ok"));
                }

                public sealed record Response(string Message);
            }

            [Feature("GET /task-ok")]
            public static class GetTaskOk
            {
                public static Task<Ok<Response>> Handle()
                    => Task.FromResult(TypedResults.Ok(new Response("ok")));

                public sealed record Response(string Message);
            }

            [Feature("GET /value-task-ok")]
            public static class GetValueTaskOk
            {
                public static ValueTask<Ok<Response>> Handle()
                    => new(TypedResults.Ok(new Response("ok")));

                public sealed record Response(string Message);
            }
            """;

        var compilation = CreateHostCompilation("WorkerTypedResultsApp", source, includeWorkersReference: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        var workersSource = GetGeneratedSource(driver, "SliceWorkersRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(4, generatorDiagnostics.Count(static diagnostic => diagnostic.Id == "SLICE008"));
        Assert.DoesNotContain("table.Add(", workersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/ok\"", workersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/union\"", workersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/task-ok\"", workersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/value-task-ok\"", workersSource, StringComparison.Ordinal);
        Assert.Equal(8, CountOccurrences(generatedSource, "\"aspnet-only\""));
    }

    [Fact]
    public void Generator_aggregates_referenced_feature_assemblies_without_extension_ambiguity()
    {
        var featureSource = """
            using Slice;

            namespace FeatureLib.Features.Products;

            [Feature("GET /products")]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }
            """;

        using var featureAssembly = CompileGeneratedAssembly("FeatureLib", featureSource);

        var hostSource = """
            using Microsoft.AspNetCore.Builder;
            using Slice;

            namespace HostApp;

            public static class Startup
            {
                public static void Configure()
                {
                    var builder = WebApplication.CreateSlimBuilder();
                    builder.Services.AddSlice();
                    using var app = builder.Build();
                    app.MapSlices();
                }
            }
            """;

        var hostCompilation = CreateHostCompilation(
            "HostApp",
            hostSource,
            extraReferences: [MetadataReference.CreateFromImage(featureAssembly.ToArray())]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("global::Slice.FeatureLib_SliceRegistrations.AddSliceServices(services);", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::Slice.FeatureLib_SliceRegistrations.MapSliceRoutes(app);", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_duplicate_endpoint_names_from_referenced_feature_assemblies()
    {
        var featureSource = """
            using Slice;

            namespace FeatureLib.Features.Products;

            [Feature("GET /products")]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }
            """;

        using var featureAssembly = CompileGeneratedAssembly("FeatureLib", featureSource);

        var hostSource = """
            using Microsoft.AspNetCore.Builder;
            using Slice;

            namespace HostApp.Features.Products
            {
                [Feature("GET /host-products")]
                public static class ListProducts
                {
                    public static string Handle() => "ok";
                }
            }

            namespace HostApp
            {
                public static class Startup
                {
                    public static void Configure()
                    {
                        var builder = WebApplication.CreateSlimBuilder();
                        builder.Services.AddSlice();
                        using var app = builder.Build();
                        app.MapSlices();
                    }
                }
            }
            """;

        var hostCompilation = CreateHostCompilation(
            "HostApp",
            hostSource,
            extraReferences: [MetadataReference.CreateFromImage(featureAssembly.ToArray())]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE004"
            && diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("FeatureLib", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_can_disable_referenced_feature_assembly_aggregation()
    {
        var featureSource = """
            using Slice;

            namespace FeatureLib.Features.Products;

            [Feature("GET /products")]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }
            """;

        using var featureAssembly = CompileGeneratedAssembly("FeatureLib", featureSource);

        var hostSource = """
            using Microsoft.AspNetCore.Builder;
            using Slice;

            namespace HostApp;

            public static class Startup
            {
                public static void Configure()
                {
                    var builder = WebApplication.CreateSlimBuilder();
                    builder.Services.AddSlice();
                    using var app = builder.Build();
                    app.MapSlices();
                }
            }
            """;

        var hostCompilation = CreateHostCompilation(
            "HostApp",
            hostSource,
            extraReferences: [MetadataReference.CreateFromImage(featureAssembly.ToArray())]);
        var driver = CreateDriver(("SliceAggregateReferences", "false"));

        var runDriver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, runDriver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("FeatureLib_SliceRegistrations", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_can_allow_list_referenced_feature_assembly_aggregation()
    {
        var firstFeatureSource = """
            using Slice;

            namespace FirstFeatureLib.Features.Products;

            [Feature("GET /products")]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }
            """;
        var secondFeatureSource = """
            using Slice;

            namespace SecondFeatureLib.Features.Orders;

            [Feature("GET /orders")]
            public static class ListOrders
            {
                public static string Handle() => "ok";
            }
            """;

        using var firstFeatureAssembly = CompileGeneratedAssembly("FirstFeatureLib", firstFeatureSource);
        using var secondFeatureAssembly = CompileGeneratedAssembly("SecondFeatureLib", secondFeatureSource);

        var hostSource = """
            using Microsoft.AspNetCore.Builder;
            using Slice;

            namespace HostApp;

            public static class Startup
            {
                public static void Configure()
                {
                    var builder = WebApplication.CreateSlimBuilder();
                    builder.Services.AddSlice();
                    using var app = builder.Build();
                    app.MapSlices();
                }
            }
            """;

        var hostCompilation = CreateHostCompilation(
            "HostApp",
            hostSource,
            extraReferences:
            [
                MetadataReference.CreateFromImage(firstFeatureAssembly.ToArray()),
                MetadataReference.CreateFromImage(secondFeatureAssembly.ToArray()),
            ]);
        var driver = CreateDriver(("SliceReferencedAssemblies", "FirstFeatureLib"));

        var runDriver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, runDriver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("global::Slice.FirstFeatureLib_SliceRegistrations.AddSliceServices(services);", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondFeatureLib_SliceRegistrations", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_registration_steps_in_the_expected_order()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using Slice;

            namespace OrderedApp.Features.Products;

            [Feature("GET /products", Summary = "List products")]
            [Filter<FirstFilter>]
            [Filter<SecondFilter>]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }

            public sealed class FirstFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }

            public sealed class SecondFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("OrderedApp", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver = driver.RunGenerators(compilation);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        AssertBefore(generatedSource, "app.MapMethods(", ".AddEndpointFilterFactory(");
        AssertBefore(generatedSource, ".AddEndpointFilterFactory(", ".AddEndpointFilter<global::OrderedApp.Features.Products.FirstFilter>()");
        AssertBefore(generatedSource, ".AddEndpointFilter<global::OrderedApp.Features.Products.FirstFilter>()", ".AddEndpointFilter<global::OrderedApp.Features.Products.SecondFilter>()");
        AssertBefore(generatedSource, ".AddEndpointFilter<global::OrderedApp.Features.Products.SecondFilter>()", ".WithTags(\"Products\")");
        AssertBefore(generatedSource, ".WithTags(\"Products\")", ".WithName(\"Products.ListProducts\")");
        AssertBefore(generatedSource, ".WithName(\"Products.ListProducts\")", ".WithSummary(\"List products\")");
    }

    [Fact]
    public void Generator_reports_SLICE010_when_filter_order_violates_FilterOrderHint()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using Slice;

            namespace OrderHintApp.Features.Things;

            [Feature("GET /things")]
            [Filter<AuthFilter>]
            [Filter<LoggingFilter>]
            public static class ListThings
            {
                public static string Handle() => "ok";
            }

            public sealed class LoggingFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }

            [FilterOrderHint(After = typeof(LoggingFilter))]
            public sealed class AuthFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("OrderHintApp", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE010"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("AuthFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("LoggingFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_report_SLICE010_when_filter_order_matches_FilterOrderHint()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using Slice;

            namespace OrderHintOkApp.Features.Things;

            [Feature("GET /things")]
            [Filter<LoggingFilter>]
            [Filter<AuthFilter>]
            public static class ListThings
            {
                public static string Handle() => "ok";
            }

            public sealed class LoggingFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }

            [FilterOrderHint(After = typeof(LoggingFilter))]
            public sealed class AuthFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("OrderHintOkApp", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Id == "SLICE010");
    }

    [Fact]
    public void Generator_reports_SLICE001_for_feature_missing_handle_method()
    {
        var source = """
            using Slice;

            namespace App.Features.Orders;

            [Feature("GET /orders")]
            public static class GetOrders
            {
                public record Response(int Count);
            }
            """;

        var compilation = CreateCompilation("App", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_reports_SLICE005_for_feature_with_multiple_handle_methods()
    {
        var source = """
            using Slice;

            namespace App.Features.Orders;

            [Feature("GET /orders")]
            public static class GetOrders
            {
                public static string Handle() => "ok";
                public static string Handle(int page) => "ok";
            }
            """;

        var compilation = CreateCompilation("App", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE005" && d.Severity == DiagnosticSeverity.Error);
    }

    private static CSharpCompilation CreateHostCompilation(
        string assemblyName,
        string source,
        bool includeWorkersReference = false,
        IEnumerable<MetadataReference>? extraReferences = null)
        => CreateCompilation(
            assemblyName,
            source,
            outputKind: OutputKind.ConsoleApplication,
            includeEntryPoint: true,
            includeWorkersReference,
            extraReferences);

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeEntryPoint = false,
        bool includeWorkersReference = false,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest)),
        };

        if (includeEntryPoint)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                "public static class __SliceTestEntryPoint { public static void Main() { } }",
                new CSharpParseOptions(LanguageVersion.Latest)));
        }

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                           && (includeWorkersReference
                               || !string.Equals(Path.GetFileName(path), "Slice.Workers.dll", StringComparison.OrdinalIgnoreCase)))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList()
            ?? [];

        references.Add(MetadataReference.CreateFromFile(typeof(FeatureAttribute).Assembly.Location));
        if (includeWorkersReference)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(WorkerHost).Assembly.Location));
        }

        if (extraReferences is not null)
        {
            references.AddRange(extraReferences);
        }

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(outputKind));
    }

    private static MemoryStream CompileGeneratedAssembly(string assemblyName, string source)
    {
        var compilation = CreateCompilation(assemblyName, source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SliceFeatureGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        stream.Position = 0;
        return stream;
    }

    private static CSharpGeneratorDriver CreateDriver(params (string Name, string Value)[] properties)
        => CSharpGeneratorDriver.Create(
            [new SliceFeatureGenerator().AsSourceGenerator()],
            optionsProvider: new TestAnalyzerConfigOptionsProvider(properties));

    private static void AssertBefore(string value, string before, string after)
    {
        var beforeIndex = value.IndexOf(before, StringComparison.Ordinal);
        var afterIndex = value.IndexOf(after, StringComparison.Ordinal);
        Assert.True(beforeIndex >= 0, $"Could not find '{before}'.");
        Assert.True(afterIndex >= 0, $"Could not find '{after}'.");
        Assert.True(beforeIndex < afterIndex, $"Expected '{before}' to appear before '{after}'.");
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string GetGeneratedSource(GeneratorDriver driver, string hintNameSuffix)
        => driver.GetRunResult()
            .GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            .GetText()
            .ToString();

    private sealed class TestAnalyzerConfigOptionsProvider(
        params (string Name, string Value)[] properties) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions = new TestAnalyzerConfigOptions(properties);

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
    }

    private sealed class TestAnalyzerConfigOptions(params (string Name, string Value)[] properties) : AnalyzerConfigOptions
    {
        public static readonly AnalyzerConfigOptions Empty = new TestAnalyzerConfigOptions();

        private readonly Dictionary<string, string> _values = properties.ToDictionary(
            static property => $"build_property.{property.Name}",
            static property => property.Value,
            StringComparer.OrdinalIgnoreCase);

        public override bool TryGetValue(string key, out string value)
            => _values.TryGetValue(key, out value!);
    }
}
