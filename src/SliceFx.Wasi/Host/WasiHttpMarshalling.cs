using System.Globalization;
using System.Text;

namespace SliceFx.Wasi;

/// <summary>
/// Pure-logic helpers for the boundary between wasi:http WIT types and the
/// <see cref="WasiRequest"/> / <see cref="WasiResponse"/> abstractions.
/// </summary>
/// <remarks>
/// All methods operate on primitive types (<c>byte[]</c>, <c>string</c>,
/// <c>IEnumerable&lt;&gt;</c>) and carry no dependency on WIT-generated types
/// (<c>ITypes.*</c>, <c>ProxyWorld.*</c>, etc.).  Call sites only cross the
/// WIT boundary once — to marshal raw bytes in or out — then delegate the rest
/// here to avoid repeating the same encoding / splitting logic per application.
/// </remarks>
public static class WasiHttpMarshalling
{
    /// <summary>
    /// Splits a wasi:http <c>path-with-query</c> value into path and optional query-string components.
    /// </summary>
    /// <param name="pathWithQuery">The raw path-with-query string from <c>IncomingRequest.PathWithQuery()</c>.</param>
    /// <param name="path">The path component (without the leading <c>?</c> delimiter).</param>
    /// <param name="query">
    /// The query-string component (without the leading <c>?</c>), or <c>null</c> when no
    /// <c>?</c> is present.
    /// </param>
    public static void SplitPathAndQuery(string pathWithQuery, out string path, out string? query)
    {
        var qIndex = pathWithQuery.IndexOf('?');
        path = qIndex >= 0 ? pathWithQuery[..qIndex] : pathWithQuery;
        query = qIndex >= 0 ? pathWithQuery[(qIndex + 1)..] : null;
    }

    /// <summary>
    /// Decodes a sequence of wasi:http header-field entries into a case-insensitive
    /// <see cref="Dictionary{TKey,TValue}">dictionary</see> of string values.
    /// </summary>
    /// <param name="entries">
    /// Header entry pairs as returned by <c>ITypes.Fields.Entries()</c>: name is an ASCII
    /// string; value is a UTF-8 byte sequence.  Non-UTF-8 values fall back to Latin-1.
    /// </param>
    /// <returns>A case-insensitive header dictionary.</returns>
    public static Dictionary<string, string> ParseHeaders(IEnumerable<(string Name, byte[] Value)> entries)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in entries)
        {
            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(value);
            }
            catch (Exception)
            {
                // Non-UTF-8 header value — fall back to Latin-1 (ISO-8859-1).
                decoded = Encoding.Latin1.GetString(value);
            }

            result[name] = decoded;
        }

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> when the declared <c>Content-Length</c> is within
    /// <paramref name="maxBodyBytes"/>; <c>false</c> when the declared length exceeds the limit.
    /// Returns <c>true</c> when <c>Content-Length</c> is absent or unparseable (unknown
    /// body size — callers may still enforce the limit during streaming).
    /// </summary>
    /// <param name="headers">The parsed request header dictionary.</param>
    /// <param name="maxBodyBytes">Maximum allowed body size in bytes.</param>
    public static bool IsBodySizeWithinLimit(IReadOnlyDictionary<string, string> headers, int maxBodyBytes)
    {
        if (headers.TryGetValue("Content-Length", out var contentLength)
            && long.TryParse(contentLength, NumberStyles.None, CultureInfo.InvariantCulture, out var declared))
        {
            return declared <= maxBodyBytes;
        }

        return true;
    }

    /// <summary>
    /// Encodes a <see cref="WasiResponse"/> header dictionary into the lowercase-name,
    /// UTF-8-encoded byte-value pairs expected by wasi:http <c>Fields.FromList</c>.
    /// </summary>
    /// <param name="headers">The response header dictionary from <see cref="WasiResponse.Headers"/>.</param>
    /// <returns>A list of <c>(lowercase name, UTF-8 encoded value)</c> pairs.</returns>
    public static IReadOnlyList<(string Name, byte[] Value)> FormatResponseHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var result = new List<(string, byte[])>(headers.Count);
        foreach (var (k, v) in headers)
        {
            result.Add((k.ToLowerInvariant(), Encoding.UTF8.GetBytes(v)));
        }

        return result;
    }
}
