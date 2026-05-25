using System.Xml.Linq;

namespace SliceFx.Cli.Internal;

internal sealed record ProjectContext(
    FileInfo ProjectFile,
    string RootNamespace,
    string AssemblyName,
    DirectoryInfo ProjectDirectory);

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

        var projectProperties = ReadProjectProperties(projectFile);
        var rawRootNamespace = projectProperties.RootNamespace ?? Path.GetFileNameWithoutExtension(projectFile.Name);
        var rootNamespace = projectProperties.RootNamespace is not null
            ? CliValidation.RequireNamespace(rawRootNamespace, "RootNamespace")
            : CliValidation.RequireNamespace(NameUtilities.ToNamespaceSegment(rawRootNamespace), "RootNamespace");
        var assemblyName = string.IsNullOrWhiteSpace(projectProperties.AssemblyName)
            ? Path.GetFileNameWithoutExtension(projectFile.Name)
            : projectProperties.AssemblyName!;
        return new ProjectContext(projectFile, rootNamespace, assemblyName, projectFile.Directory!);
    }

    private static ProjectProperties ReadProjectProperties(FileInfo projectFile)
    {
        try
        {
            var doc = XDocument.Load(projectFile.FullName);
            var rootNamespace = doc.Descendants("RootNamespace").FirstOrDefault()?.Value;
            var assemblyName = doc.Descendants("AssemblyName").FirstOrDefault()?.Value;
            return new ProjectProperties(
                string.IsNullOrWhiteSpace(rootNamespace) ? null : rootNamespace,
                string.IsNullOrWhiteSpace(assemblyName) ? null : assemblyName);
        }
        catch (Exception ex) when (ex is not CliException)
        {
            // Fall through to filename-based default
        }

        return new ProjectProperties(null, null);
    }

    private sealed record ProjectProperties(string? RootNamespace, string? AssemblyName);
}
