using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.Wasi;

namespace SliceFx.SourceGenerator.Tests;

public class SourceGeneratorCompileTests
{
    [Fact]
    public void Generator_emits_compilable_registrations_with_sanitized_names_and_portability_vocabulary()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("public static class _123_My_App_SliceRegistrations", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public static class _123_My_App_SliceRouteManifest", generatedSource, StringComparison.Ordinal);
        Assert.Contains("namespace SliceFx;", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace SliceFx.Generated;", generatedSource, StringComparison.Ordinal);
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
            using System.ComponentModel.DataAnnotations;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace ValidatorApp.Features.Users;

            [Feature("POST /users")]
            [Filter<AuditFilter>]
            public static class CreateUser
            {
                public sealed record Request([Required] string Name);

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("TryAddScoped<global::SliceFx.ISliceValidator<global::ValidatorApp.Features.Users.CreateUser.Request>, global::ValidatorApp.Features.Users.CreateUserValidator>(services)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetService<global::SliceFx.ISliceValidator<global::ValidatorApp.Features.Users.CreateUser.Request>>(invocationContext.HttpContext.RequestServices)", generatedSource, StringComparison.Ordinal);

        Assert.DoesNotContain("DataAnnotationsValidationFilter", generatedSource, StringComparison.Ordinal);

        var dataAnnotationsIndex = generatedSource.IndexOf("__CreateDataAnnotationsValidationFactory_global__ValidatorApp_Features_Users_CreateUser", StringComparison.Ordinal);
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
            using SliceFx;
            using AliasValidator = SliceFx.ISliceValidator<ValidatorAliasApp.Features.Users.CreateUser.Request>;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE012");
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE011");
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE013");
    }

    [Fact]
    public void Generator_reports_SLICE013_for_unmatched_slice_validators()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE013"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("OtherRequestValidator", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_emit_validator_filter_when_request_has_no_validator()
    {
        var source = """
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("__CreateSliceValidatorFactory_", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<global::SliceFx.ISliceValidator<global::ValidatorNoopApp.Features.Users.CreateUser.Request>>", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_checks_validator_service_type_not_request_dto_type()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("IsService(typeof(global::SliceFx.ISliceValidator<T>))", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsService(typeof(T))", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE011_for_open_generic_slice_validators()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE011"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("open generic", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE011_for_invalid_slice_validator_request_type()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace ValidatorDiagnosticsApp;

            public sealed class IntValidator : ISliceValidator<int>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(int value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;

        var compilation = CreateHostCompilation("ValidatorDiagnosticsApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE011"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("not a supported Slice request type", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE012_for_duplicate_slice_validators_in_one_assembly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE012"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("CreateUser.Request", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_empty_host_registrations_and_manifest_when_no_features_exist()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

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
            using SliceFx;
            using SliceFx.Wasi;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("namespace SliceFx;", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WasiHostBuilder AddSliceWasiRoutes(", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WasiHostBuilder AddSlice(", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceGenerated", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_wasi_routes_dispatch_requests_through_wasi_app()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.Text;
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiRuntimeApp
            {
                public sealed class OrderStore
                {
                    public string Source => "store";
                }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                [JsonSerializable(typeof(global::WasiRuntimeApp.Features.Orders.CreateOrder.Request))]
                [JsonSerializable(typeof(global::WasiRuntimeApp.Features.Orders.CreateOrder.Response))]
                [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
                public partial class WasiJsonContext : JsonSerializerContext;

                public static class RuntimeHarness
                {
                    public static async Task<string> DispatchAsync(string sku)
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.Services.AddSingleton<OrderStore>();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        var body = Encoding.UTF8.GetBytes("{\"sku\":\"" + sku + "\",\"quantity\":4}");

                        var response = await app.DispatchAsync(new WasiRequest(
                            "POST",
                            "/tenants/acme/orders",
                            new Dictionary<string, string>(),
                            "?priority=5",
                            body));

                        return Format(response);
                    }

                    public static async Task<string> DispatchMissingAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        var response = await app.DispatchAsync(new WasiRequest(
                            "GET",
                            "/missing",
                            new Dictionary<string, string>(),
                            null,
                            null));

                        return Format(response);
                    }

                    private static string Format(WasiResponse response)
                        => response.Status.ToString() + "|"
                            + (response.Headers.TryGetValue("Content-Type", out var contentType) ? contentType : "")
                            + "|"
                            + Encoding.UTF8.GetString(response.Body);
                }
            }

            namespace WasiRuntimeApp.Features.Orders
            {
                [Feature("POST /tenants/{tenant}/orders")]
                public static class CreateOrder
                {
                    public sealed record Request(
                        [Required, MinLength(2)] string Sku,
                        [Range(1, 100)] int Quantity);

                    public sealed record Response(string Tenant, string Sku, int Quantity, int Priority, string Source);

                    public static Response Handle(
                        string tenant,
                        Request request,
                        int priority,
                        [FromServices]
                        global::WasiRuntimeApp.OrderStore store,
                        CancellationToken ct)
                        => new(tenant, request.Sku, request.Quantity, priority, store.Source);
                }

                public sealed class CreateOrderValidator : ISliceValidator<CreateOrder.Request>
                {
                    public ValueTask<SliceValidationResult> ValidateAsync(CreateOrder.Request value, CancellationToken ct)
                        => string.Equals(value.Sku, "blocked", StringComparison.Ordinal)
                            ? ValueTask.FromResult(SliceValidationResult.Failure(nameof(value.Sku), "SKU is blocked."))
                            : ValueTask.FromResult(SliceValidationResult.Success);
                }
            }
            """;

        using var assemblyStream = CompileGeneratedAssembly(CreateHostCompilation("WasiRuntimeApp", source, includeWasiReference: true));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var harnessType = assembly.GetType("WasiRuntimeApp.RuntimeHarness", throwOnError: true)!;
        var dispatchAsync = harnessType.GetMethod("DispatchAsync", BindingFlags.Public | BindingFlags.Static)!;
        var dispatchMissingAsync = harnessType.GetMethod("DispatchMissingAsync", BindingFlags.Public | BindingFlags.Static)!;

        var success = await (Task<string>)dispatchAsync.Invoke(null, ["abc"])!;
        var validationFailure = await (Task<string>)dispatchAsync.Invoke(null, ["blocked"])!;
        var missing = await (Task<string>)dispatchMissingAsync.Invoke(null, null)!;

        Assert.StartsWith("200|application/json|", success, StringComparison.Ordinal);
        Assert.Contains("\"tenant\":\"acme\"", success, StringComparison.Ordinal);
        Assert.Contains("\"sku\":\"abc\"", success, StringComparison.Ordinal);
        Assert.Contains("\"priority\":5", success, StringComparison.Ordinal);
        Assert.StartsWith("400|application/problem+json|", validationFailure, StringComparison.Ordinal);
        Assert.Contains("\"Sku\":[\"SKU is blocked.\"]", validationFailure, StringComparison.Ordinal);
        Assert.StartsWith("404|application/problem+json|", missing, StringComparison.Ordinal);
        Assert.Contains("No route matched GET /missing", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_emits_typed_wasi_dispatch_for_SliceResultOfT_features()
    {
        var source = """
            using System.Collections.Generic;
            using System.Text;
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace SliceResultApp
            {
                public sealed record GetItemResponse(string Id, string Name);

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                [JsonSerializable(typeof(global::SliceResultApp.GetItemResponse))]
                [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
                public partial class WasiJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

                public static class RuntimeHarness
                {
                    public static async Task<string> DispatchFoundAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        var response = await app.DispatchAsync(new WasiRequest(
                            "GET", "/items/abc", new Dictionary<string, string>(), null, null));
                        return Format(response);
                    }

                    public static async Task<string> DispatchNotFoundAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        var response = await app.DispatchAsync(new WasiRequest(
                            "GET", "/items/missing", new Dictionary<string, string>(), null, null));
                        return Format(response);
                    }

                    private static string Format(WasiResponse r)
                        => r.Status.ToString() + "|"
                            + (r.Headers.TryGetValue("Content-Type", out var ct) ? ct : "")
                            + "|"
                            + Encoding.UTF8.GetString(r.Body);
                }
            }

            namespace SliceResultApp.Features.Items
            {
                [Feature("GET /items/{id}")]
                public static class GetItem
                {
                    public static Task<global::SliceFx.SliceResult<SliceResultApp.GetItemResponse>> Handle(
                        string id, CancellationToken ct)
                    {
                        if (id == "missing")
                            return Task.FromResult(global::SliceFx.SliceResult<SliceResultApp.GetItemResponse>.NotFound($"Item '{id}' not found."));
                        return Task.FromResult(global::SliceFx.SliceResult<SliceResultApp.GetItemResponse>.Ok(new SliceResultApp.GetItemResponse(id, "Test")));
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("SliceResultApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        // No errors in generator or compilation
        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        // Emitter wires ToWasiResponse with __JsonTypeInfo<payload>
        Assert.Contains("ToWasiResponse", wasiSource, StringComparison.Ordinal);
        Assert.Contains("__JsonTypeInfo<global::SliceResultApp.GetItemResponse>", wasiSource, StringComparison.Ordinal);
        // Manifest records the payload type (GetItemResponse), NOT the SliceResult<T> wrapper
        Assert.Contains("GetItemResponse", manifestSource, StringComparison.Ordinal);

        // Runtime dispatch: success and not-found
        using var assemblyStream = CompileGeneratedAssembly(compilation);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var harness = assembly.GetType("SliceResultApp.RuntimeHarness", throwOnError: true)!;
        var found = await (Task<string>)harness.GetMethod("DispatchFoundAsync")!.Invoke(null, null)!;
        var notFound = await (Task<string>)harness.GetMethod("DispatchNotFoundAsync")!.Invoke(null, null)!;

        Assert.StartsWith("200|application/json|", found, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"abc\"", found, StringComparison.Ordinal);
        Assert.StartsWith("404|application/problem+json|", notFound, StringComparison.Ordinal);
        Assert.Contains("missing", notFound, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_emits_typed_wasi_dispatch_for_non_generic_SliceResult_features()
    {
        var source = """
            using System.Collections.Generic;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace NonGenericSliceResultApp
            {
                public static class RuntimeHarness
                {
                    public static async Task<string> DispatchDeleteAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        var response = await app.DispatchAsync(new WasiRequest(
                            "DELETE", "/items/42", new Dictionary<string, string>(), null, null));
                        return Format(response);
                    }

                    public static async Task<string> DispatchUnauthorizedAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        var response = await app.DispatchAsync(new WasiRequest(
                            "DELETE", "/items/no-auth", new Dictionary<string, string>(), null, null));
                        return Format(response);
                    }

                    private static string Format(WasiResponse r)
                        => r.Status.ToString() + "|"
                            + (r.Headers.TryGetValue("Content-Type", out var ct) ? ct : "")
                            + "|"
                            + Encoding.UTF8.GetString(r.Body);
                }
            }

            namespace NonGenericSliceResultApp.Features.Items
            {
                [Feature("DELETE /items/{id}")]
                public static class DeleteItem
                {
                    public static Task<global::SliceFx.SliceResult> Handle(string id, CancellationToken ct)
                    {
                        if (id == "no-auth")
                            return Task.FromResult(global::SliceFx.SliceResult.Unauthorized());
                        return Task.FromResult(global::SliceFx.SliceResult.NoContent());
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("NonGenericSliceResultApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        // No errors — including no SLICE021 (non-generic SliceResult has no JSON root to check)
        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        // Emitter wires ToWasiResponse() with no args (no JsonTypeInfo needed)
        Assert.Contains("ToWasiResponse()", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("__JsonTypeInfo<", wasiSource, StringComparison.Ordinal);

        // Runtime dispatch: 204 on success, 401 on auth failure
        using var assemblyStream = CompileGeneratedAssembly(compilation);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var harness = assembly.GetType("NonGenericSliceResultApp.RuntimeHarness", throwOnError: true)!;
        var deleteResult = await (Task<string>)harness.GetMethod("DispatchDeleteAsync")!.Invoke(null, null)!;
        var unauthResult = await (Task<string>)harness.GetMethod("DispatchUnauthorizedAsync")!.Invoke(null, null)!;

        Assert.StartsWith("204||", deleteResult, StringComparison.Ordinal);
        Assert.StartsWith("401|application/problem+json|", unauthResult, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_escapes_control_characters_in_wasi_string_literals()
    {
        var source = """
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiEscapingApp.Features.Diagnostics;

            [Feature("GET /control\0")]
            public static class GetControl
            {
                public static WasiResponse Handle() => global::SliceFx.Wasi.WasiResults.NoContent();
            }
            """;

        var compilation = CreateHostCompilation("WasiEscapingApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("/control", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', generatedSource);
    }

    [Fact]
    public void Generator_classifies_wasi_route_parameters_case_insensitively()
    {
        var source = """
            using System;
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiRouteApp.Features.Users;

            [Feature("GET /users/{Id:guid}")]
            public static class GetUser
            {
                public static WasiResponse Handle(Guid id) => global::SliceFx.Wasi.WasiResults.NoContent();
            }
            """;

        var compilation = CreateHostCompilation("WasiRouteApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var id)", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".TryGetFromQuery<global::System.Guid>(ctx, \"id\", out var id)", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_wasi_query_binding_with_nullable_missing_support()
    {
        var source = """
            #nullable enable

            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiQueryApp.Features.Items;

            [Feature("GET /items")]
            public static class ListItems
            {
                public static WasiResponse Handle(int page, int? size, string? filter)
                    => global::SliceFx.Wasi.WasiResults.NoContent();
            }
            """;

        var compilation = CreateHostCompilation("WasiQueryApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".BindFromQuery<int>(ctx, \"page\")", wasiSource, StringComparison.Ordinal);
        Assert.Contains("WasiArgumentBindingStatus.Invalid", wasiSource, StringComparison.Ordinal);
        Assert.Contains("Query value 'page' is missing.", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Query value 'size' is missing.", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Query value 'filter' is missing.", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_honors_explicit_wasi_binding_metadata()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiBindingApp
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

                public sealed class Clock;
            }

            namespace WasiBindingApp.Features.Items
            {
                [Feature("POST /items/{id:guid}")]
                public static class UpdateItem
                {
                    public sealed record Payload(string Name);

                    public static WasiResponse Handle(
                        [FromRoute(Name = "id")] Guid itemId,
                        [FromQuery(Name = "p")] int page,
                        [FromHeader(Name = "x-trace")] string trace,
                        [FromBody] Payload payload,
                        [FromServices]
                        WasiBindingApp.Clock clock)
                        => global::SliceFx.Wasi.WasiResults.NoContent();
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiBindingApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var itemId)", wasiSource, StringComparison.Ordinal);
        Assert.Contains(".BindFromQuery<int>(ctx, \"p\")", wasiSource, StringComparison.Ordinal);
        Assert.Contains(".BindFromHeader<string>(ctx, \"x-trace\")", wasiSource, StringComparison.Ordinal);
        Assert.Contains(".ReadAsync<global::WasiBindingApp.Features.Items.UpdateItem.Payload>", wasiSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService(typeof(global::WasiBindingApp.Clock))", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_infers_shared_wasi_body_contracts()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiSharedBodyApp
            {
                public sealed record CreateItemRequest(string Name);
                public sealed record CreateItemResponse(string Name);

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

            namespace WasiSharedBodyApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public static WasiSharedBodyApp.CreateItemResponse Handle(WasiSharedBodyApp.CreateItemRequest request)
                        => new(request.Name);
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiSharedBodyApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".ReadAsync<global::WasiSharedBodyApp.CreateItemRequest>", wasiSource, StringComparison.Ordinal);
        Assert.Contains("\"WasiSharedBodyApp.CreateItemRequest\"", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_keyed_service_lookup_in_wasi_and_lambda()
    {
        var wasiSource = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Wasi;

            namespace KeyedServiceApp
            {
                public sealed record CreateOrder(string Sku);
                public sealed record OrderResult(string Sku);

                public interface IOrderAuditor { void Audit(string sku); }
                public sealed class ConsoleAuditor : IOrderAuditor { public void Audit(string sku) { } }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace KeyedServiceApp.Features.Orders
            {
                [Feature("POST /orders")]
                public static class PlaceOrder
                {
                    public static KeyedServiceApp.OrderResult Handle(
                        KeyedServiceApp.CreateOrder req,
                        [FromKeyedServices("primary")] KeyedServiceApp.IOrderAuditor auditor)
                    {
                        auditor.Audit(req.Sku);
                        return new(req.Sku);
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("KeyedServiceApp", wasiSource, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedWasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetRequiredKeyedService(ctx.Services, typeof(global::KeyedServiceApp.IOrderAuditor), \"primary\")", generatedWasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService(typeof(global::KeyedServiceApp.IOrderAuditor))", generatedWasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_keyed_service_lookup_in_lambda_function_per_feature()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace KeyedLambdaApp
            {
                public sealed record CreateOrder(string Sku);
                public sealed record OrderResult(string Sku);

                public interface IOrderAuditor { void Audit(string sku); }

                public sealed class Startup : ILambdaFunctionPerFeatureStartup
                {
                    public void ConfigureServices(IServiceCollection services) { }
                }

                [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]
                public sealed class LambdaJsonContext : JsonSerializerContext
                {
                    public static LambdaJsonContext Default { get; } = new();
                    private LambdaJsonContext() : base(null) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }

            namespace KeyedLambdaApp.Features.Orders
            {
                [Feature("POST /orders")]
                [LambdaFunctionStartup(typeof(KeyedLambdaApp.Startup))]
                public static class PlaceOrder
                {
                    public static System.Threading.Tasks.Task<KeyedLambdaApp.OrderResult> Handle(
                        KeyedLambdaApp.CreateOrder req,
                        [FromKeyedServices("primary")] KeyedLambdaApp.IOrderAuditor auditor,
                        System.Threading.CancellationToken ct)
                        => System.Threading.Tasks.Task.FromResult(new KeyedLambdaApp.OrderResult(req.Sku));
                }
            }
            """;

        var compilation = CreateHostCompilation("KeyedLambdaApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetRequiredKeyedService<global::KeyedLambdaApp.IOrderAuditor>(\"primary\")", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetRequiredService<global::KeyedLambdaApp.IOrderAuditor>()", lambdaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_keyed_service_lookup_with_typeof_key_in_wasi()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Wasi;

            namespace TypeofKeyApp
            {
                public sealed record Req(string Val);
                public sealed record Resp(string Val);

                public interface ISvc { }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace TypeofKeyApp.Features
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public static TypeofKeyApp.Resp Handle(
                        TypeofKeyApp.Req req,
                        [FromKeyedServices(typeof(TypeofKeyApp.ISvc))] TypeofKeyApp.ISvc svc)
                        => new(req.Val);
                }
            }
            """;

        var compilation = CreateHostCompilation("TypeofKeyApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetRequiredKeyedService(ctx.Services, typeof(global::TypeofKeyApp.ISvc), typeof(global::TypeofKeyApp.ISvc))", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_SLICE037_and_falls_back_to_unkeyed_for_unsupported_key_constant()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Wasi;

            namespace UnsupportedKeyApp
            {
                public sealed record Req(string Val);
                public sealed record Resp(string Val);

                public interface ISvc { }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace UnsupportedKeyApp.Features
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public static UnsupportedKeyApp.Resp Handle(
                        UnsupportedKeyApp.Req req,
                        [FromKeyedServices(new[] { "a", "b" })] UnsupportedKeyApp.ISvc svc)
                        => new(req.Val);
                }
            }
            """;

        var compilation = CreateHostCompilation("UnsupportedKeyApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.Contains(generatorDiagnostics, static d => d.Id == "SLICE037");
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("GetRequiredKeyedService", wasiSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService(typeof(global::UnsupportedKeyApp.ISvc))", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_skips_wasi_route_with_AsParameters_binding()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Http;
            using SliceFx;
            using SliceFx.Wasi;

            namespace AsParamsApp
            {
                public struct QueryArgs { public string Filter { get; set; } }
                public sealed record Resp(string Filter);

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace AsParamsApp.Features
            {
                [Feature("GET /items")]
                public static class ListItems
                {
                    public static AsParamsApp.Resp Handle([AsParameters] AsParamsApp.QueryArgs args)
                        => new(args.Filter);
                }
            }
            """;

        var compilation = CreateHostCompilation("AsParamsApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("\"GET\"", wasiSource, StringComparison.Ordinal);
        Assert.Contains(generatorDiagnostics, static d => d.Id == "SLICE023");
    }

    [Fact]
    public void Generator_emits_keyed_service_lookup_with_control_char_key_in_wasi()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Wasi;

            namespace CtrlCharKeyApp
            {
                public sealed record Req(string Val);
                public sealed record Resp(string Val);

                public interface ISvc { }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace CtrlCharKeyApp.Features
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public static CtrlCharKeyApp.Resp Handle(
                        CtrlCharKeyApp.Req req,
                        [FromKeyedServices("a\nb")] CtrlCharKeyApp.ISvc svc)
                        => new(req.Val);
                }
            }
            """;

        var compilation = CreateHostCompilation("CtrlCharKeyApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetRequiredKeyedService(ctx.Services, typeof(global::CtrlCharKeyApp.ISvc), \"a\\nb\")", wasiSource, StringComparison.Ordinal);
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
            using SliceFx;

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
                        int[] Items,
                        [Range(1, 10, ErrorMessage = "Count is out of range.")]
                        int Count,
                        [EmailAddress(ErrorMessage = "Email is invalid.")]
                        string? Email);

                    public sealed record Response(string Name);

                    public static Response Handle(Request req) => new(req.Name!);
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiValidationApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE022");
        Assert.Contains("Name is required.", wasiSource, StringComparison.Ordinal);
        Assert.Contains("Name length is invalid.", wasiSource, StringComparison.Ordinal);
        Assert.Contains("At least two items are required.", wasiSource, StringComparison.Ordinal);
        Assert.Contains("Count is out of range.", wasiSource, StringComparison.Ordinal);
        Assert.Contains("EmailAddressAttribute", wasiSource, StringComparison.Ordinal);
        Assert.Contains("Email is invalid.", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WasiValidationRunner.Validate", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ISliceValidator<global::WasiValidationApp.Features.Items.CreateItem.Request>", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_lambda_function_per_feature_handlers_when_opted_in()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaApp
            {
                public sealed class LambdaStartup : ILambdaFunctionPerFeatureStartup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<Clock>();
                    }
                }

                public sealed class Clock;

                [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]

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
                [LambdaFunctionStartup(typeof(LambdaApp.LambdaStartup))]
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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public static class LambdaApp_SliceLambdaFunctionPerFeatureHandlers", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("public static class LambdaApp_SliceLambdaFunctionPerFeatureHandlers_Users_GetUser", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("new global::LambdaApp.LambdaStartup().ConfigureServices(services);", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceValidatorServices(services);", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSliceServices(services);", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("Users_GetUser", lambdaSource, StringComparison.Ordinal);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var id)", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("global::System.Text.Json.JsonException or global::System.FormatException", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ISliceValidator<global::LambdaApp.Features.Users.CreateUser.Request>", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<global::LambdaApp.Clock>()", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("LambdaResponseFactory.Ok<global::LambdaApp.Features.Users.GetUser.Response>", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("\"LambdaApp\"", manifestSource, StringComparison.Ordinal);
        Assert.Contains("\"SliceFx.LambdaApp_SliceLambdaFunctionPerFeatureHandlers_Users_GetUser_", manifestSource, StringComparison.Ordinal);
        Assert.Contains("\"Users_GetUser_", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_lambda_handler_processes_api_gateway_requests()
    {
        var source = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaRuntimeApp
            {
                public sealed class OrderStore
                {
                    public string Source => "store";
                }

                public sealed class LambdaStartup : ILambdaFunctionPerFeatureStartup
                {
                    public void ConfigureServices(IServiceCollection services)
                        => services.AddSingleton<OrderStore>();
                }

                [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]
                [JsonSerializable(typeof(global::LambdaRuntimeApp.Features.Orders.CreateOrder.Request))]
                [JsonSerializable(typeof(global::LambdaRuntimeApp.Features.Orders.CreateOrder.Response))]
                [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
                public partial class LambdaJsonContext : JsonSerializerContext;
            }

            namespace LambdaRuntimeApp.Features.Orders
            {
                [Feature("POST /tenants/{tenant}/orders")]
                [LambdaFunctionStartup(typeof(global::LambdaRuntimeApp.LambdaStartup))]
                public static class CreateOrder
                {
                    public sealed record Request(
                        [Required, MinLength(2)] string Sku,
                        [Range(1, 100)] int Quantity);

                    public sealed record Response(string Tenant, string Sku, int Quantity, int Priority, string Source);

                    public static Response Handle(
                        string tenant,
                        Request request,
                        int priority,
                        [FromServices]
                        global::LambdaRuntimeApp.OrderStore store,
                        CancellationToken ct)
                        => new(tenant, request.Sku, request.Quantity, priority, store.Source);
                }

                public sealed class CreateOrderValidator : ISliceValidator<CreateOrder.Request>
                {
                    public ValueTask<SliceValidationResult> ValidateAsync(CreateOrder.Request value, CancellationToken ct)
                        => string.Equals(value.Sku, "blocked", StringComparison.Ordinal)
                            ? ValueTask.FromResult(SliceValidationResult.Failure(nameof(value.Sku), "SKU is blocked."))
                            : ValueTask.FromResult(SliceValidationResult.Success);
                }
            }
            """;

        using var assemblyStream = CompileGeneratedAssembly(CreateHostCompilation("LambdaRuntimeApp", source, includeLambdaReference: true));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var handlers = assembly.GetTypes()
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(static method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(APIGatewayHttpApiV2ProxyRequest)
                    && parameters[1].ParameterType == typeof(ILambdaContext);
            })
            .ToArray();
        var handler = Assert.Single(handlers);
        var jsonContext = assembly.GetType("LambdaRuntimeApp.LambdaJsonContext", throwOnError: true)!
            .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        var getTypeInfo = jsonContext.GetType().GetMethod("GetTypeInfo", [typeof(Type)])!;
        handler.DeclaringType!
            .GetProperty("JsonTypeInfoProvider", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, (Func<Type, System.Text.Json.Serialization.Metadata.JsonTypeInfo?>)(type =>
                (System.Text.Json.Serialization.Metadata.JsonTypeInfo?)getTypeInfo.Invoke(jsonContext, [type])));

        var success = await InvokeLambdaHandlerAsync(
            handler,
            new APIGatewayHttpApiV2ProxyRequest
            {
                PathParameters = new Dictionary<string, string> { ["tenant"] = "acme" },
                QueryStringParameters = new Dictionary<string, string> { ["priority"] = "5" },
                Body = /*lang=json,strict*/ """{"sku":"abc","quantity":4}""",
            });
        var validationFailure = await InvokeLambdaHandlerAsync(
            handler,
            new APIGatewayHttpApiV2ProxyRequest
            {
                PathParameters = new Dictionary<string, string> { ["tenant"] = "acme" },
                QueryStringParameters = new Dictionary<string, string> { ["priority"] = "5" },
                Body = /*lang=json,strict*/ """{"sku":"blocked","quantity":4}""",
            });

        Assert.Equal(200, success.StatusCode);
        Assert.Equal("application/json", success.Headers["Content-Type"]);
        Assert.Contains("\"tenant\":\"acme\"", success.Body, StringComparison.Ordinal);
        Assert.Contains("\"sku\":\"abc\"", success.Body, StringComparison.Ordinal);
        Assert.Contains("\"priority\":5", success.Body, StringComparison.Ordinal);
        Assert.Equal(400, validationFailure.StatusCode);
        Assert.Contains("\"Sku\":[\"SKU is blocked.\"]", validationFailure.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_valid_lambda_handler_identifiers_for_digit_leading_tags()
    {
        var source = """
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaIdentifierApp.Features.Users;

            [Feature("GET /users", Tag = "123")]
            public static class GetUser
            {
                public static string Handle() => "ok";
            }
            """;

        var compilation = CreateHostCompilation("LambdaIdentifierApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public static class LambdaIdentifierApp_SliceLambdaFunctionPerFeatureHandlers__123_GetUser_", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse> _123_GetUser_", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("\"_123_GetUser_", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE036_for_duplicate_lambda_artifact_ids()
    {
        var source = """
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaArtifactApp.Features.First
            {
                [Feature("GET /first", Tag = "A-B")]
                public static class GetThing
                {
                    public static string Handle() => "ok";
                }
            }

            namespace LambdaArtifactApp.Features.Second
            {
                [Feature("GET /second", Tag = "A_B")]
                public static class GetThing
                {
                    public static string Handle() => "ok";
                }
            }
            """;

        var compilation = CreateHostCompilation("LambdaArtifactApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static diagnostic => diagnostic.Id == "SLICE036"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("a-b-getthing", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_registers_referenced_validators_for_lambda_function_per_feature_body_requests()
    {
        var featureSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace FeatureLib.Features.Users;

            [Feature("POST /shared-users")]
            public static class SharedCreateUser
            {
                public sealed record Request(string Name);

                public static string Handle(Request req) => req.Name;
            }

            public sealed class SharedCreateUserValidator : ISliceValidator<SharedCreateUser.Request>
            {
                public ValueTask<SliceValidationResult> ValidateAsync(SharedCreateUser.Request value, CancellationToken ct)
                    => ValueTask.FromResult(SliceValidationResult.Success);
            }
            """;
        using var featureAssembly = CompileGeneratedAssembly("FeatureLib", featureSource);

        var hostSource = """
            using Microsoft.AspNetCore.Mvc;
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace HostApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public static string Handle([FromBody] FeatureLib.Features.Users.SharedCreateUser.Request req)
                    => req.Name;
            }
            """;

        var hostCompilation = CreateHostCompilation(
            "HostApp",
            hostSource,
            includeLambdaReference: true,
            extraReferences: [MetadataReference.CreateFromImage(featureAssembly.ToArray())]);
        GeneratorDriver driver = CreateDriver(("SliceFxAggregateReferences", "true"));

        driver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("TryAddScoped<global::SliceFx.ISliceValidator<global::FeatureLib.Features.Users.SharedCreateUser.Request>, global::FeatureLib.Features.Users.SharedCreateUserValidator>(services)", lambdaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_lambda_query_binding_with_nullable_missing_support()
    {
        var source = """
            #nullable enable

            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaQueryApp.Features.Items;

            [Feature("GET /items")]
            public static class ListItems
            {
                public static void Handle(int page, int? size, string? filter)
                {
                }
            }
            """;

        var compilation = CreateHostCompilation("LambdaQueryApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("BindFromQuery<int>(ctx, \"page\")", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("LambdaArgumentBindingStatus.Invalid", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("Query value 'page' is missing.", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Query value 'size' is missing.", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Query value 'filter' is missing.", lambdaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_honors_explicit_lambda_binding_metadata_and_proxy_response_passthrough()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Amazon.Lambda.APIGatewayEvents;
            using Microsoft.AspNetCore.Mvc;
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaBindingApp
            {
                [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]
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

            namespace LambdaBindingApp.Features.Items
            {
                [Feature("POST /items/{id:guid}")]
                public static class UpdateItem
                {
                    public sealed record Payload(string Name);

                    public static APIGatewayHttpApiV2ProxyResponse Handle(
                        [FromRoute(Name = "id")] Guid itemId,
                        [FromQuery(Name = "p")] int page,
                        [FromHeader(Name = "x-trace")] string trace,
                        [FromBody] Payload payload,
                        [FromServices] string service)
                        => new() { StatusCode = 202 };
                }
            }
            """;

        var compilation = CreateHostCompilation("LambdaBindingApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<global::System.Guid>(ctx, \"id\", out var itemId)", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("BindFromQuery<int>(ctx, \"p\")", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("BindFromHeader<string>(ctx, \"x-trace\")", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("ReadAsync<global::LambdaBindingApp.Features.Items.UpdateItem.Payload>", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<string>()", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("return __result;", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LambdaResponseFactory.Ok<global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse>", lambdaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_lambda_function_per_feature_without_global_json_context()
    {
        var source = """
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE032");
        Assert.Contains("JsonTypeInfoProvider", lambdaSource, StringComparison.Ordinal);
        Assert.Contains("\"eligible\"", manifestSource, StringComparison.Ordinal);
        Assert.Contains("SliceFx.MissingJsonLambdaApp_SliceLambdaFunctionPerFeatureHandlers", manifestSource, StringComparison.Ordinal);
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
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE040" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_reports_invalid_slice_json_context_override()
    {
        var source = """
            using SliceFx;

            namespace InvalidJsonContextApp;

            [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]
            public sealed class NotAJsonContext
            {
            }
            """;

        var compilation = CreateHostCompilation("InvalidJsonContextApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE041" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_omits_lambda_function_per_feature_metadata_when_handlers_are_not_emitted()
    {
        var source = """
            using SliceFx;

            namespace NoLambdaHandlersApp.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """;

        var compilation = CreateHostCompilation("NoLambdaHandlersApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var compactManifestSource = manifestSource
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);
        Assert.Contains("string? LambdaFunctionPerFeatureStatus", manifestSource, StringComparison.Ordinal);
        Assert.Contains("string? LambdaFunctionPerFeatureArtifactId", manifestSource, StringComparison.Ordinal);
        Assert.Contains("string? WasiDispatchStatus", manifestSource, StringComparison.Ordinal);
        // 26 args: [..., manifestSchemaVersion="1", wasiStatus="eligible", ..., lambdaRuntimeIdentifier=null, serializedSliceFilterTypes=null]
        Assert.Contains("\"1\",\"eligible\",null,null,null,null,null,null,null)]", compactManifestSource, StringComparison.Ordinal);
        Assert.Contains("\"1\",true,\"portable\",null,\"eligible\",null,null,null,null,null,null,null,null,null,null,null,global::System.Array.Empty<string>())", compactManifestSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"shared\"", compactManifestSource, StringComparison.Ordinal);
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
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace LambdaCatchAllApp
            {
                [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]
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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var lambdaSource = GetGeneratedSource(driver, "SliceLambdaFunctionPerFeatureHandlers.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".TryGetFromRoute<string>(ctx, \"path\", out var path)", lambdaSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".TryGetFromQuery<string>(ctx, \"path\", out var path)", lambdaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_invalid_lambda_startup_type()
    {
        var source = """
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

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
            [LambdaFunctionStartup(typeof(InvalidStartupApp.BadStartup))]
            public static class GetHealth
            {
                public static void Handle()
                {
                }
            }
            """;

        var compilation = CreateHostCompilation("InvalidStartupApp", source, includeLambdaReference: true);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE035" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_excludes_wasi_body_routes_without_wasi_json_context()
    {
        var source = """
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE021" && diagnostic.Severity == DiagnosticSeverity.Warning);
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
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var registrationSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("TryAddScoped<global::SliceFx.ISliceValidator<global::WasiValidatorApp.Features.Items.CreateItem.Request>, global::WasiValidatorApp.Features.Items.CreateItemValidator>(services)", registrationSource, StringComparison.Ordinal);
        Assert.Contains("ServiceProviderServiceExtensions.GetService<global::SliceFx.ISliceValidator<global::WasiValidatorApp.Features.Items.CreateItem.Request>>(ctx.Services)", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<global::SliceFx.ISliceValidator", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService(typeof(global::SliceFx.ISliceValidator", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_excludes_wasi_routes_that_require_reflection_validation()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using SliceFx;

            namespace WasiReflectionValidationApp.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public sealed record Request([CustomValidation] int Count);

                public sealed record Response(int Count);

                public static Response Handle(Request req) => new(req.Count);
            }

            public sealed class CustomValidationAttribute : ValidationAttribute
            {
                public override bool IsValid(object? value) => true;
            }
            """;

        var compilation = CreateHostCompilation("WasiReflectionValidationApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE010" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE022");
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
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(4, generatorDiagnostics.Count(static diagnostic => diagnostic.Id == "SLICE020"));
        Assert.DoesNotContain("table.Add(", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/ok\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/union\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/task-ok\"", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/value-task-ok\"", wasiSource, StringComparison.Ordinal);
        Assert.Equal(8, CountOccurrences(generatedSource, "\"aspnet-only\""));
    }

    [Fact]
    public void Generator_requires_explicit_referenced_feature_assembly_aggregation()
    {
        var featureSource = """
            using SliceFx;

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
            using SliceFx;

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

        driver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var runResult = driver.GetRunResult();

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static diagnostic => diagnostic.Id == "SLICE050"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("FeatureLib", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.DoesNotContain("FeatureLib_SliceRegistrations", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_aggregates_referenced_feature_assemblies_when_explicitly_enabled()
    {
        var featureSource = """
            using SliceFx;

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
            using SliceFx;

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
        var driver = CreateDriver(("SliceFxAggregateReferences", "true"));

        var runDriver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, runDriver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE050");
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("global::SliceFx.FeatureLib_SliceRegistrations.AddSliceServices(services);", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::SliceFx.FeatureLib_SliceRegistrations.MapSliceRoutes(app);", generatedSource, StringComparison.Ordinal);
        Assert.Contains("[assembly: global::SliceFx.SliceAggregatedFeatureAssemblyAttribute(\"FeatureLib\")]", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_duplicate_endpoint_names_from_referenced_feature_assemblies()
    {
        var featureSource = """
            using SliceFx;

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
            using SliceFx;

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
        GeneratorDriver driver = CreateDriver(("SliceFxAggregateReferences", "true"));

        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static diagnostic =>
            diagnostic.Id == "SLICE005"
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
            using SliceFx;

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
            using SliceFx;

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
        GeneratorDriver driver = CreateDriver(("SliceFxAggregateReferences", "true"));

        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static diagnostic => diagnostic.Id == "SLICE012"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("CreateProduct.Request", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_can_disable_referenced_feature_assembly_aggregation()
    {
        var featureSource = """
            using SliceFx;

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
            using SliceFx;

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
        var driver = CreateDriver(("SliceFxAggregateReferences", "false"));

        var runDriver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, runDriver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE050");
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("FeatureLib_SliceRegistrations", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_invalid_referenced_feature_assembly_aggregation_value()
    {
        var featureSource = """
            using SliceFx;

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
            using SliceFx;

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
        var driver = CreateDriver(("SliceFxAggregateReferences", "sure"));

        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static diagnostic => diagnostic.Id == "SLICE051"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("sure", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_can_allow_list_referenced_feature_assembly_aggregation()
    {
        var firstFeatureSource = """
            using SliceFx;

            namespace FirstFeatureLib.Features.Products;

            [Feature("GET /products")]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }
            """;
        var secondFeatureSource = """
            using SliceFx;

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
            using SliceFx;

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
        var driver = CreateDriver(("SliceFxReferencedAssemblies", "FirstFeatureLib"));

        var runDriver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, runDriver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "SLICE050");
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("global::SliceFx.FirstFeatureLib_SliceRegistrations.AddSliceServices(services);", generatedSource, StringComparison.Ordinal);
        Assert.Contains("[assembly: global::SliceFx.SliceAggregatedFeatureAssemblyAttribute(\"FirstFeatureLib\")]", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondFeatureLib_SliceRegistrations", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_registration_steps_in_the_expected_order()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.ComponentModel.DataAnnotations;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OrderedApp.Features.Products;

            [Feature("GET /products", Summary = "List products")]
            [Filter<FirstFilter>]
            [Filter<SecondFilter>]
            public static class ListProducts
            {
                public sealed record Request([Required] string Name);

                public static string Handle(Request request) => request.Name;
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

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        AssertBefore(generatedSource, "app.MapMethods(", "__CreateDataAnnotationsValidationFactory_global__OrderedApp_Features_Products_ListProducts");
        AssertBefore(generatedSource, "__CreateDataAnnotationsValidationFactory_global__OrderedApp_Features_Products_ListProducts", ".AddEndpointFilter<global::OrderedApp.Features.Products.FirstFilter>()");
        AssertBefore(generatedSource, ".AddEndpointFilter<global::OrderedApp.Features.Products.FirstFilter>()", ".AddEndpointFilter<global::OrderedApp.Features.Products.SecondFilter>()");
        AssertBefore(generatedSource, ".AddEndpointFilter<global::OrderedApp.Features.Products.SecondFilter>()", ".WithTags(\"Products\")");
        AssertBefore(generatedSource, ".WithTags(\"Products\")", ".WithName(\"Products.ListProducts\")");
        AssertBefore(generatedSource, ".WithName(\"Products.ListProducts\")", ".WithSummary(\"List products\")");
    }

    [Fact]
    public void Generator_uses_explicit_feature_name_for_endpoint_name()
    {
        var source = """
            using SliceFx;

            namespace NamedApp.Features.Products;

            [Feature("GET /products", Name = "Legacy.Products.List", Summary = "List products")]
            public static class ListProducts
            {
                public static string Handle() => "ok";
            }
            """;

        var compilation = CreateHostCompilation("NamedApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.Contains(".WithName(\"Legacy.Products.List\")", generatedSource, StringComparison.Ordinal);
        Assert.Contains("SliceFeatureRouteAttribute(\"Legacy.Products.List\"", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".WithName(\"Products.ListProducts\")", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE060_and_SLICE061_for_literal_raw_minimal_api_overlaps()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using SliceFx;

            namespace RawOverlapApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public static string Handle() => "ok";
            }

            namespace RawOverlapApp;

            public static class Routes
            {
                public static void Map(WebApplication app)
                {
                    app.MapPost("/users", () => "raw").WithName("Users.CreateUser");
                }
            }
            """;

        var compilation = CreateHostCompilation("RawOverlapApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE060" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE061" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_does_not_report_raw_minimal_api_overlap_for_dynamic_routes_or_names()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using SliceFx;

            namespace DynamicRawApp.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public static string Handle() => "ok";
            }

            namespace DynamicRawApp;

            public static class Routes
            {
                public static void Map(WebApplication app)
                {
                    var route = "/users";
                    var name = "Users.CreateUser";
                    app.MapPost(route, () => "raw").WithName(name);
                }
            }
            """;

        var compilation = CreateHostCompilation("DynamicRawApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Id is "SLICE060" or "SLICE061");
    }

    [Fact]
    public void Generator_reports_SLICE060_for_literal_map_methods_overlap()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using SliceFx;

            namespace RawMapMethodsApp.Features.Users;

            [Feature("PUT /users")]
            public static class UpdateUser
            {
                public static string Handle() => "ok";
            }

            namespace RawMapMethodsApp;

            public static class Routes
            {
                public static void Map(WebApplication app)
                {
                    app.MapMethods("/users", new[] { "GET", "PUT" }, () => "raw");
                }
            }
            """;

        var compilation = CreateHostCompilation("RawMapMethodsApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE060" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_reports_SLICE060_for_nested_literal_map_group_overlap()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using SliceFx;

            namespace NestedGroupApp.Features.Users;

            [Feature("POST /api/v1/users")]
            public static class CreateUser
            {
                public static string Handle() => "ok";
            }

            namespace NestedGroupApp;

            public static class Routes
            {
                public static void Map(WebApplication app)
                {
                    app.MapGroup("/api").MapGroup("/v1").MapPost("/users", () => "raw");
                }
            }
            """;

        var compilation = CreateHostCompilation("NestedGroupApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE060" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_reports_SLICE007_when_filter_order_violates_FilterOrderHint()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE007"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("AuthFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("LoggingFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_report_SLICE007_when_filter_order_matches_FilterOrderHint()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Id == "SLICE007");
    }

    [Fact]
    public void Generator_emits_closed_generic_filter_registrations()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using SliceFx;

            namespace GenericFilterApp.Features.Security;

            [Feature("DELETE /users/{id:guid}")]
            [Filter<RequireApiKeyFilter<AdminPolicy>>]
            public static class DeleteUser
            {
                public static string Handle() => "deleted";
            }

            [Feature("POST /users/{id:guid}/lock")]
            [Filter<RequireApiKeyFilter<AdminPolicy>>]
            public static class LockUser
            {
                public static string Handle() => "locked";
            }

            [Feature("GET /reports")]
            [Filter<RequireApiKeyFilter<ReadOnlyPolicy>>]
            public static class GetReports
            {
                public static string Handle() => "reports";
            }

            public sealed class RequireApiKeyFilter<TPolicy> : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }

            public sealed class AdminPolicy;
            public sealed class ReadOnlyPolicy;
            """;

        var compilation = CreateHostCompilation("GenericFilterApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var generatedSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");
        var adminFilter = "global::GenericFilterApp.Features.Security.RequireApiKeyFilter<global::GenericFilterApp.Features.Security.AdminPolicy>";
        var readOnlyFilter = "global::GenericFilterApp.Features.Security.RequireApiKeyFilter<global::GenericFilterApp.Features.Security.ReadOnlyPolicy>";

        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(1, CountOccurrences(generatedSource, $"services.AddScoped<{adminFilter}>();"));
        Assert.Equal(1, CountOccurrences(generatedSource, $"services.AddScoped<{readOnlyFilter}>();"));
        Assert.Equal(2, CountOccurrences(generatedSource, $".AddEndpointFilter<{adminFilter}>()"));
        Assert.Equal(1, CountOccurrences(generatedSource, $".AddEndpointFilter<{readOnlyFilter}>()"));
    }

    [Fact]
    public void Generator_reports_SLICE007_when_FilterOrderHint_targets_closed_generic_filter()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using SliceFx;

            namespace GenericOrderHintApp.Features.Security;

            [Feature("DELETE /users/{id:guid}")]
            [Filter<AuditFilter>]
            [Filter<RequireApiKeyFilter<AdminPolicy>>]
            public static class DeleteUser
            {
                public static string Handle() => "deleted";
            }

            public sealed class RequireApiKeyFilter<TPolicy> : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }

            [FilterOrderHint(After = typeof(RequireApiKeyFilter<AdminPolicy>))]
            public sealed class AuditFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }

            public sealed class AdminPolicy;
            """;

        var compilation = CreateHostCompilation("GenericOrderHintApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE007"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("RequireApiKeyFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("AdminPolicy", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_closed_generic_filter_resolves_from_DI_and_runs()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using System.IO;
            using System.Linq;
            using System.Threading.Tasks;

            #nullable enable

            namespace RuntimeFilterApp.Features.Health;

            [Feature("GET /health")]
            [Filter<RecordingFilter<AdminPolicy>>]
            public static class GetHealth
            {
                public static string Handle([FromServices] InvocationLog log)
                {
                    log.Value += "handler";
                    return "ok";
                }
            }

            public sealed class RecordingFilter<TPolicy>(InvocationLog log) : IEndpointFilter
            {
                public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                {
                    log.Value += "filter:";
                    var result = await next(context).ConfigureAwait(false);
                    log.Value += ":after";
                    return result;
                }
            }

            public sealed class AdminPolicy;

            public sealed class InvocationLog
            {
                public string Value { get; set; } = "";
            }

            public static class RuntimeHarness
            {
                public static async Task<string> InvokeAsync()
                {
                    var builder = WebApplication.CreateSlimBuilder();
                    builder.Services.AddSlice();
                    builder.Services.AddScoped<InvocationLog>();
                    await using var app = builder.Build();
                    app.MapSlices();

                    var endpoint = ((IEndpointRouteBuilder)app)
                        .DataSources
                        .SelectMany(static dataSource => dataSource.Endpoints)
                        .OfType<RouteEndpoint>()
                        .Single();

                    await using var scope = app.Services.CreateAsyncScope();
                    var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
                    var context = new DefaultHttpContext
                    {
                        RequestServices = scope.ServiceProvider,
                    };
                    context.Response.Body = new MemoryStream();

                    await endpoint.RequestDelegate!(context).ConfigureAwait(false);
                    return log.Value;
                }
            }
            """;

        using var assemblyStream = CompileGeneratedHostAssembly("RuntimeFilterApp", source);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var harnessType = assembly.GetType("RuntimeFilterApp.Features.Health.RuntimeHarness", throwOnError: true)!;
        var invokeAsync = harnessType.GetMethod("InvokeAsync", BindingFlags.Public | BindingFlags.Static)!;

        var log = await (Task<string>)invokeAsync.Invoke(null, null)!;

        Assert.Equal("filter:handler:after", log);
    }

    [Fact]
    public async Task MapSlices_composes_with_route_group_prefix()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;
            using SliceFx;
            using System.Linq;
            using System.Threading.Tasks;

            namespace RuntimeGroupApp.Features.Users;

            [Feature("GET /users/{id}")]
            public static class GetUser
            {
                public static string Handle(int id) => id.ToString();
            }

            public static class RuntimeHarness
            {
                public static async Task<string> GetRoutePatternAsync()
                {
                    var builder = WebApplication.CreateSlimBuilder();
                    builder.Services.AddSlice();
                    await using var app = builder.Build();
                    app.MapGroup("/api").MapSlices();

                    return ((IEndpointRouteBuilder)app)
                        .DataSources
                        .SelectMany(static dataSource => dataSource.Endpoints)
                        .OfType<RouteEndpoint>()
                        .Single()
                        .RoutePattern
                        .RawText!;
                }
            }
            """;

        using var assemblyStream = CompileGeneratedHostAssembly("RuntimeGroupApp", source);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var harnessType = assembly.GetType("RuntimeGroupApp.Features.Users.RuntimeHarness", throwOnError: true)!;
        var getRoutePatternAsync = harnessType.GetMethod("GetRoutePatternAsync", BindingFlags.Public | BindingFlags.Static)!;

        var routePattern = await (Task<string>)getRoutePatternAsync.Invoke(null, null)!;

        Assert.Equal("/api/users/{id}", routePattern);
    }

    [Fact]
    public void Runtime_generated_required_validation_is_type_aware()
    {
        var source = """
            using SliceFx;
            using System.ComponentModel.DataAnnotations;

            #nullable enable

            namespace RuntimeRequiredApp.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public sealed record Request(
                    [Required] string? Name,
                    [Required] int? Count,
                    [Required] int Quantity);

                public static string Handle(Request request) => request.Name!;
            }

            """;

        using var assemblyStream = CompileGeneratedHostAssembly("RuntimeRequiredApp", source);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var requestType = assembly.GetType("RuntimeRequiredApp.Features.Items.CreateItem+Request", throwOnError: true)!;
        var registrationsType = assembly.GetType("SliceFx.RuntimeRequiredApp_SliceRegistrations", throwOnError: true)!;
        var validate = registrationsType.GetMethod(
            "__ValidateDataAnnotations_global__RuntimeRequiredApp_Features_Items_CreateItem_0",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var emptyNameErrors = validate.Invoke(null, [Activator.CreateInstance(requestType, ["", 1, 0])]);
        var missingNullableErrors = validate.Invoke(null, [Activator.CreateInstance(requestType, ["ok", null, 0])]);
        var validValueTypeErrors = validate.Invoke(null, [Activator.CreateInstance(requestType, ["ok", 1, 0])]);

        Assert.NotNull(emptyNameErrors);
        Assert.NotNull(missingNullableErrors);
        Assert.Null(validValueTypeErrors);
    }

    [Fact]
    public void Runtime_generated_supported_data_annotations_match_framework()
    {
        var source = """
            using SliceFx;
            using System.ComponentModel.DataAnnotations;

            #nullable enable

            namespace RegexAspNetApp.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public sealed record Request(
                    [property: Required(ErrorMessage = "Name is required.")] string? Name,
                    [property: StringLength(5, MinimumLength = 2, ErrorMessage = "Sized is invalid.")] string? Sized,
                    [property: MinLength(2, ErrorMessage = "Items is invalid.")] int[] Items,
                    [property: MaxLength(2, ErrorMessage = "Tags is invalid.")] string[] Tags,
                    [property: EmailAddress(ErrorMessage = "Email is invalid.")] string? Email,
                    [property: Url(ErrorMessage = "Website is invalid.")] string? Website,
                    [property: RegularExpression("[A-Z]+", ErrorMessage = "Code is invalid.")] string? Code,
                    [property: RegularExpression(@"\d{3}", ErrorMessage = "Number is invalid.")] int Number,
                    [property: Range(1, 10, ErrorMessage = "Count is invalid.")] int Count);

                public static string Handle(Request request) => request.Name!;
            }

            """;

        using var assemblyStream = CompileGeneratedHostAssembly("RegexAspNetApp", source);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var requestType = assembly.GetType("RegexAspNetApp.Features.Items.CreateItem+Request", throwOnError: true)!;
        var validate = GetGeneratedValidationMethod(
            assembly,
            "SliceFx.RegexAspNetApp_SliceRegistrations",
            "__ValidateDataAnnotations_global__RegexAspNetApp_Features_Items_CreateItem_0");

        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("Alice", "abcd", [1, 2], ["blue"], "alice@example.com", "https://example.com", "ABC", 123, 5));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("Alice", "abcd", [1, 2], ["blue"], "alice@example.com", "https://example.com", "", 123, 5));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("Alice", "abcd", [1, 2], ["blue"], "alice@example.com", "https://example.com", "abcDEFxyz", 123, 5));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest(null, "a", [1], ["blue", "green", "red"], "not-email", "not-url", "abcDEFxyz", 12, 99));

        object CreateRequest(
            string? name,
            string? sized,
            int[] items,
            string[] tags,
            string? email,
            string? website,
            string? code,
            int number,
            int count)
        {
            return Activator.CreateInstance(requestType, [name, sized, items, tags, email, website, code, number, count])!;
        }
    }

    [Fact]
    public void Runtime_generated_wasi_regular_expression_validation_matches_framework()
    {
        var source = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using SliceFx;

            #nullable enable

            namespace RegexWasiApp
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

            namespace RegexWasiApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public sealed record Request(
                        [property: RegularExpression("[A-Z]+", ErrorMessage = "Code is invalid.")] string? Code,
                        [property: RegularExpression(@"\d{3}", ErrorMessage = "Number is invalid.")] int Number);

                    public static string Handle(Request request) => request.Code ?? "";
                }
            }

            """;

        using var assemblyStream = CompileGeneratedAssembly(CreateHostCompilation("RegexWasiApp", source, includeWasiReference: true));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var requestType = assembly.GetType("RegexWasiApp.Features.Items.CreateItem+Request", throwOnError: true)!;
        var validate = GetGeneratedValidationMethod(
            assembly,
            "SliceFx.RegexWasiApp_SliceWasiRegistrations",
            "__Validate_global__RegexWasiApp_Features_Items_CreateItem_0");

        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("ABC", 123));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("", 123));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("abcDEFxyz", 123));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("ABC", 12));

        object CreateRequest(string? code, int number)
        {
            return Activator.CreateInstance(requestType, [code, number])!;
        }
    }

    [Fact]
    public void Runtime_generated_lambda_regular_expression_validation_matches_framework()
    {
        var source = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using SliceFx;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            #nullable enable

            namespace RegexLambdaApp
            {
                [SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]
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

            namespace RegexLambdaApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public sealed record Request(
                        [property: RegularExpression("[A-Z]+", ErrorMessage = "Code is invalid.")] string? Code,
                        [property: RegularExpression(@"\d{3}", ErrorMessage = "Number is invalid.")] int Number);

                    public static string Handle(Request request) => request.Code ?? "";
                }
            }

            """;

        using var assemblyStream = CompileGeneratedAssembly(CreateHostCompilation("RegexLambdaApp", source, includeLambdaReference: true));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var requestType = assembly.GetType("RegexLambdaApp.Features.Items.CreateItem+Request", throwOnError: true)!;
        const string validationMethodName = "__Validate_global__RegexLambdaApp_Features_Items_CreateItem_0";
        var handlerType = assembly.GetTypes().Single(static type =>
            type.FullName?.StartsWith("SliceFx.RegexLambdaApp_SliceLambdaFunctionPerFeatureHandlers_Items_CreateItem_", StringComparison.Ordinal) == true
            && type.GetMethod(validationMethodName, BindingFlags.NonPublic | BindingFlags.Static) is not null);
        var validate = handlerType.GetMethod(
            validationMethodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;

        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("ABC", 123));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("", 123));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("abcDEFxyz", 123));
        AssertGeneratedValidationMatchesFramework(validate, CreateRequest("ABC", 12));

        object CreateRequest(string? code, int number)
        {
            return Activator.CreateInstance(requestType, [code, number])!;
        }
    }

    [Fact]
    public void Generator_reports_SLICE001_for_feature_missing_handle_method()
    {
        var source = """
            using SliceFx;

            namespace App.Features.Orders;

            [Feature("GET /orders")]
            public static class GetOrders
            {
                public record Response(int Count);
            }
            """;

        var compilation = CreateCompilation("App", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_reports_SLICE002_for_non_public_handle_method()
    {
        var source = """
            using SliceFx;

            namespace App.Features.Orders;

            [Feature("GET /orders")]
            public static class GetOrders
            {
                internal static string Handle() => "ok";
            }
            """;

        var compilation = CreateCompilation("App", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_reports_SLICE002_for_non_static_handle_method()
    {
        var source = """
            using SliceFx;

            namespace App.Features.Orders;

            [Feature("GET /orders")]
            public static class GetOrders
            {
                public string Handle() => "ok";
            }
            """;

        var compilation = CreateCompilation("App", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_reports_SLICE003_for_feature_with_multiple_handle_methods()
    {
        var source = """
            using SliceFx;

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

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(generatorDiagnostics, static d =>
            d.Id == "SLICE003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Diagnostic_catalog_matches_release_metadata_and_docs()
    {
        var descriptors = GetDiagnosticCatalog();
        var expected = new[]
        {
            new DiagnosticCatalogEntry("SLICE001", "MissingHandleMethod", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE002", "HandleNotPublicStatic", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE003", "AmbiguousHandleMethod", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE004", "InvalidRouteFormat", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE005", "DuplicateEndpointName", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE006", "TagInferenceFallback", "Slice", DiagnosticSeverity.Info),
            new DiagnosticCatalogEntry("SLICE007", "FilterOrderViolation", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE008", "CrossLayerFilterOrderHint", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE010", "UnsupportedValidationForAspNet", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE011", "InvalidSliceValidator", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE012", "DuplicateSliceValidator", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE013", "UnmatchedSliceValidator", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE020", "UnsupportedReturnTypeForWasi", "Slice", DiagnosticSeverity.Info),
            new DiagnosticCatalogEntry("SLICE021", "MissingWasiJsonContext", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE022", "UnsupportedValidationForWasi", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE023", "UnsupportedParameterForWasi", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE030", "UnsupportedReturnTypeForLambdaFunctionPerFeature", "Slice", DiagnosticSeverity.Info),
            new DiagnosticCatalogEntry("SLICE031", "UnsupportedFilterForLambdaFunctionPerFeature", "Slice", DiagnosticSeverity.Info),
            new DiagnosticCatalogEntry("SLICE032", "MissingLambdaJsonContext", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE033", "UnsupportedParameterForLambdaFunctionPerFeature", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE034", "UnsupportedValidationForLambdaFunctionPerFeature", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE035", "InvalidLambdaFunctionPerFeatureStartupType", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE036", "DuplicateLambdaFunctionPerFeatureArtifactId", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE037", "UnsupportedKeyedServiceKey", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE040", "DuplicateJsonContextOverride", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE041", "InvalidJsonContextOverride", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE050", "UnconfiguredReferencedSliceModules", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE051", "InvalidSliceFxAggregateReferences", "Slice", DiagnosticSeverity.Error),
            new DiagnosticCatalogEntry("SLICE060", "RawMinimalApiRouteOverlap", "Slice", DiagnosticSeverity.Warning),
            new DiagnosticCatalogEntry("SLICE061", "RawMinimalApiEndpointNameOverlap", "Slice", DiagnosticSeverity.Warning),
        };

        Assert.Equal(expected, descriptors);

        var repositoryRoot = FindRepositoryRoot();
        var releaseCatalog = ReadAnalyzerReleaseCatalog(repositoryRoot).ToArray();
        var expectedIds = expected.Select(static entry => entry.Id).ToArray();
        Assert.DoesNotContain(releaseCatalog, entry => Array.IndexOf(expectedIds, entry.Id) < 0);
        Assert.Equal(expected, releaseCatalog.OrderBy(entry => Array.IndexOf(expectedIds, entry.Id)));

        var docsBlock = ReadMarkedBlock(
            Path.Combine(repositoryRoot, "docs", "source-generator.md"),
            "<!-- diagnostics-reference:start -->",
            "<!-- diagnostics-reference:end -->");
        var documentedIds = docsBlock.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("| `SLICE", StringComparison.Ordinal))
            .Select(static line => line.Split('|')[1].Trim().Trim('`'))
            .ToArray();
        Assert.Equal(expectedIds, documentedIds);

        foreach (var entry in expected)
        {
            Assert.Contains($"| `{entry.Id}` | {entry.Severity} |", docsBlock, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Generator_emits_SLICE023_and_excludes_wasi_route_for_unannotated_concrete_service_on_post()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using SliceFx;
            using SliceFx.Wasi;

            namespace ConcreteBodyApp
            {
                public sealed record CreateItemRequest(string Name);
                public sealed record CreateItemResponse(string Name);
                public sealed class ConcreteAuditLog { public void Record(string msg) { } }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace ConcreteBodyApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public static ConcreteBodyApp.CreateItemResponse Handle(
                        ConcreteBodyApp.CreateItemRequest req,
                        ConcreteBodyApp.ConcreteAuditLog audit)
                    {
                        audit.Record(req.Name);
                        return new(req.Name);
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("ConcreteBodyApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.Contains(generatorDiagnostics, static d => d.Id == "SLICE023");
        Assert.Contains("// SLICE023:", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".ReadAsync<global::ConcreteBodyApp.CreateItemRequest>", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_includes_wasi_route_and_emits_di_service_lookup_when_concrete_service_has_FromServices()
    {
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Microsoft.AspNetCore.Mvc;
            using SliceFx;
            using SliceFx.Wasi;

            namespace ConcreteBodyApp
            {
                public sealed record CreateItemRequest(string Name);
                public sealed record CreateItemResponse(string Name);
                public sealed class ConcreteAuditLog { public void Record(string msg) { } }

                [SliceJsonContext(SliceJsonTarget.Wasi)]
                public sealed class WasiJsonContext : JsonSerializerContext
                {
                    public static WasiJsonContext Default { get; } = new();
                    private WasiJsonContext() : base(null) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }

            namespace ConcreteBodyApp.Features.Items
            {
                [Feature("POST /items")]
                public static class CreateItem
                {
                    public static ConcreteBodyApp.CreateItemResponse Handle(
                        ConcreteBodyApp.CreateItemRequest req,
                        [FromServices] ConcreteBodyApp.ConcreteAuditLog audit)
                    {
                        audit.Record(req.Name);
                        return new(req.Name);
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("ConcreteBodyApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Id == "SLICE023");
        Assert.Contains(".ReadAsync<global::ConcreteBodyApp.CreateItemRequest>", wasiSource, StringComparison.Ordinal);
        Assert.Contains("ctx.Services.GetRequiredService(typeof(global::ConcreteBodyApp.ConcreteAuditLog))", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
    }

    private static async Task<APIGatewayHttpApiV2ProxyResponse> InvokeLambdaHandlerAsync(
        MethodInfo handler,
        APIGatewayHttpApiV2ProxyRequest request)
        => await (Task<APIGatewayHttpApiV2ProxyResponse>)handler.Invoke(
            null,
            [request, new SourceGeneratorTestLambdaContext()])!;

    private static MethodInfo GetGeneratedValidationMethod(
        Assembly assembly,
        string typeName,
        string methodName)
    {
        var registrationsType = assembly.GetType(typeName, throwOnError: true)!;
        return registrationsType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
    }

    private static void AssertGeneratedValidationMatchesFramework(MethodInfo validate, object value)
    {
        var generatedKeys = GetGeneratedValidationKeys(validate.Invoke(null, [value]));
        var frameworkKeys = GetFrameworkValidationKeys(value);

        Assert.Equal(frameworkKeys, generatedKeys);
    }

    private static string[] GetGeneratedValidationKeys(object? errors)
    {
        if (errors is null)
        {
            return [];
        }

        var typedErrors = Assert.IsType<IReadOnlyDictionary<string, string[]>>(errors, exactMatch: false);
        return [.. typedErrors.Keys.OrderBy(static key => key, StringComparer.Ordinal)];
    }

    private static string[] GetFrameworkValidationKeys(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);

        return [.. results
            .SelectMany(static result => result.MemberNames.DefaultIfEmpty(""))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)];
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
                               || !string.Equals(Path.GetFileName(path), "SliceFx.Wasi.dll", StringComparison.OrdinalIgnoreCase)))
            .Where(path => includeLambdaReference
                           || !string.Equals(Path.GetFileName(path), "SliceFx.Lambda.FunctionPerFeature.dll", StringComparison.OrdinalIgnoreCase))
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
        => CompileGeneratedAssembly(CreateCompilation(assemblyName, source));

    private static MemoryStream CompileGeneratedHostAssembly(string assemblyName, string source)
        => CompileGeneratedAssembly(CreateHostCompilation(assemblyName, source));

    private static MemoryStream CompileGeneratedAssembly(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);

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

    private static DiagnosticCatalogEntry[] GetDiagnosticCatalog()
    {
        var diagnosticsType = typeof(SliceFeatureGenerator).Assembly.GetType("SliceFx.SourceGenerator.SliceDiagnostics", throwOnError: true)!;
        return [.. diagnosticsType
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .OrderBy(static field => field.MetadataToken)
            .Select(static field =>
            {
                var descriptor = (DiagnosticDescriptor)field.GetValue(null)!;
                return new DiagnosticCatalogEntry(descriptor.Id, field.Name, descriptor.Category, descriptor.DefaultSeverity);
            })];
    }

    private static IEnumerable<DiagnosticCatalogEntry> ReadAnalyzerReleaseCatalog(string repositoryRoot)
    {
        foreach (var fileName in new[] { "AnalyzerReleases.Shipped.md", "AnalyzerReleases.Unshipped.md" })
        {
            var path = Path.Combine(repositoryRoot, "src", "SliceFx.SourceGenerator", fileName);
            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith("SLICE", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split('|').Select(static part => part.Trim()).ToArray();
                Assert.Equal(4, parts.Length);
                yield return new DiagnosticCatalogEntry(
                    parts[0],
                    parts[3],
                    parts[1],
                    Enum.Parse<DiagnosticSeverity>(parts[2]));
            }
        }
    }

    private static string ReadMarkedBlock(string path, string startMarker, string endMarker)
    {
        var content = File.ReadAllText(path);
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        var end = content.IndexOf(endMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find marker '{startMarker}' in '{path}'.");
        Assert.True(end > start, $"Could not find marker '{endMarker}' after '{startMarker}' in '{path}'.");
        return content.Substring(start + startMarker.Length, end - start - startMarker.Length);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SliceFx.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private readonly record struct DiagnosticCatalogEntry(
        string Id,
        string Name,
        string Category,
        DiagnosticSeverity Severity);

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

    private sealed class SourceGeneratorTestLambdaContext : ILambdaContext
    {
        public string AwsRequestId => "request-id";

        public IClientContext ClientContext => null!;

        public string FunctionName => "function";

        public string FunctionVersion => "$LATEST";

        public ICognitoIdentity Identity => null!;

        public string InvokedFunctionArn => "arn:aws:lambda:local:000000000000:function:function";

        public ILambdaLogger Logger { get; } = new SourceGeneratorTestLambdaLogger();

        public string LogGroupName => "log-group";

        public string LogStreamName => "log-stream";

        public int MemoryLimitInMB => 128;

        public TimeSpan RemainingTime => TimeSpan.FromSeconds(10);
    }

    // ── [SliceFilter<T>] neutral filter tests ────────────────────────────────────

    [Fact]
    public void Generator_discovers_SliceFilter_attribute_and_emits_InvokeSliceFilter_in_aspnet_registrations()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace NeutralFilterApp.Features.Items;

            [Feature("GET /items/{id}")]
            [SliceFilter<ApiKeyFilter>]
            public static class GetItem
            {
                public static string Handle(string id) => id;
            }

            public sealed class ApiKeyFilter : ISliceFilter
            {
                public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                {
                    if (!context.Headers.ContainsKey("X-Api-Key"))
                        return ValueTask.FromResult(SliceFilterResult.ShortCircuit(SliceResult.Unauthorized()));
                    return next(context);
                }
            }
            """;

        var compilation = CreateHostCompilation("NeutralFilterApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var aspNetSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        // No errors
        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);

        // Neutral filter registered as scoped
        Assert.Contains("services.AddScoped<global::NeutralFilterApp.Features.Items.ApiKeyFilter>", aspNetSource, StringComparison.Ordinal);
        // ASP.NET bridge emitted
        Assert.Contains("__InvokeSliceFilter<global::NeutralFilterApp.Features.Items.ApiKeyFilter>", aspNetSource, StringComparison.Ordinal);
        // Bridge helper methods emitted
        Assert.Contains("__InvokeSliceFilter<TFilter>", aspNetSource, StringComparison.Ordinal);
        Assert.Contains("__BuildSliceFilterContext", aspNetSource, StringComparison.Ordinal);
        Assert.Contains("__SliceResultToIResult", aspNetSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_neutral_filter_before_validation_factory_in_aspnet_registrations()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.ComponentModel.DataAnnotations;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OrderedFilterApp.Features.Items;

            [Feature("POST /items")]
            [SliceFilter<ApiKeyFilter>]
            public static class CreateItem
            {
                public record Request([Required] string Name);
                public static string Handle(Request req) => req.Name;
            }

            public sealed class ApiKeyFilter : ISliceFilter
            {
                public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("OrderedFilterApp", source);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var aspNetSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        // Neutral filter AddEndpointFilter call must appear BEFORE AddEndpointFilterFactory (validation)
        var sliceFilterPos = aspNetSource.IndexOf("__InvokeSliceFilter<", StringComparison.Ordinal);
        var validationFactoryPos = aspNetSource.IndexOf("AddEndpointFilterFactory(", StringComparison.Ordinal);
        Assert.True(sliceFilterPos > 0 && sliceFilterPos < validationFactoryPos,
            "Neutral filter should be emitted before the validation factory");
    }

    [Fact]
    public void Generator_classifies_neutral_filter_only_feature_as_portable()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace PortabilityApp.Features.Items;

            [Feature("DELETE /items/{id}")]
            [SliceFilter<ApiKeyFilter>]
            public static class DeleteItem
            {
                public static void Handle(string id) { }
            }

            public sealed class ApiKeyFilter : ISliceFilter
            {
                public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("PortabilityApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // Neutral filter only → portable
        Assert.Contains("\"portable\"", manifestSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"partial\"", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_classifies_mixed_neutral_and_aspnet_filter_feature_as_partial()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace PartialPortabilityApp.Features.Items;

            [Feature("DELETE /items/{id}")]
            [SliceFilter<ApiKeyFilter>]
            [Filter<LoggingFilter>]
            public static class DeleteItem
            {
                public static void Handle(string id) { }
            }

            public sealed class ApiKeyFilter : ISliceFilter
            {
                public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                    => next(context);
            }

            public sealed class LoggingFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("PartialPortabilityApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var manifestSource = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // ASP.NET filter coexists → partial
        Assert.Contains("\"partial\"", manifestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_WASI_filter_chain_for_neutral_filter_feature()
    {
        // Use a void-returning handler so no WasiJsonContext is needed (avoids SLICE021 exclusion).
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiFilterApp.Features.Items;

            [Feature("DELETE /items/{id}")]
            [SliceFilter<ApiKeyFilter>]
            public static class DeleteItem
            {
                public static void Handle(string id) { }
            }

            public sealed class ApiKeyFilter : ISliceFilter
            {
                public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("WasiFilterApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // Filter resolved from DI
        Assert.Contains("GetRequiredService<global::WasiFilterApp.Features.Items.ApiKeyFilter>", wasiSource, StringComparison.Ordinal);
        // Filter context built
        Assert.Contains("SliceFilterContext", wasiSource, StringComparison.Ordinal);
        // Filter invoked
        Assert.Contains("__sliceFilter0.InvokeAsync", wasiSource, StringComparison.Ordinal);
        // Short-circuit decode
        Assert.Contains("IsShortCircuit", wasiSource, StringComparison.Ordinal);
        // Handler core as delegate
        Assert.Contains("__handlerCore", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_no_filter_chain_for_feature_with_no_neutral_filters()
    {
        var source = """
            using System.Threading;
            using SliceFx;
            using SliceFx.Wasi;

            namespace NoFilterApp.Features.Items;

            [Feature("GET /items")]
            public static class ListItems
            {
                public static string Handle() => "ok";
            }
            """;

        var compilation = CreateHostCompilation("NoFilterApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);
        var wasiSource = GetGeneratedSource(driver, "SliceWasiRegistrations.g.cs");

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // No filter chain emitted (zero cost invariant)
        Assert.DoesNotContain("__sliceFilter", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("__handlerCore", wasiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsShortCircuit", wasiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_SLICE008_for_cross_layer_FilterOrderHint()
    {
        var source = """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace CrossLayerHintApp.Features.Items;

            [Feature("GET /items")]
            [SliceFilter<ApiKeyFilter>]
            [Filter<LoggingFilter>]
            public static class ListItems
            {
                public static string Handle() => "ok";
            }

            // ApiKeyFilter (neutral) has a hint pointing to LoggingFilter (ASP.NET) — cross-layer
            [FilterOrderHint(After = typeof(LoggingFilter))]
            public sealed class ApiKeyFilter : ISliceFilter
            {
                public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                    => next(context);
            }

            public sealed class LoggingFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """;

        var compilation = CreateHostCompilation("CrossLayerHintApp", source);
        GeneratorDriver driver = CreateDriver();

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(generatorDiagnostics.Where(static d => d.Id == "SLICE008"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("ApiKeyFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("LoggingFilter", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_wasi_routes_execute_neutral_filter_short_circuit_before_validation()
    {
        // This test verifies that a neutral filter (API key check) short-circuits with 401
        // BEFORE body validation runs (which would return 400 for a missing required field).
        var source = """
            using System;
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace WasiFilterDispatchApp
            {
                public static class RuntimeHarness
                {
                    // Request with no API key and invalid body → should get 401 (filter short-circuits first)
                    public static async Task<string> DispatchNoKeyAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        // PascalCase JSON key; empty string fails MinLength(1)
                        var body = Encoding.UTF8.GetBytes("{\"Name\":\"\"}");
                        var response = await app.DispatchAsync(new WasiRequest(
                            "POST", "/items",
                            new Dictionary<string, string>(), null, body));
                        return Format(response);
                    }

                    // Request with valid API key and valid body → should get 200
                    public static async Task<string> DispatchWithKeyAsync()
                    {
                        var builder = WasiHost.CreateBuilder();
                        builder.AddSlice();
                        await using var app = builder.Build();
                        // PascalCase JSON key; "Alice" satisfies [Required, MinLength(1)]
                        var body = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\"}");
                        var response = await app.DispatchAsync(new WasiRequest(
                            "POST", "/items",
                            new Dictionary<string, string> { ["X-Api-Key"] = "secret" }, null, body));
                        return Format(response);
                    }

                    private static string Format(WasiResponse r)
                        => r.Status + "|" + Encoding.UTF8.GetString(r.Body);
                }
            }

            namespace WasiFilterDispatchApp.Features.Items
            {
                [Feature("POST /items")]
                [SliceFilter<ApiKeyFilter>]
                public static class CreateItem
                {
                    public record Request([Required, MinLength(1)] string Name);
                    public static Task<string> Handle(Request req, CancellationToken ct) =>
                        Task.FromResult(req.Name);
                }
            }

            namespace WasiFilterDispatchApp
            {
                [SliceFx.SliceJsonContext(SliceFx.SliceJsonTarget.Wasi)]
                [System.Text.Json.Serialization.JsonSerializable(
                    typeof(WasiFilterDispatchApp.Features.Items.CreateItem.Request))]
                public partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }

                public sealed class ApiKeyFilter : ISliceFilter
                {
                    public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
                    {
                        if (!context.Headers.ContainsKey("X-Api-Key"))
                            return ValueTask.FromResult(SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Missing API key.")));
                        return next(context);
                    }
                }
            }
            """;

        var compilation = CreateHostCompilation("WasiFilterDispatchApp", source, includeWasiReference: true);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = CompileGeneratedAssembly(compilation);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var harness = assembly.GetType("WasiFilterDispatchApp.RuntimeHarness", throwOnError: true)!;

        // No key + invalid body → 401 (filter short-circuits before body validation sees empty name)
        var noKey = await (Task<string>)harness.GetMethod("DispatchNoKeyAsync")!.Invoke(null, null)!;
        Assert.StartsWith("401|", noKey, StringComparison.Ordinal);

        // Valid key + valid body → 200
        var withKey = await (Task<string>)harness.GetMethod("DispatchWithKeyAsync")!.Invoke(null, null)!;
        Assert.StartsWith("200|", withKey, StringComparison.Ordinal);
        Assert.Contains("Alice", withKey, StringComparison.Ordinal);
    }

    private sealed class SourceGeneratorTestLambdaLogger : ILambdaLogger
    {
        public void Log(string message)
        {
        }

        public void LogLine(string message)
        {
        }
    }
}
