using System.ComponentModel;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SliceFx;

/// <summary>
/// Provides the startup migration audit used by generated Slice registrations.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SliceMigrationAuditEndpointRouteBuilderExtensions
{
    private static readonly Action<ILogger, string, Exception?> LogMigrationAuditWarning =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "SliceMigrationAuditWarning"),
            "{Message}");

    /// <summary>
    /// Audits currently mapped endpoints for duplicate route candidates and endpoint names.
    /// </summary>
    /// <param name="app">The endpoint route builder to inspect.</param>
    /// <returns>The same endpoint route builder for chaining.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IEndpointRouteBuilder RunSliceMigrationAudit(this IEndpointRouteBuilder app)
    {
        var mode = ResolveMode(app.ServiceProvider);
        if (mode == SliceMigrationAuditMode.Off)
        {
            return app;
        }

        var report = SliceMigrationAudit.CreateReport(app.DataSources.SelectMany(static dataSource => dataSource.Endpoints));
        if (report is null)
        {
            return app;
        }

        if (mode == SliceMigrationAuditMode.Throw)
        {
            throw new InvalidOperationException(report);
        }

        var logger = app.ServiceProvider.GetService<ILoggerFactory>()?
            .CreateLogger("SliceFx.MigrationAudit");
        if (logger is not null)
        {
            LogMigrationAuditWarning(logger, report, null);
        }

        return app;
    }

    private static SliceMigrationAuditMode ResolveMode(IServiceProvider serviceProvider)
    {
        var configured = serviceProvider.GetService<IConfiguration>()?["SliceFx:MigrationAudit"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Enum.TryParse<SliceMigrationAuditMode>(configured, ignoreCase: true, out var mode)
                ? mode
                : throw new InvalidOperationException(
                    $"SliceFx:MigrationAudit value '{configured}' is invalid. Use Off, Warn, or Throw.");
        }

        return serviceProvider.GetService<IHostEnvironment>()?.IsDevelopment() == true
            ? SliceMigrationAuditMode.Warn
            : SliceMigrationAuditMode.Off;
    }
}

internal enum SliceMigrationAuditMode
{
    Off,
    Warn,
    Throw,
}

internal static class SliceMigrationAudit
{
    public static string? CreateReport(IEnumerable<Endpoint> endpoints)
    {
        var routeIssues = FindDuplicateRoutes(endpoints).ToArray();
        var nameIssues = FindDuplicateNames(endpoints).ToArray();
        if (routeIssues.Length == 0 && nameIssues.Length == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("SliceFx migration audit found duplicate endpoint metadata.");
        sb.AppendLine("Set SliceFx:MigrationAudit to Off, Warn, or Throw to control this startup check.");

        if (routeIssues.Length > 0)
        {
            sb.AppendLine("Duplicate route candidates:");
            foreach (var issue in routeIssues)
            {
                sb.AppendLine(CultureInvariant($"- {issue.Method} {issue.Pattern}: {issue.DisplayNames}"));
            }
        }

        if (nameIssues.Length > 0)
        {
            sb.AppendLine("Duplicate endpoint names:");
            foreach (var issue in nameIssues)
            {
                sb.AppendLine(CultureInvariant($"- {issue.Name}: {issue.DisplayNames}"));
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<RouteIssue> FindDuplicateRoutes(IEnumerable<Endpoint> endpoints)
    {
        var records = endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(CreateRouteRecords)
            .GroupBy(static record => (record.Method, record.Pattern), RouteRecordComparer.OrdinalIgnoreCaseMethod)
            .Where(static group => group.Skip(1).Any());

        foreach (var group in records)
        {
            yield return new RouteIssue(
                group.Key.Method,
                group.Key.Pattern,
                string.Join(", ", group.Select(static record => record.DisplayName).Distinct(StringComparer.Ordinal)));
        }
    }

    private static IEnumerable<RouteRecord> CreateRouteRecords(RouteEndpoint endpoint)
    {
        var pattern = endpoint.RoutePattern.RawText ?? endpoint.RoutePattern.ToString() ?? "";
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        if (methods is null || methods.Count == 0)
        {
            yield return new RouteRecord("*", pattern, DisplayName(endpoint));
            yield break;
        }

        foreach (var method in methods)
        {
            yield return new RouteRecord(method.ToUpperInvariant(), pattern, DisplayName(endpoint));
        }
    }

    private static IEnumerable<NameIssue> FindDuplicateNames(IEnumerable<Endpoint> endpoints)
    {
        var records = endpoints
            .Select(static endpoint => new
            {
                Name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                DisplayName = DisplayName(endpoint),
            })
            .Where(static record => !string.IsNullOrWhiteSpace(record.Name))
            .GroupBy(static record => record.Name!, StringComparer.Ordinal)
            .Where(static group => group.Skip(1).Any());

        foreach (var group in records)
        {
            yield return new NameIssue(
                group.Key,
                string.Join(", ", group.Select(static record => record.DisplayName).Distinct(StringComparer.Ordinal)));
        }
    }

    private static string DisplayName(Endpoint endpoint)
        => endpoint.DisplayName ?? endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "<unnamed>";

    private static string CultureInvariant(FormattableString value)
        => FormattableString.Invariant(value);

    private sealed record RouteRecord(string Method, string Pattern, string DisplayName);

    private sealed record RouteIssue(string Method, string Pattern, string DisplayNames);

    private sealed record NameIssue(string Name, string DisplayNames);
}

internal sealed class RouteRecordComparer : IEqualityComparer<(string Method, string Pattern)>
{
    public static readonly RouteRecordComparer OrdinalIgnoreCaseMethod = new();

    public bool Equals((string Method, string Pattern) x, (string Method, string Pattern) y)
        => StringComparer.OrdinalIgnoreCase.Equals(x.Method, y.Method)
            && StringComparer.Ordinal.Equals(x.Pattern, y.Pattern);

    public int GetHashCode((string Method, string Pattern) obj)
        => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Method),
            StringComparer.Ordinal.GetHashCode(obj.Pattern));
}
