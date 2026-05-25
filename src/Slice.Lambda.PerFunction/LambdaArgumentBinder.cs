using System.Globalization;

namespace Slice.Lambda.PerFunction;

/// <summary>
/// Describes the result of binding a Lambda scalar argument.
/// </summary>
public enum LambdaArgumentBindingStatus
{
    /// <summary>
    /// The query parameter was present and converted successfully.
    /// </summary>
    Bound,

    /// <summary>
    /// The query parameter was not present.
    /// </summary>
    Missing,

    /// <summary>
    /// The query parameter was present but could not be converted.
    /// </summary>
    Invalid,
}

/// <summary>
/// Contains a Lambda scalar argument binding result and its converted value.
/// </summary>
/// <typeparam name="T">The target argument type.</typeparam>
/// <param name="Status">The binding status.</param>
/// <param name="Value">The converted value when <paramref name="Status"/> is <see cref="LambdaArgumentBindingStatus.Bound"/>.</param>
public readonly record struct LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus Status, T? Value);

/// <summary>
/// Converts route and query string values into scalar handler argument types.
/// </summary>
public static class LambdaArgumentBinder
{
    /// <summary>
    /// Attempts to read and convert a path parameter.
    /// </summary>
    public static bool TryGetFromRoute<T>(LambdaInvocationContext ctx, string name, out T? value)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Request.PathParameters is not null)
        {
            foreach (var pair in ctx.Request.PathParameters)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return TryConvertValue(pair.Value, out value);
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads and converts a query string parameter.
    /// </summary>
    /// <returns>
    /// <see cref="LambdaArgumentBindingStatus.Missing"/> when the query value is absent,
    /// <see cref="LambdaArgumentBindingStatus.Invalid"/> when it cannot be converted, or
    /// <see cref="LambdaArgumentBindingStatus.Bound"/> with the converted value.
    /// </returns>
    public static LambdaArgumentBindingResult<T> BindFromQuery<T>(LambdaInvocationContext ctx, string name)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Request.QueryStringParameters is null)
        {
            return new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Missing, default);
        }

        foreach (var pair in ctx.Request.QueryStringParameters)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return TryConvertValue(pair.Value, out T? value)
                    ? new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Bound, value)
                    : new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Invalid, default);
            }
        }

        return new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Missing, default);
    }

    /// <summary>
    /// Reads and converts a request header.
    /// </summary>
    /// <returns>
    /// <see cref="LambdaArgumentBindingStatus.Missing"/> when the header is absent,
    /// <see cref="LambdaArgumentBindingStatus.Invalid"/> when it cannot be converted, or
    /// <see cref="LambdaArgumentBindingStatus.Bound"/> with the converted value.
    /// </returns>
    public static LambdaArgumentBindingResult<T> BindFromHeader<T>(LambdaInvocationContext ctx, string name)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Request.Headers is null)
        {
            return new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Missing, default);
        }

        foreach (var pair in ctx.Request.Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return TryConvertValue(pair.Value, out T? value)
                    ? new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Bound, value)
                    : new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Invalid, default);
            }
        }

        return new LambdaArgumentBindingResult<T>(LambdaArgumentBindingStatus.Missing, default);
    }

    private static bool TryConvertValue<T>(string? raw, out T? value)
    {
        if (raw is null)
        {
            value = default;
            return false;
        }

        var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (Nullable.GetUnderlyingType(typeof(T)) is not null && raw.Length == 0)
        {
            value = default;
            return true;
        }

        if (t == typeof(string))
        {
            return Succeed((T)(object)raw, out value);
        }

        if (t == typeof(Guid))
        {
            return Guid.TryParse(raw, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(int))
        {
            return int.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(long))
        {
            return long.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(short))
        {
            return short.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(uint))
        {
            return uint.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(ulong))
        {
            return ulong.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(ushort))
        {
            return ushort.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(bool))
        {
            return bool.TryParse(raw, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(double))
        {
            return double.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(float))
        {
            return float.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(decimal))
        {
            return decimal.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(byte))
        {
            return byte.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(char))
        {
            return raw.Length == 1 ? Succeed((T)(object)raw[0], out value) : Fail(out value);
        }

        if (t == typeof(DateTime))
        {
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(DateTimeOffset))
        {
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(DateOnly))
        {
            return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(TimeOnly))
        {
            return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        if (t == typeof(TimeSpan))
        {
            return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var result) ? Succeed((T)(object)result, out value) : Fail(out value);
        }

        return t == typeof(Uri) && Uri.TryCreate(raw, UriKind.RelativeOrAbsolute, out var uri)
            ? Succeed((T)(object)uri, out value)
            : Fail(out value);
    }

    private static bool Succeed<T>(T result, out T? value)
    {
        value = result;
        return true;
    }

    private static bool Fail<T>(out T? value)
    {
        value = default;
        return false;
    }
}
