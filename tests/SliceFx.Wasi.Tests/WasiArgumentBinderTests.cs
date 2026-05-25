using Microsoft.Extensions.DependencyInjection;
using SliceFx.Wasi.Binding;
using SliceFx.Wasi.Routing;

namespace SliceFx.Wasi.Tests;

public sealed class WasiArgumentBinderTests
{
    [Fact]
    public void BindFromQuery_returns_missing_when_query_string_is_missing()
    {
        var ctx = CreateContext(null);

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_returns_missing_when_parameter_is_missing()
    {
        var ctx = CreateContext("?other=1");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_key_only_pair_as_missing()
    {
        var ctx = CreateContext("?page");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_skips_key_only_pair_when_later_value_exists()
    {
        var ctx = CreateContext("?page&page=42");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Bound, result.Status);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void BindFromQuery_uses_first_value_when_parameter_is_repeated()
    {
        var ctx = CreateContext("?page=1&page=2");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Bound, result.Status);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void BindFromQuery_matches_parameter_names_case_insensitively()
    {
        var ctx = CreateContext("?Page=42");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Bound, result.Status);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void BindFromQuery_returns_invalid_when_conversion_fails()
    {
        var ctx = CreateContext("?page=not-an-int");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_empty_nullable_value_as_bound_default()
    {
        var ctx = CreateContext("?page=");

        var result = WasiArgumentBinder.BindFromQuery<int?>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Bound, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_empty_non_nullable_value_as_invalid()
    {
        var ctx = CreateContext("?page=");

        var result = WasiArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(WasiArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_empty_string_value_as_bound()
    {
        var ctx = CreateContext("?name=");

        var result = WasiArgumentBinder.BindFromQuery<string>(ctx, "name");

        Assert.Equal(WasiArgumentBindingStatus.Bound, result.Status);
        Assert.Equal("", result.Value);
    }

    [Fact]
    public void BindFromHeader_matches_names_case_insensitively()
    {
        var ctx = CreateContext(null, new Dictionary<string, string> { ["X-Page"] = "42" });

        var result = WasiArgumentBinder.BindFromHeader<int>(ctx, "x-page");

        Assert.Equal(WasiArgumentBindingStatus.Bound, result.Status);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void BindFromHeader_returns_missing_when_header_is_missing()
    {
        var ctx = CreateContext(null);

        var result = WasiArgumentBinder.BindFromHeader<int>(ctx, "x-page");

        Assert.Equal(WasiArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromHeader_returns_invalid_when_conversion_fails()
    {
        var ctx = CreateContext(null, new Dictionary<string, string> { ["x-page"] = "nope" });

        var result = WasiArgumentBinder.BindFromHeader<int>(ctx, "x-page");

        Assert.Equal(WasiArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }

    private static WasiInvokerContext CreateContext(string? queryString, IReadOnlyDictionary<string, string>? headers = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var request = new WasiRequest("GET", "/test", headers ?? new Dictionary<string, string>(), queryString, null);
        return new WasiInvokerContext(request, services, new Dictionary<string, string>(), CancellationToken.None);
    }
}
