using System.Globalization;
using SliceFx.Wasi.Routing;

namespace SliceFx.Wasi.Binding;

/// <summary>
/// Describes the result of binding a WASI scalar argument.
/// </summary>
public enum WasiArgumentBindingStatus
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
/// Contains a WASI scalar argument binding result and its converted value.
/// </summary>
/// <typeparam name="T">The target argument type.</typeparam>
/// <param name="Status">The binding status.</param>
/// <param name="Value">The converted value when <paramref name="Status"/> is <see cref="WasiArgumentBindingStatus.Bound"/>.</param>
public readonly record struct WasiArgumentBindingResult<T>(WasiArgumentBindingStatus Status, T? Value);

/// <summary>
/// Converts route and query string values into scalar handler argument types for Slice WASI.
/// </summary>
public static class WasiArgumentBinder
{
    /// <summary>
    /// Attempts to read and convert a captured route value.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <param name="ctx">The current WASI invoker context.</param>
    /// <param name="name">The route parameter name to read.</param>
    /// <param name="value">When this method returns, contains the converted value or <c>default</c>.</param>
    /// <returns><c>true</c> when the route value exists and can be converted; otherwise, <c>false</c>.</returns>
    public static bool TryGetFromRoute<T>(WasiInvokerContext ctx, string name, out T? value)
    {
        if (ctx.RouteValues.TryGetValue(name, out var raw))
        {
            return TryConvertValue(raw, out value);
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads and converts a query string value.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <param name="ctx">The current WASI invoker context.</param>
    /// <param name="name">The query string key to read.</param>
    /// <returns>
    /// <see cref="WasiArgumentBindingStatus.Missing"/> when the query value is absent,
    /// <see cref="WasiArgumentBindingStatus.Invalid"/> when it cannot be converted, or
    /// <see cref="WasiArgumentBindingStatus.Bound"/> with the converted value.
    /// </returns>
    public static WasiArgumentBindingResult<T> BindFromQuery<T>(WasiInvokerContext ctx, string name)
    {
        var qs = ctx.Request.QueryString;
        if (string.IsNullOrEmpty(qs))
        {
            return new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Missing, default);
        }

        var query = qs.TrimStart('?');
        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            var eq = pair.IndexOf('=');
            var rawKey = eq < 0 ? pair : pair[..eq];
            var key = Uri.UnescapeDataString(rawKey);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                if (eq < 0)
                {
                    continue;
                }

                return TryConvertValue(Uri.UnescapeDataString(pair[(eq + 1)..]), out T? value)
                    ? new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Bound, value)
                    : new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Invalid, default);
            }
        }

        return new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Missing, default);
    }

    /// <summary>
    /// Reads and converts a request header.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <param name="ctx">The current WASI invoker context.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>
    /// <see cref="WasiArgumentBindingStatus.Missing"/> when the header is absent,
    /// <see cref="WasiArgumentBindingStatus.Invalid"/> when it cannot be converted, or
    /// <see cref="WasiArgumentBindingStatus.Bound"/> with the converted value.
    /// </returns>
    public static WasiArgumentBindingResult<T> BindFromHeader<T>(WasiInvokerContext ctx, string name)
    {
        foreach (var pair in ctx.Request.Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return TryConvertValue(pair.Value, out T? value)
                    ? new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Bound, value)
                    : new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Invalid, default);
            }
        }

        return new WasiArgumentBindingResult<T>(WasiArgumentBindingStatus.Missing, default);
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
            return Guid.TryParse(raw, out var g) ? Succeed((T)(object)g, out value) : Fail(out value);
        }

        if (t == typeof(int))
        {
            return int.TryParse(raw, CultureInfo.InvariantCulture, out var n) ? Succeed((T)(object)n, out value) : Fail(out value);
        }

        if (t == typeof(long))
        {
            return long.TryParse(raw, CultureInfo.InvariantCulture, out var l) ? Succeed((T)(object)l, out value) : Fail(out value);
        }

        if (t == typeof(short))
        {
            return short.TryParse(raw, CultureInfo.InvariantCulture, out var s) ? Succeed((T)(object)s, out value) : Fail(out value);
        }

        if (t == typeof(uint))
        {
            return uint.TryParse(raw, CultureInfo.InvariantCulture, out var ui) ? Succeed((T)(object)ui, out value) : Fail(out value);
        }

        if (t == typeof(ulong))
        {
            return ulong.TryParse(raw, CultureInfo.InvariantCulture, out var ul) ? Succeed((T)(object)ul, out value) : Fail(out value);
        }

        if (t == typeof(ushort))
        {
            return ushort.TryParse(raw, CultureInfo.InvariantCulture, out var us) ? Succeed((T)(object)us, out value) : Fail(out value);
        }

        if (t == typeof(bool))
        {
            return bool.TryParse(raw, out var b) ? Succeed((T)(object)b, out value) : Fail(out value);
        }

        if (t == typeof(double))
        {
            return double.TryParse(raw, CultureInfo.InvariantCulture, out var d) ? Succeed((T)(object)d, out value) : Fail(out value);
        }

        if (t == typeof(float))
        {
            return float.TryParse(raw, CultureInfo.InvariantCulture, out var f) ? Succeed((T)(object)f, out value) : Fail(out value);
        }

        if (t == typeof(decimal))
        {
            return decimal.TryParse(raw, CultureInfo.InvariantCulture, out var m) ? Succeed((T)(object)m, out value) : Fail(out value);
        }

        if (t == typeof(byte))
        {
            return byte.TryParse(raw, CultureInfo.InvariantCulture, out var by) ? Succeed((T)(object)by, out value) : Fail(out value);
        }

        if (t == typeof(char))
        {
            return raw.Length == 1 ? Succeed((T)(object)raw[0], out value) : Fail(out value);
        }

        if (t == typeof(DateTime))
        {
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, out var dt) ? Succeed((T)(object)dt, out value) : Fail(out value);
        }

        if (t == typeof(DateTimeOffset))
        {
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, out var dto) ? Succeed((T)(object)dto, out value) : Fail(out value);
        }

        if (t == typeof(DateOnly))
        {
            return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var da) ? Succeed((T)(object)da, out value) : Fail(out value);
        }

        if (t == typeof(TimeOnly))
        {
            return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out var to) ? Succeed((T)(object)to, out value) : Fail(out value);
        }

        if (t == typeof(TimeSpan))
        {
            if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts))
            {
                return Succeed((T)(object)ts, out value);
            }

            return Fail(out value);
        }

        return t == typeof(Uri)
            && Uri.TryCreate(raw, UriKind.RelativeOrAbsolute, out var uri)
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
