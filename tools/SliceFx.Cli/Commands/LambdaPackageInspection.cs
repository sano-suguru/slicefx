using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Logging.StructuredLogger;
using SliceFx.Cli.Internal;

namespace SliceFx.Cli.Commands;

internal static class LambdaPackageWarningInspector
{
    internal static LambdaPackageWarning[] ReadWarnings(string binlogPath, string projectRoot, string packageRoot)
    {
        var build = BinaryLog.ReadBuild(binlogPath);
        return [.. build.FindChildrenRecursive<Warning>(static _ => true)
            .Select(warning => CreateWarning(warning, binlogPath, projectRoot, packageRoot))
            .OrderBy(static warning => warning.Code, StringComparer.Ordinal)
            .ThenBy(static warning => warning.Project, StringComparer.Ordinal)
            .ThenBy(static warning => warning.File, StringComparer.Ordinal)
            .ThenBy(static warning => warning.Line)
            .ThenBy(static warning => warning.Message, StringComparer.Ordinal)];
    }

    internal static LambdaWarningBaselineEntry[] ReadBaseline(FileInfo baselineFile)
    {
        if (!baselineFile.Exists)
        {
            throw new CliException($"Warning baseline file not found: {baselineFile.FullName}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(baselineFile.FullName));
        var root = document.RootElement;
        var warningsElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("warnings", out var warnings)
                ? warnings
                : throw new CliException("Warning baseline JSON must be an array or an object with a 'warnings' array.");

        var entries = new List<LambdaWarningBaselineEntry>();
        foreach (var warning in warningsElement.EnumerateArray())
        {
            var hash = ReadString(warning, "messageHash");
            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new CliException("Warning baseline entry is missing required 'messageHash'.");
            }

            entries.Add(new LambdaWarningBaselineEntry(
                ReadString(warning, "code") ?? "",
                ReadString(warning, "project"),
                ReadString(warning, "file"),
                ReadInt(warning, "line"),
                hash,
                ReadString(warning, "message")));
        }

        return [.. entries];
    }

    private static LambdaPackageWarning CreateWarning(
        Warning warning,
        string binlogPath,
        string projectRoot,
        string packageRoot)
    {
        var code = string.IsNullOrWhiteSpace(warning.Code) ? "UNKNOWN" : warning.Code;
        var project = NormalizePath(warning.ProjectFile, projectRoot, packageRoot);
        var file = NormalizePath(warning.File, projectRoot, packageRoot);
        int? line = warning.LineNumber == 0 ? null : warning.LineNumber;
        int? column = warning.ColumnNumber == 0 ? null : warning.ColumnNumber;
        var message = warning.Text ?? "";
        var hash = ComputeHash(code, project, file, line);
        return new LambdaPackageWarning(
            code,
            project,
            file,
            line,
            column,
            message,
            hash,
            ToReportPath(binlogPath, packageRoot),
            "unmatched");
    }

    private static string ComputeHash(string code, string? project, string? file, int? line)
    {
        var identity = string.Join('\n', code, project ?? "", file ?? "", line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NormalizePath(string? path, string projectRoot, string packageRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        return IsUnder(fullPath, projectRoot)
            ? ToReportPath(fullPath, projectRoot)
            : ToReportPath(fullPath, packageRoot);
    }

    private static string ToReportPath(string path, string root)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsUnder(string path, string root)
        => Path.GetRelativePath(root, path) is var relative
           && !relative.StartsWith("..", StringComparison.Ordinal)
           && !Path.IsPathRooted(relative);

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}

internal static class LambdaPackageClosureInspector
{
    internal static LambdaPackageClosureInspection Inspect(
        SliceRouteInfo route,
        SliceRouteInfo[] allRoutes,
        string artifactDir,
        string wrapperBuildRoot,
        string packageRoot,
        bool skippedPublish)
    {
        if (skippedPublish)
        {
            return new LambdaPackageClosureInspection("skipped", true, [], [], [], [], []);
        }

        var mstatFiles = FindFiles(artifactDir, wrapperBuildRoot, ["*.mstat"], packageRoot);
        var mapFiles = FindFiles(artifactDir, wrapperBuildRoot, ["*.map", "*.map.xml"], packageRoot);
        var missing = new List<string>();
        if (mstatFiles.Length == 0)
        {
            missing.Add("NativeAOT mstat file was not produced.");
        }

        if (mapFiles.Length == 0)
        {
            missing.Add("NativeAOT map file was not produced.");
        }

        var typeIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reportPath in mstatFiles)
        {
            var file = Path.Combine(packageRoot, reportPath.Replace('/', Path.DirectorySeparatorChar));
            foreach (var typeName in ReadMstatTypeIdentities(file))
            {
                typeIdentities.Add(typeName);
            }
        }

        var forbidden = BuildForbiddenTypeIdentities(route, allRoutes);
        var hits = new List<LambdaPackageClosureHit>();
        foreach (var forbiddenType in forbidden)
        {
            var matchingType = FindMatchingType(typeIdentities, forbiddenType.TypeIdentity);
            if (matchingType is not null)
            {
                hits.Add(new LambdaPackageClosureHit(matchingType, forbiddenType.Reason, "mstat"));
            }
        }

        var status = missing.Count == 0 && hits.Count == 0 ? "passed" : "failed";
        return new LambdaPackageClosureInspection(
            status,
            status == "passed",
            mstatFiles,
            mapFiles,
            BuildAllowedTypeIdentities(route),
            [.. hits.OrderBy(static hit => hit.TypeIdentity, StringComparer.Ordinal)],
            [.. missing]);
    }

    private static string[] FindFiles(string artifactDir, string wrapperBuildRoot, string[] patterns, string packageRoot)
        => [.. new[] { artifactDir, wrapperBuildRoot }
            .Where(Directory.Exists)
            .SelectMany(root => patterns.SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => ToReportPath(path, packageRoot))
            .OrderBy(static path => path, StringComparer.Ordinal)];

    private static IEnumerable<string> ReadMstatTypeIdentities(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new CliException($"NativeAOT mstat file is missing metadata: {path}");
        }

        var reader = peReader.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            yield return JoinNamespaceAndName(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        foreach (var handle in reader.TypeReferences)
        {
            yield return GetTypeReferenceName(reader, handle);
        }
    }

    private static string? FindMatchingType(HashSet<string> typeIdentities, string forbiddenIdentity)
    {
        if (forbiddenIdentity.EndsWith('*'))
        {
            var prefix = forbiddenIdentity[..^1];
            return typeIdentities
                .Select(static type => type.Replace('+', '.'))
                .FirstOrDefault(type => type.StartsWith(prefix, StringComparison.Ordinal));
        }

        return typeIdentities.Contains(forbiddenIdentity)
            ? forbiddenIdentity
            : typeIdentities
                .Select(static type => type.Replace('+', '.'))
                .FirstOrDefault(type => string.Equals(type, forbiddenIdentity, StringComparison.Ordinal));
    }

    private static LambdaPackageClosureForbiddenType[] BuildForbiddenTypeIdentities(
        SliceRouteInfo route,
        SliceRouteInfo[] allRoutes)
    {
        var forbidden = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var other in allRoutes)
        {
            if (string.Equals(other.EndpointName, route.EndpointName, StringComparison.Ordinal))
            {
                continue;
            }

            Add(forbidden, other.FeatureType, $"sibling feature entrypoint '{other.EndpointName}'");
            foreach (var validatorType in other.ValidatorTypes ?? [])
            {
                Add(forbidden, validatorType, $"sibling validator '{other.EndpointName}'");
            }

            foreach (var root in GetFeatureOwnedJsonRoots(other))
            {
                Add(forbidden, root, $"sibling feature-owned JSON root '{other.EndpointName}'");
            }
        }

        Add(forbidden, "Microsoft.AspNetCore.Builder.WebApplication", "ASP.NET hosted bootstrap");
        Add(forbidden, "Microsoft.AspNetCore.Builder.WebApplicationBuilder", "ASP.NET hosted bootstrap");
        Add(forbidden, "SliceFx.Lambda.WebApplicationExtensions", "hosted Lambda adapter");
        Add(forbidden, "SliceFx.Lambda.WebApplicationBuilderExtensions", "hosted Lambda adapter");
        AddPrefix(forbidden, "SliceFx.Wasi.", "unrelated WASI satellite");
        AddPrefix(forbidden, "SliceFx.Testing.", "unrelated TestHost satellite");
        AddGeneratedRegistrationPrefixes(forbidden, route.LambdaFunctionPerFeatureHandlerType);
        foreach (var type in allRoutes.Select(static route => route.LambdaFunctionPerFeatureHandlerType).Where(static type => type is not null).Cast<string>())
        {
            if (!string.Equals(type, route.LambdaFunctionPerFeatureHandlerType, StringComparison.Ordinal))
            {
                Add(forbidden, type, "sibling generated Lambda handler");
            }
        }

        return [.. forbidden.Select(static pair => new LambdaPackageClosureForbiddenType(pair.Key, pair.Value))];
    }

    private static string[] BuildAllowedTypeIdentities(SliceRouteInfo route)
        => [.. new[]
            {
                route.FeatureType,
                route.LambdaFunctionPerFeatureHandlerType,
                route.RequestType,
            }
            .Concat(route.ValidatorTypes ?? [])
            .Concat(GetFeatureOwnedJsonRoots(route))
            .Where(static type => !string.IsNullOrWhiteSpace(type))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)];

    private static IEnumerable<string> GetFeatureOwnedJsonRoots(SliceRouteInfo route)
    {
        var featurePrefix = route.FeatureType + ".";
        foreach (var parameter in route.Parameters)
        {
            if (parameter.Type.StartsWith(featurePrefix, StringComparison.Ordinal))
            {
                yield return parameter.Type;
            }
        }

        var responseType = GetAwaitedReturnType(route.ReturnType);
        if (responseType is not null && responseType.StartsWith(featurePrefix, StringComparison.Ordinal))
        {
            yield return responseType;
        }
    }

    private static string? GetAwaitedReturnType(string returnType)
    {
        const string taskPrefix = "System.Threading.Tasks.Task<";
        const string valueTaskPrefix = "System.Threading.Tasks.ValueTask<";
        if (returnType.StartsWith(taskPrefix, StringComparison.Ordinal) && returnType.EndsWith('>'))
        {
            return returnType[taskPrefix.Length..^1];
        }

        if (returnType.StartsWith(valueTaskPrefix, StringComparison.Ordinal) && returnType.EndsWith('>'))
        {
            return returnType[valueTaskPrefix.Length..^1];
        }

        return returnType is "void" or "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
            ? null
            : returnType;
    }

    private static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope) + "." + name
            : JoinNamespaceAndName(reader.GetString(type.Namespace), name);
    }

    private static string JoinNamespaceAndName(string @namespace, string name)
        => string.IsNullOrWhiteSpace(@namespace) ? name : @namespace + "." + name;

    private static void Add(Dictionary<string, string> forbidden, string? type, string reason)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            forbidden.TryAdd(type.Replace('+', '.'), reason);
        }
    }

    private static void AddPrefix(Dictionary<string, string> forbidden, string prefix, string reason)
        => forbidden.TryAdd(prefix + "*", reason);

    private static void AddGeneratedRegistrationPrefixes(Dictionary<string, string> forbidden, string? handlerType)
    {
        const string marker = "_SliceLambdaFunctionPerFeatureHandlers";
        if (string.IsNullOrWhiteSpace(handlerType))
        {
            return;
        }

        var markerIndex = handlerType.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return;
        }

        var generatedPrefix = handlerType[..markerIndex];
        AddPrefix(forbidden, generatedPrefix + "_SliceRegistrations", "generated ASP.NET Slice registration surface");
        AddPrefix(forbidden, generatedPrefix + "_SliceWasiRegistrations", "generated WASI Slice registration surface");
    }

    private static string ToReportPath(string path, string root)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private readonly record struct LambdaPackageClosureForbiddenType(string TypeIdentity, string Reason);
}

internal sealed record LambdaPackageWarning(
    string Code,
    string? Project,
    string? File,
    int? Line,
    int? Column,
    string Message,
    string MessageHash,
    string BinlogPath,
    string BaselineStatus);

internal sealed record LambdaWarningBaselineEntry(
    string Code,
    string? Project,
    string? File,
    int? Line,
    string MessageHash,
    string? Message);

internal sealed record LambdaWarningBaselineReport(
    string? Path,
    int CurrentWarningCount,
    int UnbaselinedWarningCount,
    int StaleBaselineCount,
    LambdaPackageWarning[] UnbaselinedWarnings,
    LambdaWarningBaselineEntry[] StaleEntries);

internal sealed record LambdaPackageClosureInspection(
    string Status,
    bool Passed,
    string[] MstatPaths,
    string[] MapPaths,
    string[] AllowedTypeIdentities,
    LambdaPackageClosureHit[] ForbiddenHits,
    string[] MissingFiles);

internal sealed record LambdaPackageClosureHit(
    string TypeIdentity,
    string Reason,
    string EvidenceSource);
