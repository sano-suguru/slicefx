using System.Xml.Linq;

namespace Slice.Cli.Internal;

internal sealed record ProjectContext(FileInfo ProjectFile, string RootNamespace, DirectoryInfo ProjectDirectory);

internal static class ProjectContextDiscovery
{
    internal static ProjectContext Discover(FileInfo? explicitProject)
    {
        FileInfo projectFile;

        if (explicitProject is not null)
        {
            if (!explicitProject.Exists)
            {
                throw new CliException($"Project file not found: {explicitProject.FullName}");
            }

            projectFile = explicitProject;
        }
        else
        {
            var cwd = Directory.GetCurrentDirectory();
            var found = Directory.GetFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly);

            if (found.Length == 0)
            {
                throw new CliException($"No *.csproj found in {cwd}. Run from a project directory or pass --project.");
            }

            if (found.Length > 1)
            {
                var list = string.Join(Environment.NewLine, found.Select(f => $"  {f}"));
                throw new CliException($"Multiple *.csproj found:{Environment.NewLine}{list}{Environment.NewLine}Use --project to specify which one.");
            }

            projectFile = new FileInfo(found[0]);
        }

        var rootNamespace = CliValidation.RequireNamespace(ReadRootNamespace(projectFile), "RootNamespace");
        return new ProjectContext(projectFile, rootNamespace, projectFile.Directory!);
    }

    private static string ReadRootNamespace(FileInfo projectFile)
    {
        try
        {
            var doc = XDocument.Load(projectFile.FullName);
            var value = doc.Descendants("RootNamespace").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch (Exception ex) when (ex is not CliException)
        {
            // Fall through to filename-based default
        }

        return Path.GetFileNameWithoutExtension(projectFile.Name);
    }
}
