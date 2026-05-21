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
    public async Task Manifest_aws_lambda_generates_sam_template_with_one_function_per_feature()
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
        Assert.Contains("UsersCreateUserFunction:", yaml);
        Assert.Contains("UsersGetUserFunction:", yaml);
        Assert.Contains("Method: 'POST'", yaml);
        Assert.Contains("Method: 'GET'", yaml);
        Assert.Contains("Path: '/users'", yaml);
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
