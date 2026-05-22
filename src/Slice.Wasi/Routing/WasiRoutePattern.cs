using System.Text.RegularExpressions;

namespace Slice.Wasi.Routing;

internal sealed class WasiRoutePattern
{
    private readonly Regex _regex;
    private readonly RouteParameter[] _parameters;

    public WasiRoutePattern(string pattern)
    {
        var (regex, parameters) = BuildRegex(pattern);
        _regex = regex;
        _parameters = parameters;
    }

    public bool TryMatch(string path, out IReadOnlyDictionary<string, string> routeValues)
    {
        var m = _regex.Match(path);
        if (!m.Success)
        {
            routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return false;
        }

        if (_parameters.Length == 0)
        {
            routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in _parameters)
        {
            var g = m.Groups[parameter.GroupName];
            if (g.Success)
            {
                values[parameter.Name] = g.Value;
            }
        }
        routeValues = values;
        return true;
    }

    private static (Regex, RouteParameter[]) BuildRegex(string pattern)
    {
        var parameters = new List<RouteParameter>();
        var regexStr = "^";
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '{')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '{')
                {
                    regexStr += Regex.Escape("{");
                    i += 2;
                    continue;
                }

                var end = pattern.IndexOf('}', i);
                if (end < 0) { regexStr += Regex.Escape(pattern[i..]); break; }
                var param = pattern.Substring(i + 1, end - i - 1);
                var colon = param.IndexOf(':');
                var name = colon >= 0 ? param[..colon] : param;
                var constraint = colon >= 0 ? param[(colon + 1)..] : null;
                var groupName = "__p" + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                parameters.Add(new RouteParameter(name, groupName));
                var groupPattern = constraint?.ToLowerInvariant() switch
                {
                    "guid" => "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
                    "int" => @"-?\d+",
                    "long" => @"-?\d+",
                    _ => "[^/]+"
                };
                regexStr += $"(?<{groupName}>{groupPattern})";
                i = end + 1;
            }
            else if (c == '}' && i + 1 < pattern.Length && pattern[i + 1] == '}')
            {
                regexStr += Regex.Escape("}");
                i += 2;
            }
            else
            {
                regexStr += Regex.Escape(c.ToString());
                i++;
            }
        }
        regexStr += "$";
        return (
            new Regex(regexStr, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
            [.. parameters]);
    }

    private sealed record RouteParameter(string Name, string GroupName);
}
