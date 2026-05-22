using System.CommandLine;
using Slice.Cli.Internal;
using Slice.Cli.Templates;

namespace Slice.Cli.Commands;

internal static class NewWasiCloudflareCommand
{
    internal static Command Build()
    {
        var projectOpt = SharedOptions.CreateProject();
        var forceOpt = SharedOptions.CreateForce();
        var componentNameOpt = new Option<string?>("--component-name")
        {
            Description = "WASM component base name. Defaults to the project assembly name in kebab-case.",
        };
        var outputOpt = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to dist in the project directory.",
        };

        var cmd = new Command("wasi-cloudflare", "Scaffold Cloudflare Workers host files for a Slice.Wasi component.")
        {
            projectOpt,
            componentNameOpt,
            outputOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var componentName = parseResult.GetValue(componentNameOpt);
            var output = parseResult.GetValue(outputOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, componentName, output, force, ct).ConfigureAwait(false);
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
        string? componentName,
        DirectoryInfo? output,
        bool force,
        CancellationToken ct)
    {
        var ctx = ProjectContextDiscovery.Discover(project);
        componentName = string.IsNullOrWhiteSpace(componentName)
            ? NameUtilities.ToKebabIdentifier(ctx.AssemblyName)
            : CliValidation.RequireKebabIdentifier(componentName, "Component name");

        var outputDir = output ?? new DirectoryInfo(Path.Combine(ctx.ProjectDirectory.FullName, "dist"));
        var projectPath = ToTemplateRelativePath(outputDir.FullName, ctx.ProjectFile.FullName);
        var wasmInputPath = ToTemplateRelativePath(
            outputDir.FullName,
            Path.Combine(ctx.ProjectDirectory.FullName, "dist", componentName + ".wasm"));
        var spec = new WasiCloudflareSpec(componentName, ctx.AssemblyName, projectPath, wasmInputPath);
        var files = WasiCloudflareTemplate.Render(spec);

        foreach (var file in files)
        {
            var fullPath = Path.Combine(outputDir.FullName, file.RelativePath);
            if (File.Exists(fullPath) && !force)
            {
                throw new CliException($"File already exists: {Path.GetRelativePath(ctx.ProjectDirectory.FullName, fullPath)}\nUse --force to overwrite.");
            }
        }

        foreach (var file in files)
        {
            var fullPath = Path.Combine(outputDir.FullName, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, file.Content, ct).ConfigureAwait(false);
            Console.WriteLine($"Created {Path.GetRelativePath(ctx.ProjectDirectory.FullName, fullPath)}");
        }
    }

    private static string ToTemplateRelativePath(string relativeTo, string path)
        => Path.GetRelativePath(relativeTo, path).Replace(Path.DirectorySeparatorChar, '/');
}
