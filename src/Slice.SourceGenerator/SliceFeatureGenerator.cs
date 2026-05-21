using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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

        var referencedModules = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => FindReferencedSliceModules(pair.Left, pair.Right))
            .WithTrackingName("SliceReferencedModules");

        // Reduce Compilation/Options to cacheable primitives BEFORE the emit step so that
        // the final RegisterSourceOutput action does not take Compilation as a cache key.
        // Compilation changes on every keystroke; the derived bool/string? values are stable.
        var emitExtensions = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) =>
            {
                var role = GetGenerationRole(pair.Left, pair.Right);
                return role is SliceGenerationRole.Host or SliceGenerationRole.Both;
            })
            .WithTrackingName("SliceEmitExtensions");

        var workersJsonContextFqn = validModels
            .Combine(context.CompilationProvider)
            .Combine(hasWorkersRef)
            .Select(static (pair, _) => pair.Right ? FindWorkersJsonContext(pair.Left.Left, pair.Left.Right) : null)
            .WithTrackingName("SliceWorkersJsonContext");

        context.RegisterSourceOutput(
            validModels.Combine(assemblyName)
                .Combine(hasAspNetRef)
                .Combine(hasWorkersRef)
                .Combine(referencedModules)
                .Combine(emitExtensions)
                .Combine(workersJsonContextFqn),
            static (spc, pair) =>
            {
                var ((((((models, asmName), emitAspNet), emitWorkers), referencedModules), emitExtensions), workersJsonContextFqn) = pair;

                var duplicateDiagnostics = FindDuplicateEndpointNameDiagnostics(models, referencedModules);
                foreach (var diagnostic in duplicateDiagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                if (!duplicateDiagnostics.IsEmpty)
                {
                    return;
                }

                foreach (var diagnostic in FindFilterOrderDiagnostics(models))
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                var manifestSource = RouteManifestEmitter.Emit(models, asmName);
                spc.AddSource(
                    $"{asmName}.SliceRouteManifest.g.cs",
                    Microsoft.CodeAnalysis.Text.SourceText.From(manifestSource, Encoding.UTF8));

                if (emitAspNet)
                {
                    var source = RegistrationEmitter.Emit(models, asmName, referencedModules, emitExtensions);
                    spc.AddSource(
                        $"{asmName}.SliceRegistrations.g.cs",
                        Microsoft.CodeAnalysis.Text.SourceText.From(source, Encoding.UTF8));
                }

                if (!emitWorkers)
                {
                    return;
                }

                var (workersSource, workersDiagnostics) = WorkersRegistrationEmitter.Emit(
                    models,
                    asmName,
                    workersJsonContextFqn,
                    referencedModules,
                    emitExtensions);
                foreach (var d in workersDiagnostics)
                {
                    spc.ReportDiagnostic(d);
                }

                spc.AddSource(
                    $"{asmName}.SliceWorkersRegistrations.g.cs",
                    Microsoft.CodeAnalysis.Text.SourceText.From(workersSource, Encoding.UTF8));
            });
    }

    private static SliceGenerationRole GetGenerationRole(
        Compilation compilation,
        AnalyzerConfigOptionsProvider options)
    {
        if (options.GlobalOptions.TryGetValue("build_property.SliceRole", out var role))
        {
            if (string.Equals(role, "Host", StringComparison.OrdinalIgnoreCase))
            {
                return SliceGenerationRole.Host;
            }

            if (string.Equals(role, "Feature", StringComparison.OrdinalIgnoreCase))
            {
                return SliceGenerationRole.Feature;
            }

            if (string.Equals(role, "Both", StringComparison.OrdinalIgnoreCase))
            {
                return SliceGenerationRole.Both;
            }
        }

        return compilation.Options.OutputKind == OutputKind.DynamicallyLinkedLibrary
            ? SliceGenerationRole.Feature
            : SliceGenerationRole.Host;
    }

    private static ImmutableArray<ReferencedSliceModule> FindReferencedSliceModules(
        Compilation compilation,
        AnalyzerConfigOptionsProvider options)
    {
        var allowedAssemblies = GetReferencedAssemblyAllowList(options);
        if (allowedAssemblies.Count == 0 && !ShouldAggregateReferences(options))
        {
            return [];
        }

        var moduleAttribute = compilation.GetTypeByMetadataName("Slice.SliceFeatureModuleAttribute");
        if (moduleAttribute is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<ReferencedSliceModule>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
                     .OrderBy(static a => a.Identity.Name, StringComparer.Ordinal))
        {
            if (allowedAssemblies.Count > 0 && !allowedAssemblies.Contains(assembly.Identity.Name))
            {
                continue;
            }

            var routes = FindReferencedSliceRoutes(compilation, assembly);
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsAttribute(attribute.AttributeClass, moduleAttribute, "Slice.SliceFeatureModuleAttribute")
                    || attribute.ConstructorArguments.Length != 1
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol registrationType)
                {
                    continue;
                }

                var module = new ReferencedSliceModule(
                    assembly.Identity.Name,
                    registrationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    routes,
                    HasPublicStaticMethod(registrationType, "AddSliceServices"),
                    HasPublicStaticMethod(registrationType, "MapSliceRoutes"),
                    HasPublicStaticMethod(registrationType, "AddSliceWorkerRoutes")
                        && HasPublicStaticMethod(registrationType, "RegisterWorkerRoutes"));

                builder.Add(module);
            }
        }

        return builder.ToImmutable();
    }

    private static HashSet<string> GetReferencedAssemblyAllowList(AnalyzerConfigOptionsProvider options)
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!options.GlobalOptions.TryGetValue("build_property.SliceReferencedAssemblies", out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return assemblies;
        }

        foreach (var part in value.Split([',', ';']))
        {
            var assemblyName = part.Trim();
            if (assemblyName.Length > 0)
            {
                assemblies.Add(assemblyName);
            }
        }

        return assemblies;
    }

    private static bool ShouldAggregateReferences(AnalyzerConfigOptionsProvider options)
    {
        if (!options.GlobalOptions.TryGetValue("build_property.SliceAggregateReferences", out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<ReferencedSliceRoute> FindReferencedSliceRoutes(
        Compilation compilation,
        IAssemblySymbol assembly)
    {
        var routeAttribute = compilation.GetTypeByMetadataName("Slice.SliceFeatureRouteAttribute");
        if (routeAttribute is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<ReferencedSliceRoute>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!IsAttribute(attribute.AttributeClass, routeAttribute, "Slice.SliceFeatureRouteAttribute")
                || attribute.ConstructorArguments.Length < 4
                || attribute.ConstructorArguments[0].Value is not string endpointName
                || attribute.ConstructorArguments[1].Value is not string featureType
                || attribute.ConstructorArguments[2].Value is not string httpMethod
                || attribute.ConstructorArguments[3].Value is not string pattern)
            {
                continue;
            }

            builder.Add(new ReferencedSliceRoute(
                assembly.Identity.Name,
                endpointName,
                featureType,
                httpMethod,
                pattern));
        }

        return builder.ToImmutable();
    }

    private static bool IsAttribute(
        INamedTypeSymbol? candidate,
        INamedTypeSymbol expected,
        string metadataName)
        => SymbolEqualityComparer.Default.Equals(candidate, expected)
           || string.Equals(candidate?.ToDisplayString(), metadataName, StringComparison.Ordinal);

    private static bool HasPublicStaticMethod(INamedTypeSymbol type, string methodName)
        => type.GetMembers(methodName).OfType<IMethodSymbol>().Any(static method =>
            method.DeclaredAccessibility == Accessibility.Public && method.IsStatic);

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
        var handleBuilder = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var member in featureType.GetMembers("Handle"))
        {
            if (member is IMethodSymbol m)
            {
                handleBuilder.Add(m);
            }
        }

        var handles = handleBuilder.ToImmutable();
        if (handles.IsEmpty)
        {
            return FeatureResult.Error(Diagnostic.Create(
                SliceDiagnostics.MissingHandleMethod,
                featureType.Locations.Length > 0 ? featureType.Locations[0] : null,
                featureType.Name));
        }

        if (handles.Length > 1)
        {
            return FeatureResult.Error(Diagnostic.Create(
                SliceDiagnostics.AmbiguousHandleMethod,
                featureType.Locations.Length > 0 ? featureType.Locations[0] : null,
                featureType.Name));
        }

        var handle = handles[0];
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

        // Collect filter FQNs in declaration order, plus any [FilterOrderHint(After=...)]
        // declared on each filter type so the generator can warn on declaration-order violations.
        var filterParts = new List<string>();
        var orderHintParts = new List<string>();
        foreach (var a in featureType.GetAttributes())
        {
            if (a.AttributeClass is { IsGenericType: true } ac
                && ac.OriginalDefinition.Name == "FilterAttribute"
                && ac.ContainingNamespace?.ToDisplayString() == "Slice"
                && ac.TypeArguments.Length == 1)
            {
                var filterTypeArg = ac.TypeArguments[0];
                var filterFqn = filterTypeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                filterParts.Add(filterFqn);

                if (filterTypeArg is INamedTypeSymbol filterNamedType)
                {
                    foreach (var hintAttr in filterNamedType.GetAttributes())
                    {
                        if (hintAttr.AttributeClass?.ToDisplayString() != "Slice.FilterOrderHintAttribute")
                        {
                            continue;
                        }

                        foreach (var kv in hintAttr.NamedArguments)
                        {
                            if (kv.Key == "After" && kv.Value.Value is INamedTypeSymbol afterType)
                            {
                                var afterFqn = afterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                orderHintParts.Add(filterFqn + "|" + afterFqn);
                            }
                        }
                    }
                }
            }
        }
        var serializedFilters = string.Join(";", filterParts);
        var serializedOrderHints = string.Join(";", orderHintParts);
        var (serializedValidationRules, requiresReflectionValidation) = CreateValidationRules(featureType, handle);
        var returnsAspNetResult = ReturnsAspNetResult(handle.ReturnType, ctx.SemanticModel.Compilation);

        var featureLocation = CreateDiagnosticLocation(featureType.Locations.Length > 0 ? featureType.Locations[0] : null);
        var model = new FeatureModel(
            featureType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            featureType.Name,
            tag,
            endpointName,
            httpMethod,
            pattern,
            string.IsNullOrWhiteSpace(summary) ? null : summary,
            handle.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            returnsAspNetResult,
            serializedParams,
            serializedFilters,
            serializedValidationRules,
            requiresReflectionValidation,
            serializedOrderHints,
            featureLocation.FilePath,
            featureLocation.SourceStart,
            featureLocation.SourceLength,
            featureLocation.StartLine,
            featureLocation.StartCharacter,
            featureLocation.EndLine,
            featureLocation.EndCharacter);

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

    private static bool ReturnsAspNetResult(ITypeSymbol returnType, Compilation compilation)
    {
        var iResultType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IResult");
        if (iResultType is null)
        {
            return false;
        }

        var resultType = UnwrapGenericAwaitable(returnType, compilation);
        return SymbolEqualityComparer.Default.Equals(resultType, iResultType)
            || resultType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iResultType));
    }

    private static ITypeSymbol UnwrapGenericAwaitable(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return type;
        }

        var original = named.OriginalDefinition;
        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        if (SymbolEqualityComparer.Default.Equals(original, taskType)
            || SymbolEqualityComparer.Default.Equals(original, valueTaskType))
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    private static string? TryCreateValidationRule(IPropertySymbol property, AttributeData attribute)
    {
        if (HasResourceErrorMessage(attribute))
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

            var message = GetErrorMessage(attribute) ?? $"The {propertyName} field is required.";
            return $"{propertyName}|Required|{(allowEmptyStrings ? "true" : "false")}|{EncodeValidationMessage(message)}";
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

            var message = GetErrorMessage(attribute)
                ?? $"The field {propertyName} must be a string with a minimum length of {minimumLength} and a maximum length of {maximumLength}.";
            return $"{propertyName}|StringLength|{minimumLength}|{maximumLength}|{EncodeValidationMessage(message)}";
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

            var message = GetErrorMessage(attribute)
                ?? $"The field {propertyName} must be a string or array type with a minimum length of '{length}'.";
            return $"{propertyName}|MinLength|{length}|{lengthMember}|{EncodeValidationMessage(message)}";
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

    private static bool HasResourceErrorMessage(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key is "ErrorMessageResourceName" or "ErrorMessageResourceType")
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetErrorMessage(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "ErrorMessage" && namedArgument.Value.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    private static string EncodeValidationMessage(string message)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(message));

    private static DiagnosticLocationModel CreateDiagnosticLocation(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return DiagnosticLocationModel.None;
        }

        var lineSpan = location.GetLineSpan();
        return new DiagnosticLocationModel(
            lineSpan.Path,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lineSpan.StartLinePosition.Line,
            lineSpan.StartLinePosition.Character,
            lineSpan.EndLinePosition.Line,
            lineSpan.EndLinePosition.Character);
    }

    // Looks up WorkerJsonContext by namespace convention only. Slice.Workers projects
    // should place it at <RootNamespace>.WorkerJsonContext; SLICE009 surfaces a clear
    // info diagnostic when the conventional location turns up nothing.
    private static string? FindWorkersJsonContext(ImmutableArray<FeatureModel> models, Compilation compilation)
    {
        var jsonContextType = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");
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
            var candidateType = compilation.GetTypeByMetadataName(candidate);
            if (candidateType is not null && InheritsFrom(candidateType, jsonContextType))
            {
                return "global::" + candidate;
            }
        }

        return null;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
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

    private static ImmutableArray<Diagnostic> FindFilterOrderDiagnostics(ImmutableArray<FeatureModel> models)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var model in models)
        {
            var hints = model.GetFilterOrderHints();
            if (hints.IsEmpty)
            {
                continue;
            }

            var filters = model.GetFilterFqns();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < filters.Length; i++)
            {
                positions[filters[i]] = i;
            }

            foreach (var hint in hints)
            {
                if (!positions.TryGetValue(hint.FilterFqn, out var filterIndex))
                {
                    continue;
                }

                if (!positions.TryGetValue(hint.AfterFqn, out var afterIndex))
                {
                    continue;
                }

                if (filterIndex < afterIndex)
                {
                    diagnostics.Add(Diagnostic.Create(
                        SliceDiagnostics.FilterOrderViolation,
                        model.GetDiagnosticLocation(),
                        model.FullyQualifiedTypeName,
                        TrimGlobalPrefix(hint.FilterFqn),
                        TrimGlobalPrefix(hint.AfterFqn)));
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    private static string TrimGlobalPrefix(string value)
        => value.StartsWith("global::", StringComparison.Ordinal) ? value.Substring("global::".Length) : value;

    private static ImmutableArray<Diagnostic> FindDuplicateEndpointNameDiagnostics(
        ImmutableArray<FeatureModel> models,
        ImmutableArray<ReferencedSliceModule> referencedModules)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var model in models)
        {
            if (seen.TryGetValue(model.EndpointName, out var existing))
            {
                diagnostics.Add(Diagnostic.Create(
                    SliceDiagnostics.DuplicateEndpointName,
                    location: null,
                    model.EndpointName,
                    existing,
                    model.FullyQualifiedTypeName));
                continue;
            }

            seen.Add(model.EndpointName, model.FullyQualifiedTypeName);
        }

        foreach (var route in referencedModules
                     .SelectMany(static module => module.Routes)
                     .Distinct()
                     .OrderBy(static route => route.AssemblyName, StringComparer.Ordinal)
                     .ThenBy(static route => route.EndpointName, StringComparer.Ordinal)
                     .ThenBy(static route => route.FeatureType, StringComparer.Ordinal))
        {
            var routeIdentity = $"{route.FeatureType} ({route.AssemblyName})";
            if (seen.TryGetValue(route.EndpointName, out var existing))
            {
                diagnostics.Add(Diagnostic.Create(
                    SliceDiagnostics.DuplicateEndpointName,
                    location: null,
                    route.EndpointName,
                    existing,
                    routeIdentity));
                continue;
            }

            seen.Add(route.EndpointName, routeIdentity);
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
