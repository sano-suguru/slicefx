using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SliceFx.Cli.Internal;

internal static class RouteCatalog
{
    internal const string PortabilityPortable = "portable";
    internal const string PortabilityPartial = "partial";
    internal const string PortabilityAspNetOnly = "aspnet-only";
    internal const string PortabilityUnknown = "unknown";
    internal const string LambdaEligible = "eligible";
    internal const string LambdaIneligible = "ineligible";
    internal const string LambdaUnknown = "unknown";
    internal const string LambdaArtifactIdShared = "shared";
    internal const string LambdaArtifactLayoutShared = "shared";
    internal const string LambdaArtifactCodeUriShared = "publish";
    internal const string LambdaBootstrapModeGeneratedHandler = "generated-handler";

    private static readonly HashSet<string> s_simpleTypes = new(StringComparer.Ordinal)
    {
        "string", "Guid",
        "int", "long", "short", "uint", "ulong", "ushort",
        "bool", "double", "float", "decimal", "byte", "char",
        "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Uri",
        "System.String", "System.Guid",
        "System.Int32", "System.Int64", "System.Int16",
        "System.UInt32", "System.UInt64", "System.UInt16",
        "System.Boolean", "System.Double", "System.Single",
        "System.Decimal", "System.Byte", "System.Char",
        "System.DateTime", "System.DateTimeOffset",
        "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.Uri",
    };

    internal static SliceRouteInfo[] Discover(ProjectContext ctx)
        => DiscoverDetailed(ctx).Routes;

    internal static RouteCatalogDiscovery DiscoverDetailed(ProjectContext ctx)
    {
        var generatedRoutes = GeneratedRouteCatalog.Discover(ctx);
        return generatedRoutes.Found
            ? new RouteCatalogDiscovery(generatedRoutes.Routes, HasGeneratedMetadata: true, generatedRoutes.HasLambdaFunctionPerFeatureHandlers, generatedRoutes.AggregatedSourceAssemblyNames)
            : new RouteCatalogDiscovery(DiscoverFromSource(ctx), HasGeneratedMetadata: false, HasLambdaFunctionPerFeatureHandlers: false, []);
    }

    private static SliceRouteInfo[] DiscoverFromSource(ProjectContext ctx)
    {
        var featuresDir = Path.Combine(ctx.ProjectDirectory.FullName, "Features");
        if (!Directory.Exists(featuresDir))
        {
            throw new CliException($"Features directory not found: {featuresDir}");
        }

        return [.. Directory.EnumerateFiles(featuresDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => ReadRoutes(file, ctx.AssemblyName))
            .OrderBy(static route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(static route => route.Method, StringComparer.Ordinal)
            .ThenBy(static route => route.EndpointName, StringComparer.Ordinal)];
    }

    internal static void WriteAggregatedRouteNotice(RouteCatalogDiscovery discovery)
    {
        if (discovery.AggregatedSourceAssemblyNames.Length == 0)
        {
            return;
        }

        Console.Error.WriteLine(
            $"Slice routes include aggregated referenced assemblies: {string.Join(", ", discovery.AggregatedSourceAssemblyNames)}");
    }

    private static IEnumerable<SliceRouteInfo> ReadRoutes(string file, string sourceAssemblyName)
    {
        var source = File.ReadAllText(file);
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (!IsPublicStaticClass(classDeclaration))
            {
                continue;
            }

            var featureAttribute = FindAttribute(classDeclaration.AttributeLists, "Feature");
            if (featureAttribute is null)
            {
                continue;
            }

            var route = ReadPositionalStringArgument(featureAttribute, 0);
            if (route is null)
            {
                continue;
            }

            var parts = route.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var @namespace = FindNamespace(classDeclaration);
            var tag = ReadNamedStringArgument(featureAttribute, "Tag") ?? InferTag(@namespace);
            var summary = ReadNamedStringArgument(featureAttribute, "Summary");
            var handleMethod = FindHandleMethod(classDeclaration);
            var returnType = handleMethod is null ? "" : NormalizeWhitespace(handleMethod.ReturnType.ToString());
            var parameters = handleMethod is null ? [] : ReadParameters(handleMethod.ParameterList);
            var requestType = FindRequestType(parameters);
            var filters = ReadFilters(classDeclaration.AttributeLists);
            var (portability, portabilityReason) = ClassifyPortability(returnType, filters);
            var featureName = classDeclaration.Identifier.ValueText;
            var lambda = ClassifyLambdaFunctionPerFeature(returnType, filters, parameters, parts[1]);

            yield return new SliceRouteInfo(
                parts[0].ToUpperInvariant(),
                parts[1],
                @namespace,
                featureName,
                tag,
                $"{tag}.{featureName}",
                summary,
                requestType,
                returnType,
                portability,
                portabilityReason,
                filters,
                parameters,
                lambda.Status,
                lambda.Reason,
                SourceAssemblyName: sourceAssemblyName);
        }
    }

    private static bool IsPublicStaticClass(ClassDeclarationSyntax classDeclaration)
        => classDeclaration.Modifiers.Any(SyntaxKind.PublicKeyword)
           && classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);

    private static string FindNamespace(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                return namespaceDeclaration.Name.ToString();
            }
        }

        return "";
    }

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string name)
        => attributeLists
            .SelectMany(static list => list.Attributes)
            .FirstOrDefault(attribute => IsAttribute(attribute, name));

    private static bool IsAttribute(AttributeSyntax attribute, string name)
        => string.Equals(NormalizeAttributeName(GetAttributeIdentifier(attribute.Name)), name, StringComparison.Ordinal);

    private static string GetAttributeIdentifier(NameSyntax name)
        => name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetAttributeIdentifier(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => GetAttributeIdentifier(aliasQualified.Name),
            _ => name.ToString(),
        };

    private static string NormalizeAttributeName(string name)
        => name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;

    private static string? ReadPositionalStringArgument(AttributeSyntax attribute, int index)
    {
        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments is null)
        {
            return null;
        }

        var positionalArguments = arguments.Value
            .Where(static argument => argument.NameEquals is null && argument.NameColon is null)
            .ToArray();
        return positionalArguments.Length <= index
            ? null
            : ReadStringLiteral(positionalArguments[index].Expression);
    }

    private static string? ReadNamedStringArgument(AttributeSyntax attribute, string name)
    {
        foreach (var argument in attribute.ArgumentList?.Arguments ?? [])
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == name)
            {
                return ReadStringLiteral(argument.Expression);
            }
        }

        return null;
    }

    private static string? ReadStringLiteral(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    private static MethodDeclarationSyntax? FindHandleMethod(ClassDeclarationSyntax classDeclaration)
        => classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(static method =>
                method.Identifier.ValueText == "Handle"
                && method.Modifiers.Any(SyntaxKind.PublicKeyword)
                && method.Modifiers.Any(SyntaxKind.StaticKeyword));

    private static string[] ReadFilters(SyntaxList<AttributeListSyntax> attributeLists)
        => [.. attributeLists
            .SelectMany(static list => list.Attributes)
            .Select(ReadFilter)
            .Where(static filter => filter is not null)
            .Cast<string>()];

    private static string? ReadFilter(AttributeSyntax attribute)
    {
        var attributeName = attribute.Name switch
        {
            GenericNameSyntax generic => generic,
            QualifiedNameSyntax { Right: GenericNameSyntax generic } => generic,
            AliasQualifiedNameSyntax { Name: GenericNameSyntax generic } => generic,
            _ => null,
        };
        if (attributeName is not { TypeArgumentList.Arguments.Count: 1 }
            || NormalizeAttributeName(attributeName.Identifier.ValueText) != "Filter")
        {
            return null;
        }

        return attributeName.TypeArgumentList.Arguments[0].ToString().Trim();
    }

    private static SliceRouteParameter[] ReadParameters(ParameterListSyntax parameterList)
        => [.. parameterList.Parameters
            .Select(ReadParameter)
            .Where(static parameter => parameter is not null)
            .Cast<SliceRouteParameter>()];

    private static SliceRouteParameter? ReadParameter(ParameterSyntax parameter)
    {
        if (parameter.Type is null)
        {
            return null;
        }

        var type = NormalizeWhitespace(parameter.Type.ToString());
        var (bindingSource, bindingName) = ReadParameterBinding(parameter);
        return new SliceRouteParameter(
            type,
            parameter.Identifier.ValueText,
            IsNullableTypeName(type),
            bindingSource,
            bindingName);
    }

    private static (string? Source, string? Name) ReadParameterBinding(ParameterSyntax parameter)
    {
        string? source = null;
        string? name = null;
        foreach (var attribute in parameter.AttributeLists.SelectMany(static list => list.Attributes))
        {
            var attributeName = NormalizeAttributeName(GetAttributeIdentifier(attribute.Name));
            source ??= attributeName switch
            {
                "FromQuery" => "query",
                "FromRoute" => "route",
                "FromHeader" => "header",
                "FromBody" => "body",
                _ => null,
            };

            var nameArgument = attribute.ArgumentList?.Arguments
                .FirstOrDefault(static argument => argument.NameEquals?.Name.Identifier.ValueText == "Name");
            if (nameArgument is not null)
            {
                name = ReadStringLiteral(nameArgument.Expression);
            }
        }

        return (source, name);
    }

    private static string? FindRequestType(SliceRouteParameter[] parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Type == "Request" || parameter.Type.EndsWith(".Request", StringComparison.Ordinal))
            {
                return parameter.Type;
            }
        }

        return null;
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

    internal static string NormalizeWhitespace(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static (string Status, string? Reason) ClassifyPortability(string returnType, string[] filters)
    {
        if (returnType.Length == 0)
        {
            return (PortabilityUnknown, "Handle method not found");
        }

        if (returnType.Contains("IResult", StringComparison.Ordinal))
        {
            return (PortabilityAspNetOnly, "returns ASP.NET IResult");
        }

        return filters.Length > 0
            ? (PortabilityPartial, "endpoint filters do not run in the WASI path")
            : (PortabilityPortable, null);
    }

    internal static RouteCapability ClassifyLambdaFunctionPerFeature(
        string returnType,
        string[] filters,
        SliceRouteParameter[] parameters,
        string pattern)
    {
        if (returnType.Length == 0)
        {
            return new RouteCapability(LambdaUnknown, "Handle method not found");
        }

        if (returnType.Contains("IResult", StringComparison.Ordinal))
        {
            return new RouteCapability(LambdaIneligible, "returns ASP.NET IResult");
        }

        if (filters.Length > 0)
        {
            return new RouteCapability(LambdaIneligible, "endpoint filters require the ASP.NET endpoint filter pipeline");
        }

        foreach (var parameter in parameters)
        {
            if (parameter.Type is "CancellationToken" or "System.Threading.CancellationToken"
                || parameter.Type == "Request"
                || parameter.Type.EndsWith(".Request", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsRouteParam(parameter.Name, pattern)
                && !IsSimpleType(parameter.Type))
            {
                return new RouteCapability(
                    LambdaIneligible,
                    $"route parameter '{parameter.Name}' has unsupported type '{parameter.Type}'");
            }
        }

        return new RouteCapability(LambdaEligible, null);
    }

    private static bool IsSimpleType(string type)
        => s_simpleTypes.Contains(type)
           || (type.EndsWith('?') && s_simpleTypes.Contains(type[..^1]))
           || IsNullableType(type);

    private static bool IsNullableType(string type)
    {
        const string nullablePrefix = "System.Nullable<";
        if (!type.StartsWith(nullablePrefix, StringComparison.Ordinal)
            || !type.EndsWith('>'))
        {
            return false;
        }

        return s_simpleTypes.Contains(type[nullablePrefix.Length..^1]);
    }

    private static bool IsNullableTypeName(string type)
        => type.EndsWith('?') || IsNullableType(type);

    private static bool IsRouteParam(string name, string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '{')
            {
                continue;
            }

            if (i + 1 < pattern.Length && pattern[i + 1] == '{')
            {
                i++;
                continue;
            }

            var end = pattern.IndexOf('}', i + 1);
            if (end < 0)
            {
                return false;
            }

            var parameterName = NormalizeRouteParameterName(pattern[(i + 1)..end]);
            if (string.Equals(parameterName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            i = end;
        }

        return false;
    }

    private static string NormalizeRouteParameterName(string token)
    {
        token = token.TrimStart('*');
        var terminator = token.IndexOfAny([':', '?', '=']);
        return terminator >= 0 ? token[..terminator] : token;
    }
}

internal sealed record SliceRouteInfo(
    string Method,
    string Pattern,
    string Namespace,
    string FeatureName,
    string Tag,
    string EndpointName,
    string? Summary,
    string? RequestType,
    string ReturnType,
    string Portability,
    string? PortabilityReason,
    string[] Filters,
    SliceRouteParameter[] Parameters,
    string? LambdaFunctionPerFeatureStatus = null,
    string? LambdaFunctionPerFeatureReason = null,
    string? LambdaFunctionPerFeatureHandlerAssembly = null,
    string? LambdaFunctionPerFeatureHandlerType = null,
    string? LambdaFunctionPerFeatureHandlerMethod = null,
    string? LambdaFunctionPerFeatureArtifactId = null,
    string? LambdaFunctionPerFeatureArtifactLayout = null,
    string? LambdaFunctionPerFeatureArtifactCodeUri = null,
    string? LambdaFunctionPerFeatureBootstrapMode = null,
    string? LambdaFunctionPerFeatureRuntimeIdentifier = null,
    string? ManifestSchemaVersion = null,
    string? WasiDispatchStatus = null,
    string? WasiDispatchReason = null,
    bool HasGeneratedMetadata = false,
    string? SourceAssemblyName = null)
{
    internal string FeatureType => $"{Namespace}.{FeatureName}";

    internal string? LambdaFunctionPerFeatureHandler
        => LambdaFunctionPerFeatureHandlerAssembly is null ||
           LambdaFunctionPerFeatureHandlerType is null ||
           LambdaFunctionPerFeatureHandlerMethod is null
            ? null
            : $"{LambdaFunctionPerFeatureHandlerAssembly}::{LambdaFunctionPerFeatureHandlerType}::{LambdaFunctionPerFeatureHandlerMethod}";
}

internal sealed record SliceRouteParameter(
    string Type,
    string Name,
    bool IsNullable = false,
    string? BindingSource = null,
    string? BindingName = null)
{
    internal string WireName => string.IsNullOrWhiteSpace(BindingName) ? Name : BindingName;
}

internal sealed record RouteCatalogDiscovery(
    SliceRouteInfo[] Routes,
    bool HasGeneratedMetadata,
    bool HasLambdaFunctionPerFeatureHandlers,
    string[] AggregatedSourceAssemblyNames);
