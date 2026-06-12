using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace SliceFx;

/// <summary>
/// Describes the result of binding a scalar argument in SliceFx NativeAOT-safe ASP.NET dispatch.
/// </summary>
public enum SliceAotArgumentBindingStatus
{
    /// <summary>The value was present and converted successfully.</summary>
    Bound,

    /// <summary>The value was not present.</summary>
    Missing,

    /// <summary>The value was present but could not be converted to the target type.</summary>
    Invalid,
}

/// <summary>
/// Contains a scalar argument binding result and its converted value.
/// </summary>
/// <typeparam name="T">The target argument type.</typeparam>
/// <param name="Status">The binding status.</param>
/// <param name="Value">The converted value when <see cref="Status"/> is <see cref="SliceAotArgumentBindingStatus.Bound"/>.</param>
public readonly record struct SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus Status, T? Value);

/// <summary>
/// Converts route, query, and header values into scalar handler argument types for
/// SliceFx NativeAOT-safe ASP.NET dispatch. Mirrors
/// <c>SliceFx.Wasi.Binding.WasiArgumentBinder</c> over <see cref="HttpContext"/> instead of
/// <c>WasiInvokerContext</c>; the scalar conversion matrix is identical.
/// </summary>
public static class SliceAotArgumentBinder
{
    /// <summary>
    /// Attempts to read and convert a captured route value.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <param name="ctx">The current HTTP context.</param>
    /// <param name="name">The route parameter name to read.</param>
    /// <param name="value">When this method returns, contains the converted value or <c>default</c>.</param>
    /// <returns><c>true</c> when the route value exists and can be converted; otherwise, <c>false</c>.</returns>
    public static bool TryGetFromRoute<T>(HttpContext ctx, string name, out T? value)
    {
        if (ctx.Request.RouteValues.TryGetValue(name, out var raw))
        {
            return TryConvertValue(raw?.ToString(), out value);
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads and converts a query string value.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <param name="ctx">The current HTTP context.</param>
    /// <param name="name">The query string key to read.</param>
    /// <returns>
    /// <see cref="SliceAotArgumentBindingStatus.Missing"/> when the query value is absent,
    /// <see cref="SliceAotArgumentBindingStatus.Invalid"/> when it cannot be converted, or
    /// <see cref="SliceAotArgumentBindingStatus.Bound"/> with the converted value.
    /// </returns>
    public static SliceAotArgumentBindingResult<T> BindFromQuery<T>(HttpContext ctx, string name)
    {
        var queryValue = ctx.Request.Query[name];
        if (queryValue.Count == 0)
        {
            return new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Missing, default);
        }

        var raw = queryValue.ToString();

        // An empty value for a nullable value-type is treated as Missing — the client should
        // omit null params entirely instead of emitting "name=". string? stays Bound("").
        if (raw.Length == 0 && Nullable.GetUnderlyingType(typeof(T)) is not null)
        {
            return new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Missing, default);
        }

        return TryConvertValue(raw, out T? value)
            ? new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Bound, value)
            : new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Invalid, default);
    }

    /// <summary>
    /// Reads and converts a request header value.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <param name="ctx">The current HTTP context.</param>
    /// <param name="name">The header name to read (case-insensitive).</param>
    /// <returns>
    /// <see cref="SliceAotArgumentBindingStatus.Missing"/> when the header is absent,
    /// <see cref="SliceAotArgumentBindingStatus.Invalid"/> when it cannot be converted, or
    /// <see cref="SliceAotArgumentBindingStatus.Bound"/> with the converted value.
    /// </returns>
    public static SliceAotArgumentBindingResult<T> BindFromHeader<T>(HttpContext ctx, string name)
    {
        var headerValue = ctx.Request.Headers[name];
        if (headerValue.Count == 0)
        {
            return new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Missing, default);
        }

        var raw = headerValue.ToString();
        return TryConvertValue(raw, out T? value)
            ? new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Bound, value)
            : new SliceAotArgumentBindingResult<T>(SliceAotArgumentBindingStatus.Invalid, default);
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
            return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts) ? Succeed((T)(object)ts, out value) : Fail(out value);
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
