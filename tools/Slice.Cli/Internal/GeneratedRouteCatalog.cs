using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Slice.Cli.Internal;

internal static class GeneratedRouteCatalog
{
    internal static GeneratedRouteDiscovery Discover(ProjectContext ctx)
    {
        var assemblyFiles = FindAssemblyFiles(ctx);
        if (assemblyFiles is null)
        {
            return new GeneratedRouteDiscovery(false, []);
        }

        var routes = new List<SliceRouteInfo>();
        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                routes.AddRange(ReadRoutes(assemblyFile));
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }

        return new GeneratedRouteDiscovery(true, [.. routes
            .DistinctBy(static route => (route.EndpointName, route.Method, route.Pattern, route.FeatureType))
            .OrderBy(static route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(static route => route.Method, StringComparer.Ordinal)
            .ThenBy(static route => route.EndpointName, StringComparer.Ordinal)]);
    }

    private static FileInfo[]? FindAssemblyFiles(ProjectContext ctx)
    {
        var binDir = new DirectoryInfo(Path.Combine(ctx.ProjectDirectory.FullName, "bin"));
        if (!binDir.Exists)
        {
            return null;
        }

        var primaryAssemblyName = ctx.AssemblyName + ".dll";
        var primaryAssembly = binDir.EnumerateFiles(primaryAssemblyName, SearchOption.AllDirectories)
            .Where(static file => !IsReferenceAssemblyPath(file.FullName))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (primaryAssembly?.Directory is null)
        {
            return null;
        }

        return [.. primaryAssembly.Directory
            .EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
            .Where(static file => !file.Name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static file => file.Name, StringComparer.Ordinal)];
    }

    private static bool IsReferenceAssemblyPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => segment is "ref" or "refint");

    private static IEnumerable<SliceRouteInfo> ReadRoutes(FileInfo assemblyFile)
    {
        using var stream = new FileStream(assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            yield break;
        }

        var reader = peReader.GetMetadataReader();
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsSliceRouteAttribute(reader, attribute))
            {
                continue;
            }

            SliceRouteInfo? route;
            try
            {
                route = DecodeRoute(attribute);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            if (route is not null)
            {
                yield return route;
            }
        }
    }

    private static bool IsSliceRouteAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        var constructor = attribute.Constructor;
        EntityHandle typeHandle;
        if (constructor.Kind == HandleKind.MemberReference)
        {
            typeHandle = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        }
        else if (constructor.Kind == HandleKind.MethodDefinition)
        {
            typeHandle = reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();
        }
        else
        {
            return false;
        }

        if (typeHandle.Kind == HandleKind.TypeReference)
        {
            return IsSliceRouteType(reader, reader.GetTypeReference((TypeReferenceHandle)typeHandle));
        }

        return false;
    }

    private static bool IsSliceRouteType(MetadataReader reader, TypeReference type)
        => reader.StringComparer.Equals(type.Namespace, "Slice")
           && reader.StringComparer.Equals(type.Name, "SliceFeatureRouteAttribute")
           && type.ResolutionScope.Kind == HandleKind.AssemblyReference
           && reader.StringComparer.Equals(
               reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name,
               "Slice.Core");

    private static SliceRouteInfo? DecodeRoute(CustomAttribute attribute)
    {
        var value = attribute.DecodeValue(StringAttributeTypeProvider.Instance);
        var args = value.FixedArguments;
        if (args.Length < 4)
        {
            return null;
        }

        var endpointName = GetString(args[0]);
        var featureType = GetString(args[1]);
        var method = GetString(args[2]);
        var pattern = GetString(args[3]);
        if (endpointName is null || featureType is null || method is null || pattern is null)
        {
            return null;
        }

        var (featureNamespace, featureName) = SplitFeatureType(featureType);
        var tag = args.Length > 4 ? GetString(args[4]) : null;
        tag = string.IsNullOrWhiteSpace(tag) ? InferTag(featureNamespace) : tag;
        var summary = args.Length > 5 ? GetString(args[5]) : null;
        var requestType = args.Length > 6 ? GetString(args[6]) : null;
        var returnType = args.Length > 7 ? GetString(args[7]) ?? "" : "";
        var portability = args.Length > 8 ? GetString(args[8]) : null;
        var portabilityReason = args.Length > 9 ? GetString(args[9]) : null;
        var filters = args.Length > 10 ? SplitLines(GetString(args[10])) : [];
        var parameters = args.Length > 11 ? ReadParameters(GetString(args[11])) : [];

        return new SliceRouteInfo(
            method.ToUpperInvariant(),
            pattern,
            featureNamespace,
            featureName,
            tag,
            endpointName,
            summary,
            requestType,
            returnType,
            string.IsNullOrWhiteSpace(portability) ? RouteCatalog.PortabilityUnknown : portability,
            portabilityReason,
            filters,
            parameters);
    }

    private static string? GetString(CustomAttributeTypedArgument<string> argument)
        => argument.Value as string;

    private static (string Namespace, string Name) SplitFeatureType(string featureType)
    {
        var trimmed = featureType.StartsWith("global::", StringComparison.Ordinal)
            ? featureType["global::".Length..]
            : featureType;
        var separator = trimmed.LastIndexOf('.');
        return separator < 0
            ? ("", trimmed)
            : (trimmed[..separator], trimmed[(separator + 1)..]);
    }

    private static string InferTag(string @namespace)
    {
        var idx = @namespace.IndexOf(".Features.", StringComparison.Ordinal);
        if (idx < 0)
        {
            return "Default";
        }

        var rest = @namespace[(idx + ".Features.".Length)..];
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    private static string[] SplitLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static SliceRouteParameter[] ReadParameters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var parameters = new List<SliceRouteParameter>();
        foreach (var line in SplitLines(value))
        {
            var separator = line.LastIndexOf('|');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            parameters.Add(new SliceRouteParameter(line[..separator], line[(separator + 1)..]));
        }

        return [.. parameters];
    }

    private sealed class StringAttributeTypeProvider : ICustomAttributeTypeProvider<string>
    {
        internal static readonly StringAttributeTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}

internal sealed record GeneratedRouteDiscovery(bool Found, SliceRouteInfo[] Routes);
