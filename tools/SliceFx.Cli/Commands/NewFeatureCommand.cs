using System.CommandLine;
using SliceFx.Cli.Internal;
using SliceFx.Cli.Templates;

namespace SliceFx.Cli.Commands;

internal static class NewFeatureCommand
{
    internal static Command Build()
    {
        var nameArg = new Argument<string>("FeatureName")
        {
            Description = "Name of the feature class (PascalCase).",
        };
        var groupOpt = new Option<string?>("--group")
        {
            Description = "Namespace group folder (e.g. 'Users'). Inferred from FeatureName if omitted.",
        };
        var methodOpt = new Option<string>("--method")
        {
            Description = "HTTP method (GET, POST, PUT, DELETE, PATCH).",
            DefaultValueFactory = _ => "GET",
        };
        var routeOpt = new Option<string?>("--route")
        {
            Description = "Route pattern (e.g. '/users/{id}'). Defaults to '/<kebab-name>'.",
        };
        var projectOpt = SharedOptions.CreateProject();
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("feature", "Scaffold a new feature class.")
        {
            nameArg,
            groupOpt,
            methodOpt,
            routeOpt,
            projectOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var featureName = parseResult.GetValue(nameArg)!;
            var group = parseResult.GetValue(groupOpt);
            var method = (parseResult.GetValue(methodOpt) ?? "GET").ToUpperInvariant();
            var route = parseResult.GetValue(routeOpt);
            var project = parseResult.GetValue(projectOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(featureName, group, method, route, project, force, ct).ConfigureAwait(false);
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
        string featureName,
        string? group,
        string method,
        string? route,
        FileInfo? project,
        bool force,
        CancellationToken ct)
    {
        var ctx = ProjectContextDiscovery.Discover(project);
        featureName = CliValidation.RequireClassName(featureName, "FeatureName");
        method = CliValidation.NormalizeHttpMethod(method);

        if (string.IsNullOrWhiteSpace(group))
        {
            group = NameUtilities.InferGroup(featureName);
        }

        if (string.IsNullOrWhiteSpace(group))
        {
            Console.Write($"Group? [{featureName}]: ");
            var input = Console.ReadLine()?.Trim();
            group = string.IsNullOrWhiteSpace(input) ? featureName : input;
        }

        var groupSegments = CliValidation.RequireGroupSegments(group);
        var groupNamespace = string.Join(".", groupSegments);
        var groupPath = Path.Combine(groupSegments);
        route = CliValidation.NormalizeRoute(route, featureName);

        var outputDir = Path.Combine(ctx.ProjectDirectory.FullName, "Features", groupPath);
        var outputFile = Path.Combine(outputDir, $"{featureName}.cs");
        var displayPath = Path.Combine("Features", groupPath, $"{featureName}.cs");

        if (File.Exists(outputFile) && !force)
        {
            Console.Write($"File already exists: {displayPath}. Overwrite? [y/N]: ");
            var answer = Console.ReadLine()?.Trim();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Skipped.");
                return;
            }
        }

        Directory.CreateDirectory(outputDir);

        var spec = new FeatureSpec(ctx.RootNamespace, groupNamespace, featureName, method, route);
        var content = FeatureTemplate.Render(spec);
        await File.WriteAllTextAsync(outputFile, content, ct).ConfigureAwait(false);

        Console.WriteLine($"Created {displayPath}");
    }
}
