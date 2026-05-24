using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Slice.Cli.Internal;

internal static class BuildOutputAssemblyFinder
{
    internal static FileInfo[] FindAssemblyFiles(ProjectContext ctx)
    {
        var binDir = new DirectoryInfo(Path.Combine(ctx.ProjectDirectory.FullName, "bin"));
        if (!binDir.Exists)
        {
            return [];
        }

        var primaryAssemblyName = ctx.AssemblyName + ".dll";
        var primaryAssembly = binDir.EnumerateFiles(primaryAssemblyName, SearchOption.AllDirectories)
            .Where(static file => !IsReferenceAssemblyPath(file.FullName))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (primaryAssembly?.Directory is null)
        {
            return [];
        }

        var referencedAssemblies = ReadReferencedAssemblyNames(primaryAssembly);
        return [.. primaryAssembly.Directory.EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
            .Where(file => ShouldReadAssembly(file, primaryAssembly.Name, referencedAssemblies))
            .OrderBy(static file => file.Name, StringComparer.Ordinal)];
    }

    private static bool IsReferenceAssemblyPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => segment is "ref" or "refint");

    private static bool ShouldReadAssembly(FileInfo file, string primaryAssemblyFileName, HashSet<string> referencedAssemblies)
    {
        if (file.Name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(file.Name, primaryAssemblyFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return referencedAssemblies.Contains(Path.GetFileNameWithoutExtension(file.Name));
    }

    private static HashSet<string> ReadReferencedAssemblyNames(FileInfo assemblyFile)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = new FileStream(assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return names;
            }

            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.AssemblyReferences)
            {
                names.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
            }
        }
        catch (BadImageFormatException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return names;
    }
}
