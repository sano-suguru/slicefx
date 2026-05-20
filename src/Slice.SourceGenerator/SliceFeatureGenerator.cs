using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Slice.SourceGenerator;

/// <summary>
/// Generates Slice endpoint registration code for discovered feature types.
/// </summary>
[Generator]
public sealed class SliceFeatureGenerator : IIncrementalGenerator
{
    private static readonly char[] s_spaceSeparator = [' '];

    /// <summary>
    /// Initializes the incremental source generator pipeline.
    /// </summary>
    /// <param name="context">The initialization context used to register generator steps.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var features = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Slice.FeatureAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: TransformFeature)
            .WithTrackingName("SliceFeatureModels");

        // Report diagnostics without blocking the model pipeline.
        var diagnostics = features
            .Where(static r => r.Diagnostic is not null)
            .Select(static (r, _) => r.Diagnostic!);

        context.RegisterSourceOutput(diagnostics, static (spc, d) => spc.ReportDiagnostic(d));

        var validModels = features
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .Collect();

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName ?? "Unknown");

        // Only emit ASP.NET registration when ASP.NET Core is referenced by the target project.
        var hasAspNetRef = context.CompilationProvider
            .Select(static (c, _) =>
                c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IResult") is not null);

        // Only emit Workers code when Slice.Workers is referenced by the target project.
        var hasWorkersRef = context.CompilationProvider
            .Select(static (c, _) =>
                c.GetTypeByMetadataName("Slice.Workers.Routing.WorkerRouteTable") is not null);

        context.RegisterSourceOutput(
            validModels.Combine(assemblyName).Combine(hasAspNetRef).Combine(hasWorkersRef).Combine(context.CompilationProvider),
            static (spc, pair) =>
            {
                var ((((models, asmName), emitAspNet), emitWorkers), compilation) = pair;
                if (models.IsEmpty)
                {
                    return;
                }

                var duplicateDiagnostics = FindDuplicateEndpointNameDiagnostics(models);
                foreach (var diagnostic in duplicateDiagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                if (!duplicateDiagnostics.IsEmpty)
                {
                    return;
                }

                var manifestSource = RouteManifestEmitter.Emit(models, asmName);
                spc.AddSource(
                    $"{asmName}.SliceRouteManifest.g.cs",
                    Microsoft.CodeAnalysis.Text.SourceText.From(manifestSource, Encoding.UTF8));

                if (emitAspNet)
                {
                    var source = RegistrationEmitter.Emit(models, asmName);
                    spc.AddSource(
                        $"{asmName}.SliceRegistrations.g.cs",
                        Microsoft.CodeAnalysis.Text.SourceText.From(source, Encoding.UTF8));
                }

                if (!emitWorkers)
                {
                    return;
                }

                var workersJsonContextFqn = FindWorkersJsonContext(models, compilation);
                var (workersSource, workersDiagnostics) = WorkersRegistrationEmitter.Emit(models, asmName, workersJsonContextFqn);
                foreach (var d in workersDiagnostics)
                {
                    spc.ReportDiagnostic(d);
                }

                spc.AddSource(
                    $"{asmName}.SliceWorkersRegistrations.g.cs",
                    Microsoft.CodeAnalysis.Text.SourceText.From(workersSource, Encoding.UTF8));
            });
    }

    // ---------------------------------------------------------------------------
    // Transform
    // ---------------------------------------------------------------------------

    private static FeatureResult TransformFeature(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol featureType)
        {
            return default;
        }

        var attrData = ctx.Attributes.FirstOrDefault();
        if (attrData is null || attrData.ConstructorArguments.Length == 0)
        {
            return default;
        }

        var routeArg = attrData.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(routeArg))
        {
            return default;
        }

        var parts = routeArg!.Split(s_spaceSeparator, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return FeatureResult.Error(Diagnostic.Create(
                SliceDiagnostics.InvalidRouteFormat,
                featureType.Locations.Length > 0 ? featureType.Locations[0] : null,
                featureType.Name, routeArg));
        }

        var httpMethod = parts[0].ToUpperInvariant();
        var pattern = parts[1].Trim();

        string? tag = null;
        string? summary = null;
        foreach (var kv in attrData.NamedArguments)
        {
            if (kv.Key == "Tag")
            {
                tag = kv.Value.Value as string;
            }
            else if (kv.Key == "Summary")
            {
                summary = kv.Value.Value as string;
            }
        }

        var tagInferred = tag is null;
        tag ??= InferTag(featureType);
        var endpointName = $"{tag}.{featureType.Name}";

        // Find Handle method.
        IMethodSymbol? handle = null;
        foreach (var member in featureType.GetMembers("Handle"))
        {
            if (member is IMethodSymbol m) { handle = m; break; }
        }

        if (handle is null)
        {
            return FeatureResult.Error(Diagnostic.Create(
                SliceDiagnostics.MissingHandleMethod,
                featureType.Locations.Length > 0 ? featureType.Locations[0] : null,
                featureType.Name));
        }

        if (!handle.IsStatic || handle.DeclaredAccessibility != Accessibility.Public)
        {
            return FeatureResult.Error(Diagnostic.Create(
                SliceDiagnostics.HandleNotPublicStatic,
                handle.Locations.Length > 0 ? handle.Locations[0] : null,
                featureType.Name));
        }

        ct.ThrowIfCancellationRequested();

        // Serialise params as "typeFqn|name|K;..." where K = 'I' (interface/abstract) or 'C' (concrete).
        var paramParts = new string[handle.Parameters.Length];
        for (var i = 0; i < handle.Parameters.Length; i++)
        {
            var p = handle.Parameters[i];
            var kind = (p.Type.TypeKind == TypeKind.Interface
                        || (p.Type is INamedTypeSymbol nt && nt.IsAbstract)) ? "I" : "C";
            paramParts[i] = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" + p.Name + "|" + kind;
        }
        var serializedParams = string.Join(";", paramParts);

        // Collect filter FQNs in declaration order.
        var filterParts = new List<string>();
        foreach (var a in featureType.GetAttributes())
        {
            if (a.AttributeClass is { IsGenericType: true } ac
                && ac.OriginalDefinition.Name == "FilterAttribute"
                && ac.ContainingNamespace?.ToDisplayString() == "Slice"
                && ac.TypeArguments.Length == 1)
            {
                filterParts.Add(ac.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }
        var serializedFilters = string.Join(";", filterParts);
        var (serializedValidationRules, requiresReflectionValidation) = CreateValidationRules(featureType, handle);

        var model = new FeatureModel(
            featureType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            featureType.Name,
            tag,
            endpointName,
            httpMethod,
            pattern,
            string.IsNullOrWhiteSpace(summary) ? null : summary,
            handle.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            serializedParams,
            serializedFilters,
            serializedValidationRules,
            requiresReflectionValidation);

        return tagInferred && tag == "Default"
            ? new FeatureResult(model, Diagnostic.Create(
                SliceDiagnostics.TagInferenceFallback,
                featureType.Locations.Length > 0 ? featureType.Locations[0] : null,
                featureType.Name))
            : new FeatureResult(model, null);
    }

    private static (string SerializedRules, bool RequiresReflectionValidation) CreateValidationRules(
        INamedTypeSymbol featureType,
        IMethodSymbol handle)
    {
        var requestType = featureType.GetTypeMembers("Request").FirstOrDefault();
        if (requestType is null || !handle.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, requestType)))
        {
            return (string.Empty, RequiresReflectionValidation: false);
        }

        var requiresReflectionValidation =
            requestType.GetAttributes().Any(IsValidationAttribute)
            || ImplementsIValidatableObject(requestType);

        var primaryCtorParams = requestType.InstanceConstructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault()
            ?.Parameters
            .Where(p => p.Name is not null)
            .ToDictionary(p => p.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, IParameterSymbol>(StringComparer.Ordinal);

        var rules = new List<string>();
        foreach (var property in requestType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic || property.GetMethod is null)
            {
                continue;
            }

            var attributes = property.GetAttributes().Where(IsValidationAttribute).ToList();
            if (primaryCtorParams.TryGetValue(property.Name, out var matchingParameter))
            {
                attributes.AddRange(matchingParameter.GetAttributes().Where(IsValidationAttribute));
            }

            foreach (var attribute in attributes)
            {
                var rule = TryCreateValidationRule(property, attribute);
                if (rule is null)
                {
                    requiresReflectionValidation = true;
                    continue;
                }

                rules.Add(rule);
            }
        }

        return requiresReflectionValidation
            ? (string.Empty, RequiresReflectionValidation: true)
            : (string.Join("\n", rules), RequiresReflectionValidation: false);
    }

    private static bool ImplementsIValidatableObject(INamedTypeSymbol type)
    {
        foreach (var i in type.AllInterfaces)
        {
            if (i.ToDisplayString() == "System.ComponentModel.DataAnnotations.IValidatableObject")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidationAttribute(AttributeData attribute)
        => InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute");

    private static bool InheritsFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryCreateValidationRule(IPropertySymbol property, AttributeData attribute)
    {
        if (HasCustomErrorMessage(attribute))
        {
            return null;
        }

        var propertyName = property.Name;
        var attributeName = attribute.AttributeClass?.ToDisplayString();
        if (attributeName == "System.ComponentModel.DataAnnotations.RequiredAttribute")
        {
            var allowEmptyStrings = false;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "AllowEmptyStrings" && namedArgument.Value.Value is bool value)
                {
                    allowEmptyStrings = value;
                }
            }

            return $"{propertyName}|Required|{(allowEmptyStrings ? "true" : "false")}";
        }

        if (attributeName == "System.ComponentModel.DataAnnotations.StringLengthAttribute"
            && attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is int maximumLength)
        {
            if (property.Type.SpecialType != SpecialType.System_String)
            {
                return null;
            }

            var minimumLength = 0;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "MinimumLength" && namedArgument.Value.Value is int value)
                {
                    minimumLength = value;
                }
            }

            return $"{propertyName}|StringLength|{minimumLength}|{maximumLength}";
        }

        if (attributeName == "System.ComponentModel.DataAnnotations.MinLengthAttribute"
            && attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is int length)
        {
            var lengthMember = TryGetLengthMember(property.Type);
            if (lengthMember is null)
            {
                return null;
            }

            return $"{propertyName}|MinLength|{length}|{lengthMember}";
        }

        return null;
    }

    private static string? TryGetLengthMember(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String || type is IArrayTypeSymbol)
        {
            return "Length";
        }

        if (!type.IsReferenceType)
        {
            return null;
        }

        foreach (var member in type.GetMembers("Count").OfType<IPropertySymbol>())
        {
            if (!member.IsStatic
                && member.DeclaredAccessibility == Accessibility.Public
                && member.GetMethod is not null
                && member.Type.SpecialType == SpecialType.System_Int32)
            {
                return "Count";
            }
        }

        return null;
    }

    private static bool HasCustomErrorMessage(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key is "ErrorMessage" or "ErrorMessageResourceName" or "ErrorMessageResourceType")
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindWorkersJsonContext(ImmutableArray<FeatureModel> models, Compilation compilation)
    {
        foreach (var model in models)
        {
            var typeName = model.FullyQualifiedTypeName;
            if (typeName.StartsWith("global::", StringComparison.Ordinal))
            {
                typeName = typeName.Substring("global::".Length);
            }

            var featuresIndex = typeName.IndexOf(".Features.", StringComparison.Ordinal);
            if (featuresIndex < 0)
            {
                continue;
            }

            var rootNamespace = typeName.Substring(0, featuresIndex);
            var candidate = rootNamespace + ".WorkerJsonContext";
            if (compilation.GetTypeByMetadataName(candidate) is not null)
            {
                return "global::" + candidate;
            }
        }

        return null;
    }

    // Mirrors SliceExtensions.InferTag: same IndexOf(".Features.") string logic.
    private static string InferTag(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        var idx = ns.IndexOf(".Features.", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var rest = ns.Substring(idx + ".Features.".Length);
            var dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest.Substring(0, dot);
        }
        return "Default";
    }

    private static ImmutableArray<Diagnostic> FindDuplicateEndpointNameDiagnostics(ImmutableArray<FeatureModel> models)
    {
        var seen = new Dictionary<string, FeatureModel>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var model in models)
        {
            if (seen.TryGetValue(model.EndpointName, out var existing))
            {
                diagnostics.Add(Diagnostic.Create(
                    SliceDiagnostics.DuplicateEndpointName,
                    location: null,
                    model.EndpointName,
                    existing.FullyQualifiedTypeName,
                    model.FullyQualifiedTypeName));
                continue;
            }

            seen.Add(model.EndpointName, model);
        }

        return diagnostics.ToImmutable();
    }
}

internal readonly struct FeatureResult(FeatureModel? model, Diagnostic? diagnostic)
{
    /// <summary>
    /// Gets the discovered feature model when the feature is valid.
    /// </summary>
    public FeatureModel? Model { get; } = model;

    /// <summary>
    /// Gets the diagnostic reported for an invalid or notable feature.
    /// </summary>
    public Diagnostic? Diagnostic { get; } = diagnostic;

    /// <summary>
    /// Creates a feature result that contains only a diagnostic.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to report.</param>
    /// <returns>A feature result containing the diagnostic.</returns>
    public static FeatureResult Error(Diagnostic diagnostic) => new(null, diagnostic);
}
