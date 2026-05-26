using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SliceFx.SourceGenerator;

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
                "SliceFx.FeatureAttribute",
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

        var rawMinimalApiEndpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: TransformRawMinimalApiEndpoint)
            .Where(static endpoint => endpoint is not null)
            .Select(static (endpoint, _) => endpoint!.Value)
            .Collect()
            .WithTrackingName("SliceRawMinimalApiEndpoints");

        context.RegisterSourceOutput(
            validModels.Combine(rawMinimalApiEndpoints),
            static (spc, pair) =>
            {
                foreach (var diagnostic in FindRawMinimalApiOverlapDiagnostics(pair.Left, pair.Right))
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }
            });

        var validators = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsValidatorCandidate(node),
                transform: TransformValidator)
            .WithTrackingName("SliceValidatorModels");

        var validatorDiagnostics = validators
            .Where(static r => r.Diagnostic is not null)
            .Select(static (r, _) => r.Diagnostic!.Value);

        context.RegisterSourceOutput(validatorDiagnostics, static (spc, d) => spc.ReportDiagnostic(d.ToDiagnostic()));

        var validValidators = validators
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .Collect();

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName ?? "Unknown");

        // Only emit ASP.NET registration when ASP.NET Core is referenced by the target project.
        var hasAspNetRef = context.CompilationProvider
            .Select(static (c, _) =>
                c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IResult") is not null);

        // Only emit WASI code when SliceFx.Wasi is referenced by the target project.
        var hasWasiRef = context.CompilationProvider
            .Select(static (c, _) =>
                c.GetTypeByMetadataName("SliceFx.Wasi.Routing.WasiRouteTable") is not null);

        // Only emit Lambda function-per-feature code when the satellite is referenced and the assembly opts in.
        var lambdaFunctionPerFeatureOptions = context.CompilationProvider
            .Select(static (c, _) => FindLambdaFunctionPerFeatureOptions(c))
            .WithTrackingName("SliceLambdaFunctionPerFeatureOptions");

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
                "SliceFx.SliceJsonContextAttribute",
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
            .Combine(lambdaFunctionPerFeatureOptions)
            .Select(static (pair, _) =>
                pair.Right.Enabled
                    ? JsonContextPlanner.CreateLambdaPlan(pair.Left.Left, pair.Left.Right.LambdaFunctionPerFeatureContextFqn)
                    : new JsonContextPlan(JsonContextTarget.LambdaFunctionPerFeature, null, [], []))
            .WithTrackingName("SliceLambdaJsonContextPlan");

        var emitPlan = validModels.Combine(validValidators)
            .Combine(assemblyName)
            .Combine(hasAspNetRef)
            .Combine(hasWasiRef)
            .Combine(referencedModules)
            .Combine(emitExtensions)
            .Combine(jsonContextOverrides)
            .Combine(wasiJsonContextPlan)
            .Combine(lambdaFunctionPerFeatureOptions)
            .Combine(lambdaJsonContextPlan)
            .Select(static (pair, _) => CreateEmitPlan(EmitPipelineInputs.FromCombined(pair)))
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

    private static EmitPlan CreateEmitPlan(EmitPipelineInputs inputs)
    {
        var (models, validators, asmName, emitAspNet, emitWasi, referencedModulesResult,
            emitExtensions, jsonContextOverrides, wasiJsonContextPlan, lambdaOptions,
            lambdaJsonContextPlan) = inputs;
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        diagnostics.AddRange(referencedModulesResult.Diagnostics);
        var referencedModules = referencedModulesResult.Modules;
        var requestTypeFqns = FindSliceRequestTypeFqns(models, referencedModules);
        var matchedValidators = MatchValidators(validators, requestTypeFqns);

        var duplicateDiagnostics = FindDuplicateEndpointNameDiagnostics(models, referencedModules);
        diagnostics.AddRange(duplicateDiagnostics);
        if (!duplicateDiagnostics.IsEmpty)
        {
            return new EmitPlan([], diagnostics.ToImmutable());
        }

        if (lambdaOptions.Enabled)
        {
            diagnostics.AddRange(FindLambdaArtifactIdDiagnostics(models));
        }
        diagnostics.AddRange(FindFilterOrderDiagnostics(models));
        diagnostics.AddRange(FindUnmatchedValidatorDiagnostics(validators, requestTypeFqns));
        diagnostics.AddRange(FindDuplicateValidatorDiagnostics(matchedValidators, referencedModules));
        diagnostics.AddRange(lambdaOptions.Diagnostics);
        diagnostics.AddRange(jsonContextOverrides.Diagnostics);
        diagnostics.AddRange(wasiJsonContextPlan.Diagnostics);
        diagnostics.AddRange(lambdaJsonContextPlan.Diagnostics);
        if (emitAspNet)
        {
            diagnostics.AddRange(FindAspNetUnsupportedValidationDiagnostics(models));
        }

        var sources = ImmutableArray.CreateBuilder<GeneratedSource>();
        sources.Add(new GeneratedSource(
            $"{asmName}.SliceRouteManifest.g.cs",
            RouteManifestEmitter.Emit(
                models,
                matchedValidators,
                asmName,
                referencedModules,
                lambdaOptions.Enabled,
                wasiJsonContextPlan,
                lambdaJsonContextPlan)));

        if (emitAspNet)
        {
            sources.Add(new GeneratedSource(
                $"{asmName}.SliceRegistrations.g.cs",
                RegistrationEmitter.Emit(models, matchedValidators, asmName, referencedModules, emitExtensions)));
        }

        if (lambdaOptions.Enabled)
        {
            var (lambdaSource, lambdaDiagnostics) = LambdaFunctionPerFeatureEmitter.Emit(
                models,
                asmName,
                matchedValidators,
                referencedModules,
                lambdaJsonContextPlan);
            diagnostics.AddRange(lambdaDiagnostics);
            sources.Add(new GeneratedSource(
                $"{asmName}.SliceLambdaFunctionPerFeatureHandlers.g.cs",
                lambdaSource));
        }

        if (emitWasi)
        {
            var (wasiSource, wasiDiagnostics) = WasiRegistrationEmitter.Emit(
                models,
                matchedValidators,
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
        if (options.GlobalOptions.TryGetValue("build_property.SliceFxRole", out var role))
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

    private static LambdaFunctionPerFeatureOptions FindLambdaFunctionPerFeatureOptions(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName("SliceFx.Lambda.FunctionPerFeature.LambdaInvocationContext") is null)
        {
            return new LambdaFunctionPerFeatureOptions(false, null, []);
        }

        var attributeType = compilation.GetTypeByMetadataName("SliceFx.Lambda.FunctionPerFeature.LambdaFunctionPerFeatureAttribute");
        if (attributeType is null)
        {
            return new LambdaFunctionPerFeatureOptions(false, null, []);
        }

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!IsAttribute(attribute.AttributeClass, attributeType, "SliceFx.Lambda.FunctionPerFeature.LambdaFunctionPerFeatureAttribute"))
            {
                continue;
            }

            return new LambdaFunctionPerFeatureOptions(true, null, []);
        }

        return new LambdaFunctionPerFeatureOptions(false, null, []);
    }

    private static EquatableDiagnostic? ValidateLambdaFunctionPerFeatureStartupType(
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
            SliceDiagnostics.InvalidLambdaFunctionPerFeatureStartupType,
            location,
            startupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static ReferencedSliceModulesResult FindReferencedSliceModules(
        Compilation compilation,
        AnalyzerConfigOptionsProvider options)
    {
        var role = GetGenerationRole(compilation, options);
        if (role is not (SliceGenerationRole.Host or SliceGenerationRole.Both))
        {
            return new ReferencedSliceModulesResult([], []);
        }

        var moduleAttribute = compilation.GetTypeByMetadataName("SliceFx.SliceFeatureModuleAttribute");
        if (moduleAttribute is null)
        {
            return new ReferencedSliceModulesResult([], []);
        }

        var aggregationOptions = GetReferenceAggregationOptions(options);
        var builder = ImmutableArray.CreateBuilder<ReferencedSliceModule>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
                     .OrderBy(static a => a.Identity.Name, StringComparer.Ordinal))
        {
            var routes = default(ImmutableArray<ReferencedSliceRoute>);
            var validators = default(ImmutableArray<ReferencedSliceValidator>);
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsAttribute(attribute.AttributeClass, moduleAttribute, "SliceFx.SliceFeatureModuleAttribute")
                    || attribute.ConstructorArguments.Length != 1
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol registrationType)
                {
                    continue;
                }

                if (routes.IsDefault)
                {
                    routes = FindReferencedSliceRoutes(compilation, assembly);
                    validators = FindReferencedSliceValidators(compilation, assembly);
                }

                var module = new ReferencedSliceModule(
                    assembly.Identity.Name,
                    registrationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    routes,
                    validators,
                    HasPublicStaticMethod(registrationType, "AddSliceServices"),
                    HasPublicStaticMethod(registrationType, "AddSliceValidatorServices"),
                    HasPublicStaticMethod(registrationType, "MapSliceRoutes"),
                    HasPublicStaticMethod(registrationType, "AddSliceWasiRoutes")
                        && HasPublicStaticMethod(registrationType, "RegisterWasiRoutes"));

                builder.Add(module);
            }
        }

        var allModules = builder.ToImmutable();
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        if (aggregationOptions.InvalidAggregateReferencesValue is not null)
        {
            diagnostics.Add(EquatableDiagnostic.Create(
                SliceDiagnostics.InvalidSliceFxAggregateReferences,
                DiagnosticLocationModel.None,
                aggregationOptions.InvalidAggregateReferencesValue));
        }

        ImmutableArray<ReferencedSliceModule> effectiveModules;
        if (aggregationOptions.HasAllowList)
        {
            effectiveModules = [.. allModules.Where(module => aggregationOptions.AllowedAssemblies.Contains(module.AssemblyName))];
        }
        else if (aggregationOptions.AggregateFlagSpecified && aggregationOptions.AggregateAllReferences)
        {
            effectiveModules = allModules;
        }
        else
        {
            effectiveModules = [];
            if (!aggregationOptions.AggregateFlagSpecified
                && aggregationOptions.InvalidAggregateReferencesValue is null)
            {
                var referencedFeatureAssemblies = allModules
                    .Where(static module => !module.Routes.IsDefaultOrEmpty)
                    .Select(static module => module.AssemblyName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static assemblyName => assemblyName, StringComparer.Ordinal)
                    .ToArray();
                if (referencedFeatureAssemblies.Length > 0)
                {
                    diagnostics.Add(EquatableDiagnostic.Create(
                        SliceDiagnostics.UnconfiguredReferencedSliceModules,
                        DiagnosticLocationModel.None,
                        string.Join(", ", referencedFeatureAssemblies)));
                }
            }
        }

        return new ReferencedSliceModulesResult(effectiveModules, diagnostics.ToImmutable());
    }

    private static ImmutableArray<ReferencedSliceValidator> FindReferencedSliceValidators(
        Compilation compilation,
        IAssemblySymbol assembly)
    {
        var validatorAttribute = compilation.GetTypeByMetadataName("SliceFx.SliceFeatureValidatorAttribute");
        if (validatorAttribute is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<ReferencedSliceValidator>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!IsAttribute(attribute.AttributeClass, validatorAttribute, "SliceFx.SliceFeatureValidatorAttribute")
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[0].Value is not string requestType
                || attribute.ConstructorArguments[1].Value is not string validatorType)
            {
                continue;
            }

            builder.Add(new ReferencedSliceValidator(
                assembly.Identity.Name,
                requestType,
                validatorType));
        }

        return builder.ToImmutable();
    }

    private static SliceReferenceAggregationOptions GetReferenceAggregationOptions(AnalyzerConfigOptionsProvider options)
    {
        var assemblies = ImmutableHashSet.CreateBuilder(StringComparer.OrdinalIgnoreCase);
        if (!options.GlobalOptions.TryGetValue("build_property.SliceFxReferencedAssemblies", out var value)
            || string.IsNullOrWhiteSpace(value))
        {
        }
        else
        {
            foreach (var part in value.Split([',', ';']))
            {
                var assemblyName = part.Trim();
                if (assemblyName.Length > 0)
                {
                    assemblies.Add(assemblyName);
                }
            }
        }

        if (!options.GlobalOptions.TryGetValue("build_property.SliceFxAggregateReferences", out var aggregateValue)
            || string.IsNullOrWhiteSpace(aggregateValue))
        {
            return new SliceReferenceAggregationOptions(false, false, null, assemblies.ToImmutable());
        }

        var normalized = aggregateValue.Trim();
        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return new SliceReferenceAggregationOptions(true, true, null, assemblies.ToImmutable());
        }

        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase))
        {
            return new SliceReferenceAggregationOptions(true, false, null, assemblies.ToImmutable());
        }

        return new SliceReferenceAggregationOptions(true, false, normalized, assemblies.ToImmutable());
    }

    private static ImmutableArray<ReferencedSliceRoute> FindReferencedSliceRoutes(
        Compilation compilation,
        IAssemblySymbol assembly)
    {
        var routeAttribute = compilation.GetTypeByMetadataName("SliceFx.SliceFeatureRouteAttribute");
        if (routeAttribute is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<ReferencedSliceRoute>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!IsAttribute(attribute.AttributeClass, routeAttribute, "SliceFx.SliceFeatureRouteAttribute")
                || attribute.ConstructorArguments.Length < 4
                || attribute.ConstructorArguments[0].Value is not string endpointName
                || attribute.ConstructorArguments[1].Value is not string featureType
                || attribute.ConstructorArguments[2].Value is not string httpMethod
                || attribute.ConstructorArguments[3].Value is not string pattern)
            {
                continue;
            }

            var requestType = attribute.ConstructorArguments.Length > 6
                ? attribute.ConstructorArguments[6].Value as string
                : null;
            builder.Add(new ReferencedSliceRoute(
                assembly.Identity.Name,
                endpointName,
                featureType,
                httpMethod,
                pattern,
                requestType is null ? null : NormalizeTypeFqn(requestType)));
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

    private static bool IsValidatorCandidate(SyntaxNode node)
        => node is TypeDeclarationSyntax { BaseList: not null };

    private static ValidatorResult TransformValidator(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.Node is not TypeDeclarationSyntax
            || ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol validatorType)
        {
            return default;
        }

        var validatorInterface = ctx.SemanticModel.Compilation.GetTypeByMetadataName("SliceFx.ISliceValidator`1");
        if (validatorInterface is null)
        {
            return default;
        }

        var interfaces = validatorType.AllInterfaces
            .Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, validatorInterface))
            .ToArray();
        if (interfaces.Length == 0)
        {
            return default;
        }

        var location = CreateDiagnosticLocation(validatorType.Locations.Length > 0 ? validatorType.Locations[0] : null);
        var validatorName = validatorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (validatorType.TypeKind != TypeKind.Class || validatorType.IsAbstract)
        {
            return default;
        }

        if (validatorType.IsGenericType)
        {
            return ValidatorResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.InvalidSliceValidator,
                location,
                validatorName,
                "open generic validator implementations are not supported"));
        }

        if (interfaces.Length > 1)
        {
            return ValidatorResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.InvalidSliceValidator,
                location,
                validatorName,
                "a validator implementation must target exactly one request type"));
        }

        if (!IsAccessibleFromGeneratedCode(validatorType))
        {
            return ValidatorResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.InvalidSliceValidator,
                location,
                validatorName,
                "validator implementations must be accessible from generated code"));
        }

        var requestType = interfaces[0].TypeArguments[0];
        var requestTypeFqn = requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!IsValidValidatorRequestType(requestType, requestTypeFqn))
        {
            return ValidatorResult.Error(EquatableDiagnostic.Create(
                SliceDiagnostics.InvalidSliceValidator,
                location,
                validatorName,
                $"request type '{SourceGenerationHelpers.TrimGlobalAlias(requestTypeFqn)}' is not a supported Slice request type"));
        }

        return new ValidatorResult(
            new ValidatorModel(
                validatorName,
                requestTypeFqn,
                location.FilePath,
                location.SourceStart,
                location.SourceLength,
                location.StartLine,
                location.StartCharacter,
                location.EndLine,
                location.EndCharacter),
            null);
    }

    private static bool IsValidValidatorRequestType(ITypeSymbol requestType, string requestTypeFqn)
        => requestType.IsReferenceType
           && requestType.TypeKind != TypeKind.Interface
           && requestType.TypeKind != TypeKind.Delegate
           && !SourceGenerationHelpers.IsSimpleType(requestTypeFqn)
           && !SourceGenerationHelpers.IsFrameworkType(requestTypeFqn);

    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

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
        string? name = null;
        string? summary = null;
        foreach (var kv in attrData.NamedArguments)
        {
            if (kv.Key == "Tag")
            {
                tag = kv.Value.Value as string;
            }
            else if (kv.Key == "Name")
            {
                name = kv.Value.Value as string;
            }
            else if (kv.Key == "Summary")
            {
                summary = kv.Value.Value as string;
            }
        }

        var tagInferred = tag is null;
        tag ??= InferTag(featureType);
        var endpointName = string.IsNullOrWhiteSpace(name) ? $"{tag}.{featureType.Name}" : name!;

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

        // Serialise params as "b64(typeFqn)|b64(name)|K|N-|b64(bindingSource)|b64(bindingName)|VR;..." where
        // K = 'I' (interface/abstract) or 'C' (concrete), and N marks nullable.
        // V marks value types so emitters can avoid null checks that produce nullable warnings.
        var paramParts = new string[handle.Parameters.Length];
        for (var i = 0; i < handle.Parameters.Length; i++)
        {
            var p = handle.Parameters[i];
            var kind = (p.Type.TypeKind == TypeKind.Interface
                        || (p.Type is INamedTypeSymbol nt && nt.IsAbstract)) ? "I" : "C";
            var nullable = IsNullableParameter(p) ? "N" : "-";
            var valueType = p.Type.IsValueType ? "V" : "R";
            var (bindingSource, bindingName) = GetBindingMetadata(p);
            paramParts[i] = Encode(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) + "|" +
                Encode(p.Name) + "|" +
                kind + "|" +
                nullable + "|" +
                Encode(bindingSource) + "|" +
                Encode(bindingName) + "|" +
                valueType;
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
                && ac.ContainingNamespace?.ToDisplayString() == "SliceFx"
                && ac.TypeArguments.Length == 1)
            {
                var filterTypeArg = ac.TypeArguments[0];
                var filterFqn = filterTypeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                filterParts.Add(filterFqn);

                if (filterTypeArg is INamedTypeSymbol filterNamedType)
                {
                    foreach (var hintAttr in filterNamedType.GetAttributes())
                    {
                        if (hintAttr.AttributeClass?.ToDisplayString() != "SliceFx.FilterOrderHintAttribute")
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
        var (lambdaStartupTypeFqn, lambdaStartupDiagnostic) = FindLambdaFeatureStartup(featureType, ctx.SemanticModel.Compilation);
        if (lambdaStartupDiagnostic is not null)
        {
            return FeatureResult.Error(lambdaStartupDiagnostic.Value);
        }

        var (serializedValidationRules, requiresReflectionValidation, serializedUnsupportedValidationAttributes) = CreateValidationRules(handle);
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
            serializedUnsupportedValidationAttributes,
            serializedOrderHints,
            lambdaStartupTypeFqn,
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

    private static (string? StartupTypeFqn, EquatableDiagnostic? Diagnostic) FindLambdaFeatureStartup(
        INamedTypeSymbol featureType,
        Compilation compilation)
    {
        foreach (var attribute in featureType.GetAttributes())
        {
            if (!IsLambdaFunctionStartupAttribute(attribute.AttributeClass))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol startupType)
            {
                continue;
            }

            var startupInterface = compilation.GetTypeByMetadataName("SliceFx.Lambda.FunctionPerFeature.ILambdaFunctionPerFeatureStartup");
            var diagnostic = ValidateLambdaFunctionPerFeatureStartupType(startupType, startupInterface);
            if (diagnostic is not null)
            {
                return (null, diagnostic.Value);
            }

            return (startupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), null);
        }

        return (null, null);
    }

    private static bool IsLambdaFunctionStartupAttribute(INamedTypeSymbol? attributeType)
    {
        if (attributeType?.Name != "LambdaFunctionStartupAttribute")
        {
            return false;
        }

        return attributeType.ContainingNamespace?.ToDisplayString() == "SliceFx.Lambda.FunctionPerFeature";
    }

    private static bool IsNullableParameter(IParameterSymbol parameter)
        => parameter.NullableAnnotation == NullableAnnotation.Annotated ||
           parameter.Type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Nullable<T>";

    private static (string Source, string Name) GetBindingMetadata(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attributeName is null)
            {
                continue;
            }

            var source = attributeName switch
            {
                "global::Microsoft.AspNetCore.Mvc.FromQueryAttribute" => "query",
                "global::Microsoft.AspNetCore.Mvc.FromRouteAttribute" => "route",
                "global::Microsoft.AspNetCore.Mvc.FromHeaderAttribute" => "header",
                "global::Microsoft.AspNetCore.Mvc.FromBodyAttribute" => "body",
                "global::Microsoft.AspNetCore.Mvc.FromServicesAttribute" => "services",
                _ => null,
            };
            if (source is null)
            {
                continue;
            }

            var name = "";
            foreach (var arg in attribute.NamedArguments)
            {
                if (arg.Key == "Name" && arg.Value.Value is string value)
                {
                    name = value;
                    break;
                }
            }

            return (source, name);
        }

        return ("", "");
    }

    private static (string SerializedRules, bool RequiresReflectionValidation, string SerializedUnsupportedValidationAttributes) CreateValidationRules(
        IMethodSymbol handle)
    {
        var rules = new List<string>();
        var unsupportedAttributes = new List<string>();
        var requiresReflectionValidation = false;

        for (var parameterIndex = 0; parameterIndex < handle.Parameters.Length; parameterIndex++)
        {
            var parameter = handle.Parameters[parameterIndex];
            if (!IsRequestLikeParameter(parameter) || parameter.Type is not INamedTypeSymbol requestType)
            {
                continue;
            }

            foreach (var attribute in requestType.GetAttributes().Where(IsValidationAttribute))
            {
                if (IsMetadataOnlyValidationAttribute(attribute))
                {
                    continue;
                }

                requiresReflectionValidation = true;
                unsupportedAttributes.Add(SerializeUnsupportedValidationAttribute(handle.ContainingType.Name, attribute));
            }

            if (ImplementsIValidatableObject(requestType))
            {
                requiresReflectionValidation = true;
                unsupportedAttributes.Add(SerializeUnsupportedValidationType(handle.ContainingType.Name, requestType, "IValidatableObject"));
            }

            var primaryCtorParams = requestType.InstanceConstructors
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault()
                ?.Parameters
                .Where(p => p.Name is not null)
                .ToDictionary(p => p.Name, StringComparer.Ordinal)
                ?? new Dictionary<string, IParameterSymbol>(StringComparer.Ordinal);

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
                    if (IsMetadataOnlyValidationAttribute(attribute))
                    {
                        continue;
                    }

                    var rule = TryCreateValidationRule(property, attribute);
                    if (rule is null)
                    {
                        requiresReflectionValidation = true;
                        unsupportedAttributes.Add(SerializeUnsupportedValidationAttribute(handle.ContainingType.Name, attribute));
                        continue;
                    }

                    if (rule.Length == 0)
                    {
                        continue;
                    }

                    rules.Add(parameterIndex + "|" + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" + rule);
                }
            }
        }

        return (
            string.Join("\n", rules),
            requiresReflectionValidation,
            string.Join("\n", unsupportedAttributes));
    }

    private static bool IsRequestLikeParameter(IParameterSymbol parameter)
    {
        var typeFqn = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return typeFqn != "global::System.Threading.CancellationToken"
            && parameter.Type.TypeKind != TypeKind.Interface
            && parameter.Type is not INamedTypeSymbol { IsAbstract: true }
            && !SourceGenerationHelpers.IsSimpleType(typeFqn)
            && !SourceGenerationHelpers.IsFrameworkType(typeFqn)
            && !HasExplicitServicesBinding(parameter);
    }

    private static bool HasExplicitServicesBinding(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.AspNetCore.Mvc.FromServicesAttribute")
            {
                return true;
            }
        }

        return false;
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

    private static bool IsMetadataOnlyValidationAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == "System.ComponentModel.DataAnnotations.DataTypeAttribute";

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
            var requiredKind = GetRequiredValidationKind(property);
            if (requiredKind is null)
            {
                return "";
            }

            var allowEmptyStrings = false;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "AllowEmptyStrings" && namedArgument.Value.Value is bool value)
                {
                    allowEmptyStrings = value;
                }
            }

            var message = GetErrorMessage(attribute) ?? $"The {propertyName} field is required.";
            return $"{propertyName}|Required|{requiredKind}|{(allowEmptyStrings ? "true" : "false")}|{EncodeValidationMessage(message)}";
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

        if (attributeName == "System.ComponentModel.DataAnnotations.MaxLengthAttribute"
            && attribute.ConstructorArguments.Length <= 1)
        {
            if (attribute.ConstructorArguments.Length == 0 || attribute.ConstructorArguments[0].Value is not int maxLength)
            {
                return null;
            }

            var lengthMember = TryGetLengthMember(property.Type);
            if (lengthMember is null)
            {
                return null;
            }

            var message = GetErrorMessage(attribute)
                ?? $"The field {propertyName} must be a string or array type with a maximum length of '{maxLength}'.";
            return $"{propertyName}|MaxLength|{maxLength}|{lengthMember}|{EncodeValidationMessage(message)}";
        }

        if (attributeName == "System.ComponentModel.DataAnnotations.EmailAddressAttribute")
        {
            var message = GetErrorMessage(attribute) ?? $"The {propertyName} field is not a valid e-mail address.";
            return $"{propertyName}|EmailAddress|{EncodeValidationMessage(message)}";
        }

        if (attributeName == "System.ComponentModel.DataAnnotations.UrlAttribute")
        {
            var message = GetErrorMessage(attribute) ?? $"The {propertyName} field is not a valid fully-qualified http, https, or ftp URL.";
            return $"{propertyName}|Url|{EncodeValidationMessage(message)}";
        }

        if (attributeName == "System.ComponentModel.DataAnnotations.RegularExpressionAttribute"
            && attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string pattern)
        {
            var matchTimeoutInMilliseconds = 2000;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "MatchTimeoutInMilliseconds"
                    && namedArgument.Value.Value is int value)
                {
                    matchTimeoutInMilliseconds = value;
                }
            }

            var message = GetErrorMessage(attribute)
                ?? $"The field {propertyName} must match the regular expression '{pattern}'.";
            return $"{propertyName}|RegularExpression|{EncodeValidationMessage(pattern)}|{EncodeValidationMessage(message)}|{matchTimeoutInMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        if (attributeName == "System.ComponentModel.DataAnnotations.RangeAttribute"
            && TryCreateRangeValidationRule(property, attribute) is { } rangeRule)
        {
            return rangeRule;
        }

        return null;
    }

    private static string? GetRequiredValidationKind(IPropertySymbol property)
    {
        if (property.Type.SpecialType == SpecialType.System_String)
        {
            return "String";
        }

        if (IsNullableValueType(property.Type))
        {
            return "Nullable";
        }

        if (property.Type.IsValueType)
        {
            return null;
        }

        return "Reference";
    }

    private static bool IsNullableValueType(ITypeSymbol type)
        => type is INamedTypeSymbol named
           && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static string? TryCreateRangeValidationRule(IPropertySymbol property, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 2)
        {
            return null;
        }

        var propertyType = property.Type;
        if (property.Type is INamedTypeSymbol { IsGenericType: true } named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            propertyType = named.TypeArguments[0];
        }
        string? typeName = null;
        if (propertyType.SpecialType == SpecialType.System_Int32)
        {
            typeName = "int";
        }
        else if (propertyType.SpecialType == SpecialType.System_Int64)
        {
            typeName = "long";
        }
        else if (propertyType.SpecialType == SpecialType.System_Double)
        {
            typeName = "double";
        }
        else if (propertyType.SpecialType == SpecialType.System_Single)
        {
            typeName = "float";
        }
        else if (propertyType.SpecialType == SpecialType.System_Decimal)
        {
            typeName = "decimal";
        }
        if (typeName is null)
        {
            return null;
        }

        var minimum = FormatRangeLiteral(attribute.ConstructorArguments[0], propertyType.SpecialType);
        var maximum = FormatRangeLiteral(attribute.ConstructorArguments[1], propertyType.SpecialType);
        if (minimum is null || maximum is null)
        {
            return null;
        }

        var propertyName = property.Name;
        var displayMinimum = Convert.ToString(attribute.ConstructorArguments[0].Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var displayMaximum = Convert.ToString(attribute.ConstructorArguments[1].Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var message = GetErrorMessage(attribute)
            ?? $"The field {propertyName} must be between {displayMinimum} and {displayMaximum}.";
        return $"{propertyName}|Range|{typeName}|{minimum}|{maximum}|{EncodeValidationMessage(message)}";
    }

    private static string? FormatRangeLiteral(TypedConstant constant, SpecialType specialType)
    {
        if (constant.Value is null)
        {
            return null;
        }

        if (specialType == SpecialType.System_Int32)
        {
            return constant.Value is int value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        if (specialType == SpecialType.System_Int64)
        {
            return constant.Value is long value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L" : null;
        }

        if (specialType == SpecialType.System_Double)
        {
            return constant.Value is double value ? value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        if (specialType == SpecialType.System_Single)
        {
            return constant.Value is float value ? value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "F" : null;
        }

        if (specialType == SpecialType.System_Decimal)
        {
            return constant.Value is decimal value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M" : null;
        }

        return null;
    }

    private static string SerializeUnsupportedValidationAttribute(string featureName, AttributeData attribute)
    {
        var location = attribute.ApplicationSyntaxReference is null
            ? DiagnosticLocationModel.None
            : CreateDiagnosticLocation(attribute.ApplicationSyntaxReference.SyntaxTree.GetLocation(attribute.ApplicationSyntaxReference.Span));
        var attributeName = attribute.AttributeClass?.Name ?? "ValidationAttribute";
        return SerializeUnsupportedValidation(featureName, attributeName, location);
    }

    private static string SerializeUnsupportedValidationType(string featureName, INamedTypeSymbol type, string attributeName)
    {
        var location = CreateDiagnosticLocation(type.Locations.Length > 0 ? type.Locations[0] : null);
        return SerializeUnsupportedValidation(featureName, attributeName, location);
    }

    private static string SerializeUnsupportedValidation(
        string featureName,
        string attributeName,
        DiagnosticLocationModel location)
        => string.Join("|", [
            Encode(location.FilePath),
            location.SourceStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
            location.SourceLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            location.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
            location.StartCharacter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            location.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
            location.EndCharacter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Encode(featureName),
            Encode(attributeName),
        ]);

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

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

    private static ImmutableArray<EquatableDiagnostic> FindAspNetUnsupportedValidationDiagnostics(ImmutableArray<FeatureModel> models)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        foreach (var model in models)
        {
            foreach (var unsupported in model.GetUnsupportedValidationAttributes())
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.UnsupportedValidationForAspNet,
                    unsupported.GetDiagnosticLocationModel(),
                    unsupported.FeatureName,
                    unsupported.AttributeName));
            }
        }

        return diagnostics.ToImmutable();
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

    private static ImmutableArray<string> FindSliceRequestTypeFqns(
        ImmutableArray<FeatureModel> models,
        ImmutableArray<ReferencedSliceModule> referencedModules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var model in models)
        {
            foreach (var parameter in model.GetParams())
            {
                if (SourceGenerationHelpers.IsRequestLikeParameter(parameter)
                    && seen.Add(parameter.TypeFqn))
                {
                    builder.Add(parameter.TypeFqn);
                }
            }
        }

        foreach (var requestType in referencedModules
                     .SelectMany(static module => module.Routes)
                     .Select(static route => route.RequestType)
                     .Where(static requestType => requestType is not null))
        {
            if (seen.Add(requestType!))
            {
                builder.Add(requestType!);
            }
        }

        return builder.ToImmutable();
    }

    private static string NormalizeTypeFqn(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName
            : "global::" + typeName;

    private static ImmutableArray<ValidatorModel> MatchValidators(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<string> requestTypeFqns)
    {
        var requestTypes = new HashSet<string>(requestTypeFqns, StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<ValidatorModel>();
        foreach (var validator in validators)
        {
            if (requestTypes.Contains(validator.RequestTypeFqn))
            {
                builder.Add(validator);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<EquatableDiagnostic> FindUnmatchedValidatorDiagnostics(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<string> requestTypeFqns)
    {
        var requestTypes = new HashSet<string>(requestTypeFqns, StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        foreach (var validator in validators)
        {
            if (!requestTypes.Contains(validator.RequestTypeFqn))
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.UnmatchedSliceValidator,
                    validator.GetDiagnosticLocationModel(),
                    SourceGenerationHelpers.TrimGlobalAlias(validator.ImplementationTypeFqn),
                    SourceGenerationHelpers.TrimGlobalAlias(validator.RequestTypeFqn)));
            }
        }

        return diagnostics.ToImmutable();
    }

    private static ImmutableArray<EquatableDiagnostic> FindDuplicateValidatorDiagnostics(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<ReferencedSliceModule> referencedModules)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        foreach (var validator in validators)
        {
            if (seen.TryGetValue(validator.RequestTypeFqn, out var existing))
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.DuplicateSliceValidator,
                    validator.GetDiagnosticLocationModel(),
                    SourceGenerationHelpers.TrimGlobalAlias(validator.RequestTypeFqn),
                    SourceGenerationHelpers.TrimGlobalAlias(existing),
                    SourceGenerationHelpers.TrimGlobalAlias(validator.ImplementationTypeFqn)));
                continue;
            }

            seen.Add(validator.RequestTypeFqn, validator.ImplementationTypeFqn);
        }

        foreach (var validator in referencedModules.SelectMany(static module => module.Validators))
        {
            if (seen.TryGetValue(validator.RequestType, out var existing))
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.DuplicateSliceValidator,
                    DiagnosticLocationModel.None,
                    SourceGenerationHelpers.TrimGlobalAlias(validator.RequestType),
                    SourceGenerationHelpers.TrimGlobalAlias(existing),
                    SourceGenerationHelpers.TrimGlobalAlias(validator.ValidatorType)));
                continue;
            }

            seen.Add(validator.RequestType, validator.ValidatorType);
        }

        return diagnostics.ToImmutable();
    }

    private static ImmutableArray<EquatableDiagnostic> FindLambdaArtifactIdDiagnostics(ImmutableArray<FeatureModel> models)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        foreach (var model in models)
        {
            var artifactId = SourceGenerationHelpers.ToLambdaArtifactId(model.EndpointName);
            if (seen.TryGetValue(artifactId, out var existing))
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.DuplicateLambdaFunctionPerFeatureArtifactId,
                    model.GetDiagnosticLocationModel(),
                    artifactId,
                    existing,
                    model.FullyQualifiedTypeName));
                continue;
            }

            seen.Add(artifactId, model.FullyQualifiedTypeName);
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

    private static RawMinimalApiEndpoint? TransformRawMinimalApiEndpoint(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var invokedName = GetInvokedName(invocation);
        if (invokedName == "WithName")
        {
            var endpointName = GetStringLiteralArgument(invocation, 0);
            if (endpointName is null || !ContainsRawMapInvocation(GetReceiver(invocation)))
            {
                return null;
            }

            return new RawMinimalApiEndpoint(
                "",
                "",
                endpointName,
                CreateDiagnosticLocation(invocation.GetLocation()));
        }

        if (!TryGetMapInvocation(invocation, out var methods, out var pattern))
        {
            return null;
        }

        var prefix = FindLiteralMapGroupPrefix(GetReceiver(invocation));
        return new RawMinimalApiEndpoint(
            methods,
            CombineRoutePatterns(prefix, pattern),
            "",
            CreateDiagnosticLocation(invocation.GetLocation()));
    }

    private static ImmutableArray<EquatableDiagnostic> FindRawMinimalApiOverlapDiagnostics(
        ImmutableArray<FeatureModel> features,
        ImmutableArray<RawMinimalApiEndpoint> rawEndpoints)
    {
        if (features.IsDefaultOrEmpty || rawEndpoints.IsDefaultOrEmpty)
        {
            return [];
        }

        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        var featureRoutes = features
            .GroupBy(static feature => RouteKey(feature.HttpMethod, feature.Pattern), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var featureNames = features
            .GroupBy(static feature => feature.EndpointName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var endpoint in rawEndpoints)
        {
            if (!string.IsNullOrEmpty(endpoint.EndpointName)
                && featureNames.TryGetValue(endpoint.EndpointName, out var nameFeature))
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.RawMinimalApiEndpointNameOverlap,
                    endpoint.Location,
                    endpoint.EndpointName,
                    nameFeature.FullyQualifiedTypeName));
            }

            foreach (var method in SplitMethods(endpoint.Methods))
            {
                if (featureRoutes.TryGetValue(RouteKey(method, endpoint.Pattern), out var routeFeature))
                {
                    diagnostics.Add(EquatableDiagnostic.Create(
                        SliceDiagnostics.RawMinimalApiRouteOverlap,
                        endpoint.Location,
                        method,
                        endpoint.Pattern,
                        routeFeature.FullyQualifiedTypeName));
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    private static bool TryGetMapInvocation(
        InvocationExpressionSyntax invocation,
        out string method,
        out string pattern)
    {
        method = "";
        pattern = "";
        var invokedName = GetInvokedName(invocation);
        method = invokedName switch
        {
            "MapGet" => "GET",
            "MapPost" => "POST",
            "MapPut" => "PUT",
            "MapDelete" => "DELETE",
            "MapPatch" => "PATCH",
            _ => "",
        };

        if (invokedName == "MapMethods")
        {
            var mapMethodsRoute = GetStringLiteralArgument(invocation, 0);
            var methods = invocation.ArgumentList.Arguments.Count > 1
                ? GetStringLiteralCollection(invocation.ArgumentList.Arguments[1].Expression)
                : [];
            if (mapMethodsRoute is null || methods.Length == 0)
            {
                return false;
            }

            method = string.Join(";", methods.Select(static value => value.ToUpperInvariant()));
            pattern = mapMethodsRoute;
            return true;
        }

        if (method.Length == 0)
        {
            return false;
        }

        var route = GetStringLiteralArgument(invocation, 0);
        if (route is null)
        {
            return false;
        }

        pattern = route;
        return true;
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;

    private static string? GetStringLiteralArgument(InvocationExpressionSyntax invocation, int index)
    {
        if (invocation.ArgumentList.Arguments.Count <= index)
        {
            return null;
        }

        return invocation.ArgumentList.Arguments[index].Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static string[] GetStringLiteralCollection(ExpressionSyntax expression)
    {
        return expression switch
        {
            ArrayCreationExpressionSyntax arrayCreation when arrayCreation.Initializer is not null =>
                GetStringLiteralExpressions(arrayCreation.Initializer.Expressions),
            ImplicitArrayCreationExpressionSyntax arrayCreation when arrayCreation.Initializer is not null =>
                GetStringLiteralExpressions(arrayCreation.Initializer.Expressions),
            CollectionExpressionSyntax collectionExpression =>
                [.. collectionExpression.Elements
                    .OfType<ExpressionElementSyntax>()
                    .Select(static element => element.Expression)
                    .Select(GetStringLiteral)
                    .Where(static value => value is not null)
                    .Select(static value => value!)],
            _ => [],
        };
    }

    private static string[] GetStringLiteralExpressions(SeparatedSyntaxList<ExpressionSyntax> expressions)
        => [.. expressions
            .Select(GetStringLiteral)
            .Where(static value => value is not null)
            .Select(static value => value!)];

    private static string? GetStringLiteral(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    private static bool ContainsRawMapInvocation(ExpressionSyntax? expression)
    {
        while (expression is InvocationExpressionSyntax invocation)
        {
            if (TryGetMapInvocation(invocation, out _, out _))
            {
                return true;
            }

            expression = GetReceiver(invocation);
        }

        return false;
    }

    private static string? FindLiteralMapGroupPrefix(ExpressionSyntax? expression)
    {
        List<string>? prefixes = null;
        while (expression is InvocationExpressionSyntax invocation)
        {
            if (GetInvokedName(invocation) == "MapGroup")
            {
                var prefix = GetStringLiteralArgument(invocation, 0);
                if (prefix is null)
                {
                    return null;
                }

                prefixes ??= [];
                prefixes.Add(prefix);
            }

            expression = GetReceiver(invocation);
        }

        if (prefixes is null)
        {
            return null;
        }

        prefixes.Reverse();
        var combined = prefixes[0];
        foreach (var prefix in prefixes.Skip(1))
        {
            combined = CombineRoutePatterns(combined, prefix);
        }

        return combined;
    }

    private static string CombineRoutePatterns(string? prefix, string pattern)
    {
        var routePrefix = prefix ?? "";
        if (routePrefix.Length == 0)
        {
            return pattern;
        }

        if (routePrefix == "/")
        {
            return pattern.StartsWith("/", StringComparison.Ordinal) ? pattern : "/" + pattern;
        }

        if (pattern == "/")
        {
            return routePrefix;
        }

        return routePrefix.TrimEnd('/') + "/" + pattern.TrimStart('/');
    }

    private static string RouteKey(string method, string pattern)
        => method.ToUpperInvariant() + " " + pattern;

    private static string[] SplitMethods(string methods)
        => string.IsNullOrEmpty(methods) ? [] : methods.Split(';');

    // Keep this order in sync with the emitPlan .Combine(...) chain in Initialize.
    private readonly record struct EmitPipelineInputs(
        ImmutableArray<FeatureModel> Models,
        ImmutableArray<ValidatorModel> Validators,
        string AssemblyName,
        bool EmitAspNet,
        bool EmitWasi,
        ReferencedSliceModulesResult ReferencedModules,
        bool EmitExtensions,
        JsonContextOverrides JsonContextOverrides,
        JsonContextPlan WasiJsonContextPlan,
        LambdaFunctionPerFeatureOptions LambdaOptions,
        JsonContextPlan LambdaJsonContextPlan)
    {
        public static EmitPipelineInputs FromCombined(
            ((((((((((ImmutableArray<FeatureModel>, ImmutableArray<ValidatorModel>),
                string), bool), bool), ReferencedSliceModulesResult), bool),
                JsonContextOverrides), JsonContextPlan),
                LambdaFunctionPerFeatureOptions), JsonContextPlan) pair)
        {
            var (lambdaOptionsPair, lambdaJsonContextPlan) = pair;
            var (wasiPlanPair, lambdaOptions) = lambdaOptionsPair;
            var (overridesPair, wasiJsonContextPlan) = wasiPlanPair;
            var (emitExtensionsPair, jsonContextOverrides) = overridesPair;
            var (referencedModulesPair, emitExtensions) = emitExtensionsPair;
            var (emitWasiPair, referencedModules) = referencedModulesPair;
            var (emitAspNetPair, emitWasi) = emitWasiPair;
            var (modelsAndValidatorsPair, emitAspNet) = emitAspNetPair;
            var (modelsAndValidators, asmName) = modelsAndValidatorsPair;
            var (models, validators) = modelsAndValidators;

            return new EmitPipelineInputs(
                models,
                validators,
                asmName,
                emitAspNet,
                emitWasi,
                referencedModules,
                emitExtensions,
                jsonContextOverrides,
                wasiJsonContextPlan,
                lambdaOptions,
                lambdaJsonContextPlan);
        }
    }
}

internal readonly record struct RawMinimalApiEndpoint(
    string Methods,
    string Pattern,
    string EndpointName,
    DiagnosticLocationModel Location);

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
