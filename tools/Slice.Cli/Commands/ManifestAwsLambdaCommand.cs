using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Slice.Cli.Internal;

namespace Slice.Cli.Commands;

internal static partial class ManifestAwsLambdaCommand
{
    // Managed runtime handler: version-dependent, emit as a FIXME placeholder.
    private const string ManagedRuntimeHandlerComment =
        "# FIXME: replace with the actual handler for your Lambda hosting package version";

    private static readonly HashSet<string> KnownNativeAotRuntimes =
        new(StringComparer.OrdinalIgnoreCase) { "provided", "provided.al2", "provided.al2023" };

    private static readonly HashSet<string> KnownRuntimes =
        new(StringComparer.OrdinalIgnoreCase) { "provided", "provided.al2", "provided.al2023", "dotnet8", "dotnet9" };

    private static readonly HashSet<string> KnownModes =
        new(StringComparer.OrdinalIgnoreCase) { "hosted", "per-feature" };

    internal static Command Build()
    {
        var projectOpt = SharedOptions.CreateProject();
        var outputOpt = new Option<FileInfo?>("--output")
        {
            Description = "Output file. Defaults to template.yaml in the project directory.",
        };
        var runtimeOpt = new Option<string>("--runtime")
        {
            Description = "Lambda runtime identifier. Use 'provided.al2023' for self-contained or NativeAOT builds (default). Use 'dotnet8' or 'dotnet9' for managed runtimes.",
            DefaultValueFactory = _ => "provided.al2023",
        };
        var memoryOpt = new Option<int>("--memory")
        {
            Description = "Default function memory in MB.",
            DefaultValueFactory = _ => 256,
        };
        var timeoutOpt = new Option<int>("--timeout")
        {
            Description = "Default function timeout in seconds.",
            DefaultValueFactory = _ => 30,
        };
        var modeOpt = new Option<string>("--mode")
        {
            Description = "Lambda manifest mode: 'hosted' for one ASP.NET-hosted Lambda function, or 'per-feature' for future independent handlers.",
            DefaultValueFactory = _ => "hosted",
        };
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("aws-lambda", "Generate an AWS SAM template.yaml for Slice routes on AWS Lambda.")
        {
            projectOpt,
            outputOpt,
            runtimeOpt,
            memoryOpt,
            timeoutOpt,
            modeOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);
            var runtime = parseResult.GetValue(runtimeOpt) ?? "provided.al2023";
            var memory = parseResult.GetValue(memoryOpt);
            var timeout = parseResult.GetValue(timeoutOpt);
            var mode = parseResult.GetValue(modeOpt) ?? "hosted";
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, runtime, memory, timeout, mode, force, ct).ConfigureAwait(false);
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
        FileInfo? output,
        string runtime,
        int memory,
        int timeout,
        string mode,
        bool force,
        CancellationToken ct)
    {
        if (!KnownRuntimes.Contains(runtime))
        {
            throw new CliException($"Unknown runtime '{runtime}'. Expected one of: {string.Join(", ", KnownRuntimes)}.");
        }

        if (!KnownModes.Contains(mode))
        {
            throw new CliException($"Unknown Lambda manifest mode '{mode}'. Expected one of: {string.Join(", ", KnownModes)}.");
        }

        if (string.Equals(mode, "per-feature", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "Lambda per-feature manifest mode is not implemented yet. " +
                "Use '--mode hosted' for the current ASP.NET-hosted Lambda app model.");
        }

        var ctx = ProjectContextDiscovery.Discover(project);
        var routes = RouteCatalog.Discover(ctx);

        if (routes.Length == 0)
        {
            throw new CliException("No [Feature] routes found. Build the project first if the generated manifest is not yet available.");
        }

        var outputFile = output ?? new FileInfo(Path.Combine(ctx.ProjectDirectory.FullName, "template.yaml"));
        if (outputFile.Exists && !force)
        {
            throw new CliException($"Output file already exists: {outputFile.FullName}\nUse --force to overwrite.");
        }

        var yaml = GenerateHostedSamTemplate(ctx, routes, runtime, memory, timeout);

        Directory.CreateDirectory(outputFile.DirectoryName!);
        await File.WriteAllTextAsync(outputFile.FullName, yaml, ct).ConfigureAwait(false);

        Console.WriteLine($"Generated {outputFile.FullName}");
        Console.WriteLine($"  hosted mode — 1 function, {routes.Length} route event(s), runtime: {runtime}, memory: {memory} MB, timeout: {timeout}s");
    }

    private static string GenerateHostedSamTemplate(
        ProjectContext ctx,
        SliceRouteInfo[] routes,
        string runtime,
        int memory,
        int timeout)
    {
        // Detect event logical ID collisions before emitting any YAML.
        var seen = new HashSet<string>();
        foreach (var route in routes)
        {
            var id = ToLogicalId(route.EndpointName, "Event");
            if (!seen.Add(id))
            {
                throw new CliException(
                    $"Duplicate CloudFormation logical ID '{id}' would be generated. " +
                    $"Add a unique Tag= to the conflicting features.");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("AWSTemplateFormatVersion: '2010-09-09'");
        sb.AppendLine("Transform: 'AWS::Serverless-2016-10-31'");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Description: '{YamlSingleQuoted($"Generated by slice manifest aws-lambda --mode hosted from {ctx.AssemblyName}. Edit as needed.")}'");
        sb.AppendLine();
        sb.AppendLine("# Hosted mode: one Lambda function hosts the ASP.NET Core app generated by Slice.");
        sb.AppendLine("# Each [Feature] becomes an API Gateway HttpApi event on the same function.");
        sb.AppendLine("# This is not independent per-feature handler or binary output.");
        sb.AppendLine();
        sb.AppendLine("Globals:");
        sb.AppendLine("  Function:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    Runtime: '{YamlSingleQuoted(runtime)}'");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    MemorySize: {memory}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    Timeout: {timeout}");
        sb.AppendLine("    CodeUri: '.'  # TODO: set to your publish output directory (e.g. './publish') or use sam build");
        sb.AppendLine();
        sb.AppendLine("Resources:");
        sb.AppendLine();
        sb.AppendLine("  SliceApi:");
        sb.AppendLine("    Type: AWS::Serverless::HttpApi");
        sb.AppendLine();

        var handler = GetHandler(ctx.AssemblyName, runtime);

        sb.AppendLine("  SliceHostedFunction:");
        sb.AppendLine("    Type: AWS::Serverless::Function");
        sb.AppendLine("    Properties:");
        if (IsNativeAotRuntime(runtime))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"      Handler: '{YamlSingleQuoted(handler)}'");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"      Handler: '{YamlSingleQuoted(handler)}'  {ManagedRuntimeHandlerComment}");
        }

        sb.AppendLine("      Events:");

        foreach (var route in routes)
        {
            var eventId = ToLogicalId(route.EndpointName, "Event");
            var samPath = ToSamPath(route.Pattern);

            sb.AppendLine(CultureInfo.InvariantCulture, $"        # {SanitizeComment(route.EndpointName)} — {SanitizeComment(route.Method)} {SanitizeComment(route.Pattern)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {eventId}:");
            sb.AppendLine("          Type: HttpApi");
            sb.AppendLine("          Properties:");
            sb.AppendLine("            ApiId: !Ref SliceApi");
            sb.AppendLine(CultureInfo.InvariantCulture, $"            Method: '{YamlSingleQuoted(route.Method)}'");
            sb.AppendLine(CultureInfo.InvariantCulture, $"            Path: '{YamlSingleQuoted(samPath)}'");
        }

        return sb.ToString();
    }

    private static string GetHandler(string assemblyName, string runtime)
    {
        // NativeAOT / self-contained: Lambda looks for an executable named "bootstrap".
        if (IsNativeAotRuntime(runtime))
        {
            return "bootstrap";
        }

        // Managed runtime with Amazon.Lambda.AspNetCoreServer.Hosting.
        // The handler class depends on the package version — users must verify this.
        return $"{assemblyName}::Amazon.Lambda.AspNetCoreServer.Hosting.LambdaRuntimeSupportServer::Run";
    }

    private static bool IsNativeAotRuntime(string runtime)
        => KnownNativeAotRuntimes.Contains(runtime);

    /// <summary>
    /// Converts a Slice/ASP.NET route pattern to an API Gateway HttpApi path.
    /// Strips inline constraints ({id:int} → {id}) and maps catch-all segments
    /// ({**slug} or {*slug}) to API Gateway catch-all syntax ({slug+}).
    /// </summary>
    private static string ToSamPath(string pattern)
    {
        // Apply catch-all conversion first: {**name} or {*name} (with optional constraint) → {name+}
        var result = RouteCatchAllRegex().Replace(pattern, "{$1+}");
        // Then strip inline constraints: {name:constraint} → {name}
        result = RouteConstraintRegex().Replace(result, "{$1}");
        return result;
    }

    /// <summary>
    /// Derives a CloudFormation logical resource ID from an endpoint name.
    /// "Users.CreateUser", "Event" → "UsersCreateUserEvent"
    /// </summary>
    private static string ToLogicalId(string endpointName, string suffix)
    {
        var sb = new StringBuilder();
        foreach (var segment in endpointName.Split('.'))
        {
            var clean = new string([.. segment.Where(char.IsAsciiLetterOrDigit)]);
            if (clean.Length == 0)
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(clean[0]));
            if (clean.Length > 1)
            {
                sb.Append(clean[1..]);
            }
        }

        sb.Append(suffix);
        // CloudFormation logical IDs are capped at 255 characters.
        return sb.Length > 255 ? sb.ToString(0, 255) : sb.ToString();
    }

    /// <summary>Strips CR/LF from a value before embedding it in a YAML comment line.</summary>
    private static string SanitizeComment(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>
    /// Returns <paramref name="value"/> with single-quotes escaped for YAML single-quoted scalars.
    /// The caller is responsible for wrapping the result in the surrounding <c>'…'</c>.
    /// </summary>
    private static string YamlSingleQuoted(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex RouteConstraintRegex();

    [GeneratedRegex(@"\{\*{1,2}(\w+)(?::[^}]+)?\}")]
    private static partial Regex RouteCatchAllRegex();
}
