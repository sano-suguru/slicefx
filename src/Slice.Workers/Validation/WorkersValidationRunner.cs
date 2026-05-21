using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Slice.Workers.Validation;

/// <summary>
/// Runs DataAnnotations validation for request models in the Slice Workers dispatch path.
/// </summary>
public static class WorkersValidationRunner
{
    /// <summary>
    /// Validates a value using public property attributes, matching primary-constructor parameter attributes, type attributes, and <see cref="IValidatableObject"/>.
    /// </summary>
    /// <typeparam name="T">The model type to validate.</typeparam>
    /// <param name="value">The value to validate.</param>
    /// <returns>
    /// A dictionary of field names to validation messages when validation fails; otherwise, <c>null</c>.
    /// Returns <c>null</c> when the type has no applicable validation metadata.
    /// </returns>
    public static IReadOnlyDictionary<string, string[]>? Validate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] T>(T value) where T : notnull
    {
        var plan = ValidationPlanCache<T>.Plan;
        if (plan is null)
        {
            return null;
        }

        var errors = plan.Validate(value);
        return errors.Count == 0 ? null : errors;
    }

    private static class ValidationPlanCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] T>
    {
        public static readonly TypeValidationPlan? Plan = TypeValidationPlan.Create(typeof(T));
    }

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

        public static TypeValidationPlan? Create([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
        {
            var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

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

                List<ValidationAttribute> attrs = [.. prop.GetCustomAttributes<ValidationAttribute>(inherit: true)];

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

        private static void AddValidationResult(Dictionary<string, List<string>> errors, ValidationResult? result, string modelErrorKey)
        {
            if (result is null || ReferenceEquals(result, ValidationResult.Success))
            {
                return;
            }

            var memberNames = result.MemberNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            var message = result.ErrorMessage ?? $"{modelErrorKey} is invalid.";
            if (memberNames.Length == 0) { AddError(errors, modelErrorKey, message); return; }
            foreach (var m in memberNames)
            {
                AddError(errors, m, message);
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

    private static Func<object, object?> CreateGetter(PropertyInfo property)
        => instance => property.GetValue(instance);
}
