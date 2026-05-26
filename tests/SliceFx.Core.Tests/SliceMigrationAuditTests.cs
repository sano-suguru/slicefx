using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace SliceFx.Core.Tests;

#pragma warning disable ASP0022 // Tests intentionally create duplicate routes for migration-audit coverage.

public sealed class SliceMigrationAuditTests
{
    [Fact]
    public async Task Migration_audit_throw_mode_reports_duplicate_routes()
    {
        await using var app = CreateApp("Throw");
        app.MapGet("/health", static () => "raw");
        app.MapGet("/health", static () => "slice");

        var ex = Assert.Throws<InvalidOperationException>(app.RunSliceMigrationAudit);

        Assert.Contains("Duplicate route candidates", ex.Message, StringComparison.Ordinal);
        Assert.Contains("GET /health", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_audit_throw_mode_reports_duplicate_endpoint_names()
    {
        await using var app = CreateApp("Throw");
        app.MapGet("/first", static () => "first").WithName("Users.Get");
        app.MapGet("/second", static () => "second").WithName("Users.Get");

        var ex = Assert.Throws<InvalidOperationException>(app.RunSliceMigrationAudit);

        Assert.Contains("Duplicate endpoint names", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Users.Get", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_audit_off_mode_ignores_duplicates()
    {
        await using var app = CreateApp("Off");
        app.MapGet("/health", static () => "raw");
        app.MapGet("/health", static () => "slice");

        app.RunSliceMigrationAudit();
    }

    [Fact]
    public async Task Migration_audit_uses_final_grouped_route_pattern()
    {
        await using var app = CreateApp("Throw");
        app.MapGet("/api/users", static () => "raw");
        app.MapGroup("/api").MapGet("/users", static () => "slice");

        var ex = Assert.Throws<InvalidOperationException>(app.RunSliceMigrationAudit);

        Assert.Contains("GET /api/users", ex.Message, StringComparison.Ordinal);
    }

    private static WebApplication CreateApp(string auditMode)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SliceFx:MigrationAudit"] = auditMode,
        });
        return builder.Build();
    }

}
