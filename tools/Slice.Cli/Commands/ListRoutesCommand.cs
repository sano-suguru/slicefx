using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using Slice.Cli.Internal;

namespace Slice.Cli.Commands;

internal static partial class ListRoutesCommand
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
        var formatOpt = new Option<string>("--format")
        {
            Description = "Output format: table or json.",
            DefaultValueFactory = _ => "table",
        };

        var cmd = new Command("routes", "List Slice feature routes in a project.")
        {
            projectOpt,
            formatOpt
        };

        cmd.SetAction((parseResult) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var format = parseResult.GetValue(formatOpt) ?? "table";

            try
            {
                Run(project, format);
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

    private static void Run(FileInfo? project, string format)
    {
        format = NormalizeFormat(format);
        var ctx = ProjectContextDiscovery.Discover(project);
        var routes = RouteCatalog.Discover(ctx);

        if (routes.Length == 0)
        {
            Console.WriteLine("No [Feature] routes found.");
            return;
        }

        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            WriteJson(routes);
            return;
        }

        WriteTable(routes);
    }

    private static void WriteTable(SliceRouteInfo[] routes)
    {
        Console.WriteLine("METHOD  ROUTE                         ENDPOINT                    PORTABILITY   NOTE");
        Console.WriteLine("------  ----------------------------  --------------------------  ------------  --------------------------------------------");
        foreach (var route in routes)
        {
            Console.WriteLine(
                $"{Pad(route.Method, 6)}  {Pad(route.Pattern, 28)}  {Pad(route.EndpointName, 26)}  {Pad(route.Portability, 12)}  {route.PortabilityReason ?? "-"}");
        }

        var portable = routes.Count(static route => route.Portability == RouteCatalog.PortabilityPortable);
        var partial = routes.Count(static route => route.Portability == RouteCatalog.PortabilityPartial);
        var aspnetOnly = routes.Count(static route => route.Portability == RouteCatalog.PortabilityAspNetOnly);

        Console.WriteLine();
        Console.WriteLine($"Summary: {routes.Length} routes ({portable} portable, {partial} partial, {aspnetOnly} aspnet-only)");
    }

    private static void WriteJson(SliceRouteInfo[] routes)
    {
        var jsonRoutes = routes.Select(static route => new RouteJson(
            route.Method,
            route.Pattern,
            route.FeatureName,
            route.Tag,
            route.EndpointName,
            route.Summary,
            route.RequestType,
            route.ReturnType,
            route.Portability,
            route.PortabilityReason,
            route.Filters,
            RouteTargetCapabilities.Classify(route)));

        var json = JsonSerializer.Serialize(jsonRoutes, JsonOptions);
        Console.WriteLine(json);
    }

    private static string Pad(string value, int width)
        => value.Length >= width ? value : value.PadRight(width);

    private static string NormalizeFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            throw new CliException("Output format is required.");
        }

        var normalized = format.Trim().ToLowerInvariant();
        return normalized is "table" or "json"
            ? normalized
            : throw new CliException($"Unsupported output format '{format}'. Supported formats: table, json.");
    }

    private sealed record RouteJson(
        string Method,
        string Pattern,
        string FeatureName,
        string Tag,
        string EndpointName,
        string? Summary,
        string? RequestType,
        string ReturnType,
        string Portability,
        string? PortabilityReason,
        string[] Filters,
        RouteCapabilities Capabilities);
}
