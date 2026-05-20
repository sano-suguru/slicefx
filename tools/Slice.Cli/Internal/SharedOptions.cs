using System.CommandLine;

namespace Slice.Cli.Internal;

internal static class SharedOptions
{
    internal static Option<FileInfo?> CreateProject() => new("--project")
    {
        Description = "Path to the target *.csproj. Defaults to a *.csproj in the current directory.",
    };

    internal static Option<bool> CreateForce() => new("--force")
    {
        Description = "Overwrite existing files without prompting.",
    };
}
