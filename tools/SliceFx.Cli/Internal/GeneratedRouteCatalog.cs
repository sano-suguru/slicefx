using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SliceFx.Shared;

namespace SliceFx.Cli.Internal;

internal static class GeneratedRouteCatalog
{
    internal static GeneratedRouteDiscovery Discover(ProjectContext ctx)
    {
        var assemblyFiles = FindAssemblyFiles(ctx);
        if (assemblyFiles is null)
        {
            return new GeneratedRouteDiscovery(false, [], false, []);
        }

        var routes = new List<SliceRouteInfo>();
        var hasLambdaFunctionPerFeatureHandlers = false;
        var primaryAssemblyName = Path.GetFileNameWithoutExtension(assemblyFiles[0].Name);
        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                var metadata = ReadMetadata(assemblyFile);
                routes.AddRange(metadata.Routes);
                hasLambdaFunctionPerFeatureHandlers |= metadata.LambdaFunctionPerFeatureHandlerTypeName is not null;
            }
            catch (UnsupportedRouteManifestSchemaException ex)
            {
                throw new CliException(
                    $"Unsupported SliceFx route manifest schema '{ex.SchemaVersion}' in {assemblyFile.FullName}. Update the SliceFx CLI to read this project.");
            }
            catch (InvalidRouteManifestException ex)
            {
                throw new CliException(
                    $"Invalid SliceFx route manifest in {assemblyFile.FullName}: {ex.Message}. Rebuild the project with the current SliceFx source generator.");
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

        var aggregatedSourceAssemblies = routes
            .Select(static route => route.SourceAssemblyName)
            .Where(sourceAssemblyName => sourceAssemblyName is not null
                                         && !string.Equals(sourceAssemblyName, primaryAssemblyName, StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static assemblyName => assemblyName, StringComparer.Ordinal)
            .ToArray();

        return new GeneratedRouteDiscovery(true, [.. routes
            .DistinctBy(static route => (route.EndpointName, route.Method, route.Pattern, route.FeatureType))
            .OrderBy(static route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(static route => route.Method, StringComparer.Ordinal)
            .ThenBy(static route => route.EndpointName, StringComparer.Ordinal)], hasLambdaFunctionPerFeatureHandlers, aggregatedSourceAssemblies);
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

        var primaryMetadata = ReadPrimaryAssemblyMetadata(primaryAssembly);
        return [
            primaryAssembly,
            .. primaryAssembly.Directory
            .EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(file.FullName, primaryAssembly.FullName, StringComparison.OrdinalIgnoreCase))
            .Where(file => ShouldReadAssembly(file, primaryAssembly.Name, primaryMetadata.ReferencedAssemblyNames))
            .Where(file => primaryMetadata.AggregatedAssemblyNames.Contains(Path.GetFileNameWithoutExtension(file.Name)))
            .OrderBy(static file => file.Name, StringComparer.Ordinal)
        ];
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
        var validatorsByRequestType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var stream = new FileStream(assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return new GeneratedAssemblyRouteMetadata([], null);
        }

        var reader = peReader.GetMetadataReader();
        var sourceAssemblyName = reader.GetString(reader.GetAssemblyDefinition().Name);
        var lambdaHandlerTypeName = ReadLambdaFunctionPerFeatureHandlerTypeName(reader);
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (IsLambdaFunctionPerFeatureModuleAttribute(reader, attribute))
            {
                continue;
            }

            if (IsSliceValidatorAttribute(reader, attribute))
            {
                try
                {
                    var (requestType, validatorType) = DecodeValidator(attribute);
                    if (!string.IsNullOrWhiteSpace(requestType) && !string.IsNullOrWhiteSpace(validatorType))
                    {
                        if (!validatorsByRequestType.TryGetValue(requestType, out var validators))
                        {
                            validators = [];
                            validatorsByRequestType.Add(requestType, validators);
                        }

                        validators.Add(validatorType);
                    }
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                continue;
            }

            if (!IsSliceRouteAttribute(reader, attribute))
            {
                continue;
            }

            SliceRouteInfo? route;
            try
            {
                route = DecodeRoute(attribute, sourceAssemblyName);
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

        return new GeneratedAssemblyRouteMetadata([.. routes.Select(route =>
        {
            var validators = route.RequestType is not null && validatorsByRequestType.TryGetValue(route.RequestType, out var routeValidators)
                ? routeValidators.Distinct(StringComparer.Ordinal).OrderBy(static type => type, StringComparer.Ordinal).ToArray()
                : [];
            return route with { ValidatorTypes = validators };
        })], lambdaHandlerTypeName);
    }

    private static PrimaryAssemblyMetadata ReadPrimaryAssemblyMetadata(FileInfo assemblyFile)
    {
        var referencedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aggregatedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = new FileStream(assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return new PrimaryAssemblyMetadata(referencedAssemblyNames, aggregatedAssemblyNames);
            }

            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.AssemblyReferences)
            {
                referencedAssemblyNames.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
            }

            foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (!IsSliceAggregatedFeatureAssemblyAttribute(reader, attribute))
                {
                    continue;
                }

                string? assemblyName;
                try
                {
                    assemblyName = DecodeAggregatedAssemblyName(attribute);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    aggregatedAssemblyNames.Add(assemblyName);
                }
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

        return new PrimaryAssemblyMetadata(referencedAssemblyNames, aggregatedAssemblyNames);
    }

    private static string? DecodeAggregatedAssemblyName(CustomAttribute attribute)
    {
        var value = attribute.DecodeValue(StringAttributeTypeProvider.Instance);
        return value.FixedArguments.Length == 0 ? null : GetString(value.FixedArguments[0]);
    }

    private static (string? RequestType, string? ValidatorType) DecodeValidator(CustomAttribute attribute)
    {
        var value = attribute.DecodeValue(StringAttributeTypeProvider.Instance);
        return value.FixedArguments.Length < 2
            ? (null, null)
            : (TrimGlobalPrefix(GetString(value.FixedArguments[0])), TrimGlobalPrefix(GetString(value.FixedArguments[1])));
    }

    private static bool IsSliceRouteAttribute(MetadataReader reader, CustomAttribute attribute)
        => IsAttribute(reader, attribute, "SliceFx", "SliceFeatureRouteAttribute", "SliceFx.Core");

    private static bool IsSliceValidatorAttribute(MetadataReader reader, CustomAttribute attribute)
        => IsAttribute(reader, attribute, "SliceFx", "SliceFeatureValidatorAttribute", "SliceFx.Core");

    private static bool IsSliceAggregatedFeatureAssemblyAttribute(MetadataReader reader, CustomAttribute attribute)
        => IsAttribute(reader, attribute, "SliceFx", "SliceAggregatedFeatureAssemblyAttribute", "SliceFx.Core");

    private static bool IsLambdaFunctionPerFeatureModuleAttribute(MetadataReader reader, CustomAttribute attribute)
        => IsAttribute(reader, attribute, "SliceFx.Lambda.FunctionPerFeature", "LambdaFunctionPerFeatureModuleAttribute", "SliceFx.Lambda.FunctionPerFeature");

    private static bool IsAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        string attributeNamespace,
        string attributeName,
        string assemblyName)
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
        return reader.StringComparer.Equals(type.Namespace, attributeNamespace)
               && reader.StringComparer.Equals(type.Name, attributeName)
               && type.ResolutionScope.Kind == HandleKind.AssemblyReference
               && reader.StringComparer.Equals(
                  reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name,
                  assemblyName);
    }

    private static string? ReadLambdaFunctionPerFeatureHandlerTypeName(MetadataReader reader)
    {
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsLambdaFunctionPerFeatureModuleAttribute(reader, attribute))
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

    private static SliceRouteInfo? DecodeRoute(CustomAttribute attribute, string sourceAssemblyName)
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
        var manifestSchemaVersion = GetString(args[17]);
        if (string.IsNullOrWhiteSpace(manifestSchemaVersion))
        {
            throw new InvalidRouteManifestException("manifest schema version is missing");
        }

        if (manifestSchemaVersion != SliceRouteManifestSchema.CurrentVersion)
        {
            throw new UnsupportedRouteManifestSchemaException(manifestSchemaVersion);
        }

        var filters = SplitLines(GetString(args[10]));
        var parameters = ReadParameters(GetString(args[11]));
        var lambdaStatus = GetString(args[12]);
        var lambdaReason = GetString(args[13]);
        var lambdaHandlerAssembly = GetString(args[14]);
        var lambdaHandlerType = TrimGlobalPrefix(GetString(args[15]));
        var lambdaHandlerMethod = GetString(args[16]);
        var wasiStatus = GetString(args[18]);
        var wasiReason = GetString(args[19]);
        var lambdaArtifactId = GetString(args[20]);
        var lambdaArtifactLayout = GetString(args[21]);
        var lambdaArtifactCodeUri = GetString(args[22]);
        var lambdaBootstrapMode = GetString(args[23]);
        var lambdaRuntimeIdentifier = GetString(args[24]);

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
            lambdaArtifactId,
            lambdaArtifactLayout,
            lambdaArtifactCodeUri,
            lambdaBootstrapMode,
            lambdaRuntimeIdentifier,
            manifestSchemaVersion,
            wasiStatus,
            wasiReason,
            HasGeneratedMetadata: true,
            SourceAssemblyName: sourceAssemblyName);
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
            if (parts.Length != 5 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                continue;
            }

            parameters.Add(new SliceRouteParameter(
                DecodeManifestField(parts[0]),
                DecodeManifestField(parts[1]),
                IsNullable: parts[2] == "N",
                BindingSource: parts[3].Length > 0 ? DecodeManifestField(parts[3]) : null,
                BindingName: parts[4].Length > 0 ? DecodeManifestField(parts[4]) : null));
        }

        return [.. parameters];
    }

    private static string DecodeManifestField(string value)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            throw new InvalidRouteManifestException("parameter metadata contains an invalid encoded field");
        }
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

internal sealed record GeneratedRouteDiscovery(
    bool Found,
    SliceRouteInfo[] Routes,
    bool HasLambdaFunctionPerFeatureHandlers,
    string[] AggregatedSourceAssemblyNames);

internal sealed record GeneratedAssemblyRouteMetadata(SliceRouteInfo[] Routes, string? LambdaFunctionPerFeatureHandlerTypeName);

internal sealed record PrimaryAssemblyMetadata(
    HashSet<string> ReferencedAssemblyNames,
    HashSet<string> AggregatedAssemblyNames);

internal sealed class UnsupportedRouteManifestSchemaException(string schemaVersion) : Exception
{
    internal string SchemaVersion { get; } = schemaVersion;
}

internal sealed class InvalidRouteManifestException(string message) : Exception(message);
