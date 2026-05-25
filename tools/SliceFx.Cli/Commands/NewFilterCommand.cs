using System.CommandLine;
using SliceFx.Cli.Internal;
using SliceFx.Cli.Templates;

namespace SliceFx.Cli.Commands;

internal static class NewFilterCommand
{
    internal static Command Build()
    {
        var nameArg = new Argument<string>("FilterName")
        {
            Description = "Name of the filter class (PascalCase).",
        };
        var projectOpt = SharedOptions.CreateProject();
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("filter", "Scaffold a new endpoint filter class.")
        {
            nameArg,
            projectOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var filterName = parseResult.GetValue(nameArg)!;
            var project = parseResult.GetValue(projectOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(filterName, project, force, ct).ConfigureAwait(false);
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

    private static async Task RunAsync(string filterName, FileInfo? project, bool force, CancellationToken ct)
    {
        var ctx = ProjectContextDiscovery.Discover(project);
        filterName = CliValidation.RequireClassName(filterName, "FilterName");

        var outputDir = Path.Combine(ctx.ProjectDirectory.FullName, "Filters");
        var outputFile = Path.Combine(outputDir, $"{filterName}.cs");
        var displayPath = $"Filters/{filterName}.cs";

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

        var spec = new FilterSpec(ctx.RootNamespace, filterName);
        var content = FilterTemplate.Render(spec);
        await File.WriteAllTextAsync(outputFile, content, ct).ConfigureAwait(false);

        Console.WriteLine($"Created {displayPath}");
    }
}
