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
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("aws-lambda", "Generate an AWS SAM template.yaml with one Lambda function per Slice feature.")
        {
            projectOpt,
            outputOpt,
            runtimeOpt,
            memoryOpt,
            timeoutOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);
            var runtime = parseResult.GetValue(runtimeOpt) ?? "provided.al2023";
            var memory = parseResult.GetValue(memoryOpt);
            var timeout = parseResult.GetValue(timeoutOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, runtime, memory, timeout, force, ct).ConfigureAwait(false);
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
        bool force,
        CancellationToken ct)
    {
        if (!KnownRuntimes.Contains(runtime))
        {
            throw new CliException($"Unknown runtime '{runtime}'. Expected one of: {string.Join(", ", KnownRuntimes)}.");
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

        var yaml = GenerateSamTemplate(ctx, routes, runtime, memory, timeout);

        Directory.CreateDirectory(outputFile.DirectoryName!);
        await File.WriteAllTextAsync(outputFile.FullName, yaml, ct).ConfigureAwait(false);

        Console.WriteLine($"Generated {outputFile.FullName}");
        Console.WriteLine($"  {routes.Length} function(s) — runtime: {runtime}, memory: {memory} MB, timeout: {timeout}s");

        var partial = routes.Count(static r => r.Portability == RouteCatalog.PortabilityPartial);
        if (partial > 0)
        {
            Console.WriteLine($"  Note: {partial} route(s) are 'partial' — non-validator endpoint filters will not run in Lambda. See comments in template.yaml.");
        }
    }

    private static string GenerateSamTemplate(
        ProjectContext ctx,
        SliceRouteInfo[] routes,
        string runtime,
        int memory,
        int timeout)
    {
        // Detect logical ID collisions before emitting any YAML.
        var seen = new HashSet<string>();
        foreach (var route in routes)
        {
            var id = ToLogicalId(route.EndpointName);
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"Description: '{YamlSingleQuoted($"Generated by slice manifest aws-lambda from {ctx.AssemblyName}. Edit as needed.")}'");
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

        foreach (var route in routes)
        {
            var logicalId = ToLogicalId(route.EndpointName);
            var samPath = ToSamPath(route.Pattern);
            var handler = GetHandler(ctx.AssemblyName, runtime);

            sb.AppendLine(CultureInfo.InvariantCulture, $"  # {SanitizeComment(route.EndpointName)} — {SanitizeComment(route.Method)} {SanitizeComment(route.Pattern)} ({SanitizeComment(route.Portability)})");

            if (route.Portability == RouteCatalog.PortabilityPartial)
            {
                var nonPortableFilters = route.Filters
                    .Where(static f =>
                        !f.StartsWith("SliceValidatorFilter<", StringComparison.Ordinal) &&
                        !f.StartsWith("Slice.SliceValidatorFilter<", StringComparison.Ordinal) &&
                        !f.StartsWith("global::Slice.SliceValidatorFilter<", StringComparison.Ordinal))
                    .ToArray();
                if (nonPortableFilters.Length > 0)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  # Warning: the following filters will not run in Lambda: {SanitizeComment(string.Join(", ", nonPortableFilters))}");
                }
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"  {logicalId}:");
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
            sb.AppendLine("        HttpEvent:");
            sb.AppendLine("          Type: HttpApi");
            sb.AppendLine("          Properties:");
            sb.AppendLine("            ApiId: !Ref SliceApi");
            sb.AppendLine(CultureInfo.InvariantCulture, $"            Method: '{YamlSingleQuoted(route.Method)}'");
            sb.AppendLine(CultureInfo.InvariantCulture, $"            Path: '{YamlSingleQuoted(samPath)}'");
            sb.AppendLine();
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
    /// "Users.CreateUser" → "UsersCreateUserFunction"
    /// </summary>
    private static string ToLogicalId(string endpointName)
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

        sb.Append("Function");
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
