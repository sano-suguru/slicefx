using System.Xml.Linq;

namespace SliceFx.Cli.Internal;

internal sealed record ProjectContext(
    FileInfo ProjectFile,
    string RootNamespace,
    string AssemblyName,
    DirectoryInfo ProjectDirectory);

internal static class ProjectContextDiscovery
{
    internal static ProjectContext Discover(string? explicitProject)
    {
        var projectFile = explicitProject is null
            ? FindSingleCsproj(Directory.GetCurrentDirectory())
            : Directory.Exists(explicitProject)
                ? FindSingleCsproj(explicitProject)
                : File.Exists(explicitProject)
                    ? new FileInfo(explicitProject)
                    : throw new CliException($"Project file not found: {Path.GetFullPath(explicitProject)}");

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

    private static FileInfo FindSingleCsproj(string directory)
    {
        var found = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);

        if (found.Length == 0)
        {
            throw new CliException($"No *.csproj found in {directory}. Run from a project directory or pass --project.");
        }

        if (found.Length > 1)
        {
            var list = string.Join(Environment.NewLine, found.Select(f => $"  {f}"));
            throw new CliException($"Multiple *.csproj found:{Environment.NewLine}{list}{Environment.NewLine}Use --project to specify which one.");
        }

        return new FileInfo(found[0]);
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
