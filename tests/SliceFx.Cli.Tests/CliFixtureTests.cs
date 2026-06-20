extern alias SliceSample;

using System.Reflection;
using System.Text.Json;
using SliceFx.Cli.Commands;
using SliceFx.Cli.Internal;
using SliceFx.Testing;

namespace SliceFx.Cli.Tests;

[Collection("DotnetPublish")]
public class CliFixtureTests
{
    [Fact]
    public void Project_discovery_sanitizes_project_file_name_when_root_namespace_is_missing()
    {
        using var fixture = CliProjectFixture.Create("my-app");

        var context = ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName);

        Assert.Equal("my_app", context.RootNamespace);
    }

    [Fact]
    public void Route_catalog_discovers_partial_features_and_generic_parameters_from_real_project_files()
    {
        using var fixture = CliProjectFixture.Create("my-app");
        fixture.WriteFeature(
            "Features/Things/GetThing.cs",
            """
            namespace My.App.Features.Things;

            [Feature("GET /things/{id:int}", Summary = "Get things")]
            public static partial class GetThing
            {
                public static Response Handle(int id, Dictionary<string, int> counts, int[] ids, CancellationToken ct)
                    => new(id);

                public sealed record Response(int Id);
            }
            """);

        var routes = RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName));

        var route = Assert.Single(routes);
        Assert.Equal("Things.GetThing", route.EndpointName);
        Assert.Equal("Get things", route.Summary);
        Assert.Equal("my-app", route.SourceAssemblyName);
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "Dictionary<string, int>", Name: "counts" });
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "int[]", Name: "ids" });
    }

    [Fact]
    public void Route_catalog_discovers_multiple_features_from_one_source_file_in_fallback_mode()
    {
        using var fixture = CliProjectFixture.Create("my-app");
        fixture.WriteFeature(
            "Features/Things/ThingFeatures.cs",
            """"
            namespace My.App.Features.Things;

            [Feature("GET /things")]
            public static class ListThings
            {
                public static Response Handle() => new();

                public sealed record Response();
            }

            [Feature("POST /things")]
            public static class CreateThing
            {
                public static Response Handle(Request req) => new();

                public sealed record Request(string Name);

                public sealed record Response();
            }
            """");

        var routes = RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName));

        Assert.Equal(2, routes.Length);
        Assert.Contains(routes, static route => route.EndpointName == "Things.ListThings");
        Assert.Contains(routes, static route => route.EndpointName == "Things.CreateThing");
    }

    [Fact]
    public void Route_catalog_discovers_filters_before_feature_in_fallback_mode()
    {
        using var fixture = CliProjectFixture.Create("my-app");
        fixture.WriteFeature(
            "Features/Things/GetThing.cs",
            """
            namespace My.App.Features.Things;

            [Filter<RequestLoggingFilter>]
            [Feature("GET /things")]
            public static class GetThing
            {
                public static Response Handle() => new();

                public sealed record Response();
            }
            """);

        var route = Assert.Single(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));

        Assert.Equal(RouteCatalog.PortabilityPartial, route.Portability);
        Assert.Contains("RequestLoggingFilter", route.Filters);
    }

    [Fact]
    public void Route_catalog_discovers_closed_generic_filters_in_fallback_mode()
    {
        using var fixture = CliProjectFixture.Create("my-app");
        fixture.WriteFeature(
            "Features/Things/DeleteThing.cs",
            """
            namespace My.App.Features.Things;

            [Filter<RequireApiKeyFilter<AdminPolicy>>]
            [Feature("DELETE /things/{id:guid}")]
            public static class DeleteThing
            {
                public static Response Handle(Guid id) => new(id);

                public sealed record Response(Guid Id);
            }
            """);

        var route = Assert.Single(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));

        Assert.Contains("RequireApiKeyFilter<AdminPolicy>", route.Filters);
        Assert.Equal(RouteCatalog.PortabilityPartial, route.Portability);
        Assert.Equal(RouteCatalog.LambdaIneligible, route.LambdaFunctionPerFeatureStatus);
        Assert.Equal("endpoint filters require the ASP.NET endpoint filter pipeline", route.LambdaFunctionPerFeatureReason);
    }

    [Fact]
    public void Route_catalog_ignores_filter_like_text_in_comments_and_literals_in_fallback_mode()
    {
        using var fixture = CliProjectFixture.Create("my-app");
        fixture.WriteFeature(
            "Features/Things/DeleteThing.cs",
            """"
            namespace My.App.Features.Things;

            public static class Noise
            {
                private const char Quote = '"';
                private const string Verbatim = @"[Filter<VerbatimFilter>]; }";
                private const string Raw = """
                    [Filter<RawFilter>]
                    ; }
                    """;
            }

            // [Filter<CommentFilter>]; }
            /* [Filter<BlockCommentFilter>]; } */
            [Filter<RequireApiKeyFilter<AdminPolicy>>]
            [Feature("DELETE /things/{id:guid}")]
            public static class DeleteThing
            {
                public static Response Handle(Guid id) => new(id);

                public sealed record Response(Guid Id);
            }
            """");

        var route = Assert.Single(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));

        var filter = Assert.Single(route.Filters);
        Assert.Equal("RequireApiKeyFilter<AdminPolicy>", filter);
        Assert.Equal(RouteCatalog.PortabilityPartial, route.Portability);
    }

    [Fact]
    public void Route_target_capabilities_are_orthogonal_to_wasi_portability()
    {
        var aspNetOnlyRoute = new SliceRouteInfo(
            "GET",
            "/things/{id}",
            "My.App.Features.Things",
            "GetThing",
            "Things",
            "Things.GetThing",
            null,
            null,
            "Microsoft.AspNetCore.Http.IResult",
            RouteCatalog.PortabilityAspNetOnly,
            "returns ASP.NET IResult",
            [],
            []);

        var filteredRoute = aspNetOnlyRoute with
        {
            ReturnType = "My.App.Features.Things.GetThing.Response",
            Portability = RouteCatalog.PortabilityPartial,
            PortabilityReason = "endpoint filters do not run in the WASI path",
            Filters = ["RequestLoggingFilter"]
        };
        var typedAspNetRoute = aspNetOnlyRoute with
        {
            ReturnType = "Microsoft.AspNetCore.Http.HttpResults.Ok<My.App.Features.Things.GetThing.Response>",
        };

        var portableRoute = filteredRoute with
        {
            Portability = RouteCatalog.PortabilityPortable,
            PortabilityReason = null,
            Filters = []
        };

        Assert.Equal(RouteTargetCapabilities.Eligible, RouteTargetCapabilities.Classify(aspNetOnlyRoute).LambdaHostedApp.Status);
        Assert.Equal(RouteTargetCapabilities.Ineligible, RouteTargetCapabilities.Classify(aspNetOnlyRoute).LambdaFunctionPerFeature.Status);
        Assert.Equal(RouteTargetCapabilities.Ineligible, RouteTargetCapabilities.Classify(typedAspNetRoute).LambdaFunctionPerFeature.Status);
        Assert.Equal(RouteTargetCapabilities.Ineligible, RouteTargetCapabilities.Classify(filteredRoute).LambdaFunctionPerFeature.Status);
        Assert.Equal(RouteTargetCapabilities.Eligible, RouteTargetCapabilities.Classify(portableRoute).LambdaFunctionPerFeature.Status);

        var generatedRoute = portableRoute with
        {
            ManifestSchemaVersion = "1",
            WasiDispatchStatus = RouteTargetCapabilities.Eligible,
            WasiDispatchReason = null,
            Portability = RouteCatalog.PortabilityPartial,
            PortabilityReason = "ignored",
        };
        Assert.Equal(RouteTargetCapabilities.Eligible, RouteTargetCapabilities.Classify(generatedRoute).WasiDispatch.Status);

        var generatedRouteWithoutWasiMetadata = portableRoute with
        {
            ManifestSchemaVersion = "1",
            WasiDispatchStatus = null,
            WasiDispatchReason = null,
            HasGeneratedMetadata = true,
        };

        var capabilities = RouteTargetCapabilities.Classify(generatedRouteWithoutWasiMetadata);
        Assert.Equal("unknown", capabilities.WasiDispatch.Status);
        Assert.Equal("WASI dispatch metadata missing", capabilities.WasiDispatch.Reason);
    }

    [Fact]
    public async Task Route_catalog_prefers_generated_metadata_from_built_project()
    {
        using var fixture = CliProjectFixture.Create(
            "generated-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Generated.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Things/GetThing.cs",
            """
            using Microsoft.AspNetCore.Mvc;
            using SliceFx;

            namespace Generated.App.Features.Things;

            [Feature("GET /things/{id:int}", Summary = "Get a generated thing")]
            public static class GetThing
            {
                public static Response Handle(int id, [FromHeader(Name = "X|Tenant\nName;Segment")] string tenant) => new(id);

                public sealed record Response(int Id);
            }
            """);

        await fixture.BuildAsync();

        var route = Assert.Single(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));
        Assert.Equal("Things.GetThing", route.EndpointName);
        Assert.Equal("Get a generated thing", route.Summary);
        Assert.Equal("Generated.App.Features.Things.GetThing.Response", route.ReturnType);
        Assert.Equal(RouteCatalog.PortabilityPortable, route.Portability);
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "int", Name: "id" });
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "string", Name: "tenant", BindingSource: "header", BindingName: "X|Tenant\nName;Segment" });
    }

    [Fact]
    public async Task Route_catalog_fails_clearly_for_unsupported_generated_manifest_schema()
    {
        using var fixture = CliProjectFixture.Create(
            "unsupported-schema-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Unsupported.Schema.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "RouteMetadata.cs",
            """
            using SliceFx;

            [assembly: SliceFeatureRouteAttribute(
                "Health.GetHealth",
                "Unsupported.Schema.App.Features.Health.GetHealth",
                "GET",
                "/health",
                "Health",
                null,
                null,
                "Unsupported.Schema.App.Features.Health.GetHealth.Response",
                "portable",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "999",
                "eligible",
                null,
                null,
                null,
                null,
                null,
                null)]

            namespace Unsupported.Schema.App.Features.Health;

            public static class GetHealth
            {
                public sealed record Response(string Status);
            }
            """);

        await fixture.BuildAsync();

        var exception = Assert.Throws<CliException>(() => RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));
        Assert.Contains("Unsupported SliceFx route manifest schema '999'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Route_catalog_fails_clearly_for_invalid_generated_manifest_shape()
    {
        using var fixture = CliProjectFixture.Create(
            "legacy-manifest-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Legacy.Manifest.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="OldSliceCore/**/*.cs" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine("OldSliceCore", "SliceFx.Core.csproj")}}" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "OldSliceCore/SliceFx.Core.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>SliceFx.Core</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "OldSliceCore/SliceFeatureRouteAttribute.cs",
            """
            namespace SliceFx;

            [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class SliceFeatureRouteAttribute : global::System.Attribute
            {
                public SliceFeatureRouteAttribute(
                    string endpointName,
                    string featureType,
                    string httpMethod,
                    string pattern,
                    string? tag,
                    string? summary,
                    string? requestType,
                    string? returnType,
                    string? portability,
                    string? portabilityReason,
                    string? serializedFilterTypes,
                    string? serializedParameters,
                    string? lambdaFunctionPerFeatureStatus,
                    string? lambdaFunctionPerFeatureReason,
                    string? lambdaFunctionPerFeatureHandlerAssembly,
                    string? lambdaFunctionPerFeatureHandlerType,
                    string? lambdaFunctionPerFeatureHandlerMethod)
                {
                }
            }
            """);
        fixture.WriteFeature(
            "RouteMetadata.cs",
            """
            using SliceFx;

            [assembly: SliceFeatureRouteAttribute(
                "Health.GetHealth",
                "Legacy.Manifest.App.Features.Health.GetHealth",
                "GET",
                "/health",
                "Health",
                null,
                null,
                "Legacy.Manifest.App.Features.Health.GetHealth.Response",
                "portable",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null)]

            namespace Legacy.Manifest.App.Features.Health;

            public static class GetHealth
            {
                public sealed record Response(string Status);
            }
            """);

        await fixture.BuildAsync();

        var exception = Assert.Throws<CliException>(() => RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));
        Assert.Contains("Invalid SliceFx route manifest", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 26 constructor arguments but found 17", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Rebuild the project", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Route_catalog_returns_empty_when_built_project_has_empty_generated_metadata()
    {
        using var fixture = CliProjectFixture.Create(
            "empty-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Empty.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        await fixture.BuildAsync();

        Assert.Empty(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName)));
    }

    [Fact]
    public async Task Route_catalog_does_not_include_referenced_feature_assemblies_without_explicit_aggregation()
    {
        using var fixture = CreateHostWithReferencedFeatureLibrary(null);

        await fixture.BuildAsync();

        var discovery = RouteCatalog.DiscoverDetailed(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName));

        Assert.Empty(discovery.Routes);
        Assert.Empty(discovery.AggregatedSourceAssemblyNames);
    }

    [Fact]
    public async Task Route_catalog_includes_referenced_feature_assemblies_from_host_aggregation_metadata()
    {
        using var fixture = CreateHostWithReferencedFeatureLibrary("<SliceFxReferencedAssemblies>feature-lib</SliceFxReferencedAssemblies>");

        await fixture.BuildAsync();

        var discovery = RouteCatalog.DiscoverDetailed(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName));
        var route = Assert.Single(discovery.Routes);

        Assert.Equal("Health.GetHealth", route.EndpointName);
        Assert.Equal("feature-lib", route.SourceAssemblyName);
        Assert.Equal(["feature-lib"], discovery.AggregatedSourceAssemblyNames);
    }

    [Fact]
    public async Task Csharp_client_unwraps_async_return_type_from_generated_metadata()
    {
        using var fixture = CliProjectFixture.Create(
            "generated-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Generated.Client.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Things/GetThing.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace Generated.Client.App.Features.Things;

            [Feature("GET /things/{id:int}")]
            public static class GetThing
            {
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id));

                public sealed record Response(int Id);
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("public async Task<Generated.Client.App.Features.Things.GetThing.Response> GetThingAsync(int id, CancellationToken cancellationToken = default)", client);
        Assert.DoesNotContain("Task<System.Threading.Tasks.Task", client);
        Assert.DoesNotContain("GetFromJsonAsync<System.Threading.Tasks.Task", client);
    }

    [Fact]
    public async Task Generated_metadata_clients_and_openapi_infer_shared_body_contracts()
    {
        using var fixture = CliProjectFixture.Create(
            "shared-body-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Shared.Body.Client.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Contracts.cs",
            """
            namespace Shared.Body.Client.App.Contracts;

            public sealed record CreateItemRequest(string Name);

            public sealed record CreateItemResponse(int Id, string Name);
            """);
        fixture.WriteFeature(
            "Features/Items/CreateItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using Shared.Body.Client.App.Contracts;

            namespace Shared.Body.Client.App.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public static Task<CreateItemResponse> Handle(CreateItemRequest request, CancellationToken ct)
                    => Task.FromResult(new CreateItemResponse(1, request.Name));
            }
            """);

        await fixture.BuildAsync();

        var discovery = RouteCatalog.DiscoverDetailed(ProjectContextDiscovery.Discover(fixture.ProjectFile.FullName));
        var route = Assert.Single(discovery.Routes);
        Assert.Equal("Shared.Body.Client.App.Contracts.CreateItemRequest", route.RequestType);

        var csharpOutput = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var csharpExitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", csharpOutput, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, csharpExitCode);
        var csharpClient = await File.ReadAllTextAsync(csharpOutput, TestContext.Current.CancellationToken);
        Assert.Contains("Task<Shared.Body.Client.App.Contracts.CreateItemResponse> CreateItemAsync(Shared.Body.Client.App.Contracts.CreateItemRequest request", csharpClient);
        Assert.Contains("JsonContent.Create(request, ", csharpClient);
        Assert.DoesNotContain("CreateItem.Request", csharpClient);

        var typescriptOutput = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var typescriptExitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", typescriptOutput, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, typescriptExitCode);
        var typescriptClient = await File.ReadAllTextAsync(typescriptOutput, TestContext.Current.CancellationToken);
        Assert.Contains("body: ContractsCreateItemRequest", typescriptClient);
        Assert.Contains("JSON.stringify(body)", typescriptClient);

        var openApiOutput = Path.Combine(fixture.Directory.FullName, "openapi.json");
        var openApiExitCode = await GenerateOpenApiCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", openApiOutput])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, openApiExitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(openApiOutput, TestContext.Current.CancellationToken));
        Assert.True(document.RootElement
            .GetProperty("paths")
            .GetProperty("/items")
            .GetProperty("post")
            .TryGetProperty("requestBody", out _));
    }

    [Fact]
    public async Task Csharp_client_emits_extensibility_hooks()
    {
        using var fixture = CliProjectFixture.Create(
            "extensibility-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Extensibility.Client.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace Extensibility.Client.App.Features.Items;

            [Feature("GET /items/{id:int}")]
            public static class GetItem
            {
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id));

                public sealed record Response(int Id);
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("public partial class SliceApiClient", client);
        Assert.Contains("public SliceApiClient(HttpMessageHandler handler)", client);
        Assert.DoesNotContain("IHttpClientFactory", client);
        Assert.Contains("partial void OnRequestPreparing(HttpRequestMessage request)", client);
        Assert.Contains("_prepareRequest(__message)", client);
    }

    [Fact]
    public async Task Typescript_client_generates_fetch_client_for_portable_routes()
    {
        using var fixture = CliProjectFixture.Create(
            "typescript-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TypeScript.Client.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.Client.App.Features.Items;

            [Feature("GET /items/{id:int}")]
            public static class GetItem
            {
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id, "Widget"));

                public sealed record Response(int Id, string Name);
            }
            """);
        fixture.WriteFeature(
            "Features/Items/CreateItem.cs",
            """
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.Client.App.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public record Request(
                    [property: JsonPropertyName("display_name")] string Name,
                    byte[] Payload,
                    [property: JsonIgnore] string Secret);
                public record Response(int Id, string Name);

                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1, req.Name));
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var exitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("export class SliceApiClient", client);
        Assert.Contains("export class ItemsClient", client);
        Assert.Contains("async getItemAsync(", client);
        Assert.Contains("async createItemAsync(", client);
        Assert.Contains("encodeURIComponent(String(id))", client);
        Assert.Contains("JSON.stringify(body)", client);
        Assert.Contains("readonly 'display_name': string;", client);
        Assert.Contains("readonly 'payload': string;", client);
        Assert.DoesNotContain("secret", client, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Typescript_client_handles_request_edge_cases()
    {
        using var fixture = CliProjectFixture.Create(
            "typescript-request-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TypeScript.Request.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.Request.App.Features.Items;

            [Feature("GET /items/`/{id:int}")]
            public static class GetItem
            {
                public static Task<Response> Handle(int id, string? search, string[]? tags, CancellationToken ct)
                    => Task.FromResult(new Response(id));

                public sealed record Response(int Id);
            }
            """);
        fixture.WriteFeature(
            "Features/Items/CreateItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.Request.App.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public record Request(string Name);
                public record Response(int Id);

                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1));
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var exitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("let url = `${this.baseUrl}/items/\\`/${encodeURIComponent(String(id))}`;", client);
        Assert.Contains("if (search != null) params.set('search', String(search));", client);
        Assert.Contains("if (tags != null) for (const v of tags) params.append('tags', String(v));", client);
        Assert.Contains("signal: signal ?? this.init?.signal", client);
        Assert.Contains("const headers = new Headers(this.init?.headers);", client);
        Assert.Contains("headers.set('Content-Type', 'application/json');", client);
        Assert.Contains("const text = await response.text();", client);
        Assert.Contains("returned an empty response body", client);
    }

    [Fact]
    public async Task Typescript_client_generates_recursive_unique_dto_schemas()
    {
        using var fixture = CliProjectFixture.Create(
            "typescript-schema-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TypeScript.Schema.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Users/Get.cs",
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.Schema.App.Features.Users;

            [Feature("GET /users")]
            public static class Get
            {
                public static Task<Response> Handle(CancellationToken ct)
                    => Task.FromResult(new Response([], [], new Dictionary<string, User>()));

                public sealed record User(string Name);

                public sealed record Response(User[] Items, IReadOnlyList<User> Recent, Dictionary<string, User> ById);
            }
            """);
        fixture.WriteFeature(
            "Features/Orders/Get.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.Schema.App.Features.Orders;

            [Feature("GET /orders")]
            public static class Get
            {
                public static Task<Response> Handle(CancellationToken ct)
                    => Task.FromResult(new Response(new User(1)));

                public sealed record User(int Id);

                public sealed record Response(User User);
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var exitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("export interface GetResponse", client);
        Assert.Contains("export interface UsersGetResponse", client);
        Assert.Contains("readonly 'items': UsersGetUser[];", client);
        Assert.Contains("readonly 'recent': UsersGetUser[];", client);
        Assert.Contains("readonly 'byId': Record<string, UsersGetUser>;", client);
        Assert.DoesNotContain("unknown[]", client);
    }

    [Fact]
    public async Task Typescript_client_excludes_aspnet_only_routes()
    {
        using var fixture = CliProjectFixture.Create(
            "typescript-aspnet-only-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TypeScript.AspNet.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.AspNet.App.Features.Items;

            [Feature("GET /items/{id:int}")]
            public static class GetItem
            {
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id));

                public sealed record Response(int Id);
            }
            """);
        fixture.WriteFeature(
            "Features/Items/DeleteItem.cs",
            """
            using Microsoft.AspNetCore.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TypeScript.AspNet.App.Features.Items;

            [Feature("DELETE /items/{id:int}")]
            public static class DeleteItem
            {
                public static Task<IResult> Handle(int id, CancellationToken ct)
                    => Task.FromResult(Results.NoContent());
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var exitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("getItemAsync", client);
        Assert.DoesNotContain("deleteItemAsync", client);
    }

    [Fact]
    public async Task Openapi_command_generates_manifest_projection_for_portable_routes()
    {
        using var fixture = CliProjectFixture.Create(
            "openapi-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>OpenApi.Client.App</RootNamespace>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/CreateItem.cs",
            """
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OpenApi.Client.App.Features.Items;

            [Feature("POST /items/{id:int}", Summary = "Create item")]
            public static class CreateItem
            {
                [JsonConverter(typeof(JsonStringEnumConverter))]
                public enum Priority
                {
                    Low = 0,
                    High = 1,
                }

                public sealed record Request(
                    [property: JsonPropertyName("display_name")] string Name,
                    int? Notes,
                    Priority Priority,
                    byte[] Payload,
                    [property: JsonIgnore] string Secret,
                    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Optional);

                public sealed record Response(int Id, string Name, int? Notes, Priority Priority);

                public static Task<Response> Handle(int id, string? filter, Request req, CancellationToken ct)
                    => Task.FromResult(new Response(id, req.Name, req.Notes, req.Priority));
            }
            """);
        fixture.WriteFeature(
            "Features/Items/DeleteItem.cs",
            """
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OpenApi.Client.App.Features.Items;

            [Feature("DELETE /items/{id:int}", Summary = "Delete item")]
            public static class DeleteItem
            {
                public static Task<IResult> Handle(int id)
                    => Task.FromResult(Results.NoContent());
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "openapi.json");
        var exitCode = await GenerateOpenApiCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken));
        var root = document.RootElement;

        Assert.Equal("3.1.1", root.GetProperty("openapi").GetString());
        Assert.Equal("manifest", root.GetProperty("x-slicefx-source").GetString());
        Assert.Equal("Items.DeleteItem", root.GetProperty("x-slicefx-omitted")[0].GetProperty("operationId").GetString());

        var post = root.GetProperty("paths").GetProperty("/items/{id}").GetProperty("post");
        Assert.Equal("Items.CreateItem", post.GetProperty("operationId").GetString());
        Assert.Equal("Create item", post.GetProperty("summary").GetString());
        Assert.Equal("portable", post.GetProperty("x-slicefx-portability").GetString());
        Assert.False(root.GetProperty("paths").GetProperty("/items/{id}").TryGetProperty("delete", out _));

        var parameters = post.GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), static parameter =>
            parameter.GetProperty("name").GetString() == "id" &&
            parameter.GetProperty("in").GetString() == "path" &&
            parameter.GetProperty("required").GetBoolean());
        Assert.Contains(parameters.EnumerateArray(), static parameter =>
            parameter.GetProperty("name").GetString() == "filter" &&
            parameter.GetProperty("in").GetString() == "query" &&
            !parameter.GetProperty("required").GetBoolean());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        // Schemas now use parent-qualified names (e.g. "CreateItem_Request") so bare names such as
        // "Request" are never claimed by whichever feature happens to be processed first.
        Assert.False(schemas.TryGetProperty("Request", out _), "Bare 'Request' schema name should not exist; expected parent-qualified name.");
        var request = schemas.GetProperty("CreateItem_Request");
        var required = request.GetProperty("required").EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("display_name", required);
        Assert.Contains("priority", required);
        Assert.Contains("payload", required);
        Assert.DoesNotContain("notes", required);
        Assert.DoesNotContain("optional", required);

        Assert.True(request.GetProperty("properties").TryGetProperty("display_name", out _));
        Assert.False(request.GetProperty("properties").TryGetProperty("secret", out _));
        var notesType = request.GetProperty("properties").GetProperty("notes").GetProperty("type");
        Assert.Contains(notesType.EnumerateArray(), static item => item.GetString() == "null");
        var payload = request.GetProperty("properties").GetProperty("payload");
        Assert.Equal("string", payload.GetProperty("type").GetString());
        Assert.Equal("byte", payload.GetProperty("format").GetString());
        var priorityRef = request.GetProperty("properties").GetProperty("priority").GetProperty("$ref").GetString()!;
        var priority = schemas.GetProperty(priorityRef[(priorityRef.LastIndexOf('/') + 1)..]);
        Assert.Equal("string", priority.GetProperty("type").GetString());
        Assert.Contains(priority.GetProperty("enum").EnumerateArray(), static item => item.GetString() == "High");
        Assert.Contains(priority.GetProperty("x-enumNames").EnumerateArray(), static item => item.GetString() == "High");
    }

    [Fact]
    public async Task Openapi_command_schema_names_are_qualified_and_order_independent()
    {
        // Regression: two features with same-named nested types (both called "Request") must both
        // receive parent-qualified component names regardless of which feature is processed first.
        using var fixture = CliProjectFixture.Create(
            "openapi-ordering-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>OpenApi.Ordering.App</RootNamespace>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Orders/CreateOrder.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OpenApi.Ordering.App.Features.Orders;

            [Feature("POST /orders")]
            public static class CreateOrder
            {
                public sealed record Request(string Name);
                public sealed record Response(int Id, string Name);
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1, req.Name));
            }
            """);
        fixture.WriteFeature(
            "Features/Products/CreateProduct.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OpenApi.Ordering.App.Features.Products;

            [Feature("POST /products")]
            public static class CreateProduct
            {
                public sealed record Request(string Title);
                public sealed record Response(int Id, string Title);
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1, req.Title));
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "openapi.json");
        var exitCode = await GenerateOpenApiCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        // Both features have a "Request" and "Response" nested type.
        // Neither should claim the bare name; both must get parent-qualified names.
        Assert.False(schemas.TryGetProperty("Request", out _), "Bare 'Request' must not exist.");
        Assert.False(schemas.TryGetProperty("Response", out _), "Bare 'Response' must not exist.");
        Assert.True(schemas.TryGetProperty("CreateOrder_Request", out _), "Expected 'CreateOrder_Request'.");
        Assert.True(schemas.TryGetProperty("CreateOrder_Response", out _), "Expected 'CreateOrder_Response'.");
        Assert.True(schemas.TryGetProperty("CreateProduct_Request", out _), "Expected 'CreateProduct_Request'.");
        Assert.True(schemas.TryGetProperty("CreateProduct_Response", out _), "Expected 'CreateProduct_Response'.");
    }

    [Fact]
    public async Task Sample_openapi_document_exposes_generated_slice_routes()
    {
        var contentRoot = Path.Combine(FindRepoRoot(), "samples", "SliceFx.Sample");
        await using var host = SliceTestHost.Create<SliceSample::Program>(contentRoot: contentRoot);

        using var document = JsonDocument.Parse(await host.Client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Equal("3.1.1", root.GetProperty("openapi").GetString());

        var paths = root.GetProperty("paths");
        Assert.Equal("Health.GetHealth", paths.GetProperty("/health").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("Users.CreateUser", paths.GetProperty("/users").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("Users.GetUser", paths.GetProperty("/users/{id}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("Users.DeleteUser", paths.GetProperty("/users/{id}").GetProperty("delete").GetProperty("operationId").GetString());
    }

    [Fact]
    public void Openapi_dependency_stays_in_aspnet_sample()
    {
        var root = FindRepoRoot();
        var references = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static file => File.ReadAllText(file).Contains("Microsoft.AspNetCore.OpenApi", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["samples/SliceFx.Sample/SliceFx.Sample.csproj"], references);
    }

    [Fact]
    public async Task Manifest_aws_lambda_generates_hosted_sam_template_with_one_function_and_route_events()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-manifest-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Manifest.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Users/CreateUser.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace Lambda.Manifest.App.Features.Users;

            [Feature("POST /users", Summary = "Create user")]
            public static class CreateUser
            {
                public record Request(string Name);
                public record Response(int Id);
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1));
            }
            """);
        fixture.WriteFeature(
            "Features/Users/GetUser.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace Lambda.Manifest.App.Features.Users;

            [Feature("GET /users/{id:guid}")]
            public static class GetUser
            {
                public record Response(System.Guid Id);
                public static Task<Response> Handle(System.Guid id, CancellationToken ct)
                    => Task.FromResult(new Response(id));
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "template.yaml");
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("AWS::Serverless-2016-10-31", yaml);
        Assert.Contains("SliceApi:", yaml);
        Assert.Contains("Hosted mode: one Lambda function hosts the ASP.NET Core app generated by SliceFx.", yaml);
        Assert.Contains("SliceHostedFunction:", yaml);
        Assert.Contains("UsersCreateUserEvent:", yaml);
        Assert.Contains("UsersGetUserEvent:", yaml);
        Assert.DoesNotContain("UsersCreateUserFunction:", yaml);
        Assert.DoesNotContain("UsersGetUserFunction:", yaml);
        Assert.Contains("Method: 'POST'", yaml);
        Assert.Contains("Method: 'GET'", yaml);
        Assert.Contains("Path: '/users'", yaml);
        Assert.DoesNotContain("(portable)", yaml);
    }

    [Fact]
    public async Task Manifest_aws_lambda_strips_route_constraints_for_api_gateway()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-constraints-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Constraints.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using SliceFx;

            namespace Lambda.Constraints.App.Features.Items;

            [Feature("GET /items/{id:int}")]
            public static class GetItem
            {
                public static int Handle(int id) => id;
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "template.yaml");
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("Path: '/items/{id}'", yaml);
        Assert.DoesNotContain("Path: '/items/{id:int}'", yaml);
    }

    [Fact]
    public async Task Manifest_aws_lambda_uses_bootstrap_handler_for_provided_runtime()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-runtime-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Runtime.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Lambda.Runtime.App.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        await fixture.BuildAsync();

        var nativeAotOutput = Path.Combine(fixture.Directory.FullName, "template-native.yaml");
        var managedOutput = Path.Combine(fixture.Directory.FullName, "template-managed.yaml");

        await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", nativeAotOutput, "--runtime", "provided.al2023"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", managedOutput, "--runtime", "dotnet8"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var nativeYaml = await File.ReadAllTextAsync(nativeAotOutput, TestContext.Current.CancellationToken);
        var managedYaml = await File.ReadAllTextAsync(managedOutput, TestContext.Current.CancellationToken);

        Assert.Contains("Handler: 'bootstrap'", nativeYaml);
        Assert.Contains("Handler: 'lambda-runtime-app::Amazon.Lambda.AspNetCoreServer.Hosting.LambdaRuntimeSupportServer::Run'", managedYaml);
    }

    [Fact]
    public async Task Manifest_aws_lambda_converts_catch_all_segments_to_api_gateway_syntax()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-catchall-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Catchall.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Files/GetFile.cs",
            """
            using SliceFx;

            namespace Lambda.Catchall.App.Features.Files;

            [Feature("GET /files/{**path}")]
            public static class GetFile
            {
                public static string Handle(string path) => path;
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "template.yaml");
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("Path: '/files/{path+}'", yaml);
        Assert.DoesNotContain("Path: '/files/{**path}'", yaml);
    }

    [Fact]
    public async Task Manifest_aws_lambda_force_overwrites_existing_output_file()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-force-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Force.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Lambda.Force.App.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "template.yaml");

        // First run succeeds.
        var firstExit = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, firstExit);

        // Second run without --force fails.
        var noForceExit = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, noForceExit);

        // Third run with --force succeeds.
        var forceExit = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, forceExit);
    }

    [Fact]
    public async Task Manifest_aws_lambda_creates_output_directory_if_not_exists()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-mkdir-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Mkdir.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Lambda.Mkdir.App.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "deploy", "infra", "template.yaml");
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputFile));
    }

    [Fact]
    public async Task Manifest_aws_lambda_rejects_removed_shared_function_per_feature_layout()
    {
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--mode", "function-per-feature", "--artifact-layout", "shared"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Manifest_aws_lambda_function_per_feature_mode_emits_eligible_functions_and_exclusions()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-per-feature-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.PerFeature.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Lambda.FunctionPerFeature", "SliceFx.Lambda.FunctionPerFeature.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "LambdaSetup.cs",
            """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace Lambda.PerFeature.App;

            [SliceFx.SliceJsonContext(SliceFx.SliceJsonTarget.LambdaFunctionPerFeature)]

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
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Lambda.PerFeature.App.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);
        fixture.WriteFeature(
            "Features/Health/GetFilteredHealth.cs",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using SliceFx;

            namespace Lambda.PerFeature.App.Features.Health;

            [Feature("GET /health/filtered")]
            [Filter<AuditFilter>]
            public static class GetFilteredHealth
            {
                public static string Handle() => "ok";
            }

            public sealed class AuditFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                    => next(context);
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "template.yaml");
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--mode", "function-per-feature"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("Function-per-feature mode: each eligible [Feature] becomes a separate Lambda function resource.", yaml);
        Assert.Contains("Artifact layout: per-feature. Each function points at its own NativeAOT custom-runtime artifact.", yaml);
        Assert.Contains("HealthGetHealthFunction:", yaml);
        Assert.Contains("HealthGetHealthEvent:", yaml);
        Assert.Contains("CodeUri: './artifacts/health-gethealth'", yaml);
        Assert.Contains("Handler: 'bootstrap'", yaml);
        Assert.Contains("Path: '/health'", yaml);
        Assert.DoesNotContain("HealthGetFilteredHealthFunction:", yaml);
        Assert.Contains("# - Health.GetFilteredHealth: endpoint filters require the ASP.NET endpoint filter pipeline", yaml);
    }

    [Fact]
    public async Task Manifest_aws_lambda_function_per_feature_uses_referenced_feature_assembly_handlers()
    {
        using var fixture = CliProjectFixture.Create(
            "host-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Host.App</RootNamespace>
                <SliceFxRole>Host</SliceFxRole>
                <SliceFxReferencedAssemblies>feature-lib</SliceFxReferencedAssemblies>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="FeatureLib/feature-lib.csproj" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
              <ItemGroup>
                <CompilerVisibleProperty Include="SliceFxRole" />
                <CompilerVisibleProperty Include="SliceFxReferencedAssemblies" />
                <CompilerVisibleProperty Include="SliceFxAggregateReferences" />
              </ItemGroup>
              <ItemGroup>
                <Compile Remove="FeatureLib/**/*.cs" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "FeatureLib/feature-lib.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>feature-lib</AssemblyName>
                <RootNamespace>Feature.Lib</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Lambda.FunctionPerFeature", "SliceFx.Lambda.FunctionPerFeature.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "FeatureLib/LambdaSetup.cs",
            """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]

            namespace Feature.Lib;

            public sealed class Marker;

            [SliceFx.SliceJsonContext(SliceFx.SliceJsonTarget.LambdaFunctionPerFeature)]

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
            """);
        fixture.WriteFeature(
            "UseFeatureLib.cs",
            """
            namespace Host.App;

            public sealed class UseFeatureLib
            {
                private Feature.Lib.Marker _marker = new();
            }
            """);
        fixture.WriteFeature(
            "FeatureLib/Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Feature.Lib.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "template.yaml");
        var exitCode = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--mode", "function-per-feature"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        Assert.Contains("HealthGetHealthFunction:", yaml);
        Assert.Contains("CodeUri: './artifacts/health-gethealth'", yaml);
        Assert.DoesNotContain("host_app_SliceLambdaFunctionPerFeatureHandlers", yaml);
    }

    [Fact]
    public async Task Package_aws_lambda_function_per_feature_writes_artifact_manifest()
    {
        using var fixture = CreateLambdaPackageFixture();

        await fixture.BuildAsync();

        var outputDir = Path.Combine(fixture.Directory.FullName, "artifacts", "lambda");
        var exitCode = await PackageAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputDir, "--mode", "function-per-feature", "--skip-publish"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var manifestPath = Path.Combine(outputDir, "slicefx-lambda-package.json");
        Assert.True(File.Exists(manifestPath));

        var json = await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"mode\": \"function-per-feature\"", json);
        Assert.Contains("\"artifactLayout\": \"per-feature\"", json);
        Assert.Contains("\"artifacts\": [", json);
        Assert.Contains("\"artifactId\": \"health-gethealth\"", json);
        Assert.Contains("\"codeUri\": \"artifacts/health-gethealth\"", json);
        Assert.Contains("\"bootstrapMode\": \"native-aot-bootstrap\"", json);
        Assert.Contains("\"endpointName\": \"Health.GetHealth\"", json);
        Assert.Contains("lambda-package-app::SliceFx.lambda_package_app_SliceLambdaFunctionPerFeatureHandlers_Health_GetHealth_", json);
        Assert.DoesNotContain(outputDir, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_aws_lambda_function_per_feature_writes_per_feature_artifacts()
    {
        using var fixture = CreateLambdaPackageFixture(includeSecondEligibleRoute: true, includeFilteredRoute: true);
        await fixture.BuildAsync();

        var outputDir = Path.Combine(fixture.Directory.FullName, "artifacts", "lambda");
        var exitCode = await PackageAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputDir, "--mode", "function-per-feature", "--artifact-layout", "per-feature", "--skip-publish"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var manifestPath = Path.Combine(outputDir, "slicefx-lambda-package.json");
        var reportPath = Path.Combine(outputDir, "slicefx-lambda-package-report.json");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(reportPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken));
        var root = document.RootElement;

        Assert.Equal("function-per-feature", root.GetProperty("mode").GetString());
        Assert.Equal("per-feature", root.GetProperty("artifactLayout").GetString());
        Assert.True(root.GetProperty("selfContained").GetBoolean());
        Assert.Equal(1, root.GetProperty("excludedRouteCount").GetInt32());

        var artifacts = root.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Equal(2, artifacts.Length);
        Assert.All(artifacts, static artifact =>
        {
            Assert.Equal("per-feature", artifact.GetProperty("artifactLayout").GetString());
            Assert.Equal("native-aot-bootstrap", artifact.GetProperty("bootstrapMode").GetString());
        });

        var artifactIds = artifacts.Select(static artifact => artifact.GetProperty("artifactId").GetString()).ToArray();
        Assert.Contains("health-gethealth", artifactIds);
        Assert.Contains("users-getuser", artifactIds);

        var codeUris = artifacts.Select(static artifact => artifact.GetProperty("codeUri").GetString()).ToArray();
        Assert.Equal(2, codeUris.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("artifacts/health-gethealth", codeUris);
        Assert.Contains("artifacts/users-getuser", codeUris);
        Assert.DoesNotContain(outputDir, string.Join('\n', codeUris), StringComparison.Ordinal);

        var functions = root.GetProperty("functions").EnumerateArray().ToArray();
        Assert.Equal(2, functions.Length);
        Assert.Contains(functions, static function =>
            function.GetProperty("endpointName").GetString() == "Health.GetHealth" &&
            function.GetProperty("artifactId").GetString() == "health-gethealth");
        Assert.Contains(functions, static function =>
            function.GetProperty("endpointName").GetString() == "Users.GetUser" &&
            function.GetProperty("artifactId").GetString() == "users-getuser");
        Assert.DoesNotContain(functions, static function => function.GetProperty("endpointName").GetString() == "Health.GetFilteredHealth");

        var healthProject = Path.Combine(outputDir, "obj", "aws-lambda", "per-feature", "health-gethealth", "bootstrap.csproj");
        var healthProgram = Path.Combine(outputDir, "obj", "aws-lambda", "per-feature", "health-gethealth", "Program.slicefx");
        Assert.True(File.Exists(healthProject));
        Assert.True(File.Exists(healthProgram));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "artifacts", "health-gethealth")));

        var projectXml = await File.ReadAllTextAsync(healthProject, TestContext.Current.CancellationToken);
        Assert.Contains("<AssemblyName>bootstrap</AssemblyName>", projectXml);
        Assert.Contains("<PublishAot>true</PublishAot>", projectXml);
        Assert.Contains("<TrimMode>full</TrimMode>", projectXml);
        Assert.Contains("<IlcOptimizationPreference>Size</IlcOptimizationPreference>", projectXml);
        Assert.Contains("<StripSymbols>false</StripSymbols>", projectXml);
        Assert.Contains("<BaseIntermediateOutputPath>", projectXml);
        Assert.Contains("<BaseOutputPath>", projectXml);
        Assert.Contains(Path.Combine("obj", "aws-lambda", "per-feature", "health-gethealth", "build"), projectXml);
        Assert.Contains("""<Compile Include="Program.slicefx" />""", projectXml);
        Assert.Contains("<Reference Include=\"lambda-package-app\"", projectXml);
        Assert.Contains("""<FrameworkReference Include="Microsoft.AspNetCore.App" />""", projectXml);
        Assert.Contains("Amazon.Lambda.APIGatewayEvents", projectXml);
        Assert.Contains("Amazon.Lambda.RuntimeSupport", projectXml);
        Assert.Contains("Amazon.Lambda.Serialization.SystemTextJson", projectXml);

        // CPM opt-out: ManagePackageVersionsCentrally=false must appear AFTER the Sdk.props import
        // so it overrides any ManagePackageVersionsCentrally=true inherited from a consumer repository's
        // Directory.Packages.props (which is imported by Sdk.props). Placing it before Sdk.props would
        // be silently overwritten (error NU1008 at restore time in CPM repos).
        var mpvcIndex = projectXml.IndexOf("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>", StringComparison.Ordinal);
        var sdkPropsIndex = projectXml.IndexOf("<Import Project=\"Sdk.props\"", StringComparison.Ordinal);
        Assert.True(mpvcIndex > 0, "bootstrap.csproj must opt out of Central Package Management.");
        Assert.True(mpvcIndex > sdkPropsIndex,
            "ManagePackageVersionsCentrally=false must appear after the Sdk.props import to override an inherited Directory.Packages.props.");

        var programSource = await File.ReadAllTextAsync(healthProgram, TestContext.Current.CancellationToken);
        Assert.Contains("JsonTypeInfoProvider = static type => LambdaFeatureJsonContext.Default.GetTypeInfo(type);", programSource);
        Assert.Contains("SourceGeneratorLambdaJsonSerializer<LambdaFeatureJsonContext>", programSource);
        Assert.Contains("SliceFx.lambda_package_app_SliceLambdaFunctionPerFeatureHandlers_Health_GetHealth_", programSource);
        Assert.Contains("[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]", programSource);
        Assert.Contains("[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]", programSource);

        using var reportDocument = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
        var reportRoot = reportDocument.RootElement;
        Assert.Equal("1", reportRoot.GetProperty("schemaVersion").GetString());
        Assert.Equal("function-per-feature", reportRoot.GetProperty("mode").GetString());
        Assert.Equal("per-feature", reportRoot.GetProperty("artifactLayout").GetString());
        Assert.Equal("native-aot", reportRoot.GetProperty("reportKind").GetString());
        Assert.True(reportRoot.GetProperty("skippedPublish").GetBoolean());
        Assert.Equal(0, reportRoot.GetProperty("warningBaseline").GetProperty("currentWarningCount").GetInt32());

        var reportArtifacts = reportRoot.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Equal(2, reportArtifacts.Length);
        Assert.Contains(reportArtifacts, static artifact =>
            artifact.GetProperty("artifactId").GetString() == "health-gethealth" &&
            artifact.GetProperty("endpointName").GetString() == "Health.GetHealth" &&
            artifact.GetProperty("skippedPublish").GetBoolean() &&
            artifact.GetProperty("sizeBytes").GetInt64() == 0 &&
            artifact.GetProperty("topFiles").GetArrayLength() == 0 &&
            artifact.GetProperty("warnings").GetArrayLength() == 0 &&
            artifact.GetProperty("closureInspection").GetProperty("status").GetString() == "skipped");
    }

    [Fact]
    public async Task Package_aws_lambda_function_per_feature_rejects_stale_warning_baseline()
    {
        using var fixture = CreateLambdaPackageFixture();
        await fixture.BuildAsync();

        var baselinePath = Path.Combine(fixture.Directory.FullName, "lambda-warning-baseline.json");
        await File.WriteAllTextAsync(
            baselinePath,
            JsonSerializer.Serialize(
                new
                {
                    warnings = new[]
                    {
                        new
                        {
                            code = "IL2026",
                            project = "stale.csproj",
                            file = "Program.cs",
                            line = 12,
                            messageHash = "stale-warning-hash",
                            message = "Stale warning",
                        },
                    },
                }), TestContext.Current.CancellationToken);

        var outputDir = Path.Combine(fixture.Directory.FullName, "artifacts", "lambda");
        var exitCode = await PackageAwsLambdaCommand.Build()
            .Parse([
                "--project", fixture.ProjectFile.FullName,
                "--output", outputDir,
                "--mode", "function-per-feature",
                "--skip-publish",
                "--warning-baseline", baselinePath,
            ])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);

        var reportPath = Path.Combine(outputDir, "slicefx-lambda-package-report.json");
        using var reportDocument = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
        var baseline = reportDocument.RootElement.GetProperty("warningBaseline");
        Assert.Equal(0, baseline.GetProperty("currentWarningCount").GetInt32());
        Assert.Equal(1, baseline.GetProperty("staleBaselineCount").GetInt32());
        Assert.Equal("stale-warning-hash", baseline.GetProperty("staleEntries")[0].GetProperty("messageHash").GetString());
    }

    [Fact]
    public void Lambda_package_closure_inspection_fails_on_sibling_feature_type_leak()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), "slice-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageRoot);
        try
        {
            var artifactDir = Path.Combine(packageRoot, "artifacts", "health-gethealth");
            var buildRoot = Path.Combine(packageRoot, "obj", "aws-lambda", "per-feature", "health-gethealth", "build");
            Directory.CreateDirectory(artifactDir);
            Directory.CreateDirectory(buildRoot);

            File.Copy(typeof(CliFixtureTests).Assembly.Location, Path.Combine(buildRoot, "bootstrap.mstat"));
            File.WriteAllText(Path.Combine(buildRoot, "bootstrap.map"), "");

            var current = CreateClosureTestRoute("Health", "GetHealth");
            var sibling = CreateClosureTestRoute("Users", "GetUser");

            var inspection = LambdaPackageClosureInspector.Inspect(
                current,
                [current, sibling],
                artifactDir,
                buildRoot,
                packageRoot,
                skippedPublish: false);

            Assert.False(inspection.Passed);
            Assert.Equal("failed", inspection.Status);
            Assert.Empty(inspection.MissingFiles);
            Assert.Contains(inspection.ForbiddenHits, static hit =>
                hit.TypeIdentity == "SliceFx.Cli.Tests.ClosureFixture.Features.Users.GetUser" &&
                hit.Reason.Contains("sibling feature entrypoint", StringComparison.Ordinal));
            Assert.Contains(inspection.ForbiddenHits, static hit =>
                hit.TypeIdentity == "SliceFx.Cli.Tests.ClosureFixture.Features.Users.GetUserValidator" &&
                hit.Reason.Contains("sibling validator", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Package_aws_lambda_rejects_unknown_artifact_layout()
    {
        var exitCode = await PackageAwsLambdaCommand.Build()
            .Parse(["--mode", "function-per-feature", "--artifact-layout", "unknown"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Package_aws_lambda_per_feature_requires_rid_when_publishing()
    {
        using var fixture = CreateLambdaPackageFixture();
        await fixture.BuildAsync();

        var outputDir = Path.Combine(fixture.Directory.FullName, "artifacts", "lambda");
        var exitCode = await PackageAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputDir, "--mode", "function-per-feature", "--artifact-layout", "per-feature"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(outputDir));
    }

    [Fact]
    public async Task New_wasi_cloudflare_scaffolds_deployment_host_files()
    {
        using var fixture = CliProjectFixture.Create(
            "My.Wasi.App",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>My.Wasi.App</RootNamespace>
                <AssemblyName>My.Wasi.App</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var exitCode = await NewWasiCloudflareCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);

        var dist = Path.Combine(fixture.Directory.FullName, "dist");
        Assert.True(File.Exists(Path.Combine(dist, "package.json")));
        Assert.True(File.Exists(Path.Combine(dist, "shim.mjs")));
        Assert.True(File.Exists(Path.Combine(dist, "generate-module-map.mjs")));
        Assert.True(File.Exists(Path.Combine(dist, "wrangler.toml")));
        Assert.True(File.Exists(Path.Combine(dist, "wrangler.deploy.toml")));
        Assert.True(File.Exists(Path.Combine(dist, "stubs", "tcp.js")));
        Assert.True(File.Exists(Path.Combine(dist, "stubs", "udp.js")));

        var packageJson = await File.ReadAllTextAsync(Path.Combine(dist, "package.json"), TestContext.Current.CancellationToken);
        var shim = await File.ReadAllTextAsync(Path.Combine(dist, "shim.mjs"), TestContext.Current.CancellationToken);
        var moduleMap = await File.ReadAllTextAsync(Path.Combine(dist, "generate-module-map.mjs"), TestContext.Current.CancellationToken);

        Assert.Contains("\"name\": \"my-wasi-app-host\"", packageJson);
        Assert.Contains("dotnet publish \\\"../My.Wasi.App.csproj\\\" -r wasi-wasm", packageJson);
        Assert.Contains("jco transpile \\\"my-wasi-app.wasm\\\"", packageJson);
        Assert.Contains("\"node\": \">=22.0.0\"", packageJson);
        Assert.Contains("\"binaryen\": \"129.0.0\"", packageJson);
        Assert.Contains("\"wrangler\": \"4.93.1\"", packageJson);
        Assert.DoesNotContain("\"wrangler\": \"^", packageJson);
        Assert.DoesNotContain("npx wasm-opt", packageJson);
        Assert.Contains("import { instantiate } from \"./component/my-wasi-app.js\";", shim);
        Assert.Contains("_setArgs([\"My.Wasi.App\"]);", shim);
        Assert.Contains("cfRequest.body.getReader()", shim);
        Assert.DoesNotContain("arrayBuffer()", shim);
        Assert.Contains("my-wasi-app", moduleMap);
        Assert.Contains("new URL(\"modules.mjs\", componentDir)", moduleMap);
        Assert.DoesNotContain("componentDir.pathname", moduleMap);
        Assert.DoesNotContain("__COMPONENT_NAME__", moduleMap, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_wasi_cloudflare_custom_output_keeps_scripts_project_relative()
    {
        using var fixture = CliProjectFixture.Create(
            "My.Wasi.App",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>My.Wasi.App</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var outputDir = Path.Combine(fixture.Directory.FullName, "deploy", "cloudflare");
        var exitCode = await NewWasiCloudflareCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputDir])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);

        var packageJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "package.json"), TestContext.Current.CancellationToken);

        Assert.Contains("dotnet publish \\\"../../My.Wasi.App.csproj\\\" -r wasi-wasm", packageJson);
        Assert.Contains("jco transpile \\\"../../dist/my-wasi-app.wasm\\\"", packageJson);
    }

    [Fact]
    public async Task New_wasi_cloudflare_escapes_app_name_in_generated_javascript()
    {
        using var fixture = CliProjectFixture.Create(
            "My.Wasi.App",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>My&quot;\Wasi</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var exitCode = await NewWasiCloudflareCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);

        var shim = await File.ReadAllTextAsync(Path.Combine(fixture.Directory.FullName, "dist", "shim.mjs"), TestContext.Current.CancellationToken);

        Assert.Contains("""_setArgs(["My\"\\Wasi"]);""", shim);
        Assert.DoesNotContain("_setArgs(['", shim);
    }

    private static CliProjectFixture CreateHostWithReferencedFeatureLibrary(string? aggregationProperty)
    {
        var propertyLine = string.IsNullOrWhiteSpace(aggregationProperty)
            ? ""
            : Environment.NewLine + "    " + aggregationProperty;
        var fixture = CliProjectFixture.Create(
            "host-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Host.App</RootNamespace>
                <SliceFxRole>Host</SliceFxRole>{{propertyLine}}
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="FeatureLib/feature-lib.csproj" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
              <ItemGroup>
                <CompilerVisibleProperty Include="SliceFxRole" />
                <CompilerVisibleProperty Include="SliceFxReferencedAssemblies" />
                <CompilerVisibleProperty Include="SliceFxAggregateReferences" />
              </ItemGroup>
              <ItemGroup>
                <Compile Remove="FeatureLib/**/*.cs" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "FeatureLib/feature-lib.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>feature-lib</AssemblyName>
                <RootNamespace>Feature.Lib</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "UseFeatureLib.cs",
            """
            namespace Host.App;

            public sealed class UseFeatureLib
            {
                private Feature.Lib.Marker _marker = new();
            }
            """);
        fixture.WriteFeature(
            "FeatureLib/Marker.cs",
            """
            namespace Feature.Lib;

            public sealed class Marker;
            """);
        fixture.WriteFeature(
            "FeatureLib/Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Feature.Lib.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        return fixture;
    }

    private static CliProjectFixture CreateLambdaPackageFixture(
        bool includeSecondEligibleRoute = false,
        bool includeFilteredRoute = false)
    {
        var fixture = CliProjectFixture.Create(
            "lambda-package-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Package.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Lambda.FunctionPerFeature", "SliceFx.Lambda.FunctionPerFeature.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "LambdaSetup.cs",
            """
            using SliceFx.Lambda.FunctionPerFeature;

            [assembly: LambdaFunctionPerFeature]
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using SliceFx;

            namespace Lambda.Package.App.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        if (includeSecondEligibleRoute)
        {
            fixture.WriteFeature(
                "Features/Users/GetUser.cs",
                """
                using SliceFx;

                namespace Lambda.Package.App.Features.Users;

                [Feature("GET /users/{id:int}")]
                public static class GetUser
                {
                    public static string Handle(int id) => id.ToString();
                }
                """);
        }

        if (includeFilteredRoute)
        {
            fixture.WriteFeature(
                "Features/Health/GetFilteredHealth.cs",
                """
                using System.Threading.Tasks;
                using Microsoft.AspNetCore.Http;
                using SliceFx;

                namespace Lambda.Package.App.Features.Health;

                [Feature("GET /health/filtered")]
                [Filter<AuditFilter>]
                public static class GetFilteredHealth
                {
                    public static string Handle() => "ok";
                }

                public sealed class AuditFilter : IEndpointFilter
                {
                    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                        => next(context);
                }
                """);
        }

        return fixture;
    }

    private static string FindRepoRoot()
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

    [Fact]
    public async Task Package_aws_lambda_rejects_invalid_generated_handler_metadata()
    {
        var route = CreateClosureTestRoute("Health", "GetHealth") with
        {
            LambdaFunctionPerFeatureHandlerType = "SliceFx.Bad\"Handler",
        };
        var projectDir = Path.Combine(Path.GetTempPath(), "slice-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);
        try
        {
            var method = typeof(PackageAwsLambdaCommand).GetMethod(
                "WritePerFeatureProjectAsync",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var task = (Task)method.Invoke(
                null,
                [route, route.LambdaFunctionPerFeatureArtifactId!, projectDir, Array.Empty<FileInfo>(), CancellationToken.None])!;

            var ex = await Assert.ThrowsAsync<CliException>(async () => await task.ConfigureAwait(false));
            Assert.Contains("invalid Lambda function-per-feature handler type metadata", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    private static SliceRouteInfo CreateClosureTestRoute(string tag, string featureName)
    {
        var artifactId = (tag + "-" + featureName).ToLowerInvariant();
        var @namespace = $"SliceFx.Cli.Tests.ClosureFixture.Features.{tag}";
        var validatorTypes = tag == "Users"
            ? [$"{@namespace}.GetUserValidator"]
            : Array.Empty<string>();
        return new SliceRouteInfo(
            "GET",
            "/" + tag.ToLowerInvariant(),
            @namespace,
            featureName,
            tag,
            tag + "." + featureName,
            null,
            null,
            "System.String",
            "portable",
            null,
            [],
            [],
            LambdaFunctionPerFeatureStatus: "eligible",
            LambdaFunctionPerFeatureHandlerAssembly: "SliceFx.Cli.Tests",
            LambdaFunctionPerFeatureHandlerType: @namespace + "." + featureName + "Handler",
            LambdaFunctionPerFeatureHandlerMethod: "FunctionHandler",
            LambdaFunctionPerFeatureArtifactId: artifactId,
            LambdaFunctionPerFeatureArtifactLayout: "per-feature",
            LambdaFunctionPerFeatureArtifactCodeUri: "artifacts/" + artifactId,
            LambdaFunctionPerFeatureBootstrapMode: "native-aot-bootstrap",
            HasGeneratedMetadata: true,
            ValidatorTypes: validatorTypes);
    }

    [Fact]
    public async Task Generated_csharp_client_emits_typed_exception_helpers_and_compiles_cleanly()
    {
        using var fixture = CliProjectFixture.Create(
            "client-exception-helpers",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>ClientExceptionHelpers</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Users/CreateUser.cs",
            """
            using System.ComponentModel.DataAnnotations;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace ClientExceptionHelpers.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public sealed record Request([Required, MinLength(2)] string Name);
                public sealed record Response(int Id, string Name);
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1, req.Name));
            }
            """);
        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("class SliceApiException", client);
        Assert.Contains("class SliceProblemDetails", client);
        Assert.Contains("if (!__response.IsSuccessStatusCode)", client);
        Assert.Contains("PropertyNameCaseInsensitive = true", client);
        Assert.Contains("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase", client);
        Assert.Contains("application/problem+json", client);
        Assert.Contains("application/json", client);
        Assert.DoesNotContain("__response.EnsureSuccessStatusCode();", client);

        // Verify generated client compiles cleanly via real SDK (includes SliceApiException, nullable refs, all usings)
        await fixture.BuildAsync();
    }

    [Fact]
    public async Task Generated_csharp_client_compiles_against_minimal_runtime_sdk()
    {
        // The server uses non-nested Contracts types (shared-contracts pattern) so the generated
        // client references fully-qualified names from a contracts namespace that the client fixture
        // can reproduce locally without any server assembly reference.
        using var serverFixture = CliProjectFixture.Create(
            "minimal-sdk-server",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>MinimalSdkServer</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        // Non-nested Contracts types: the generator emits FQN references to these.
        serverFixture.WriteFeature("Contracts/UserContracts.cs",
            """
            namespace MinimalSdkServer.Contracts;

            public record CreateUserRequest(string Name);
            public record CreateUserResponse(int Id, string Name);
            """);
        serverFixture.WriteFeature("Features/Users/CreateUser.cs",
            """
            using System.ComponentModel.DataAnnotations;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using MinimalSdkServer.Contracts;

            namespace MinimalSdkServer.Features.Users;

            [Feature("POST /users")]
            public static class CreateUser
            {
                public static Task<CreateUserResponse> Handle(
                    [Required] CreateUserRequest req, CancellationToken ct)
                    => Task.FromResult(new CreateUserResponse(1, req.Name));
            }
            """);
        await serverFixture.BuildAsync();

        var generatedClientFile = Path.Combine(serverFixture.Directory.FullName, "SliceApiClient.g.cs");
        var genExit = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", serverFixture.ProjectFile.FullName, "--output", generatedClientFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, genExit);

        // Client fixture: bare Microsoft.NET.Sdk — no SliceFx.Core, no Microsoft.AspNetCore.App,
        // no Microsoft.Extensions.Http. Reproduces the contracts types locally (the shared-contracts
        // pattern). This is the load-bearing test: if the generator ever emits a type from
        // Microsoft.Extensions.Http or the ASP.NET shared framework, this build will fail.
        using var clientFixture = CliProjectFixture.Create(
            "minimal-sdk-client",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>MinimalSdkClient</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        // Reproduce the same contracts types under the same FQN namespace so the generated
        // client method signatures resolve without referencing the server assembly.
        clientFixture.WriteFeature("Contracts/UserContracts.cs",
            """
            namespace MinimalSdkServer.Contracts;

            public record CreateUserRequest(string Name);
            public record CreateUserResponse(int Id, string Name);
            """);

        File.Copy(generatedClientFile, Path.Combine(clientFixture.Directory.FullName, "SliceApiClient.g.cs"));

        await clientFixture.BuildAsync();
    }

    [Theory]
    [InlineData("routes")]
    public async Task Project_option_accepts_directory_path(string verb)
    {
        using var fixture = CliProjectFixture.Create(
            "dir-path-test",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>DirPathTest</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Health/GetHealth.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace DirPathTest.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public record Response(string Status);
                public static Task<Response> Handle(CancellationToken ct)
                    => Task.FromResult(new Response("ok"));
            }
            """);

        // Pass the directory, not the .csproj file — this is what the test exercises.
        var exitCode = verb switch
        {
            "routes" => await ListRoutesCommand.Build()
                .Parse(["--project", fixture.Directory.FullName])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken),
            _ => throw new InvalidOperationException($"Unmapped verb: {verb}")
        };

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Project_option_directory_with_no_csproj_throws()
    {
        var emptyDir = Directory.CreateTempSubdirectory("slicefx-test-empty-").FullName;
        try
        {
            var ex = Assert.Throws<CliException>(() => ProjectContextDiscovery.Discover(emptyDir));
            Assert.Contains("No *.csproj found in", ex.Message);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void Project_option_directory_with_multiple_csprojs_throws()
    {
        var dir = Directory.CreateTempSubdirectory("slicefx-test-multi-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "A.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(dir, "B.csproj"), "<Project />");
            var ex = Assert.Throws<CliException>(() => ProjectContextDiscovery.Discover(dir));
            Assert.Contains("Multiple *.csproj found", ex.Message);
            Assert.Contains("Use --project to specify which one.", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveOutputFile_helper_handles_null_file_and_directory_inputs()
    {
        var defaultDir = new DirectoryInfo(Directory.CreateTempSubdirectory("slicefx-resolve-out-").FullName);
        try
        {
            // null → default file in default directory
            var result1 = SharedOptions.ResolveOutputFile(null, "out.json", defaultDir);
            Assert.Equal(Path.Combine(defaultDir.FullName, "out.json"), result1);

            // explicit file path → resolved to absolute
            var explicitFile = Path.Combine(defaultDir.FullName, "custom.json");
            var result2 = SharedOptions.ResolveOutputFile(explicitFile, "out.json", defaultDir);
            Assert.Equal(Path.GetFullPath(explicitFile), result2);

            // directory path → default file name inside that directory
            var result3 = SharedOptions.ResolveOutputFile(defaultDir.FullName, "out.json", defaultDir);
            Assert.Equal(Path.Combine(Path.GetFullPath(defaultDir.FullName), "out.json"), result3);

            // relative file path → resolved to absolute
            var cwd = Directory.GetCurrentDirectory();
            var relative = "relative-output.json";
            var result4 = SharedOptions.ResolveOutputFile(relative, "out.json", defaultDir);
            Assert.Equal(Path.GetFullPath(relative, cwd), result4);
        }
        finally
        {
            defaultDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Output_option_accepts_directory_path_for_csharp_client()
    {
        using var fixture = CliProjectFixture.Create(
            "output-dir-path-test",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>OutputDirPathTest</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Health/GetHealth.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace OutputDirPathTest.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public record Response(string Status);
                public static Task<Response> Handle(CancellationToken ct)
                    => Task.FromResult(new Response("ok"));
            }
            """);

        await fixture.BuildAsync();

        // Pass the directory, not a file — the command should place SliceApiClient.g.cs inside it.
        var outputDir = fixture.Directory.FullName;
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputDir, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var expectedFile = Path.Combine(outputDir, "SliceApiClient.g.cs");
        Assert.True(File.Exists(expectedFile), $"Expected generated file at {expectedFile}");
    }

    [Fact]
    public async Task Generated_csharp_client_surfaces_structured_validation_errors_at_runtime()
    {
        using var fixture = CliProjectFixture.Create(
            "client-runtime-errors",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>ClientRuntimeErrors</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace ClientRuntimeErrors.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public sealed record Response(int Id);
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id));
            }
            """);
        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        await fixture.BuildAsync();

        var dllPath = Path.Combine(fixture.Directory.FullName, "bin", "Debug", "net10.0", "client-runtime-errors.dll");
        var assembly = Assembly.LoadFrom(dllPath);

        var problemJson = /*lang=json,strict*/ "{\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"Name\":[\"Name is required.\"]}}";
        var stubResponse = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
        {
            Content = new StringContent(problemJson, System.Text.Encoding.UTF8, "application/problem+json"),
        };
        using var handler = new StubHttpHandler(stubResponse);

        var clientType = assembly.GetTypes().Single(static t => t.Name == "SliceApiClient" && !t.IsNested);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var ctor = clientType.GetConstructor([typeof(HttpClient)])!;
        var clientInstance = ctor.Invoke([httpClient])!;

        var groupProp = clientType.GetProperties().First(static p => p.PropertyType.Name.EndsWith("Client", StringComparison.Ordinal));
        var groupClientInstance = groupProp.GetValue(clientInstance)!;
        var method = groupClientInstance.GetType().GetMethod("GetItemAsync")!;
        var task = (Task)method.Invoke(groupClientInstance, [42, CancellationToken.None])!;

        Exception? caught = null;
        try { await task; }
        catch (Exception ex) { caught = ex; }

        Assert.NotNull(caught);
        Assert.Equal("SliceApiException", caught.GetType().Name);

        var errorsProp = caught.GetType().GetProperty("Errors")!;
        var errors = (IReadOnlyDictionary<string, string[]>?)errorsProp.GetValue(caught);
        Assert.NotNull(errors);
        Assert.True(errors.ContainsKey("Name"), "Key 'Name' must be preserved verbatim (not lowercased to 'name').");
        Assert.Contains("Name is required.", errors["Name"]);
    }

    [Fact]
    public async Task Generated_csharp_client_serializes_request_body_as_camelCase()
    {
        // Regression test: the auto-emitted JsonContext must include PropertyNamingPolicy = CamelCase so
        // request bodies are serialized as camelCase (matching ASP.NET / Web-defaults / SliceFx AOT convention).
        // Previously only PropertyNameCaseInsensitive = true was emitted, which only affects deserialization,
        // causing servers configured with CamelCase to receive null-bound request properties.
        using var fixture = CliProjectFixture.Create(
            "client-camelcase-body",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>CamelCaseBody</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Items/CreateItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace CamelCaseBody.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public sealed record Request(string Name);
                public sealed record Response(int Id);
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1));
            }
            """);
        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        // Static assertion: the generated context attribute must include the camelCase naming policy.
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase", client);

        await fixture.BuildAsync();

        var dllPath = Path.Combine(fixture.Directory.FullName, "bin", "Debug", "net10.0", "client-camelcase-body.dll");
        var assembly = Assembly.LoadFrom(dllPath);

        string? capturedBody = null;
        var okJson = /*lang=json,strict*/ "{\"id\":1}";
        var stubResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(okJson, System.Text.Encoding.UTF8, "application/json"),
        };
        using var handler = new CapturingHttpHandler(stubResponse, req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
        });

        var clientType = assembly.GetTypes().Single(static t => t.Name == "SliceApiClient" && !t.IsNested);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var ctor = clientType.GetConstructor([typeof(HttpClient)])!;
        var clientInstance = ctor.Invoke([httpClient])!;

        var groupProp = clientType.GetProperties().First(static p => p.PropertyType.Name.EndsWith("Client", StringComparison.Ordinal));
        var groupClientInstance = groupProp.GetValue(clientInstance)!;
        var method = groupClientInstance.GetType().GetMethod("CreateItemAsync")!;

        // Construct the generated Request type (first nested type named "Request").
        var requestType = assembly.GetTypes().Single(static t => t.Name == "Request" && t.IsNested);
        var requestInstance = Activator.CreateInstance(requestType, ["TestName"]);
        var task = (Task)method.Invoke(groupClientInstance, [requestInstance, CancellationToken.None])!;
        await task;

        // Runtime assertion: the captured JSON body must be camelCase.
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.True(doc.RootElement.TryGetProperty("name", out _),
            $"Expected camelCase key 'name' in serialized body, got: {capturedBody}");
        Assert.False(doc.RootElement.TryGetProperty("Name", out _),
            $"Expected no PascalCase key 'Name' in serialized body, got: {capturedBody}");
    }

    [Fact]
    public async Task Csharp_client_default_emits_internal_json_context_for_trim_safety()
    {
        using var fixture = CliProjectFixture.Create(
            "trim-safe-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TrimSafe.Client.App</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Contracts.cs",
            """
            namespace TrimSafe.Client.App.Contracts;
            public sealed record CreateItemRequest(string Name);
            public sealed record CreateItemResponse(int Id, string Name);
            public sealed record ItemSummary(int Id, string Name);
            """);
        fixture.WriteFeature("Features/Items/CreateItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using System.Collections.Generic;
            using SliceFx;
            using TrimSafe.Client.App.Contracts;

            namespace TrimSafe.Client.App.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public static Task<CreateItemResponse> Handle(CreateItemRequest request, CancellationToken ct)
                    => Task.FromResult(new CreateItemResponse(1, request.Name));
            }
            """);
        fixture.WriteFeature("Features/Items/ListItems.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using System.Collections.Generic;
            using SliceFx;
            using TrimSafe.Client.App.Contracts;

            namespace TrimSafe.Client.App.Features.Items;

            [Feature("GET /items")]
            public static class ListItems
            {
                public static Task<IReadOnlyList<ItemSummary>> Handle(CancellationToken ct)
                    => Task.FromResult<IReadOnlyList<ItemSummary>>([]);
            }
            """);
        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        // Auto-emitted context class
        Assert.Contains("internal sealed partial class SliceApiClientJsonContext : JsonSerializerContext", client);
        Assert.Contains("[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]", client);
        Assert.Contains("[JsonSerializable(typeof(TrimSafe.Client.App.Contracts.CreateItemRequest))]", client);
        Assert.Contains("[JsonSerializable(typeof(TrimSafe.Client.App.Contracts.CreateItemResponse))]", client);
        Assert.Contains("TypeInfoPropertyName = \"ItemSummaryList\"", client);
        Assert.Contains("[JsonSerializable(typeof(SliceApiClient.SliceProblemDetails))]", client);

        // Trim-safe method bodies
        Assert.Contains("ReadFromJsonAsync(SliceApiClientJsonContext.Default.ItemSummaryList, cancellationToken)", client);
        Assert.Contains("JsonContent.Create(request, SliceApiClientJsonContext.Default.CreateItemRequest)", client);
        Assert.Contains("ReadFromJsonAsync(SliceApiClientJsonContext.Default.CreateItemResponse, cancellationToken)", client);
        Assert.Contains("JsonSerializer.Deserialize(__body, SliceApiClientJsonContext.Default.SliceProblemDetails)", client);

        // No reflection-based paths
        Assert.DoesNotContain("ReadFromJsonAsync<", client);
        Assert.DoesNotContain("JsonContent.Create(request)", client);
        Assert.DoesNotContain("__ProblemJsonOptions", client);

        // Compiles cleanly
        await fixture.BuildAsync();
    }

    [Fact]
    public async Task Csharp_client_uses_user_provided_json_context_when_specified()
    {
        using var fixture = CliProjectFixture.Create(
            "custom-context-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>CustomContext.Client.App</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Contracts.cs",
            """
            namespace CustomContext.Client.App.Contracts;
            public sealed record GetThingResponse(int Id);
            """);
        fixture.WriteFeature("Features/Things/GetThing.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using CustomContext.Client.App.Contracts;

            namespace CustomContext.Client.App.Features.Things;

            [Feature("GET /things/{id:int}")]
            public static class GetThing
            {
                public static Task<GetThingResponse> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new GetThingResponse(id));
            }
            """);
        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse([
                "--project", fixture.ProjectFile.FullName,
                "--output", outputFile,
                "--json-context", "My.App.MyJsonContext",
                "--force"
            ])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        // User context FQN is referenced
        Assert.Contains("My.App.MyJsonContext.Default.GetThingResponse", client);
        Assert.Contains("My.App.MyJsonContext.Default.SliceProblemDetails", client);

        // Auto-emitted context must NOT appear
        Assert.DoesNotContain("internal sealed partial class SliceApiClientJsonContext", client);
        Assert.DoesNotContain("[JsonSerializable(", client);

        // Write a minimal MyJsonContext that satisfies the --json-context contract and verify compilation.
        // The generated client's default namespace is {RootNamespace}.Client = "CustomContext.Client.App.Client".
        fixture.WriteFeature("MyJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using CustomContext.Client.App.Client;
            using CustomContext.Client.App.Contracts;

            namespace My.App;

            [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
            [JsonSerializable(typeof(GetThingResponse))]
            [JsonSerializable(typeof(SliceApiClient.SliceProblemDetails))]
            public partial class MyJsonContext : JsonSerializerContext { }
            """);
        await fixture.BuildAsync();
    }

    // Skip locally with: dotnet test --filter "Category!=RequiresPublish"
    [Fact]
    [Trait("Category", "RequiresPublish")]
    public async Task Csharp_client_publishes_trim_safe_under_release_publishtrimmed_full()
    {
        // Phase 1: Server fixture (SliceFx.Core + SourceGenerator) — build to produce route manifest.
        using var serverFixture = CliProjectFixture.Create(
            "trim-publish-server-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TrimPublish.Server.App</RootNamespace>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        // Contracts live in a shared namespace so the generated client's type references
        // (TrimPublish.Shared.Contracts.*) resolve in the separate client fixture.
        serverFixture.WriteFeature("Contracts.cs",
            """
            namespace TrimPublish.Shared.Contracts;
            public sealed record CreateItemRequest(string Name, string Email);
            public sealed record CreateItemResponse(System.Guid Id, string Name, string Email);
            public sealed record ItemSummary(System.Guid Id, string Name);
            """);
        serverFixture.WriteFeature("Features/Items/CreateItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using TrimPublish.Shared.Contracts;

            namespace TrimPublish.Server.App.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public static Task<CreateItemResponse> Handle(CreateItemRequest request, CancellationToken ct)
                    => Task.FromResult(new CreateItemResponse(System.Guid.NewGuid(), request.Name, request.Email));
            }
            """);
        serverFixture.WriteFeature("Features/Items/ListItems.cs",
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using TrimPublish.Shared.Contracts;

            namespace TrimPublish.Server.App.Features.Items;

            [Feature("GET /items")]
            public static class ListItems
            {
                public static Task<IReadOnlyList<ItemSummary>> Handle(CancellationToken ct)
                    => Task.FromResult<IReadOnlyList<ItemSummary>>([]);
            }
            """);
        await serverFixture.BuildAsync();

        // Phase 2: Generate SliceApiClient.g.cs from the server manifest.
        var clientSrcDir = Path.Combine(serverFixture.Directory.FullName, "client-src");
        Directory.CreateDirectory(clientSrcDir);
        var generatedClientPath = Path.Combine(clientSrcDir, "SliceApiClient.g.cs");
        var genExitCode = await GenerateCSharpClientCommand.Build()
            .Parse([
                "--project", serverFixture.ProjectFile.FullName,
                "--namespace", "TrimPublish.Client.App",
                "--output", generatedClientPath,
                "--force",
            ])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, genExitCode);

        // Phase 3: Client fixture — intentionally no SliceFx.Core/ASP.NET reference.
        // PublishTrimmed=true + TrimMode=full here; the client only uses System.Net.Http + System.Text.Json.
        using var clientFixture = CliProjectFixture.Create(
            "trim-publish-client-app",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TrimPublish.Client.App</RootNamespace>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <PublishTrimmed>true</PublishTrimmed>
                <TrimMode>full</TrimMode>
                <TrimmerSingleWarn>false</TrimmerSingleWarn>
                <SuppressTrimAnalysisWarnings>false</SuppressTrimAnalysisWarnings>
                <InvariantGlobalization>true</InvariantGlobalization>
              </PropertyGroup>
            </Project>
            """);
        // Contract records declared in the same shared namespace used by the server.
        // This mirrors the shared-contracts project pattern (no separate project needed here).
        clientFixture.WriteFeature("Contracts.cs",
            """
            namespace TrimPublish.Shared.Contracts;
            public sealed record CreateItemRequest(string Name, string Email);
            public sealed record CreateItemResponse(System.Guid Id, string Name, string Email);
            public sealed record ItemSummary(System.Guid Id, string Name);
            """);
        File.Copy(generatedClientPath, Path.Combine(clientFixture.Directory.FullName, "SliceApiClient.g.cs"));
        // Program.cs establishes a real trim root into every generated method body.
        // typeof() alone would not cause ILLink to walk method bodies; Func<> + conditional call does.
        clientFixture.WriteFeature("Program.cs",
            """
            using System;
            using System.Collections.Generic;
            using System.Net;
            using System.Net.Http;
            using TrimPublish.Client.App;
            using TrimPublish.Shared.Contracts;

            var client = new SliceApiClient(new HttpClient());
            Func<System.Threading.Tasks.Task<CreateItemResponse>> createCall =
                () => client.Items.CreateItemAsync(new CreateItemRequest("x", "y@z"));
            Func<System.Threading.Tasks.Task<IReadOnlyList<ItemSummary>>> listCall =
                () => client.Items.ListItemsAsync();
            _ = new SliceApiClient.SliceApiException("t", HttpStatusCode.OK, null);

            if (args.Length > 999_999)
            {
                _ = await createCall();
                _ = await listCall();
            }

            Console.WriteLine("ok");
            """);

        // Phase 4: Build (sanity-check that trim analyzer doesn't fire at build time either).
        await clientFixture.BuildAsync();

        // Phase 5: Publish with full-trim enabled.
        var publishDir = Path.Combine(clientFixture.Directory.FullName, "publish-out");
        var binlogRoot = Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath();
        var binlogDir = Path.Combine(binlogRoot, "slicefx-test-binlogs");
        Directory.CreateDirectory(binlogDir);
        var binlogPath = Path.Combine(binlogDir, $"csharp-client-trim-{Guid.NewGuid():N}.binlog");

        var (publishExit, publishOut, publishErr) = await RunProcessAsync(
            "dotnet",
            [
                "publish", clientFixture.ProjectFile.FullName,
                "--configuration", "Release",
                "--runtime", "linux-x64",
                "--output", publishDir,
                $"-bl:{binlogPath}",
                "--verbosity", "minimal",
                "-nologo",
            ],
            clientFixture.Directory.FullName);

        Assert.True(publishExit == 0,
            $"dotnet publish failed (exit {publishExit}).\nbinlog: {binlogPath}\nstdout:\n{publishOut}\nstderr:\n{publishErr}");

        // Phase 6: Parse binlog and assert no ILLink trim warnings (IL2026, IL3050, etc.).
        var build = Microsoft.Build.Logging.StructuredLogger.BinaryLog.ReadBuild(binlogPath);
        var ilWarnings = build
            .FindChildrenRecursive<Microsoft.Build.Logging.StructuredLogger.Warning>(static _ => true)
            .Where(static w => w.Code is { Length: >= 3 } code
                && code.StartsWith("IL", StringComparison.Ordinal)
                && code[2..].All(char.IsAsciiDigit))
            .ToArray();

        if (ilWarnings.Length > 0)
        {
            var shown = ilWarnings.Take(10).Select(w =>
                $"  {w.Code} at {w.File ?? "?"}:{w.LineNumber} — {w.Text}");
            var suffix = ilWarnings.Length > 10
                ? $"\n  … and {ilWarnings.Length - 10} more (open binlog: {binlogPath})"
                : "";
            Assert.Fail(
                $"Generated SliceApiClient.g.cs has {ilWarnings.Length} trim warning(s). " +
                "The emitter in GenerateCSharpClientCommand.cs must use JsonTypeInfo<T> overloads only.\n" +
                string.Join("\n", shown) + suffix);
        }
    }

    [Fact]
    public async Task Generated_typescript_client_emits_typed_error_helpers()
    {
        using var fixture = CliProjectFixture.Create(
            "ts-exception-helpers",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TsExceptionHelpers</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TsExceptionHelpers.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public sealed record Response(int Id);
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id));
            }
            """);
        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var exitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);

        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("class SliceApiError", client);
        Assert.Contains("interface SliceProblemDetails", client);
        Assert.Contains("response.json()", client);
        Assert.Contains("content-type", client);
        Assert.Contains("throw new SliceApiError(", client);
    }

    [Fact]
    public async Task Generated_typescript_client_passes_tsc_type_check()
    {
        var tsDir = Path.Combine(FindRepoRoot(), "eng", "typescript-typecheck");
        var localTsc = Path.Combine(tsDir, "node_modules", "typescript", "bin", "tsc");

        // Verify node is reachable; skip if absent. CI provisions Node via setup-node.
        bool nodeAvailable;
        try
        {
            var (nodeExit, _, _) = await RunProcessAsync("node", ["--version"], null);
            nodeAvailable = nodeExit == 0;
        }
        catch { nodeAvailable = false; }
        if (!nodeAvailable)
        {
            Assert.Skip("node not found / not runnable; CI provisions Node via setup-node.");
        }

        // Provision local TypeScript via npm ci when node_modules is absent (first local run).
        // CI pre-installs via the "Install TypeScript for client type-check tests" step.
        if (!File.Exists(localTsc))
        {
            var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
            bool npmRestored;
            try
            {
                var (npmExit, _, _) = await RunProcessAsync(npm, ["ci"], tsDir);
                npmRestored = npmExit == 0;
            }
            catch { npmRestored = false; }
            if (!npmRestored)
            {
                Assert.Skip("npm ci failed / npm not found; CI provisions Node+npm via setup-node.");
            }
        }

        using var fixture = CliProjectFixture.Create(
            "ts-compile-check",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TsCompileCheck</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature("Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TsCompileCheck.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public sealed record Response(int Id);
                public static Task<Response> Handle(int id, CancellationToken ct)
                    => Task.FromResult(new Response(id));
            }
            """);
        await fixture.BuildAsync();

        var tsOutput = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var tsExitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", tsOutput, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, tsExitCode);

        var tsconfig = Path.Combine(fixture.Directory.FullName, "tsconfig.json");
        await File.WriteAllTextAsync(tsconfig,
            /*lang=json,strict*/ """{"compilerOptions":{"strict":true,"noEmit":true,"lib":["DOM","ES2020"],"target":"ES2020"}}""",
            TestContext.Current.CancellationToken);

        // Run tsc via `node <localTsc>` to avoid platform differences in .bin shim scripts.
        var (tscExit, tscOut, tscErr) = await RunProcessAsync(
            "node", [localTsc, "--project", tsconfig], fixture.Directory.FullName);
        Assert.True(tscExit == 0, tscOut + tscErr);
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
        string fileName, string[] args, string? workingDirectory)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }
        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'{fileName}' did not exit within the timeout.");
        }
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    [Fact]
    public async Task Csharp_client_skips_WasiResponse_returning_routes_and_emits_typed_routes()
    {
        // gap (a): routes returning WasiResponse must be excluded from generated clients;
        // routes returning a POCO must still be included.
        using var fixture = CliProjectFixture.Create(
            "transport-routes-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>TransportRoutesApp</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Wasi", "SliceFx.Wasi.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        // Feature 1: returns WasiResponse (server-side transport) — must be skipped by client generation
        fixture.WriteFeature(
            "Features/Items/DeleteItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace TransportRoutesApp.Features.Items;

            [Feature("DELETE /items/{id}")]
            public static class DeleteItem
            {
                public static Task<WasiResponse> Handle(string id, CancellationToken ct)
                    => Task.FromResult(global::SliceFx.Wasi.WasiResults.NoContent());
            }
            """);

        // Feature 2: returns a POCO — must be included
        fixture.WriteFeature(
            "Features/Items/GetItems.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace TransportRoutesApp.Features.Items;

            [Feature("GET /items")]
            public static class GetItems
            {
                public static Task<Response> Handle(CancellationToken ct)
                    => Task.FromResult(new Response([]));

                public sealed record Response(string[] Items);
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        // WasiResponse (server-side transport) route must be entirely absent from the client
        Assert.DoesNotContain("DeleteItemAsync", client);
        // WasiResponse must not appear as a method return type; the namespace is absent so "WasiResponse"
        // in the output would indicate a leaked type reference.
        Assert.DoesNotContain("Task<WasiResponse>", client);

        // POCO route must be present
        Assert.Contains("GetItemsAsync", client);
        Assert.Contains("GetItems.Response", client);
    }

    [Fact]
    public async Task Csharp_client_generates_typed_method_for_SliceResultOfT_routes_and_void_for_non_generic()
    {
        // SliceResult<T>-returning routes must produce Task<__SliceClientResponse<T>> methods so that
        // the caller can observe StatusCode and Location (e.g. Created 201 Location header).
        // Non-generic SliceResult routes must produce Task<__SliceClientResponse> (status-only, no body).
        // Also verifies that namespace-qualified "SliceFx.SliceResult<T>" unwraps correctly (#2/#11).
        using var fixture = CliProjectFixture.Create(
            "slice-result-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>SliceResultClientApp</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Wasi", "SliceFx.Wasi.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        // Feature 1: returns SliceResult<T> with namespace-qualified form — must produce Task<GetItemResponse>
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using SliceFx.Wasi;

            namespace SliceResultClientApp.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public sealed record GetItemResponse(string Id, string Name);

                public static Task<SliceFx.SliceResult<GetItemResponse>> Handle(string id, CancellationToken ct)
                    => Task.FromResult(SliceFx.SliceResult<GetItemResponse>.Ok(new GetItemResponse(id, "Test")));
            }
            """);

        // Feature 2: returns non-generic SliceResult — must produce Task (void), not Task<WasiResponse>
        fixture.WriteFeature(
            "Features/Items/DeleteItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace SliceResultClientApp.Features.Items;

            [Feature("DELETE /items/{id}")]
            public static class DeleteItem
            {
                public static Task<SliceFx.SliceResult> Handle(string id, CancellationToken ct)
                    => Task.FromResult(SliceFx.SliceResult.NoContent());
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        // SliceResult<T> route: Task<__SliceClientResponse<GetItemResponse>> must appear.
        Assert.Contains("GetItemAsync", client);
        Assert.Contains("__SliceClientResponse<", client);
        Assert.Contains("GetItem.GetItemResponse", client);

        // Non-generic SliceResult route: Task<__SliceClientResponse> must appear (no body, status + location).
        Assert.Contains("DeleteItemAsync", client);
        Assert.Contains("async Task<SliceApiClient.__SliceClientResponse> DeleteItemAsync", client);

        // The raw SliceResult<T> / SliceResult wrapper must NOT appear as a return type.
        Assert.DoesNotContain("Task<SliceResult<", client, StringComparison.Ordinal);
        Assert.DoesNotContain("Task<SliceFx.SliceResult", client, StringComparison.Ordinal);
        Assert.DoesNotContain("Task<WasiResponse>", client, StringComparison.Ordinal);

        // The struct types must be emitted in the client file.
        Assert.Contains("readonly struct __SliceClientResponse<T>", client, StringComparison.Ordinal);
        Assert.Contains("readonly struct __SliceClientResponse", client, StringComparison.Ordinal);

        // Created (201) Location must be observable: .Location and .StatusCode on the wrapper.
        Assert.Contains(".StatusCode", client, StringComparison.Ordinal);
        Assert.Contains(".Location", client, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csharp_client_SliceResult_wrapper_surfaces_created_location()
    {
        // Regression: SliceResult<T>.Created(value, location) previously dropped the Location header.
        // After the fix, the generated client returns __SliceClientResponse<T> with a Location property.
        using var fixture = CliProjectFixture.Create(
            "created-location-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>CreatedLocationApp</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Links/CreateLink.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;

            namespace CreatedLocationApp.Features.Links;

            [Feature("POST /links")]
            public static class CreateLink
            {
                public sealed record Request(string TargetUrl);
                public sealed record Response(int Id, string Code);

                public static Task<SliceResult<Response>> Handle(Request req, CancellationToken ct)
                {
                    var resp = new Response(1, "abc1234");
                    return Task.FromResult(SliceResult<Response>.Created(resp, "/r/abc1234"));
                }
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        // CreateLink returns SliceResult<Response> → must produce Task<__SliceClientResponse<Response>>.
        Assert.Contains("async Task<SliceApiClient.__SliceClientResponse<CreatedLocationApp.Features.Links.CreateLink.Response>>", client, StringComparison.Ordinal);
        // The method must capture statusCode and location before disposing the response.
        Assert.Contains("var __statusCode = (int)__response.StatusCode;", client, StringComparison.Ordinal);
        Assert.Contains("var __location = __response.Headers.Location;", client, StringComparison.Ordinal);
        // The result must be returned as the wrapper struct.
        Assert.Contains("return new SliceApiClient.__SliceClientResponse<", client, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csharp_client_omits_null_nullable_scalar_query_param_from_query_string()
    {
        // gap (b): when a nullable scalar query param is null, the client must NOT emit "name=" (empty).
        using var fixture = CliProjectFixture.Create(
            "nullable-query-client-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>NullableQueryApp</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        fixture.WriteFeature(
            "Features/Items/GetItems.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            using Microsoft.AspNetCore.Mvc;

            namespace NullableQueryApp.Features.Items;

            [Feature("GET /items")]
            public static class GetItems
            {
                public static Task<Response> Handle(
                    [FromQuery] int? page,
                    [FromQuery] string? q,
                    CancellationToken ct)
                    => Task.FromResult(new Response([]));

                public sealed record Response(string[] Items);
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "SliceApiClient.g.cs");
        var exitCode = await GenerateCSharpClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);

        // Nullable value-type (int?) must emit a null guard before the query add.
        Assert.Contains("if (page is not null)", client);
        // Nullable reference (string?) must also emit a null guard.
        Assert.Contains("if (q is not null)", client);
        // The params should appear inside the null-guard block (indented), not as bare top-level adds.
        // Verify the guard + add pattern is present (the add is inside the if):
        Assert.Contains("if (page is not null)\n                __query.Add(\"page=\" + ", client);
        Assert.Contains("if (q is not null)\n                __query.Add(\"q=\" + ", client);
    }

    [Fact]
    public async Task Json_context_check_reports_missing_types_and_exits_nonzero()
    {
        using var fixture = CliProjectFixture.Create("json-ctx-check-app");
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using SliceFx;
            namespace Json.Ctx.Check.App.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public static Response Handle(int id) => new(id, "Test");
                public sealed record Response(int Id, string Name);
            }
            """);
        // Context has an unrelated registration but is missing GetItem.Response
        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Check.App.Features.Items.OtherType))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Json_context_check_exits_zero_when_all_types_are_registered()
    {
        using var fixture = CliProjectFixture.Create("json-ctx-ok-app");
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using SliceFx;
            namespace Json.Ctx.Ok.App.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public static Response Handle(int id) => new(id, "Test");
                public sealed record Response(int Id, string Name);
            }
            """);
        // Fully qualified registration — suffix match covers the short return type from source scan
        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Ok.App.Features.Items.GetItem.Response))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Json_context_fix_refuses_in_source_scan_mode_and_does_not_modify_file()
    {
        // --fix requires a compiled manifest so it can resolve fully-qualified type names.
        // In source-scan mode (no bin/ DLL) it must refuse cleanly rather than writing
        // unresolvable [JsonSerializable(typeof(global::Request))] entries into the user's file.
        using var fixture = CliProjectFixture.Create("json-ctx-fix-scan-app");
        fixture.WriteFeature(
            "Features/Orders/CreateOrder.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            namespace Json.Ctx.Fix.Scan.App.Features.Orders;

            [Feature("POST /orders")]
            public static class CreateOrder
            {
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1));

                public sealed record Request(string ProductId);
                public sealed record Response(int OrderId);
            }
            """);
        var originalContext =
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Fix.Scan.App.Features.Orders.OtherType))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """;
        var contextPath = Path.Combine(fixture.Directory.FullName, "AotJsonContext.cs");
        File.WriteAllText(contextPath, originalContext);

        // --fix in source-scan mode must exit non-zero and not touch the context file.
        var fixExitCode = await JsonContextCommand.Build()
            .Parse(["--fix", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, fixExitCode);

        // The context file must be untouched — no broken global:: entries inserted.
        var contextSource = await File.ReadAllTextAsync(contextPath, TestContext.Current.CancellationToken);
        Assert.Equal(originalContext, contextSource);
    }

    [Fact]
    public async Task Json_context_check_unwraps_task_return_type_in_source_scan_mode()
    {
        // Source-scan emits "Task<Response>" as the raw return-type string. StripTaskWrapper must
        // unwrap the unqualified Task<> wrapper so --check can suffix-match the inner type against
        // the registered FQN, whether the inner type is a nested record or a shared-contracts type.
        using var fixture = CliProjectFixture.Create("json-ctx-task-scan-app");
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading.Tasks;
            using SliceFx;
            namespace Json.Ctx.Task.Scan.App.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                // Unqualified Task<Response> — this is the source-scan entry point for the bug.
                public static Task<Response> Handle(int id) => Task.FromResult(new Response(id, "Test"));
                public sealed record Response(int Id, string Name);
            }
            """);

        // Register the inner type with its FQN — suffix match must hit this even though the
        // source-scan root starts as "Task<Response>" before unwrapping.
        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Task.Scan.App.Features.Items.GetItem.Response))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        // --check must exit 0: Task<Response> unwraps to Response, suffix match finds the FQN entry.
        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Json_context_check_reports_missing_when_task_return_type_not_registered()
    {
        // Mirror of the above: when the inner type is NOT registered --check must exit 1.
        using var fixture = CliProjectFixture.Create("json-ctx-task-missing-app");
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading.Tasks;
            using SliceFx;
            namespace Json.Ctx.Task.Missing.App.Features.Items;

            [Feature("GET /items/{id}")]
            public static class GetItem
            {
                public static Task<Response> Handle(int id) => Task.FromResult(new Response(id, "Test"));
                public sealed record Response(int Id, string Name);
            }
            """);

        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Task.Missing.App.Features.Items.GetItem.SomeOtherType))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Json_context_check_unwraps_valuetask_return_type_in_source_scan_mode()
    {
        // ValueTask<T> variant of the Task<T> unwrap test.
        using var fixture = CliProjectFixture.Create("json-ctx-valuetask-scan-app");
        fixture.WriteFeature(
            "Features/Ping/Pong.cs",
            """
            using System.Threading.Tasks;
            using SliceFx;
            namespace Json.Ctx.ValueTask.Scan.App.Features.Ping;

            [Feature("GET /ping")]
            public static class Pong
            {
                public static ValueTask<Response> Handle() => ValueTask.FromResult(new Response("pong"));
                public sealed record Response(string Message);
            }
            """);
        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.ValueTask.Scan.App.Features.Ping.Pong.Response))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Json_context_check_reports_missing_generic_collection_container()
    {
        // A feature returning a generic collection of a user type needs the CONTAINER registered.
        // Registering only the element (Todo) is insufficient — List<Todo> itself is the root the
        // AOT/WASI emitter asks for. The return type is written fully-qualified and synchronous so
        // the source-scan root string is "System.Collections.Generic.List<Todo>": the unfixed CLI
        // excluded it as a System.* framework type (exit 0); the fix flags it (exit 1).
        using var fixture = CliProjectFixture.Create("json-ctx-collection-app");
        fixture.WriteFeature(
            "Features/Todos/ListTodos.cs",
            """
            using SliceFx;
            namespace Json.Ctx.Collection.App.Features.Todos;

            [Feature("GET /todos")]
            public static class ListTodos
            {
                public static System.Collections.Generic.List<Todo> Handle() => new();
            }

            public sealed record Todo(int Id, string Title);
            """);
        // Context registers only the element type Todo, NOT the List<Todo> container.
        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Collection.App.Features.Todos.Todo))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Json_context_check_passes_when_generic_collection_container_is_registered()
    {
        // Registering the List<Todo> container itself (identical text to the scanned root) clears
        // the missing-root — guards that the fix is satisfiable, not just noisier.
        using var fixture = CliProjectFixture.Create("json-ctx-collection-ok-app");
        fixture.WriteFeature(
            "Features/Todos/ListTodos.cs",
            """
            using SliceFx;
            namespace Json.Ctx.Collection.Ok.App.Features.Todos;

            [Feature("GET /todos")]
            public static class ListTodos
            {
                public static System.Collections.Generic.List<Todo> Handle() => new();
            }

            public sealed record Todo(int Id, string Title);
            """);
        // Register the container with text identical to the scanned root
        // "System.Collections.Generic.List<Todo>" (short Todo resolved via the using), so the
        // CLI's exact-match registration check is satisfied.
        fixture.WriteFeature(
            "AotJsonContext.cs",
            """
            using System.Text.Json.Serialization;
            using SliceFx;
            using Json.Ctx.Collection.Ok.App.Features.Todos;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(System.Collections.Generic.List<Todo>))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        var exitCode = await JsonContextCommand.Build()
            .Parse(["--check", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Json_context_fix_inserts_correct_fqn_entries_in_manifest_mode()
    {
        // Verifies the --fix happy path: with a built project (manifest mode) the command
        // inserts fully-qualified [JsonSerializable] entries that actually compile.
        using var fixture = CliProjectFixture.Create(
            "json-ctx-fix-manifest-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Json.Ctx.Fix.Manifest.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.Core", "SliceFx.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "SliceFx.SourceGenerator", "SliceFx.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Orders/CreateOrder.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using SliceFx;
            namespace Json.Ctx.Fix.Manifest.App.Features.Orders;

            [Feature("POST /orders")]
            public static class CreateOrder
            {
                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1));

                public sealed record Request(string ProductId);
                public sealed record Response(int OrderId);
            }
            """);
        var contextPath = Path.Combine(fixture.Directory.FullName, "AotJsonContext.cs");
        File.WriteAllText(contextPath,
            """
            using System.Text.Json.Serialization;
            using SliceFx;

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(Json.Ctx.Fix.Manifest.App.Features.Orders.CreateOrder.Request))]
            internal partial class AotJsonContext : JsonSerializerContext { }
            """);

        // Build first so the source generator emits the manifest (manifest mode).
        await fixture.BuildAsync();

        // --fix must succeed and insert the missing Response entry.
        var fixExitCode = await JsonContextCommand.Build()
            .Parse(["--fix", "--target", "aspnet", "--project", fixture.ProjectFile.FullName])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, fixExitCode);

        // The inserted entry must be the correct FQN, not a bare global:: short name.
        var contextSource = await File.ReadAllTextAsync(contextPath, TestContext.Current.CancellationToken);
        Assert.Contains("global::Json.Ctx.Fix.Manifest.App.Features.Orders.CreateOrder.Response", contextSource);
        Assert.DoesNotContain("global::Response", contextSource);

        // The updated context file must compile successfully.
        await fixture.BuildAsync();
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        internal StubHttpHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_response);
    }

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly Action<HttpRequestMessage> _capture;
        internal CapturingHttpHandler(HttpResponseMessage response, Action<HttpRequestMessage> capture)
        {
            _response = response;
            _capture = capture;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _capture(request);
            return Task.FromResult(_response);
        }
    }

    private sealed class CliProjectFixture : IDisposable
    {
        private CliProjectFixture(DirectoryInfo directory, FileInfo projectFile)
        {
            Directory = directory;
            ProjectFile = projectFile;
        }

        internal DirectoryInfo Directory { get; }

        internal FileInfo ProjectFile { get; }

        internal static CliProjectFixture Create(string projectName, string? projectSource = null)
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "slice-cli-tests", Guid.NewGuid().ToString("N")));
            root.Create();
            var projectFile = new FileInfo(Path.Combine(root.FullName, projectName + ".csproj"));
            File.WriteAllText(
                projectFile.FullName,
                projectSource ?? """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
            return new CliProjectFixture(root, projectFile);
        }

        internal void WriteFeature(string relativePath, string source)
        {
            var path = Path.Combine(Directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                System.IO.Directory.CreateDirectory(parent);
            }

            File.WriteAllText(path, source);
        }

        internal async Task BuildAsync()
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{ProjectFile.FullName}\" --configuration Debug --nologo --verbosity quiet",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            }) ?? throw new InvalidOperationException("Could not start dotnet build.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await stdoutTask + await stderrTask);
        }

        public void Dispose() => Directory.Delete(recursive: true);
    }
}

// Serializes any publish-heavy facts so they don't run concurrently.
[CollectionDefinition("DotnetPublish", DisableParallelization = true)]
public sealed class DotnetPublishSuite { }
