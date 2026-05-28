using System.CommandLine;

namespace SliceFx.Cli.Internal;

internal static class SharedOptions
{
    internal static Option<string?> CreateProject() => new("--project")
    {
        Description = "Path to the target *.csproj, or a directory containing exactly one. Defaults to a *.csproj in the current directory.",
    };

    internal static Option<bool> CreateForce() => new("--force")
    {
        Description = "Overwrite existing files without prompting.",
    };

    internal static string ResolveOutputFile(string? explicitOutput, string defaultFileName, DirectoryInfo defaultDirectory)
    {
        if (explicitOutput is null)
        {
            return Path.Combine(defaultDirectory.FullName, defaultFileName);
        }
        if (Directory.Exists(explicitOutput))
        {
            return Path.Combine(Path.GetFullPath(explicitOutput), defaultFileName);
        }
        return Path.GetFullPath(explicitOutput);
    }
}
