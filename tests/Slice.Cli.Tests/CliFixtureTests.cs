using Slice.Cli.Commands;
using Slice.Cli.Internal;

namespace Slice.Cli.Tests;

public class CliFixtureTests
{
    [Fact]
    public void Project_discovery_sanitizes_project_file_name_when_root_namespace_is_missing()
    {
        using var fixture = CliProjectFixture.Create("my-app");

        var context = ProjectContextDiscovery.Discover(fixture.ProjectFile);

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

        var routes = RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile));

        var route = Assert.Single(routes);
        Assert.Equal("Things.GetThing", route.EndpointName);
        Assert.Equal("Get things", route.Summary);
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "Dictionary<string, int>", Name: "counts" });
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "int[]", Name: "ids" });
    }

    [Fact]
    public void Route_catalog_discovers_multiple_features_from_one_source_file_in_fallback_mode()
    {
        using var fixture = CliProjectFixture.Create("my-app");
        fixture.WriteFeature(
            "Features/Things/ThingFeatures.cs",
            """
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
            """);

        var routes = RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile));

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

        var route = Assert.Single(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile)));

        Assert.Equal(RouteCatalog.PortabilityPartial, route.Portability);
        Assert.Contains("RequestLoggingFilter", route.Filters);
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
            PortabilityReason = "non-validator endpoint filters do not run in the WASI path",
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
        Assert.Equal(RouteTargetCapabilities.Ineligible, RouteTargetCapabilities.Classify(aspNetOnlyRoute).LambdaPerFeature.Status);
        Assert.Equal(RouteTargetCapabilities.Ineligible, RouteTargetCapabilities.Classify(typedAspNetRoute).LambdaPerFeature.Status);
        Assert.Equal(RouteTargetCapabilities.Ineligible, RouteTargetCapabilities.Classify(filteredRoute).LambdaPerFeature.Status);
        Assert.Equal(RouteTargetCapabilities.Eligible, RouteTargetCapabilities.Classify(portableRoute).LambdaPerFeature.Status);

        var generatedRoute = portableRoute with
        {
            ManifestSchemaVersion = "2",
            WasiDispatchStatus = RouteTargetCapabilities.Eligible,
            WasiDispatchReason = null,
            Portability = RouteCatalog.PortabilityPartial,
            PortabilityReason = "ignored",
        };
        Assert.Equal(RouteTargetCapabilities.Eligible, RouteTargetCapabilities.Classify(generatedRoute).WasiDispatch.Status);

        var generatedRouteWithoutWasiMetadata = portableRoute with
        {
            ManifestSchemaVersion = "2",
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Things/GetThing.cs",
            """
            using Slice;

            namespace Generated.App.Features.Things;

            [Feature("GET /things/{id:int}", Summary = "Get a generated thing")]
            public static class GetThing
            {
                public static Response Handle(int id) => new(id);

                public sealed record Response(int Id);
            }
            """);

        await fixture.BuildAsync();

        var route = Assert.Single(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile)));
        Assert.Equal("Things.GetThing", route.EndpointName);
        Assert.Equal("Get a generated thing", route.Summary);
        Assert.Equal("Generated.App.Features.Things.GetThing.Response", route.ReturnType);
        Assert.Equal(RouteCatalog.PortabilityPortable, route.Portability);
        Assert.Contains(route.Parameters, static parameter => parameter is { Type: "int", Name: "id" });
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "RouteMetadata.cs",
            """
            using Slice;

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
                null)]

            namespace Unsupported.Schema.App.Features.Health;

            public static class GetHealth
            {
                public sealed record Response(string Status);
            }
            """);

        await fixture.BuildAsync();

        var exception = Assert.Throws<CliException>(() => RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile)));
        Assert.Contains("Unsupported Slice route manifest schema '999'", exception.Message, StringComparison.Ordinal);
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
                <ProjectReference Include="{{Path.Combine("OldSliceCore", "Slice.Core.csproj")}}" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "OldSliceCore/Slice.Core.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "OldSliceCore/SliceFeatureRouteAttribute.cs",
            """
            namespace Slice;

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
                    string? lambdaPerFeatureStatus,
                    string? lambdaPerFeatureReason,
                    string? lambdaPerFeatureHandlerAssembly,
                    string? lambdaPerFeatureHandlerType,
                    string? lambdaPerFeatureHandlerMethod)
                {
                }
            }
            """);
        fixture.WriteFeature(
            "RouteMetadata.cs",
            """
            using Slice;

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

        var exception = Assert.Throws<CliException>(() => RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile)));
        Assert.Contains("Invalid Slice route manifest", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 20 constructor arguments but found 17", exception.Message, StringComparison.Ordinal);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        await fixture.BuildAsync();

        Assert.Empty(RouteCatalog.Discover(ProjectContextDiscovery.Discover(fixture.ProjectFile)));
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Things/GetThing.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("public async Task<Generated.Client.App.Features.Things.GetThing.Response> GetThingAsync(int id, CancellationToken cancellationToken = default)", client);
        Assert.DoesNotContain("Task<System.Threading.Tasks.Task", client);
        Assert.DoesNotContain("GetFromJsonAsync<System.Threading.Tasks.Task", client);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("public partial class SliceApiClient", client);
        Assert.Contains("public SliceApiClient(HttpMessageHandler handler)", client);
        Assert.Contains("public static SliceApiClient Create(IHttpClientFactory factory", client);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

            namespace TypeScript.Client.App.Features.Items;

            [Feature("POST /items")]
            public static class CreateItem
            {
                public record Request(string Name);
                public record Response(int Id, string Name);

                public static Task<Response> Handle(Request req, CancellationToken ct)
                    => Task.FromResult(new Response(1, req.Name));
            }
            """);

        await fixture.BuildAsync();

        var outputFile = Path.Combine(fixture.Directory.FullName, "slice-api-client.ts");
        var exitCode = await GenerateTypeScriptClientCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("export class SliceApiClient", client);
        Assert.Contains("export class ItemsClient", client);
        Assert.Contains("async getItemAsync(", client);
        Assert.Contains("async createItemAsync(", client);
        Assert.Contains("encodeURIComponent(String(id))", client);
        Assert.Contains("JSON.stringify(body)", client);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile);

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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Users/Get.cs",
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("export interface GetResponse", client);
        Assert.Contains("export interface UsersGetResponse", client);
        Assert.Contains("readonly items: UsersGetUser[];", client);
        Assert.Contains("readonly recent: UsersGetUser[];", client);
        Assert.Contains("readonly byId: Record<string, UsersGetUser>;", client);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var client = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("getItemAsync", client);
        Assert.DoesNotContain("deleteItemAsync", client);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Users/CreateUser.cs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Slice;

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
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("AWS::Serverless-2016-10-31", yaml);
        Assert.Contains("SliceApi:", yaml);
        Assert.Contains("Hosted mode: one Lambda function hosts the ASP.NET Core app generated by Slice.", yaml);
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Items/GetItem.cs",
            """
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile);

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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using Slice;

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
            .InvokeAsync();
        await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", managedOutput, "--runtime", "dotnet8"])
            .InvokeAsync();

        var nativeYaml = await File.ReadAllTextAsync(nativeAotOutput);
        var managedYaml = await File.ReadAllTextAsync(managedOutput);

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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Files/GetFile.cs",
            """
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile);

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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using Slice;

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
            .InvokeAsync();
        Assert.Equal(0, firstExit);

        // Second run without --force fails.
        var noForceExit = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile])
            .InvokeAsync();
        Assert.Equal(1, noForceExit);

        // Third run with --force succeeds.
        var forceExit = await ManifestAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--force"])
            .InvokeAsync();
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteFeature(
            "Features/Health/GetHealth.cs",
            """
            using Slice;

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputFile));
    }

    [Fact]
    public async Task Manifest_aws_lambda_per_feature_mode_emits_eligible_functions_and_exclusions()
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Lambda.PerFunction", "Slice.Lambda.PerFunction.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
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
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction]

            namespace Lambda.PerFeature.App;

            [Slice.SliceJsonContext(Slice.SliceJsonTarget.LambdaPerFeature)]

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
            using Slice;

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
            using Slice;

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
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--mode", "per-feature"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("Per-feature mode: each eligible [Feature] becomes an independent Lambda function.", yaml);
        Assert.Contains("HealthGetHealthFunction:", yaml);
        Assert.Contains("HealthGetHealthEvent:", yaml);
        Assert.Contains("Handler: 'lambda-per-feature-app::Slice.lambda_per_feature_app_SliceLambdaPerFunctionHandlers::Health_GetHealth'", yaml);
        Assert.Contains("Path: '/health'", yaml);
        Assert.DoesNotContain("HealthGetFilteredHealthFunction:", yaml);
        Assert.Contains("# - Health.GetFilteredHealth: non-validator endpoint filters require the ASP.NET endpoint filter pipeline", yaml);
    }

    [Fact]
    public async Task Manifest_aws_lambda_per_feature_uses_referenced_feature_assembly_handlers()
    {
        using var fixture = CliProjectFixture.Create(
            "host-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Host.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="FeatureLib/feature-lib.csproj" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
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
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Lambda.PerFunction", "Slice.Lambda.PerFunction.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
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
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction]

            namespace Feature.Lib;

            public sealed class Marker;

            [Slice.SliceJsonContext(Slice.SliceJsonTarget.LambdaPerFeature)]

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
            using Slice;

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
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputFile, "--mode", "per-feature"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var yaml = await File.ReadAllTextAsync(outputFile);

        Assert.Contains("Handler: 'feature-lib::Slice.feature_lib_SliceLambdaPerFunctionHandlers::Health_GetHealth'", yaml);
        Assert.DoesNotContain("Handler: 'host-app::Slice.host_app_SliceLambdaPerFunctionHandlers::Health_GetHealth'", yaml);
    }

    [Fact]
    public async Task Package_aws_lambda_per_feature_writes_artifact_manifest()
    {
        using var fixture = CliProjectFixture.Create(
            "lambda-package-app",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Lambda.Package.App</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Core", "Slice.Core.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.Lambda.PerFunction", "Slice.Lambda.PerFunction.csproj")}}" />
                <ProjectReference Include="{{Path.Combine(FindRepoRoot(), "src", "Slice.SourceGenerator", "Slice.SourceGenerator.csproj")}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
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
            using Slice.Lambda.PerFunction;

            [assembly: LambdaPerFunction]

            namespace Lambda.Package.App;

            [Slice.SliceJsonContext(Slice.SliceJsonTarget.LambdaPerFeature)]

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
            using Slice;

            namespace Lambda.Package.App.Features.Health;

            [Feature("GET /health")]
            public static class GetHealth
            {
                public static string Handle() => "ok";
            }
            """);

        await fixture.BuildAsync();

        var outputDir = Path.Combine(fixture.Directory.FullName, "artifacts", "lambda");
        var exitCode = await PackageAwsLambdaCommand.Build()
            .Parse(["--project", fixture.ProjectFile.FullName, "--output", outputDir, "--mode", "per-feature", "--skip-publish"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        var manifestPath = Path.Combine(outputDir, "slice-lambda-package.json");
        Assert.True(File.Exists(manifestPath));

        var json = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains("\"mode\": \"per-feature\"", json);
        Assert.Contains("\"publishDirectory\": \"publish\"", json);
        Assert.Contains("\"endpointName\": \"Health.GetHealth\"", json);
        Assert.Contains("lambda-package-app::Slice.lambda_package_app_SliceLambdaPerFunctionHandlers::Health_GetHealth", json);
        Assert.DoesNotContain(outputDir, json, StringComparison.Ordinal);
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
            .InvokeAsync();

        Assert.Equal(0, exitCode);

        var dist = Path.Combine(fixture.Directory.FullName, "dist");
        Assert.True(File.Exists(Path.Combine(dist, "package.json")));
        Assert.True(File.Exists(Path.Combine(dist, "shim.mjs")));
        Assert.True(File.Exists(Path.Combine(dist, "generate-module-map.mjs")));
        Assert.True(File.Exists(Path.Combine(dist, "wrangler.toml")));
        Assert.True(File.Exists(Path.Combine(dist, "wrangler.deploy.toml")));
        Assert.True(File.Exists(Path.Combine(dist, "stubs", "tcp.js")));
        Assert.True(File.Exists(Path.Combine(dist, "stubs", "udp.js")));

        var packageJson = await File.ReadAllTextAsync(Path.Combine(dist, "package.json"));
        var shim = await File.ReadAllTextAsync(Path.Combine(dist, "shim.mjs"));
        var moduleMap = await File.ReadAllTextAsync(Path.Combine(dist, "generate-module-map.mjs"));

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);

        var packageJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "package.json"));

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
            .InvokeAsync();

        Assert.Equal(0, exitCode);

        var shim = await File.ReadAllTextAsync(Path.Combine(fixture.Directory.FullName, "dist", "shim.mjs"));

        Assert.Contains("""_setArgs(["My\"\\Wasi"]);""", shim);
        Assert.DoesNotContain("_setArgs(['", shim);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Slice.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
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

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, output + error);
        }

        public void Dispose() => Directory.Delete(recursive: true);
    }
}
