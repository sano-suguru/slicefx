using System.CommandLine;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Slice.Cli.Internal;

namespace Slice.Cli.Commands;

internal static class PackageAwsLambdaCommand
{
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
            Description = "Package mode. Only 'per-feature' is supported.",
            DefaultValueFactory = _ => "per-feature",
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
            var mode = parseResult.GetValue(modeOpt) ?? "per-feature";
            var configuration = parseResult.GetValue(configurationOpt) ?? "Release";
            var lambdaRuntime = parseResult.GetValue(lambdaRuntimeOpt) ?? "provided.al2023";
            var rid = parseResult.GetValue(ridOpt);
            var selfContained = parseResult.GetValue(selfContainedOpt);
            var skipPublish = parseResult.GetValue(skipPublishOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, mode, configuration, lambdaRuntime, rid, selfContained, skipPublish, force, ct).ConfigureAwait(false);
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
        string configuration,
        string lambdaRuntime,
        string? rid,
        bool selfContained,
        bool skipPublish,
        bool force,
        CancellationToken ct)
    {
        if (!string.Equals(mode, "per-feature", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Only `slice package aws-lambda --mode per-feature` is supported. Use `dotnet publish` for hosted Lambda packages.");
        }

        var ctx = ProjectContextDiscovery.Discover(project);
        var discovery = RouteCatalog.DiscoverDetailed(ctx);
        if (!discovery.HasGeneratedMetadata)
        {
            throw new CliException("Lambda per-feature packaging requires generated route metadata. Build the project before running this command.");
        }

        if (!discovery.HasLambdaPerFunctionHandlers)
        {
            throw new CliException(
                "Lambda per-feature handlers were not found in the built project. " +
                "Reference Slice.Lambda.PerFunction and add [assembly: LambdaPerFunction(...)] to opt in.");
        }

        var eligible = discovery.Routes
            .Where(IsEmittedLambdaPerFeatureRoute)
            .ToArray();
        if (eligible.Length == 0)
        {
            throw new CliException("No routes are eligible for Lambda per-feature packaging.");
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
        var manifestPath = Path.Combine(outputDir.FullName, "slice-lambda-package.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct).ConfigureAwait(false);

        Console.WriteLine($"Generated {manifestPath}");
        Console.WriteLine($"  per-feature package — {eligible.Length} handler(s), publish: {publishDir}");
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
            route.LambdaPerFeatureHandler!,
            "publish")).ToArray();

        return new LambdaPackageManifest(
            "per-feature",
            ctx.AssemblyName,
            lambdaRuntime,
            configuration,
            rid,
            selfContained,
            "publish",
            excludedCount,
            functions);
    }

    private static bool IsEmittedLambdaPerFeatureRoute(SliceRouteInfo route)
        => route.LambdaPerFeatureHandler is not null
           && string.Equals(
               RouteTargetCapabilities.Classify(route).LambdaPerFeature.Status,
               RouteTargetCapabilities.Eligible,
               StringComparison.OrdinalIgnoreCase);

    private sealed record LambdaPackageManifest(
        string Mode,
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
