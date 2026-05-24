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
            .Select(static (r, _) => r.Diagnostic!.Value);

        context.RegisterSourceOutput(diagnostics, static (spc, d) => spc.ReportDiagnostic(d.ToDiagnostic()));

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

        // Only emit WASI code when Slice.Wasi is referenced by the target project.
        var hasWasiRef = context.CompilationProvider
            .Select(static (c, _) =>
                c.GetTypeByMetadataName("Slice.Wasi.Routing.WasiRouteTable") is not null);

        // Only emit Lambda per-feature code when the satellite is referenced and the assembly opts in.
        var lambdaPerFunctionOptions = context.CompilationProvider
            .Select(static (c, _) => FindLambdaPerFunctionOptions(c))
            .WithTrackingName("SliceLambdaPerFunctionOptions");

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

        var jsonContextOverrides = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Slice.SliceJsonContextAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => JsonContextPlanner.CreateOverrideCandidate(ctx))
            .Collect()
            .Select(static (candidates, _) => JsonContextPlanner.CreateOverrides(candidates))
            .WithTrackingName("SliceJsonContextOverrides");

        var wasiJsonContextPlan = validModels
            .Combine(jsonContextOverrides)
            .Combine(hasWasiRef)
            .Select(static (pair, _) =>
                pair.Right
                    ? JsonContextPlanner.CreateWasiPlan(pair.Left.Left, pair.Left.Right.WasiContextFqn)
                    : new JsonContextPlan(JsonContextTarget.Wasi, null, [], []))
            .WithTrackingName("SliceWasiJsonContextPlan");

        var lambdaJsonContextPlan = validModels
            .Combine(jsonContextOverrides)
            .Combine(lambdaPerFunctionOptions)
            .Select(static (pair, _) =>
                pair.Right.Enabled
                    ? JsonContextPlanner.CreateLambdaPlan(pair.Left.Left, pair.Left.Right.LambdaPerFeatureContextFqn)
                    : new JsonContextPlan(JsonContextTarget.LambdaPerFeature, null, [], []))
            .WithTrackingName("SliceLambdaJsonContextPlan");

        var emitPlan = validModels.Combine(assemblyName)
            .Combine(hasAspNetRef)
            .Combine(hasWasiRef)
            .Combine(referencedModules)
            .Combine(emitExtensions)
            .Combine(jsonContextOverrides)
            .Combine(wasiJsonContextPlan)
            .Combine(lambdaPerFunctionOptions)
            .Combine(lambdaJsonContextPlan)
            .Select(static (pair, _) =>
            {
                var (lambdaOptionsPair, lambdaJsonContextPlan) = pair;
                var (wasiPlanPair, lambdaOptions) = lambdaOptionsPair;
                var (overridesPair, wasiJsonContextPlan) = wasiPlanPair;
                var (emitExtensionsPair, jsonContextOverrides) = overridesPair;
                var (referencedModulesPair, emitExtensions) = emitExtensionsPair;
                var (emitWasiPair, referencedModules) = referencedModulesPair;
                var (emitAspNetPair, emitWasi) = emitWasiPair;
                var (modelsPair, emitAspNet) = emitAspNetPair;
                var (models, asmName) = modelsPair;
                return CreateEmitPlan(
                    models,
                    asmName,
                    emitAspNet,
                    emitWasi,
                    referencedModules,
                    emitExtensions,
                    jsonContextOverrides,
                    wasiJsonContextPlan,
                    lambdaOptions,
                    lambdaJsonContextPlan);
            })
            .WithTrackingName("SliceEmitPlan");

        context.RegisterSourceOutput(
            emitPlan,
            static (spc, plan) =>
            {
                foreach (var diagnostic in plan.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                foreach (var source in plan.Sources)
                {
                    spc.AddSource(
                        source.HintName,
                        Microsoft.CodeAnalysis.Text.SourceText.From(source.Source, Encoding.UTF8));
                }
            });
    }

    private static EmitPlan CreateEmitPlan(
        ImmutableArray<FeatureModel> models,
        string asmName,
        bool emitAspNet,
        bool emitWasi,
        ImmutableArray<ReferencedSliceModule> referencedModules,
        bool emitExtensions,
        JsonContextOverrides jsonContextOverrides,
        JsonContextPlan wasiJsonContextPlan,
        LambdaPerFunctionOptions lambdaOptions,
        JsonContextPlan lambdaJsonContextPlan)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        var duplicateDiagnostics = FindDuplicateEndpointNameDiagnostics(models, referencedModules);
        diagnostics.AddRange(duplicateDiagnostics);
        if (!duplicateDiagnostics.IsEmpty)
        {
            return new EmitPlan([], diagnostics.ToImmutable());
        }

        diagnostics.AddRange(FindFilterOrderDiagnostics(models));
        diagnostics.AddRange(lambdaOptions.Diagnostics);
        diagnostics.AddRange(jsonContextOverrides.Diagnostics);
        diagnostics.AddRange(wasiJsonContextPlan.Diagnostics);
        diagnostics.AddRange(lambdaJsonContextPlan.Diagnostics);

        var sources = ImmutableArray.CreateBuilder<GeneratedSource>();
        sources.Add(new GeneratedSource(
            $"{asmName}.SliceRouteManifest.g.cs",
            RouteManifestEmitter.Emit(
                models,
                asmName,
                lambdaOptions.Enabled,
                wasiJsonContextPlan,
                lambdaJsonContextPlan)));

        if (emitAspNet)
        {
            sources.Add(new GeneratedSource(
                $"{asmName}.SliceRegistrations.g.cs",
                RegistrationEmitter.Emit(models, asmName, referencedModules, emitExtensions)));
        }

        if (lambdaOptions.Enabled)
        {
            var (lambdaSource, lambdaDiagnostics) = LambdaPerFunctionEmitter.Emit(
                models,
                asmName,
                lambdaJsonContextPlan,
                lambdaOptions.StartupTypeFqn);
            diagnostics.AddRange(lambdaDiagnostics);
            sources.Add(new GeneratedSource(
                $"{asmName}.SliceLambdaPerFunctionHandlers.g.cs",
                lambdaSource));
        }

        if (emitWasi)
        {
            var (wasiSource, wasiDiagnostics) = WasiRegistrationEmitter.Emit(
                models,
                asmName,
                wasiJsonContextPlan,
                referencedModules,
                emitExtensions);
            diagnostics.AddRange(wasiDiagnostics);
            sources.Add(new GeneratedSource(
                $"{asmName}.SliceWasiRegistrations.g.cs",
                wasiSource));
        }

        return new EmitPlan(sources.ToImmutable(), diagnostics.ToImmutable());
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

    private static LambdaPerFunctionOptions FindLambdaPerFunctionOptions(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName("Slice.Lambda.PerFunction.LambdaInvocationContext") is null)
        {
            return new LambdaPerFunctionOptions(false, null, []);
        }

        var attributeType = compilation.GetTypeByMetadataName("Slice.Lambda.PerFunction.LambdaPerFunctionAttribute");
        if (attributeType is null)
        {
            return new LambdaPerFunctionOptions(false, null, []);
        }

        var startupInterface = compilation.GetTypeByMetadataName("Slice.Lambda.PerFunction.ILambdaPerFunctionStartup");

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!IsAttribute(attribute.AttributeClass, attributeType, "Slice.Lambda.PerFunction.LambdaPerFunctionAttribute"))
            {
                continue;
            }

            string? startupTypeFqn = null;
            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is INamedTypeSymbol startupType)
            {
                var diagnostic = ValidateLambdaPerFunctionStartupType(startupType, startupInterface);
                if (diagnostic is not null)
                {
                    return new LambdaPerFunctionOptions(true, null, [diagnostic.Value]);
                }

                startupTypeFqn = startupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return new LambdaPerFunctionOptions(true, startupTypeFqn, []);
        }

        return new LambdaPerFunctionOptions(false, null, []);
    }

    private static EquatableDiagnostic? ValidateLambdaPerFunctionStartupType(
        INamedTypeSymbol startupType,
        INamedTypeSymbol? startupInterface)
    {
        var implementsStartup = startupInterface is not null
                                && startupType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, startupInterface));
        var hasPublicParameterlessCtor = startupType.InstanceConstructors.Any(static ctor =>
            ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0);
        if (startupType.TypeKind == TypeKind.Class
            && !startupType.IsAbstract
            && implementsStartup
            && hasPublicParameterlessCtor)
        {
            return null;
        }

        var location = CreateDiagnosticLocation(startupType.Locations.Length > 0 ? startupType.Locations[0] : null);
        return EquatableDiagnostic.Create(
            SliceDiagnostics.InvalidLambdaPerFeatureStartupType,
            location,
            startupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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
                    HasPublicStaticMethod(registrationType, "AddSliceWasiRoutes")
                        && HasPublicStaticMethod(registrationType, "RegisterWasiRoutes"));

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
            return FeatureResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.InvalidRouteFormat,
                CreateDiagnosticLocation(featureType.Locations.Length > 0 ? featureType.Locations[0] : null),
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
            return FeatureResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.MissingHandleMethod,
                CreateDiagnosticLocation(featureType.Locations.Length > 0 ? featureType.Locations[0] : null),
                featureType.Name));
        }

        if (handles.Length > 1)
        {
            return FeatureResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.AmbiguousHandleMethod,
                CreateDiagnosticLocation(featureType.Locations.Length > 0 ? featureType.Locations[0] : null),
                featureType.Name));
        }

        var handle = handles[0];
        if (!handle.IsStatic || handle.DeclaredAccessibility != Accessibility.Public)
        {
            return FeatureResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.HandleNotPublicStatic,
                CreateDiagnosticLocation(handle.Locations.Length > 0 ? handle.Locations[0] : null),
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
            ? new FeatureResult(model, EquatableDiagnostic.Create(
                SliceDiagnostics.TagInferenceFallback,
                CreateDiagnosticLocation(featureType.Locations.Length > 0 ? featureType.Locations[0] : null),
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

    private static ImmutableArray<EquatableDiagnostic> FindFilterOrderDiagnostics(ImmutableArray<FeatureModel> models)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
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
                    diagnostics.Add(EquatableDiagnostic.Create(
                        SliceDiagnostics.FilterOrderViolation,
                        model.GetDiagnosticLocationModel(),
                        model.FullyQualifiedTypeName,
                        SourceGenerationHelpers.TrimGlobalAlias(hint.FilterFqn),
                        SourceGenerationHelpers.TrimGlobalAlias(hint.AfterFqn)));
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    private static ImmutableArray<EquatableDiagnostic> FindDuplicateEndpointNameDiagnostics(
        ImmutableArray<FeatureModel> models,
        ImmutableArray<ReferencedSliceModule> referencedModules)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();

        foreach (var model in models)
        {
            if (seen.TryGetValue(model.EndpointName, out var existing))
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.DuplicateEndpointName,
                    DiagnosticLocationModel.None,
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
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.DuplicateEndpointName,
                    DiagnosticLocationModel.None,
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

internal readonly struct FeatureResult(FeatureModel? model, EquatableDiagnostic? diagnostic) : IEquatable<FeatureResult>
{
    /// <summary>
    /// Gets the discovered feature model when the feature is valid.
    /// </summary>
    public FeatureModel? Model { get; } = model;

    /// <summary>
    /// Gets the diagnostic reported for an invalid or notable feature.
    /// </summary>
    public EquatableDiagnostic? Diagnostic { get; } = diagnostic;

    public bool Equals(FeatureResult other)
        => EqualityComparer<FeatureModel?>.Default.Equals(Model, other.Model)
           && Nullable.Equals(Diagnostic, other.Diagnostic);

    public override bool Equals(object? obj) => obj is FeatureResult other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((Model?.GetHashCode() ?? 0) * 397) ^ Diagnostic.GetHashCode();
        }
    }

    /// <summary>
    /// Creates a feature result that contains only a diagnostic.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to report.</param>
    /// <returns>A feature result containing the diagnostic.</returns>
    public static FeatureResult Error(EquatableDiagnostic diagnostic) => new(null, diagnostic);
}
