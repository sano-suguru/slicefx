using System.Globalization;

namespace Slice.Lambda.PerFunction;

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
    /// Attempts to read and convert a query string parameter.
    /// </summary>
    public static bool TryGetFromQuery<T>(LambdaInvocationContext ctx, string name, out T? value)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        value = default;
        if (ctx.Request.QueryStringParameters is null)
        {
            return true;
        }

        foreach (var pair in ctx.Request.QueryStringParameters)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return TryConvertValue(pair.Value, out value);
            }
        }

        return true;
    }

    private static bool TryConvertValue<T>(string raw, out T? value)
    {
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
