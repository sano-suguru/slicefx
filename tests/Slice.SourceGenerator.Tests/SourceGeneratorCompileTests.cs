using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Slice.Lambda.PerFunction;
using Slice.Wasi;

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
        GeneratorDriver driver = CreateDriver();

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
    public void Generator_auto_registers_and_runs_slice_validators_before_user_filters()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace ValidatorApp.Features.Users;

            [Feature("POST /users")]
            [Filter<AuditFilter>]
            public static class CreateUser
            {
                public sealed record Request(string Name);

                public sealed record Response(string Name);

                public static Response Handle(Request req) => new(req.Name);
            }

            public sealed class CreateUserValidator : ISliceValidator<CreateUser.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }

            public sealed class AuditFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("TryAddScoped<global::Slice.ISliceValidator<global::ValidatorApp.Features.Users.CreateUser.Request>, global::ValidatorApp.Features.Users.CreateUserValidator>(services)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetService<global::Slice.ISliceValidator<global::ValidatorApp.Features.Users.CreateUser.Request>>(invocationContext.HttpContext.RequestServices)", generatedSource, StringComparison.Ordinal);

        var dataAnnotationsIndex = generatedSource.IndexOf("DataAnnotationsValidationFilter.CreateFilterFactory", StringComparison.Ordinal);
        var validatorIndex = generatedSource.IndexOf("__CreateSliceValidatorFactory_global__ValidatorApp_Features_Users_CreateUser", StringComparison.Ordinal);
        var userFilterIndex = generatedSource.IndexOf(".AddEndpointFilter<global::ValidatorApp.Features.Users.AuditFilter>()", StringComparison.Ordinal);
        Assert.True(dataAnnotationsIndex >= 0 && dataAnnotationsIndex < validatorIndex);
        Assert.True(validatorIndex < userFilterIndex);
    }

    [Fact]
    public void Generator_discovers_slice_validators_through_aliases_and_derived_interfaces()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;
            using AliasValidator = Slice.ISliceValidator<ValidatorAliasApp.Features.Users.CreateUser.Request>;

            namespace ValidatorAliasApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }

            public sealed class AliasCreateUserValidator : AliasValidator
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }

            public interface IDerivedCreateUserValidator : ISliceValidator<CreateUser.Request>
            {
            }

            public sealed class DerivedCreateUserValidator : IDerivedCreateUserValidator
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }

            public abstract class ValidatorBase<T> : ISliceValidator<T>
                where T : class
            {
                public abstract ValueTask<SliceValidationResult> ValidateAsync(T value, CancellationToken ct);
            }

            public sealed class BaseCreateUserValidator : ValidatorBase<CreateUser.Request>
            {
                public override ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorAliasApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE021");
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE020");
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE022");
    }

    [Fact]
    public void Generator_reports_SLICE022_for_unmatched_slice_validators()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace ValidatorDiagnosticsApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }

            public sealed record OtherRequest(string Name);

            public sealed class OtherRequestValidator : ISliceValidator<OtherRequest>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(OtherRequest value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorDiagnosticsApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE022"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("OtherRequestValidator", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_emit_validator_filter_when_request_has_no_validator()
    {
        var source = """
            using Slice;

            namespace ValidatorNoopApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }
            """;

        var compilation = CreateHostCompilation("ValidatorNoopApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("__CreateSliceValidatorFactory_", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<global::Slice.ISliceValidator<global::ValidatorNoopApp.Features.Users.CreateUser.Request>>", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_checks_validator_service_type_not_request_dto_type()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace ValidatorGateApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }

            public sealed class CreateUserValidator : ISliceValidator<CreateUser.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorGateApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("IsService(typeof(global::Slice.ISliceValidator<T>))", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsService(typeof(T))", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE020_for_open_generic_slice_validators()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace ValidatorDiagnosticsApp;

            public sealed class GenericValidator<T> : ISliceValidator<T>
                where T : class
            {
                public ValueTask<SliceValidationResult> ValidateAsync(T value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorDiagnosticsApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE020"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("open generic", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE020_for_invalid_slice_validator_request_type()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace ValidatorDiagnosticsApp;

            public sealed class IntValidator : ISliceValidator<int>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(int value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorDiagnosticsApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE020"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("not a supported Slice request type", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE021_for_duplicate_slice_validators_in_one_assembly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace ValidatorDiagnosticsApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }

            public sealed class FirstValidator : ISliceValidator<CreateUser.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }

            public sealed class SecondValidator : ISliceValidator<CreateUser.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorDiagnosticsApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE021"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("CreateUser.Request", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
        GeneratorDriver driver = CreateDriver();

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
    public void Generator_emits_wasi_AddSlice_extension_in_host_compilations()
    {
        var source = """
            using Slice;
            using Slice.Wasi;

            namespace WasiHostApp.Features.Health
            {
                [Feature("GET /health")]
                public static class GetHealth
                {
                    public static string Handle() => "ok";
                }
            }

            namespace WasiHostApp
            {
                public static class Startup
                {
                    public static void Configure()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiHostApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("namespace Slice;", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WasiHostBuilder AddSliceWasiRoutes(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WasiHostBuilder AddSlice(", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceGenerated", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_escapes_control_characters_in_wasi_string_literals()
    {
        var source = """
            using Slice;
            using Slice.Wasi;

            namespace WasiEscapingApp.Features.Diagnostics;

            [Feature("GET /control\0")]
            public static class GetControl
            {
                public static WasiResponse Handle() => SliceResult.NoContent();
            }
            """;

        var compilation = CreateHostCompilation("WasiEscapingApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("/control", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', generatedSource);
    }

    [Fact]
    public void Generator_classifies_wasi_route_parameters_case_insensitively()
    {
        var source = """
            using System;
            using Slice;
            using Slice.Wasi;

            namespace WasiRouteApp.Features.Users;

            [Feature("GET /users/{Id:guid}")]
            public static class GetUser
            {
                public static WasiResponse Handle(Guid id) => SliceResult.NoContent();
            }
            """;

        var compilation = CreateHostCompilation("WasiRouteApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var id)", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".TryGetFromQuery<global::System.Guid>(ctx, \"id\", out var id)", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_wasi_validation_for_supported_attributes_with_custom_messages()
    {
        var source = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Slice;

            namespace WasiValidationApp
            {
                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();

                    private WasiJsonContext()
                        : base(null)
                    {
                    }

                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace WasiValidationApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public sealed record Request(
                        [Required(ErrorMessage = "Name is required.")]
                        [StringLength(10, MinimumLength = 2, ErrorMessage = "Name length is invalid.")]
                        string? Name,
                        [MinLength(2, ErrorMessage = "At least two items are required.")]
                        int[] Items);

                    public sealed record Response(string Name);

                    public static Response Handle(Request req) => new(req.Name!);
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiValidationApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE011");
        Assert.Contains("Name is required.", wasiSource, StringComparison.Ordinal);
        Assert.Contains("Name length is invalid.", wasiSource, StringComparison.Ordinal);
        Assert.Contains("At least two items are required.", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WasiValidationRunner.Validate", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ISliceValidator<global::WasiValidationApp.Features.Items.CreateItem.Request>", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_lambda_per_feature_handlers_when_opted_in()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.Extensions.DependencyInjection;
            using Slice;
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction(typeof(LambdaApp.LambdaStartup))]

            namespace LambdaApp
            {
                public sealed class LambdaStartup : ILambdaPerFunctionStartup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<Clock>();
                    }
                }

                public sealed class Clock;

                [SliceJsonContext(SliceJsonTarget.LambdaPerFeature)]

                public sealed class LambdaJsonContext : JsonSerializerContext
                {
                    public static LambdaJsonContext Default { get; } = new();

                    private LambdaJsonContext()
                        : base(null)
                    {
                    }

                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace LambdaApp.Features.Users
            {
                [Feature("GET /users/{id:guid}")]
                public static class GetUser
                {
                    public sealed record Response(Guid Id);

                    public static Response Handle(Guid id, LambdaApp.Clock clock, CancellationToken ct) => new(id);
                }

                [Feature("POST /users")]
                public static class CreateUser
                {
                    public sealed record Request(string Name);

                    public sealed record Response(string Name);

                    public static Response Handle(Request req) => new(req.Name);
                }
            }
            """;

        var compilation = CreateHostCompilation("LambdaApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaPerFunctionHandlers.g.cs");
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public static class LambdaApp_SliceLambdaPerFunctionHandlers", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("global::Slice.LambdaApp_SliceRegistrations.AddSliceValidatorServices(services);", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceServices(services);", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("Users_GetUser", lambdaSource, StringComparison.Ordinal);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var id)", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("global::System.Text.Json.JsonException or global::System.FormatException", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ISliceValidator<global::LambdaApp.Features.Users.CreateUser.Request>", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<global::LambdaApp.Clock>()", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("LambdaResponseFactory.Ok<global::LambdaApp.Features.Users.GetUser.Response>", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("\"LambdaApp\"", manifestSource, StringComparison.Ordinal);
        Assert.Contains("\"Slice.LambdaApp_SliceLambdaPerFunctionHandlers\"", manifestSource, StringComparison.Ordinal);
        Assert.Contains("\"Users_GetUser\"", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_lambda_json_context_diagnostic_when_missing()
    {
        var source = """
            using Slice;
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction]

            namespace MissingJsonLambdaApp.Features.Users;

            [Feature("GET /users")]
            public static class ListUsers
            {
                public sealed record Response(string Name);

                public static Response Handle() => new("Alice");
            }
            """;

        var compilation = CreateHostCompilation("MissingJsonLambdaApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE014");
        Assert.Contains("\"ineligible\"", manifestSource, StringComparison.Ordinal);
        Assert.Contains("explicit [SliceJsonContext] JsonSerializerContext", manifestSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Slice.MissingJsonLambdaApp_SliceLambdaPerFunctionHandlers", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_duplicate_slice_json_context_overrides()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Slice;

            namespace DuplicateJsonContextApp;

            [SliceJsonContext(SliceJsonTarget.Wasi)]
            public sealed class FirstWasiJsonContext : JsonSerializerContext
            {
                public static FirstWasiJsonContext Default { get; } = new();

                private FirstWasiJsonContext()
                    : base(null)
                {
                }

                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                public override JsonTypeInfo? GetTypeInfo(Type type) => null;
            }

            [SliceJsonContext(SliceJsonTarget.Wasi)]
            public sealed class SecondWasiJsonContext : JsonSerializerContext
            {
                public static SecondWasiJsonContext Default { get; } = new();

                private SecondWasiJsonContext()
                    : base(null)
                {
                }

                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                public override JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

        var compilation = CreateHostCompilation("DuplicateJsonContextApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE018" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_reports_invalid_slice_json_context_override()
    {
        var source = """
            using Slice;

            namespace InvalidJsonContextApp;

            [SliceJsonContext(SliceJsonTarget.LambdaPerFeature)]
            public sealed class NotAJsonContext
            {
            }
            """;

        var compilation = CreateHostCompilation("InvalidJsonContextApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE019" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_omits_lambda_per_feature_metadata_when_handlers_are_not_emitted()
    {
        var source = """
            using Slice;

            namespace NoLambdaHandlersApp.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """;

        var compilation = CreateHostCompilation("NoLambdaHandlersApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var compactManifestSource = manifestSource
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);
        Assert.Contains("string? LambdaPerFeatureStatus", manifestSource, StringComparison.Ordinal);
        Assert.Contains("string? WasiDispatchStatus", manifestSource, StringComparison.Ordinal);
        Assert.Contains("null,null,null,null,null,\"1\",\"eligible\",null)]", compactManifestSource, StringComparison.Ordinal);
        Assert.Contains("\"1\",true,\"portable\",null,\"eligible\",null,null,null,null,null,null,global::System.Array.Empty<string>())", compactManifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_classifies_lambda_catch_all_route_parameters_as_route_values()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Slice;
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction]

            namespace LambdaCatchAllApp
            {
                [SliceJsonContext(SliceJsonTarget.LambdaPerFeature)]
                public sealed class LambdaJsonContext : JsonSerializerContext
                {
                    public static LambdaJsonContext Default { get; } = new();

                    private LambdaJsonContext()
                        : base(null)
                    {
                    }

                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace LambdaCatchAllApp.Features.Files
            {
                [Feature("GET /files/{**path}")]
                public static class GetFile
                {
                    public static string Handle(string path) => path;
                }
            }
            """;

        var compilation = CreateHostCompilation("LambdaCatchAllApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaPerFunctionHandlers.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<string>(ctx, \"path\", out var path)", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".TryGetFromQuery<string>(ctx, \"path\", out var path)", lambdaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_invalid_lambda_startup_type()
    {
        var source = """
            using Slice;
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction(typeof(InvalidStartupApp.BadStartup))]

            namespace InvalidStartupApp
            {
                public sealed class BadStartup
                {
                    public BadStartup(string value)
                    {
                    }
                }
            }

            namespace InvalidStartupApp.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static void Handle()
                {
                }
            }
            """;

        var compilation = CreateHostCompilation("InvalidStartupApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE017" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_excludes_wasi_body_routes_without_wasi_json_context()
    {
        var source = """
            using Slice;

            namespace WasiMissingJsonContextApp.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public sealed record Request(string Name);

                public sealed record Response(string Name);

                public static Response Handle(Request req) => new(req.Name);
            }
            """;

        var compilation = CreateHostCompilation("WasiMissingJsonContextApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE009" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain("table.Add(", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/items\"", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_auto_registers_and_resolves_wasi_slice_validators()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Slice;

            namespace WasiValidatorApp
            {
                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();

                    private WasiJsonContext()
                        : base(null)
                    {
                    }

                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace WasiValidatorApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public sealed record Request(string Name);

                    public sealed record Response(string Name);

                    public static Response Handle(Request req) => new(req.Name);
                }

                public sealed class CreateItemValidator : ISliceValidator<CreateItem.Request>
                {
                    public ValueTask<SliceValidationResult> ValidateAsync(CreateItem.Request value, CancellationToken ct)
                        => ValueTask.FromResult(SliceValidationResult.Success);
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiValidatorApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var registrationSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("TryAddScoped<global::Slice.ISliceValidator<global::WasiValidatorApp.Features.Items.CreateItem.Request>, global::WasiValidatorApp.Features.Items.CreateItemValidator>(services)", registrationSource, StringComparison.Ordinal);
        Assert.Contains("ServiceProviderServiceExtensions.GetService<global::Slice.ISliceValidator<global::WasiValidatorApp.Features.Items.CreateItem.Request>>(ctx.Services)", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<global::Slice.ISliceValidator", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService(typeof(global::Slice.ISliceValidator", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_excludes_wasi_routes_that_require_reflection_validation()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Slice;

            namespace WasiReflectionValidationApp.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public sealed record Request([Range(1, 10)] int Count);

                public sealed record Response(int Count);

                public static Response Handle(Request req) => new(req.Count);
            }
            """;

        var compilation = CreateHostCompilation("WasiReflectionValidationApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE011");
        Assert.DoesNotContain("table.Add(", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/items\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WasiValidationRunner.Validate", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_excludes_aspnet_typed_results_from_wasi_routes_and_manifest()
    {
        var source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Http.HttpResults;
            using Slice;

            namespace WasiTypedResultsApp.Features.Results;

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

        var compilation = CreateHostCompilation("WasiTypedResultsApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(4, generatorDiagnostics.Count(static diagnostic => diagnostic.Id == "SLICE008"));
        Assert.DoesNotContain("table.Add(", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/ok\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/union\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/task-ok\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/value-task-ok\"", wasiSource, StringComparison.Ordinal);
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
        GeneratorDriver driver = CreateDriver();

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
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE004"
            && diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("FeatureLib", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_reports_duplicate_validators_from_referenced_feature_assemblies()
    {
        var featureSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace FeatureLib.Features.Products;

            [Feature("POST /products")]
            public static class CreateProduct
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }

            public sealed class CreateProductValidator : ISliceValidator<CreateProduct.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(CreateProduct.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        using var featureAssembly = CompileGeneratedAssembly("FeatureLib", featureSource);

        var hostSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            using Slice;

            namespace HostApp;

            public sealed class HostCreateProductValidator : ISliceValidator<FeatureLib.Features.Products.CreateProduct.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(FeatureLib.Features.Products.CreateProduct.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }

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
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out _, out var generatorDiagnostics);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static diagnostic => diagnostic.Id == "SLICE021"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("CreateProduct.Request", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
        GeneratorDriver driver = CreateDriver();

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
        GeneratorDriver driver = CreateDriver();

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
        GeneratorDriver driver = CreateDriver();

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
        GeneratorDriver driver = CreateDriver();

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
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE005" && d.Severity == DiagnosticSeverity.Error);
    }

    private static CSharpCompilation CreateHostCompilation(
        string assemblyName,
        string source,
        bool includeWasiReference = false,
        bool includeLambdaReference = false,
        IEnumerable<MetadataReference>? extraReferences = null)
        => CreateCompilation(
            assemblyName,
            source,
            outputKind: OutputKind.ConsoleApplication,
            includeEntryPoint: true,
            includeWasiReference,
            includeLambdaReference,
            extraReferences);

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeEntryPoint = false,
        bool includeWasiReference = false,
        bool includeLambdaReference = false,
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
                           && (includeWasiReference
                               || !string.Equals(Path.GetFileName(path), "Slice.Wasi.dll", StringComparison.OrdinalIgnoreCase)))
            .Where(path => includeLambdaReference
                           || !string.Equals(Path.GetFileName(path), "Slice.Lambda.PerFunction.dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList()
            ?? [];

        references.Add(MetadataReference.CreateFromFile(typeof(FeatureAttribute).Assembly.Location));
        if (includeWasiReference)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(WasiHost).Assembly.Location));
        }

        if (includeLambdaReference)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(LambdaInvocationContext).Assembly.Location));
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
        GeneratorDriver driver = CreateDriver();
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
            [new SliceFeatureGenerator().AsSourceGenerator(), CreateSystemTextJsonGenerator().AsSourceGenerator()],
            optionsProvider: new TestAnalyzerConfigOptionsProvider(properties));

    private static IIncrementalGenerator CreateSystemTextJsonGenerator()
    {
        var jsonAssemblyPath = typeof(JsonSerializer).Assembly.Location;
        var runtimeVersion = new DirectoryInfo(Path.GetDirectoryName(jsonAssemblyPath)!).Name;
        var dotnetRoot = Directory.GetParent(Path.GetDirectoryName(jsonAssemblyPath)!)!
            .Parent!
            .Parent!
            .FullName;
        var generatorPath = Path.Combine(
            dotnetRoot,
            "packs",
            "Microsoft.NETCore.App.Ref",
            runtimeVersion,
            "analyzers",
            "dotnet",
            "cs",
            "System.Text.Json.SourceGeneration.dll");
        var generatorType = Assembly.LoadFrom(generatorPath).GetType("System.Text.Json.SourceGeneration.JsonSourceGenerator", throwOnError: true)!;
        return (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
    }

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
