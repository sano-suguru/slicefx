extern alias SliceSample;

using System.Reflection;
using System.Text.Json;
using SliceFx.Cli.Commands;
using SliceFx.Cli.Internal;
using SliceFx.Testing;

namespace SliceFx.Cli.Tests;

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
        Assert.Contains("expected 25 constructor arguments but found 17", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("JsonContent.Create(request)", csharpClient);
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
        var request = schemas.GetProperty("Request");
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

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        internal StubHttpHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_response);
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
