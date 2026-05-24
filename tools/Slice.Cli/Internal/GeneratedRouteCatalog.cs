using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Slice.Shared;

namespace Slice.Cli.Internal;

internal static class GeneratedRouteCatalog
{
    internal static GeneratedRouteDiscovery Discover(ProjectContext ctx)
    {
        var assemblyFiles = FindAssemblyFiles(ctx);
        if (assemblyFiles is null)
        {
            return new GeneratedRouteDiscovery(false, [], false);
        }

        var routes = new List<SliceRouteInfo>();
        var hasLambdaPerFunctionHandlers = false;
        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                var metadata = ReadMetadata(assemblyFile);
                routes.AddRange(metadata.Routes);
                hasLambdaPerFunctionHandlers |= metadata.LambdaPerFunctionHandlerTypeName is not null;
            }
            catch (UnsupportedRouteManifestSchemaException ex)
            {
                throw new CliException(
                    $"Unsupported Slice route manifest schema '{ex.SchemaVersion}' in {assemblyFile.FullName}. Update the slice CLI to read this project.");
            }
            catch (InvalidRouteManifestException ex)
            {
                throw new CliException(
                    $"Invalid Slice route manifest in {assemblyFile.FullName}: {ex.Message}. Rebuild the project with the current Slice source generator.");
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
            .ThenBy(static route => route.EndpointName, StringComparer.Ordinal)], hasLambdaPerFunctionHandlers);
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

        var referencedAssemblies = ReadReferencedAssemblyNames(primaryAssembly);
        return [.. primaryAssembly.Directory
            .EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
            .Where(file => ShouldReadAssembly(file, primaryAssembly.Name, referencedAssemblies))
            .OrderBy(static file => file.Name, StringComparer.Ordinal)];
    }

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

        var assemblyName = Path.GetFileNameWithoutExtension(file.Name);
        return referencedAssemblies.Contains(assemblyName);
    }

    private static bool IsReferenceAssemblyPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => segment is "ref" or "refint");

    private static GeneratedAssemblyRouteMetadata ReadMetadata(FileInfo assemblyFile)
    {
        var routes = new List<SliceRouteInfo>();
        using var stream = new FileStream(assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return new GeneratedAssemblyRouteMetadata([], null);
        }

        var reader = peReader.GetMetadataReader();
        var lambdaHandlerTypeName = ReadLambdaPerFunctionHandlerTypeName(reader);
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (IsLambdaPerFunctionModuleAttribute(reader, attribute))
            {
                continue;
            }

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
                routes.Add(route);
            }
        }

        return new GeneratedAssemblyRouteMetadata([.. routes], lambdaHandlerTypeName);
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

    private static bool IsLambdaPerFunctionModuleAttribute(MetadataReader reader, CustomAttribute attribute)
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

        if (typeHandle.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var type = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
        return reader.StringComparer.Equals(type.Namespace, "Slice.Lambda.PerFunction")
               && reader.StringComparer.Equals(type.Name, "LambdaPerFunctionModuleAttribute")
               && type.ResolutionScope.Kind == HandleKind.AssemblyReference
               && reader.StringComparer.Equals(
                  reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name,
                  "Slice.Lambda.PerFunction");
    }

    private static string? ReadLambdaPerFunctionHandlerTypeName(MetadataReader reader)
    {
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsLambdaPerFunctionModuleAttribute(reader, attribute))
            {
                continue;
            }

            var value = attribute.DecodeValue(StringAttributeTypeProvider.Instance);
            if (value.FixedArguments.Length == 0)
            {
                return null;
            }

            return TrimGlobalPrefix(GetString(value.FixedArguments[0]));
        }

        return null;
    }

    private static SliceRouteInfo? DecodeRoute(CustomAttribute attribute)
    {
        var value = attribute.DecodeValue(StringAttributeTypeProvider.Instance);
        var args = value.FixedArguments;
        if (args.Length != SliceRouteManifestSchema.AttributeConstructorArgumentCount)
        {
            throw new InvalidRouteManifestException(
                $"expected {SliceRouteManifestSchema.AttributeConstructorArgumentCount} constructor arguments but found {args.Length}");
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
        var tag = GetString(args[4]);
        tag = string.IsNullOrWhiteSpace(tag) ? InferTag(featureNamespace) : tag;
        var summary = GetString(args[5]);
        var requestType = GetString(args[6]);
        var returnType = GetString(args[7]) ?? "";
        var portability = GetString(args[8]);
        var portabilityReason = GetString(args[9]);
        var filters = SplitLines(GetString(args[10]));
        var parameters = ReadParameters(GetString(args[11]));
        var lambdaStatus = GetString(args[12]);
        var lambdaReason = GetString(args[13]);
        var lambdaHandlerAssembly = GetString(args[14]);
        var lambdaHandlerType = TrimGlobalPrefix(GetString(args[15]));
        var lambdaHandlerMethod = GetString(args[16]);
        var manifestSchemaVersion = GetString(args[17]);
        if (string.IsNullOrWhiteSpace(manifestSchemaVersion))
        {
            throw new InvalidRouteManifestException("manifest schema version is missing");
        }

        if (manifestSchemaVersion != SliceRouteManifestSchema.CurrentVersion)
        {
            throw new UnsupportedRouteManifestSchemaException(manifestSchemaVersion);
        }

        var wasiStatus = GetString(args[18]);
        var wasiReason = GetString(args[19]);

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
            parameters,
            lambdaStatus,
            lambdaReason,
            lambdaHandlerAssembly,
            lambdaHandlerType,
            lambdaHandlerMethod,
            manifestSchemaVersion,
            wasiStatus,
            wasiReason,
            HasGeneratedMetadata: true);
    }

    private static string? GetString(CustomAttributeTypedArgument<string> argument)
        => argument.Value as string;

    private static string? TrimGlobalPrefix(string? value)
        => value?.Replace("global::", "");

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
            var parts = line.Split('|');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                continue;
            }

            parameters.Add(new SliceRouteParameter(
                parts[0],
                parts[1],
                IsNullable: parts.Length > 2 && parts[2] == "N",
                BindingSource: parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null,
                BindingName: parts.Length > 4 && parts[4].Length > 0 ? parts[4] : null));
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

internal sealed record GeneratedRouteDiscovery(bool Found, SliceRouteInfo[] Routes, bool HasLambdaPerFunctionHandlers);

internal sealed record GeneratedAssemblyRouteMetadata(SliceRouteInfo[] Routes, string? LambdaPerFunctionHandlerTypeName);

internal sealed class UnsupportedRouteManifestSchemaException(string schemaVersion) : Exception
{
    internal string SchemaVersion { get; } = schemaVersion;
}

internal sealed class InvalidRouteManifestException(string message) : Exception(message);
