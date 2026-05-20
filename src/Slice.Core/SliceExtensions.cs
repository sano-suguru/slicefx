using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Slice;

/// <summary>
/// Extension methods for registering Slice services and mapping Slice features.
/// </summary>
public static class SliceExtensions
{
    /// <summary>
    /// Registers Slice services. Discovers feature filters declared via <c>[Filter&lt;T&gt;]</c>
    /// and registers them as scoped services (one instance per request).
    /// </summary>
    /// <param name="services">The service collection to add Slice services to.</param>
    /// <param name="assemblies">
    /// Assemblies to scan for features. When omitted, the application entry assembly is used.
    /// </param>
    /// <returns>The same service collection so calls can be chained.</returns>
    public static IServiceCollection AddSlice(this IServiceCollection services, params ReadOnlySpan<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var toScan = ResolveAssemblies(assemblies);

        var filterTypes = new HashSet<Type>();
        foreach (var assembly in toScan)
        {
            // Use source-generated registrations when available (AOT-friendly path).
            if (TryInvokeAddGenerated(assembly, services))
            {
                continue;
            }

            foreach (var type in GetTypesForScanning(assembly))
            {
                if (type.GetCustomAttribute<FeatureAttribute>() is null)
                {
                    continue;
                }

                foreach (var f in type.GetCustomAttributes(inherit: false).OfType<IFilterAttribute>())
                {
                    filterTypes.Add(f.FilterType);
                }
            }
        }

        foreach (var ft in filterTypes)
        {
            services.AddScoped(ft);
        }

        return services;
    }

    /// <summary>
    /// Discovers every [Feature]-attributed class in the given assemblies (defaults to the entry assembly)
    /// and maps it to a Minimal API endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map features onto.</param>
    /// <param name="assemblies">
    /// Assemblies to scan for features. When omitted, the application entry assembly is used.
    /// </param>
    /// <returns>The same endpoint route builder so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapSlices(
        this IEndpointRouteBuilder endpoints,
        params ReadOnlySpan<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var toScan = ResolveAssemblies(assemblies);
        var endpointNames = CollectExistingEndpointNames(endpoints);

        foreach (var assembly in toScan)
        {
            // Use source-generated registrations when available (AOT-friendly path).
            if (TryInvokeMapGenerated(assembly, endpoints))
            {
                continue;
            }

            foreach (var type in GetTypesForScanning(assembly))
            {
                var attr = type.GetCustomAttribute<FeatureAttribute>();
                if (attr is null)
                {
                    continue;
                }

                MapFeature(endpoints, type, attr, endpointNames);
            }
        }

        return endpoints;
    }

    private static void MapFeature(
        IEndpointRouteBuilder endpoints,
        Type featureType,
        FeatureAttribute attr,
        Dictionary<string, Type?> endpointNames)
    {
        var handle = FindHandleMethod(featureType);

        var tag = attr.Tag ?? InferTag(featureType);
        var endpointName = $"{tag}.{featureType.Name}";
        if (endpointNames.TryGetValue(endpointName, out var existingFeatureType))
        {
            var existingName = existingFeatureType?.FullName ?? "an already mapped endpoint";
            throw new InvalidOperationException(
                $"Duplicate Slice endpoint name '{endpointName}' for features " +
                $"'{existingName}' and '{featureType.FullName}'. " +
                "Use distinct feature class names or set FeatureAttribute.Tag to disambiguate.");
        }
        endpointNames.Add(endpointName, featureType);

        // Build a strongly-typed delegate so Minimal API can do its normal parameter binding.
        var paramTypes = handle.GetParameters().Select(p => p.ParameterType).ToList();
        paramTypes.Add(handle.ReturnType);
        var delegateType = System.Linq.Expressions.Expression.GetDelegateType([.. paramTypes]);
        var del = handle.CreateDelegate(delegateType);

        var route = endpoints.MapMethods(attr.Pattern, [attr.Method], del);

        // Apply validation only to request DTO parameters, not DI services.
        var validationFilter = DataAnnotationsValidationFilter.Create(handle, endpoints.ServiceProvider);
        if (validationFilter is not null)
        {
            route.AddEndpointFilter(validationFilter);
        }

        // Apply per-feature filters declared via [Filter<T>] (in declaration order: outermost first).
        // Resolved per-request from DI — no reflection on EndpointFilterExtensions needed.
        foreach (var f in featureType.GetCustomAttributes(inherit: false).OfType<IFilterAttribute>())
        {
            var filterType = f.FilterType;
            route.AddEndpointFilter(async (ctx, next) =>
            {
                var filter = (IEndpointFilter)ctx.HttpContext.RequestServices.GetRequiredService(filterType);
                return await filter.InvokeAsync(ctx, next).ConfigureAwait(false);
            });
        }

        // Metadata: tag + summary
        route.WithTags(tag);
        if (!string.IsNullOrWhiteSpace(attr.Summary))
        {
            route.WithSummary(attr.Summary);
        }

        route.WithName(endpointName);
    }

    // Looks for the source-generated class produced by Slice.SourceGenerator.
    // Returns the type if found, null otherwise.
    private static Type? FindGeneratedRegistrationsType(Assembly assembly)
    {
        var sanitized = ToGeneratedIdentifier(assembly.GetName().Name ?? "Unknown");
        return assembly.GetType($"Slice.Generated.{sanitized}_SliceRegistrations");
    }

    private static MethodInfo FindHandleMethod(Type featureType)
    {
        var handles = featureType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method => method.Name == "Handle")
            .ToArray();

        return handles.Length switch
        {
            1 => handles[0],
            0 => throw new InvalidOperationException(
                $"Feature '{featureType.FullName}' must define exactly one public static 'Handle' method."),
            _ => throw new InvalidOperationException(
                $"Feature '{featureType.FullName}' defines multiple public static 'Handle' methods. " +
                "Slice features must expose exactly one handler method.")
        };
    }

    private static Dictionary<string, Type?> CollectExistingEndpointNames(IEndpointRouteBuilder endpoints)
    {
        var endpointNames = new Dictionary<string, Type?>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints.DataSources.SelectMany(static source => source.Endpoints))
        {
            var endpointName = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            if (!string.IsNullOrWhiteSpace(endpointName))
            {
                endpointNames.TryAdd(endpointName, null);
            }
        }

        return endpointNames;
    }

    private static string ToGeneratedIdentifier(string value)
    {
        var chars = value.Length == 0 ? "Unknown" : value;
        var sb = new System.Text.StringBuilder(chars.Length + 1);
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (i == 0 && !IsIdentifierStart(ch))
            {
                sb.Append('_');
            }

            sb.Append(IsIdentifierPart(ch) ? ch : '_');
        }

        return sb.Length == 0 ? "Unknown" : sb.ToString();
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsLetterOrDigit(value);

    private static bool TryInvokeAddGenerated(Assembly assembly, IServiceCollection services)
    {
        var generated = FindGeneratedRegistrationsType(assembly);
        if (generated is null)
        {
            return false;
        }

        var method = generated.GetMethod("AddSliceGenerated", BindingFlags.Public | BindingFlags.Static);
        if (method is null)
        {
            return false;
        }

        method.Invoke(null, [services]);
        return true;
    }

    private static bool TryInvokeMapGenerated(Assembly assembly, IEndpointRouteBuilder endpoints)
    {
        var generated = FindGeneratedRegistrationsType(assembly);
        if (generated is null)
        {
            return false;
        }

        var method = generated.GetMethod("MapSlicesGenerated", BindingFlags.Public | BindingFlags.Static);
        if (method is null)
        {
            return false;
        }

        method.Invoke(null, [endpoints]);
        return true;
    }

    private static Type[] GetTypesForScanning(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaderMessages = ex.LoaderExceptions
                .Where(e => e is not null)
                .Select(e => $"{e!.GetType().Name}: {e.Message}");

            var message = $"Slice could not scan assembly '{assembly.FullName}' because one or more types failed to load.";
            var details = string.Join(Environment.NewLine, loaderMessages);
            if (!string.IsNullOrWhiteSpace(details))
            {
                message += Environment.NewLine + details;
            }

            throw new InvalidOperationException(message, ex);
        }
    }

    private static Assembly[] ResolveAssemblies(ReadOnlySpan<Assembly> assemblies)
    {
        if (!assemblies.IsEmpty)
        {
            return assemblies.ToArray();
        }

        var entryAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "Slice could not determine the entry assembly. Pass assemblies explicitly to AddSlice(...) and MapSlices(...).");

        return [entryAssembly];
    }

    private static string InferTag(Type t)
    {
        // Features/Users/CreateUser.cs => namespace ...Features.Users => tag "Users"
        var ns = t.Namespace ?? "";
        var idx = ns.IndexOf(".Features.", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var rest = ns[(idx + ".Features.".Length)..];
            var dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest[..dot];
        }
        return "Default";
    }
}
