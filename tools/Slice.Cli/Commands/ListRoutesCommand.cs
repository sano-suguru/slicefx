using System.CommandLine;
using System.Text.RegularExpressions;
using Slice.Cli.Internal;

namespace Slice.Cli.Commands;

internal static partial class ListRoutesCommand
{
    internal static Command Build()
    {
        var projectOpt = SharedOptions.CreateProject();

        var cmd = new Command("routes", "List Slice feature routes in a project.")
        {
            projectOpt
        };

        cmd.SetAction((parseResult) =>
        {
            var project = parseResult.GetValue(projectOpt);

            try
            {
                Run(project);
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

    private static void Run(FileInfo? project)
    {
        var ctx = ProjectContextDiscovery.Discover(project);
        var featuresDir = Path.Combine(ctx.ProjectDirectory.FullName, "Features");
        if (!Directory.Exists(featuresDir))
        {
            throw new CliException($"Features directory not found: {featuresDir}");
        }

        var routes = Directory.EnumerateFiles(featuresDir, "*.cs", SearchOption.AllDirectories)
            .Select(ReadRoute)
            .Where(static route => route is not null)
            .Cast<RouteInfo>()
            .OrderBy(static route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(static route => route.Method, StringComparer.Ordinal)
            .ToArray();

        if (routes.Length == 0)
        {
            Console.WriteLine("No [Feature] routes found.");
            return;
        }

        WriteTable(routes);
    }

    private static RouteInfo? ReadRoute(string file)
    {
        var source = File.ReadAllText(file);
        var featureMatch = FeatureAttributeRegex().Match(source);
        if (!featureMatch.Success)
        {
            return null;
        }

        var route = UnescapeCSharpString(featureMatch.Groups["route"].Value);
        var parts = route.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var classMatch = ClassRegex().Match(source, featureMatch.Index);
        if (!classMatch.Success)
        {
            return null;
        }

        var namespaceMatch = NamespaceRegex().Match(source);
        var @namespace = namespaceMatch.Success ? namespaceMatch.Groups["namespace"].Value : "";
        var args = featureMatch.Groups["args"].Value;
        var tag = ReadNamedStringArgument(args, "Tag") ?? InferTag(@namespace);
        var summary = ReadNamedStringArgument(args, "Summary");
        var handleMatch = HandleRegex().Match(source);
        var returnType = handleMatch.Success ? NormalizeWhitespace(handleMatch.Groups["return"].Value) : "";
        var filters = FilterRegex().Matches(source)
            .Select(static match => match.Groups["filter"].Value.Trim())
            .ToArray();

        return new RouteInfo(
            parts[0].ToUpperInvariant(),
            parts[1],
            classMatch.Groups["class"].Value,
            tag,
            $"{tag}.{classMatch.Groups["class"].Value}",
            summary,
            returnType,
            !returnType.Contains("IResult", StringComparison.Ordinal),
            filters);
    }

    private static void WriteTable(RouteInfo[] routes)
    {
        Console.WriteLine("METHOD  ROUTE                         ENDPOINT                    WORKERS  RETURN");
        Console.WriteLine("------  ----------------------------  --------------------------  -------  ----------------");
        foreach (var route in routes)
        {
            Console.WriteLine(
                $"{Pad(route.Method, 6)}  {Pad(route.Pattern, 28)}  {Pad(route.EndpointName, 26)}  {Pad(route.IsWorkersCompatible ? "yes" : "no", 7)}  {route.ReturnType}");
        }
    }

    private static string? ReadNamedStringArgument(string args, string name)
    {
        var match = Regex.Match(args, $@"\b{name}\s*=\s*""(?<value>(?:\\.|[^""\\])*)""");
        return match.Success ? UnescapeCSharpString(match.Groups["value"].Value) : null;
    }

    private static string InferTag(string @namespace)
    {
        var idx = @namespace.IndexOf(".Features.", StringComparison.Ordinal);
        if (idx < 0)
        {
            return "Default";
        }

        var rest = @namespace[(idx + ".Features.".Length)..];
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    private static string Pad(string value, int width)
        => value.Length >= width ? value : value.PadRight(width);

    private static string NormalizeWhitespace(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string UnescapeCSharpString(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace(@"\\", @"\", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\[Feature\(\s*""(?<route>(?:\\.|[^""\\])*)""(?<args>(?:[^\)""]|""(?:\\.|[^""\\])*"")*)\)\]")]
    private static partial Regex FeatureAttributeRegex();

    [GeneratedRegex(@"\bpublic\s+static\s+class\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\b")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"\bnamespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*;")]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"\[Filter<(?<filter>[^>\]]+)>\]")]
    private static partial Regex FilterRegex();

    [GeneratedRegex(@"\bpublic\s+static\s+(?:async\s+)?(?<return>[A-Za-z0-9_<>,\.\?\s]+?)\s+Handle\s*\(")]
    private static partial Regex HandleRegex();

    private sealed record RouteInfo(
        string Method,
        string Pattern,
        string FeatureName,
        string Tag,
        string EndpointName,
        string? Summary,
        string ReturnType,
        bool IsWorkersCompatible,
        string[] Filters);
}
