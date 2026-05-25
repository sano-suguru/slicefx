using System.CommandLine;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using SliceFx.Cli.Internal;

namespace SliceFx.Cli.Commands;

internal static class PackageAwsLambdaCommand
{
    private const string FunctionPerFeatureMode = "function-per-feature";
    private const string SharedArtifactLayout = "shared";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static Command Build()
    {
        var projectOpt = SharedOptions.CreateProject();
        var outputOpt = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to artifacts/aws-lambda in the project directory.",
        };
        var modeOpt = new Option<string>("--mode")
        {
            Description = "Package mode. Only 'function-per-feature' is supported.",
            DefaultValueFactory = _ => FunctionPerFeatureMode,
        };
        var artifactLayoutOpt = new Option<string>("--artifact-layout")
        {
            Description = "Function-per-feature artifact layout. Only 'shared' is supported today; future per-feature artifacts are reserved.",
            DefaultValueFactory = _ => SharedArtifactLayout,
        };
        var configurationOpt = new Option<string>("--configuration")
        {
            Description = "dotnet publish configuration.",
            DefaultValueFactory = _ => "Release",
        };
        var lambdaRuntimeOpt = new Option<string>("--runtime")
        {
            Description = "Lambda runtime identifier recorded in the artifact manifest.",
            DefaultValueFactory = _ => "provided.al2023",
        };
        var ridOpt = new Option<string?>("--rid")
        {
            Description = "Optional .NET runtime identifier passed to dotnet publish.",
        };
        var selfContainedOpt = new Option<bool>("--self-contained")
        {
            Description = "Pass --self-contained true to dotnet publish.",
            DefaultValueFactory = _ => false,
        };
        var skipPublishOpt = new Option<bool>("--skip-publish")
        {
            Description = "Write packaging metadata without running dotnet publish.",
            DefaultValueFactory = _ => false,
        };
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("aws-lambda", "Publish/package Slice routes for AWS Lambda.")
        {
            projectOpt,
            outputOpt,
            modeOpt,
            artifactLayoutOpt,
            configurationOpt,
            lambdaRuntimeOpt,
            ridOpt,
            selfContainedOpt,
            skipPublishOpt,
            forceOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);
            var mode = parseResult.GetValue(modeOpt) ?? FunctionPerFeatureMode;
            var artifactLayout = parseResult.GetValue(artifactLayoutOpt) ?? SharedArtifactLayout;
            var configuration = parseResult.GetValue(configurationOpt) ?? "Release";
            var lambdaRuntime = parseResult.GetValue(lambdaRuntimeOpt) ?? "provided.al2023";
            var rid = parseResult.GetValue(ridOpt);
            var selfContained = parseResult.GetValue(selfContainedOpt);
            var skipPublish = parseResult.GetValue(skipPublishOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, mode, artifactLayout, configuration, lambdaRuntime, rid, selfContained, skipPublish, force, ct).ConfigureAwait(false);
                return 0;
            }
            catch (CliException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        return cmd;
    }

    private static async Task RunAsync(
        FileInfo? project,
        DirectoryInfo? output,
        string mode,
        string artifactLayout,
        string configuration,
        string lambdaRuntime,
        string? rid,
        bool selfContained,
        bool skipPublish,
        bool force,
        CancellationToken ct)
    {
        if (!string.Equals(mode, FunctionPerFeatureMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Only `slicefx package aws-lambda --mode function-per-feature --artifact-layout shared` is supported. Use `dotnet publish` for hosted Lambda packages.");
        }

        if (!string.Equals(artifactLayout, SharedArtifactLayout, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(artifactLayout, "per-feature", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException("Lambda artifact layout 'per-feature' is not implemented yet. Use `--artifact-layout shared`.");
            }

            throw new CliException("Only `--artifact-layout shared` is supported.");
        }

        var ctx = ProjectContextDiscovery.Discover(project);
        var discovery = RouteCatalog.DiscoverDetailed(ctx);
        RouteCatalog.WriteAggregatedRouteNotice(discovery);
        if (!discovery.HasGeneratedMetadata)
        {
            throw new CliException("Lambda function-per-feature packaging requires generated route metadata. Build the project before running this command.");
        }

        if (!discovery.HasLambdaFunctionPerFeatureHandlers)
        {
            throw new CliException(
                "Lambda function-per-feature handlers were not found in the built project. " +
                "Reference SliceFx.Lambda.FunctionPerFeature and add [assembly: LambdaFunctionPerFeature(...)] to opt in.");
        }

        var eligible = discovery.Routes
            .Where(IsEmittedLambdaFunctionPerFeatureRoute)
            .ToArray();
        if (eligible.Length == 0)
        {
            throw new CliException("No routes are eligible for Lambda function-per-feature packaging.");
        }

        var outputDir = output ?? new DirectoryInfo(Path.Combine(ctx.ProjectDirectory.FullName, "artifacts", "aws-lambda"));
        if (outputDir.Exists && !force)
        {
            throw new CliException($"Output directory already exists: {outputDir.FullName}\nUse --force to overwrite.");
        }

        Directory.CreateDirectory(outputDir.FullName);
        var publishDir = Path.Combine(outputDir.FullName, "publish");
        Directory.CreateDirectory(publishDir);

        if (!skipPublish)
        {
            await RunDotnetPublishAsync(ctx.ProjectFile.FullName, publishDir, configuration, rid, selfContained, ct).ConfigureAwait(false);
        }

        var manifest = CreateManifest(ctx, eligible, discovery.Routes.Length - eligible.Length, lambdaRuntime, configuration, rid, selfContained);
        var manifestPath = Path.Combine(outputDir.FullName, "slicefx-lambda-package.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct).ConfigureAwait(false);

        Console.WriteLine($"Generated {manifestPath}");
        Console.WriteLine($"  function-per-feature package — {eligible.Length} handler(s), artifact layout: {SharedArtifactLayout}, publish: {publishDir}");
    }

    private static async Task RunDotnetPublishAsync(
        string projectFile,
        string publishDir,
        string configuration,
        string? rid,
        bool selfContained,
        CancellationToken ct)
    {
        var args = new List<string>
        {
            "publish",
            projectFile,
            "--configuration",
            configuration,
            "--output",
            publishDir,
            "--self-contained",
            selfContained ? "true" : "false",
        };
        if (!string.IsNullOrWhiteSpace(rid))
        {
            args.Add("--runtime");
            args.Add(rid);
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new CliException($"dotnet publish failed with exit code {process.ExitCode}.\n{stdout}\n{stderr}");
        }
    }

    private static LambdaPackageManifest CreateManifest(
        ProjectContext ctx,
        SliceRouteInfo[] eligible,
        int excludedCount,
        string lambdaRuntime,
        string configuration,
        string? rid,
        bool selfContained)
    {
        var functions = eligible.Select(route => new LambdaPackageFunction(
            route.EndpointName,
            route.FeatureType,
            route.Method,
            route.Pattern,
            route.LambdaFunctionPerFeatureHandler!,
            "publish")).ToArray();

        return new LambdaPackageManifest(
            FunctionPerFeatureMode,
            SharedArtifactLayout,
            ctx.AssemblyName,
            lambdaRuntime,
            configuration,
            rid,
            selfContained,
            "publish",
            excludedCount,
            functions);
    }

    private static bool IsEmittedLambdaFunctionPerFeatureRoute(SliceRouteInfo route)
        => route.LambdaFunctionPerFeatureHandler is not null
           && string.Equals(
               RouteTargetCapabilities.Classify(route).LambdaFunctionPerFeature.Status,
               RouteTargetCapabilities.Eligible,
               StringComparison.OrdinalIgnoreCase);

    private sealed record LambdaPackageManifest(
        string Mode,
        string ArtifactLayout,
        string AssemblyName,
        string LambdaRuntime,
        string Configuration,
        string? RuntimeIdentifier,
        bool SelfContained,
        string PublishDirectory,
        int ExcludedRouteCount,
        LambdaPackageFunction[] Functions);

    private sealed record LambdaPackageFunction(
        string EndpointName,
        string FeatureType,
        string Method,
        string Pattern,
        string Handler,
        string CodeUri);
}
