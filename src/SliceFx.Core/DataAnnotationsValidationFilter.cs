using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx;

/// <summary>
/// Endpoint filter that validates selected request arguments using
/// <see cref="System.ComponentModel.DataAnnotations"/>.
/// Reads validation attributes from both properties AND matching constructor parameters,
/// so positional records like <c>record Request([Required] string Name)</c> just work.
/// </summary>
public sealed class DataAnnotationsValidationFilter : IEndpointFilter
{
    private readonly ParameterValidation[] _parameters;

    private DataAnnotationsValidationFilter(ParameterValidation[] parameters) => _parameters = parameters;

    /// <summary>
    /// Creates a validation filter for the request-like parameters accepted by a feature handler.
    /// </summary>
    /// <param name="handle">The public static feature handler method.</param>
    /// <param name="services">The application service provider used to identify service parameters.</param>
    /// <returns>
    /// A configured validation filter, or <see langword="null"/> when no parameters require validation.
    /// </returns>
    public static DataAnnotationsValidationFilter? Create(MethodInfo handle, IServiceProvider services)
    {
        var serviceProviderIsService = services.GetService<IServiceProviderIsService>();
        var parameters = handle.GetParameters();
        var validations = new List<ParameterValidation>();

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (!ShouldValidate(parameter, serviceProviderIsService))
            {
                continue;
            }

            var typePlan = TypeValidationPlan.Create(parameter.ParameterType);
            if (typePlan is not null)
            {
                validations.Add(new ParameterValidation(i, typePlan));
            }
        }

        return validations.Count == 0 ? null : new DataAnnotationsValidationFilter([.. validations]);
    }

    /// <summary>
    /// Validates configured endpoint arguments before continuing the endpoint pipeline.
    /// </summary>
    /// <param name="context">The current endpoint filter invocation context.</param>
    /// <param name="next">The next filter or endpoint delegate.</param>
    /// <returns>The endpoint result, or a validation problem response when validation fails.</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var parameter in _parameters)
        {
            var arg = context.Arguments[parameter.Index];
            if (arg is null)
            {
                continue;
            }

            var errors = parameter.TypePlan.Validate(arg);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Factory delegate for use with <c>AddEndpointFilterFactory</c> in source-generated registrations.
    /// Builds the validation filter from the endpoint's MethodInfo at startup, then returns a
    /// per-request delegate (or passes through unchanged if no parameters require validation).
    /// </summary>
    /// <param name="context">The endpoint filter factory context.</param>
    /// <param name="next">The next filter or endpoint delegate.</param>
    /// <returns>An endpoint filter delegate with validation applied when needed.</returns>
    public static EndpointFilterDelegate CreateFilterFactory(
        EndpointFilterFactoryContext context,
        EndpointFilterDelegate next)
    {
        var filter = Create(context.MethodInfo, context.ApplicationServices);
        return filter is null ? next : (invCtx => filter.InvokeAsync(invCtx, next));
    }

    private static bool ShouldValidate(ParameterInfo parameter, IServiceProviderIsService? serviceProviderIsService)
    {
        var type = parameter.ParameterType;
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        return !effectiveType.IsByRef
            && !effectiveType.IsPointer
            && !effectiveType.IsInterface
            && !effectiveType.IsAbstract
            && !typeof(Delegate).IsAssignableFrom(effectiveType)
            && !IsSimpleType(effectiveType)
            && !IsFrameworkType(effectiveType) && serviceProviderIsService?.IsService(type) != true
            && serviceProviderIsService?.IsService(effectiveType) != true;
    }

    private static bool IsSimpleType(Type type)
        => type.IsPrimitive
           || type.IsEnum
           || type == typeof(string)
           || type == typeof(decimal)
           || type == typeof(DateTime)
           || type == typeof(DateTimeOffset)
           || type == typeof(DateOnly)
           || type == typeof(TimeOnly)
           || type == typeof(TimeSpan)
           || type == typeof(Guid)
           || type == typeof(Uri);

    private static bool IsFrameworkType(Type type)
        => type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
           || type.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) == true;

    private sealed class TypeValidationPlan
    {
        private readonly string _modelErrorKey;
        private readonly PropertyValidation[] _properties;
        private readonly ValidationAttribute[] _typeAttributes;
        private readonly bool _validatesSelf;

        private TypeValidationPlan(
            string modelErrorKey,
            PropertyValidation[] properties,
            ValidationAttribute[] typeAttributes,
            bool validatesSelf)
        {
            _modelErrorKey = modelErrorKey;
            _properties = properties;
            _typeAttributes = typeAttributes;
            _validatesSelf = validatesSelf;
        }

        public static TypeValidationPlan? Create(Type type)
        {
            var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

            // For records: the primary constructor's parameter attributes are the ones the user wrote.
            var primaryCtorParams = effectiveType.GetConstructors()
                .MaxBy(c => c.GetParameters().Length)
                ?.GetParameters()
                .Where(p => p.Name is not null)
                .ToDictionary(p => p.Name!, StringComparer.Ordinal)
                ?? [];

            var properties = new List<PropertyValidation>();
            foreach (var prop in effectiveType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetMethod is null)
                {
                    continue;
                }

                // Attributes from the property itself
                List<ValidationAttribute> attrs = [.. prop.GetCustomAttributes<ValidationAttribute>(inherit: true)];

                // Plus attributes from the matching primary-ctor parameter (records)
                if (primaryCtorParams.TryGetValue(prop.Name, out var matching))
                {
                    attrs.AddRange(matching.GetCustomAttributes<ValidationAttribute>(inherit: true));
                }

                if (attrs.Count == 0)
                {
                    continue;
                }

                properties.Add(new PropertyValidation(prop.Name, CreateGetter(prop), [.. attrs]));
            }

            var typeAttributes = effectiveType.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
            var validatesSelf = typeof(IValidatableObject).IsAssignableFrom(effectiveType);

            return properties.Count == 0 && typeAttributes.Length == 0 && !validatesSelf
                ? null
                : new TypeValidationPlan(effectiveType.Name, [.. properties], typeAttributes, validatesSelf);
        }

        public Dictionary<string, string[]> Validate(object instance)
        {
            var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var prop in _properties)
            {
                var value = prop.GetValue(instance);
                var vctx = new ValidationContext(instance) { MemberName = prop.Name };

                foreach (var attr in prop.Attributes)
                {
                    var result = attr.GetValidationResult(value, vctx);
                    if (result is null || ReferenceEquals(result, ValidationResult.Success))
                    {
                        continue;
                    }

                    AddError(errors, prop.Name, result.ErrorMessage ?? $"{prop.Name} is invalid.");
                }
            }

            var objectContext = new ValidationContext(instance);
            foreach (var attr in _typeAttributes)
            {
                AddValidationResult(errors, attr.GetValidationResult(instance, objectContext), _modelErrorKey);
            }

            if (_validatesSelf && instance is IValidatableObject validatable)
            {
                foreach (var result in validatable.Validate(objectContext))
                {
                    AddValidationResult(errors, result, _modelErrorKey);
                }
            }

            return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        private static void AddValidationResult(
            Dictionary<string, List<string>> errors,
            ValidationResult? result,
            string modelErrorKey)
        {
            if (result is null || ReferenceEquals(result, ValidationResult.Success))
            {
                return;
            }

            var memberNames = result.MemberNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            var message = result.ErrorMessage ?? $"{modelErrorKey} is invalid.";

            if (memberNames.Length == 0)
            {
                AddError(errors, modelErrorKey, message);
                return;
            }

            foreach (var memberName in memberNames)
            {
                AddError(errors, memberName, message);
            }
        }

        private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
        {
            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(errors, key, out _);
            list ??= [];
            list.Add(message);
        }
    }

    private sealed class PropertyValidation(string name, Func<object, object?> getValue, ValidationAttribute[] attributes)
    {
        public string Name { get; } = name;
        public Func<object, object?> GetValue { get; } = getValue;
        public ValidationAttribute[] Attributes { get; } = attributes;
    }

    private sealed class ParameterValidation(int index, TypeValidationPlan typePlan)
    {
        public int Index { get; } = index;
        public TypeValidationPlan TypePlan { get; } = typePlan;
    }

    private static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        var getMethod = property.GetMethod
            ?? throw new InvalidOperationException($"Property '{property.DeclaringType?.FullName}.{property.Name}' must have a getter.");
        var helper = typeof(DataAnnotationsValidationFilter)
            .GetMethod(nameof(CreateGetterCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(getMethod.DeclaringType!, property.PropertyType);
        return (Func<object, object?>)helper.Invoke(null, [getMethod])!;
    }

    private static Func<object, object?> CreateGetterCore<TDeclaring, TValue>(MethodInfo getMethod)
    {
        var getter = getMethod.CreateDelegate<Func<TDeclaring, TValue>>();
        return instance => getter((TDeclaring)instance);
    }
}
