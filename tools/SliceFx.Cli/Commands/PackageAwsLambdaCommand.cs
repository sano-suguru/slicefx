using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using SliceFx.Cli.Internal;

namespace SliceFx.Cli.Commands;

internal static class PackageAwsLambdaCommand
{
    private const string FunctionPerFeatureMode = "function-per-feature";
    private const string PerFeatureArtifactLayout = "per-feature";
    private const string PerFeatureBootstrapMode = "native-aot-bootstrap";
    private const string PerFeatureArtifactsDirectory = "artifacts";
    private const string PerFeatureObjDirectory = "obj";
    private const string PackageManifestFileName = "slicefx-lambda-package.json";
    private const string ClosureReportFileName = "slicefx-lambda-package-report.json";
    private const string LambdaApiGatewayEventsVersion = "3.0.0";
    private const string LambdaRuntimeSupportVersion = "1.14.3";
    private const string LambdaSerializationSystemTextJsonVersion = "3.0.0";
    private const string AotWarningsNotAsErrors = "IL2026%3BIL2070%3BIL3050";

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
            Description = "Function-per-feature artifact layout. Only 'per-feature' is supported.",
            DefaultValueFactory = _ => PerFeatureArtifactLayout,
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
        var skipPublishOpt = new Option<bool>("--skip-publish")
        {
            Description = "Write packaging metadata without running dotnet publish.",
            DefaultValueFactory = _ => false,
        };
        var warningBaselineOpt = new Option<FileInfo?>("--warning-baseline")
        {
            Description = "Optional structured warning baseline JSON. Without a baseline, NativeAOT publish must produce zero warnings.",
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
            skipPublishOpt,
            warningBaselineOpt,
            forceOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);
            var mode = parseResult.GetValue(modeOpt) ?? FunctionPerFeatureMode;
            var artifactLayout = parseResult.GetValue(artifactLayoutOpt) ?? PerFeatureArtifactLayout;
            var configuration = parseResult.GetValue(configurationOpt) ?? "Release";
            var lambdaRuntime = parseResult.GetValue(lambdaRuntimeOpt) ?? "provided.al2023";
            var rid = parseResult.GetValue(ridOpt);
            var skipPublish = parseResult.GetValue(skipPublishOpt);
            var warningBaseline = parseResult.GetValue(warningBaselineOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, mode, artifactLayout, configuration, lambdaRuntime, rid, skipPublish, warningBaseline, force, ct).ConfigureAwait(false);
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
        bool skipPublish,
        FileInfo? warningBaseline,
        bool force,
        CancellationToken ct)
    {
        if (!string.Equals(mode, FunctionPerFeatureMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Only `slicefx package aws-lambda --mode function-per-feature` is supported. Use `dotnet publish` for hosted Lambda packages.");
        }

        if (!IsPerFeatureLayout(artifactLayout))
        {
            throw new CliException("Unknown Lambda artifact layout '" + artifactLayout + "'. Expected: per-feature.");
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

        await PackagePerFeatureAsync(ctx, discovery, eligible, outputDir, lambdaRuntime, configuration, rid, skipPublish, warningBaseline, force, ct).ConfigureAwait(false);
    }

    private static async Task PackagePerFeatureAsync(
        ProjectContext ctx,
        RouteCatalogDiscovery discovery,
        SliceRouteInfo[] eligible,
        DirectoryInfo outputDir,
        string lambdaRuntime,
        string configuration,
        string? rid,
        bool skipPublish,
        FileInfo? warningBaseline,
        bool force,
        CancellationToken ct)
    {
        if (!skipPublish && string.IsNullOrWhiteSpace(rid))
        {
            throw new CliException("Lambda binary-per-feature NativeAOT packaging requires an explicit `--rid` such as `linux-x64` or `linux-arm64`. Use `--skip-publish` for a dry-run manifest without a RID.");
        }

        Directory.CreateDirectory(outputDir.FullName);
        var objRoot = Path.Combine(outputDir.FullName, PerFeatureObjDirectory, "aws-lambda", "per-feature");
        var artifactsRoot = Path.Combine(outputDir.FullName, PerFeatureArtifactsDirectory);
        if (force)
        {
            DeleteDirectoryIfExists(objRoot);
            DeleteDirectoryIfExists(artifactsRoot);
        }

        Directory.CreateDirectory(objRoot);
        Directory.CreateDirectory(artifactsRoot);

        var buildOutputAssemblies = BuildOutputAssemblyFinder.FindAssemblyFiles(ctx);
        if (buildOutputAssemblies.Length == 0)
        {
            throw new CliException("Lambda function-per-feature packaging requires built project outputs. Build the project before running this command.");
        }

        var artifacts = CreatePerFeatureArtifacts(eligible);
        var artifactReports = new List<LambdaPackageArtifactReport>(artifacts.Length);
        foreach (var artifact in artifacts)
        {
            var projectDir = Path.Combine(objRoot, artifact.ArtifactId);
            var artifactDir = Path.Combine(outputDir.FullName, artifact.CodeUri);
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(artifactDir);

            await WritePerFeatureProjectAsync(artifact.Route, artifact.ArtifactId, projectDir, buildOutputAssemblies, ct).ConfigureAwait(false);
            DotnetPublishResult? publishResult = null;
            var binlogPath = Path.Combine(projectDir, "publish.binlog");
            if (!skipPublish)
            {
                var projectFile = Path.Combine(projectDir, "bootstrap.csproj");
                try
                {
                    publishResult = await RunDotnetPublishAsync(
                        projectFile,
                        artifactDir,
                        configuration,
                        rid,
                        selfContained: true,
                        publishAot: true,
                        binlogPath,
                        ctx.ProjectDirectory.FullName,
                        outputDir.FullName,
                        ct).ConfigureAwait(false);
                }
                catch (CliException ex)
                {
                    throw new CliException(ex.Message + "\nLambda binary-per-feature NativeAOT packaging requires the app project and its full dependency closure to be NativeAOT-compatible.");
                }
            }

            var wrapperBuildRoot = Path.Combine(projectDir, "build");
            artifactReports.Add(CreateArtifactReport(artifact, discovery.Routes, artifactDir, outputDir.FullName, wrapperBuildRoot, publishResult, skipPublish));
        }

        var baselineReport = ApplyWarningBaseline(artifactReports, warningBaseline);

        var manifest = CreatePerFeatureManifest(ctx, artifacts, discovery.Routes.Length - eligible.Length, lambdaRuntime, configuration, rid);
        var manifestPath = Path.Combine(outputDir.FullName, PackageManifestFileName);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct).ConfigureAwait(false);

        var report = new LambdaPackageClosureReport(
            "1",
            FunctionPerFeatureMode,
            PerFeatureArtifactLayout,
            "native-aot",
            skipPublish,
            baselineReport,
            [.. artifactReports]);
        var reportPath = Path.Combine(outputDir.FullName, ClosureReportFileName);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), ct).ConfigureAwait(false);

        Console.WriteLine($"Generated {manifestPath}");
        Console.WriteLine($"Generated {reportPath}");
        Console.WriteLine($"  function-per-feature package — {eligible.Length} handler(s), artifact layout: {PerFeatureArtifactLayout}, artifacts: {artifactsRoot}");
        WriteArtifactReportSummary(artifactReports);
        var closureFailures = artifactReports
            .Where(static report => !report.ClosureInspection.Passed)
            .ToArray();
        if (!skipPublish && closureFailures.Length > 0)
        {
            throw new CliException($"NativeAOT closure inspection failed. See {reportPath}.");
        }

        if (baselineReport.UnbaselinedWarningCount > 0)
        {
            throw new CliException($"NativeAOT publish produced unbaselined warnings. See {reportPath}.");
        }

        if (baselineReport.StaleBaselineCount > 0)
        {
            throw new CliException($"NativeAOT warning baseline contains stale entries. See {reportPath}.");
        }
    }

    private static async Task<DotnetPublishResult> RunDotnetPublishAsync(
        string projectFile,
        string publishDir,
        string configuration,
        string? rid,
        bool selfContained,
        bool publishAot,
        string binlogPath,
        string projectRoot,
        string packageRoot,
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
            $"-bl:{binlogPath}",
        };
        if (publishAot)
        {
            args.Add("-p:PublishAot=true");
            args.Add($"-p:WarningsNotAsErrors={AotWarningsNotAsErrors}");
        }

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

        var warnings = File.Exists(binlogPath)
            ? LambdaPackageWarningInspector.ReadWarnings(binlogPath, projectRoot, packageRoot)
            : [];
        return new DotnetPublishResult(stdout, stderr, ToPackageRelativePath(packageRoot, binlogPath), warnings);
    }

    private static PerFeatureArtifact[] CreatePerFeatureArtifacts(SliceRouteInfo[] eligible)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var artifacts = new List<PerFeatureArtifact>(eligible.Length);
        foreach (var route in eligible)
        {
            var artifactId = RequireLambdaArtifactId(route);
            var codeUri = RequireLambdaCodeUri(route, artifactId);
            if (seen.TryGetValue(artifactId, out var existingEndpoint))
            {
                throw new CliException(
                    $"Duplicate per-feature Lambda artifact ID '{artifactId}' was generated for '{existingEndpoint}' and '{route.EndpointName}'. Add a unique Tag= to the conflicting features and rebuild.");
            }

            seen.Add(artifactId, route.EndpointName);
            artifacts.Add(new PerFeatureArtifact(route, artifactId, codeUri));
        }

        return [.. artifacts];
    }

    private static LambdaPackageManifest CreatePerFeatureManifest(
        ProjectContext ctx,
        PerFeatureArtifact[] perFeatureArtifacts,
        int excludedCount,
        string lambdaRuntime,
        string configuration,
        string? rid)
    {
        var artifacts = perFeatureArtifacts.Select(artifact => new LambdaPackageArtifact(
            artifact.ArtifactId,
            PerFeatureArtifactLayout,
            artifact.CodeUri,
            PerFeatureBootstrapMode,
            rid)).ToArray();

        var functions = perFeatureArtifacts.Select(artifact => new LambdaPackageFunction(
            artifact.Route.EndpointName,
            artifact.Route.FeatureType,
            artifact.Route.Method,
            artifact.Route.Pattern,
            artifact.Route.LambdaFunctionPerFeatureHandler!,
            artifact.ArtifactId)).ToArray();

        return new LambdaPackageManifest(
            FunctionPerFeatureMode,
            PerFeatureArtifactLayout,
            ctx.AssemblyName,
            lambdaRuntime,
            configuration,
            rid,
            SelfContained: true,
            excludedCount,
            artifacts,
            functions);
    }

    private static async Task WritePerFeatureProjectAsync(
        SliceRouteInfo route,
        string artifactId,
        string projectDir,
        FileInfo[] buildOutputAssemblies,
        CancellationToken ct)
    {
        var projectPath = Path.Combine(projectDir, "bootstrap.csproj");
        var programPath = Path.Combine(projectDir, "Program.slicefx");
        var wrapperBuildRoot = Path.Combine(projectDir, "build");
        var wrapperIntermediateOutputPath = EnsureTrailingDirectorySeparator(Path.Combine(wrapperBuildRoot, "obj"));
        var wrapperBaseOutputPath = EnsureTrailingDirectorySeparator(Path.Combine(wrapperBuildRoot, "bin"));
        var referencesXml = new System.Text.StringBuilder();
        foreach (var assembly in buildOutputAssemblies)
        {
            var referenceName = Path.GetFileNameWithoutExtension(assembly.Name);
            referencesXml.AppendLine(CultureInfo.InvariantCulture, $"""                <Reference Include="{XmlEscape(referenceName)}" HintPath="{XmlEscape(assembly.FullName)}" Private="true" />""");
        }

        var projectXml = $$"""
            <Project>
              <PropertyGroup>
                <BaseIntermediateOutputPath>{{XmlEscape(wrapperIntermediateOutputPath)}}</BaseIntermediateOutputPath>
                <BaseOutputPath>{{XmlEscape(wrapperBaseOutputPath)}}</BaseOutputPath>
              </PropertyGroup>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>bootstrap</AssemblyName>
                <RootNamespace>SliceFx.Lambda.PerFeature.{{ToCSharpIdentifier(artifactId)}}</RootNamespace>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <InvariantGlobalization>true</InvariantGlobalization>
                <PublishAot>true</PublishAot>
                <TrimMode>full</TrimMode>
                <IlcOptimizationPreference>Size</IlcOptimizationPreference>
                <StripSymbols>false</StripSymbols>
                <IlcGenerateMstatFile>true</IlcGenerateMstatFile>
                <IlcGenerateMapFile>true</IlcGenerateMapFile>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.slicefx" />
                {{referencesXml.ToString().TrimEnd()}}
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <PackageReference Include="Amazon.Lambda.APIGatewayEvents" Version="{{LambdaApiGatewayEventsVersion}}" />
                <PackageReference Include="Amazon.Lambda.RuntimeSupport" Version="{{LambdaRuntimeSupportVersion}}" />
                <PackageReference Include="Amazon.Lambda.Serialization.SystemTextJson" Version="{{LambdaSerializationSystemTextJsonVersion}}" />
              </ItemGroup>
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
            </Project>
            """;
        var handlerType = RequireCSharpTypeName(route, route.LambdaFunctionPerFeatureHandlerType, "handler type");
        var handlerMethod = RequireCSharpIdentifier(route, route.LambdaFunctionPerFeatureHandlerMethod, "handler method");
        var jsonSerializableAttributes = CreateLambdaJsonSerializableAttributes(route);
        var programSource = $$"""
            using Amazon.Lambda.APIGatewayEvents;
            using Amazon.Lambda.Core;
            using Amazon.Lambda.RuntimeSupport;
            using Amazon.Lambda.Serialization.SystemTextJson;
            using System.Text.Json.Serialization;

            {{handlerType}}.JsonTypeInfoProvider = static type => LambdaFeatureJsonContext.Default.GetTypeInfo(type);
            var serializer = new SourceGeneratorLambdaJsonSerializer<LambdaFeatureJsonContext>();
            await LambdaBootstrapBuilder
                .Create<APIGatewayHttpApiV2ProxyRequest, APIGatewayHttpApiV2ProxyResponse>(
                    {{handlerType}}.{{handlerMethod}},
                    serializer)
                .Build()
                .RunAsync(CancellationToken.None)
                .ConfigureAwait(false);

            [JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
            [JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
            {{jsonSerializableAttributes}}
            internal sealed partial class LambdaFeatureJsonContext : JsonSerializerContext
            {
            }
            """;

        await File.WriteAllTextAsync(projectPath, projectXml, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(programPath, programSource, ct).ConfigureAwait(false);
    }

    private static string CreateLambdaJsonSerializableAttributes(SliceRouteInfo route)
    {
        var roots = GetLambdaJsonRootTypes(route);
        if (roots.Length == 0)
        {
            return "";
        }

        return string.Join(
            Environment.NewLine,
            roots.Select(root => $"[JsonSerializable(typeof({ToTypeOfName(RequireCSharpTypeName(route, root, "JSON root type"))}))]"));
    }

    private static string[] GetLambdaJsonRootTypes(SliceRouteInfo route)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var body = ClientGenerationHelpers.FindBodyParameter(route);
        if (body is not null)
        {
            roots.Add(body.Type);
        }

        var responseType = GetAwaitedReturnType(route.ReturnType);
        if (responseType is not null
            && !string.Equals(responseType, "Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse", StringComparison.Ordinal)
            && !string.Equals(responseType, "global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse", StringComparison.Ordinal))
        {
            roots.Add(responseType);
        }

        return [.. roots.OrderBy(static root => root, StringComparer.Ordinal)];
    }

    private static string? GetAwaitedReturnType(string returnType)
    {
        if (returnType is "void" or "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
        {
            return null;
        }

        const string taskPrefix = "System.Threading.Tasks.Task<";
        const string valueTaskPrefix = "System.Threading.Tasks.ValueTask<";
        if (returnType.StartsWith(taskPrefix, StringComparison.Ordinal) && returnType.EndsWith('>'))
        {
            return returnType[taskPrefix.Length..^1];
        }

        if (returnType.StartsWith(valueTaskPrefix, StringComparison.Ordinal) && returnType.EndsWith('>'))
        {
            return returnType[valueTaskPrefix.Length..^1];
        }

        return returnType;
    }

    private static string ToTypeOfName(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName : "global::" + typeName;

    private static LambdaPackageArtifactReport CreateArtifactReport(
        PerFeatureArtifact artifact,
        SliceRouteInfo[] allRoutes,
        string artifactDir,
        string outputRoot,
        string wrapperBuildRoot,
        DotnetPublishResult? publishResult,
        bool skippedPublish)
    {
        var files = Directory.Exists(artifactDir)
            ? Directory.EnumerateFiles(artifactDir, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(static file => file.Exists)
                .Select(file => new LambdaPackageArtifactFile(
                    ToPackageRelativePath(artifactDir, file.FullName),
                    file.Length))
                .OrderByDescending(static file => file.SizeBytes)
                .ThenBy(static file => file.Path, StringComparer.Ordinal)
                .ToArray()
            : [];
        var closureInspection = LambdaPackageClosureInspector.Inspect(
            artifact.Route,
            allRoutes,
            artifactDir,
            wrapperBuildRoot,
            outputRoot,
            skippedPublish);
        return new LambdaPackageArtifactReport(
            artifact.ArtifactId,
            artifact.CodeUri,
            artifact.Route.EndpointName,
            skippedPublish,
            files.Sum(static file => file.SizeBytes),
            [.. files.Take(20)],
            publishResult?.BinlogPath,
            publishResult?.Warnings ?? [],
            closureInspection);
    }

    private static LambdaWarningBaselineReport ApplyWarningBaseline(
        List<LambdaPackageArtifactReport> reports,
        FileInfo? warningBaseline)
    {
        var baselineEntries = warningBaseline is null
            ? []
            : LambdaPackageWarningInspector.ReadBaseline(warningBaseline);
        var baselineHashes = baselineEntries
            .Select(static entry => entry.MessageHash)
            .ToHashSet(StringComparer.Ordinal);
        var seenHashes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < reports.Count; i++)
        {
            var warnings = reports[i].Warnings
                .Select(warning =>
                {
                    var matched = baselineHashes.Contains(warning.MessageHash);
                    if (matched)
                    {
                        seenHashes.Add(warning.MessageHash);
                    }

                    var status = warningBaseline is null
                        ? "unbaselined"
                        : matched ? "matched" : "unbaselined";
                    return warning with { BaselineStatus = status };
                })
                .ToArray();
            reports[i] = reports[i] with { Warnings = warnings };
        }

        var allWarnings = reports.SelectMany(static report => report.Warnings).ToArray();
        var unbaselined = allWarnings
            .Where(static warning => warning.BaselineStatus == "unbaselined")
            .ToArray();
        var stale = warningBaseline is null
            ? []
            : baselineEntries
                .Where(entry => !seenHashes.Contains(entry.MessageHash))
                .ToArray();
        return new LambdaWarningBaselineReport(
            warningBaseline?.FullName,
            allWarnings.Length,
            unbaselined.Length,
            stale.Length,
            unbaselined,
            stale);
    }

    private static void WriteArtifactReportSummary(IReadOnlyCollection<LambdaPackageArtifactReport> reports)
    {
        if (reports.Count == 0)
        {
            return;
        }

        Console.WriteLine("  closure report:");
        foreach (var report in reports)
        {
            Console.WriteLine($"    - {report.ArtifactId}: {report.SizeBytes} byte(s), {report.Warnings.Length} warning(s)");
        }
    }

    private static string ToPackageRelativePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string EnsureTrailingDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool IsPerFeatureLayout(string artifactLayout)
        => string.Equals(artifactLayout, PerFeatureArtifactLayout, StringComparison.OrdinalIgnoreCase);

    private static bool IsEmittedLambdaFunctionPerFeatureRoute(SliceRouteInfo route)
        => route.LambdaFunctionPerFeatureHandler is not null
           && string.Equals(
               RouteTargetCapabilities.Classify(route).LambdaFunctionPerFeature.Status,
               RouteTargetCapabilities.Eligible,
               StringComparison.OrdinalIgnoreCase);

    private static string ToCSharpIdentifier(string value)
    {
        var chars = value.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return char.IsDigit(chars[0]) ? "_" + new string(chars) : new string(chars);
    }

    private static string RequireLambdaArtifactId(SliceRouteInfo route)
    {
        var artifactId = route.LambdaFunctionPerFeatureArtifactId;
        if (string.IsNullOrWhiteSpace(artifactId) || artifactId.Any(static ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-'))
        {
            throw new CliException($"Route '{route.EndpointName}' has invalid Lambda function-per-feature artifact metadata. Rebuild the project to refresh generated route metadata.");
        }

        return artifactId;
    }

    private static string RequireLambdaCodeUri(SliceRouteInfo route, string artifactId)
    {
        var codeUri = route.LambdaFunctionPerFeatureArtifactCodeUri;
        if (string.IsNullOrWhiteSpace(codeUri)
            || codeUri != $"{PerFeatureArtifactsDirectory}/{artifactId}"
            || codeUri.Contains('\\', StringComparison.Ordinal)
            || codeUri.Contains("..", StringComparison.Ordinal)
            || codeUri.Any(static ch => char.IsControl(ch) || ch is '\'' or '"'))
        {
            throw new CliException($"Route '{route.EndpointName}' has invalid Lambda function-per-feature CodeUri metadata. Rebuild the project to refresh generated route metadata.");
        }

        return codeUri;
    }

    private static string RequireCSharpIdentifier(SliceRouteInfo route, string? value, string metadataName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsCSharpIdentifier(value))
        {
            throw new CliException($"Route '{route.EndpointName}' has invalid Lambda function-per-feature {metadataName} metadata. Rebuild the project to refresh generated route metadata.");
        }

        return value;
    }

    private static string RequireCSharpTypeName(SliceRouteInfo route, string? value, string metadataName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(char.IsWhiteSpace)
            || value.Any(static ch => char.IsControl(ch) || ch is '\'' or '"' or ';' or '{' or '}' or '(' or ')' or '\\')
            || !HasBalancedGenericBrackets(value))
        {
            throw new CliException($"Route '{route.EndpointName}' has invalid Lambda function-per-feature {metadataName} metadata. Rebuild the project to refresh generated route metadata.");
        }

        return value;
    }

    private static bool IsCSharpIdentifier(string value)
    {
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0])))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != '_' && !char.IsLetterOrDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasBalancedGenericBrackets(string value)
    {
        var depth = 0;
        foreach (var ch in value)
        {
            if (ch == '<')
            {
                depth++;
            }
            else if (ch == '>')
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    private static string XmlEscape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private sealed record LambdaPackageManifest(
        string Mode,
        string ArtifactLayout,
        string AssemblyName,
        string LambdaRuntime,
        string Configuration,
        string? RuntimeIdentifier,
        bool SelfContained,
        int ExcludedRouteCount,
        LambdaPackageArtifact[] Artifacts,
        LambdaPackageFunction[] Functions);

    private sealed record LambdaPackageArtifact(
        string ArtifactId,
        string ArtifactLayout,
        string CodeUri,
        string BootstrapMode,
        string? RuntimeIdentifier);

    private sealed record LambdaPackageFunction(
        string EndpointName,
        string FeatureType,
        string Method,
        string Pattern,
        string Handler,
        string ArtifactId);

    private sealed record LambdaPackageClosureReport(
        string SchemaVersion,
        string Mode,
        string ArtifactLayout,
        string ReportKind,
        bool SkippedPublish,
        LambdaWarningBaselineReport WarningBaseline,
        LambdaPackageArtifactReport[] Artifacts);

    private sealed record LambdaPackageArtifactReport(
        string ArtifactId,
        string CodeUri,
        string EndpointName,
        bool SkippedPublish,
        long SizeBytes,
        LambdaPackageArtifactFile[] TopFiles,
        string? BinlogPath,
        LambdaPackageWarning[] Warnings,
        LambdaPackageClosureInspection ClosureInspection);

    private sealed record LambdaPackageArtifactFile(
        string Path,
        long SizeBytes);

    private sealed record DotnetPublishResult(
        string Stdout,
        string Stderr,
        string BinlogPath,
        LambdaPackageWarning[] Warnings);

    private sealed record PerFeatureArtifact(
        SliceRouteInfo Route,
        string ArtifactId,
        string CodeUri);
}
