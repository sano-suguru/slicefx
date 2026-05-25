using Amazon.Lambda.APIGatewayEvents;

namespace Slice.Lambda.PerFunction.Tests;

public sealed class LambdaArgumentBinderTests
{
    [Fact]
    public void TryGetFromRoute_returns_false_when_parameter_is_missing()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest());

        var found = LambdaArgumentBinder.TryGetFromRoute<int>(ctx, "id", out var value);

        Assert.False(found);
        Assert.Equal(default, value);
    }

    [Fact]
    public void BindFromQuery_returns_missing_when_query_collection_is_missing()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest());

        var result = LambdaArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_returns_missing_when_parameter_is_missing()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["other"] = "1",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_matches_parameter_names_case_insensitively()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["Page"] = "42",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Bound, result.Status);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TryGetFromRoute_converts_supported_scalar_values()
    {
        var id = Guid.NewGuid();
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            PathParameters = new Dictionary<string, string>
            {
                ["id"] = id.ToString("D"),
                ["enabled"] = "true",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        Assert.True(LambdaArgumentBinder.TryGetFromRoute<Guid>(ctx, "id", out var parsedId));
        Assert.Equal(id, parsedId);
        Assert.True(LambdaArgumentBinder.TryGetFromRoute<bool>(ctx, "enabled", out var enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void TryGetFromRoute_returns_false_when_route_value_is_null()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            PathParameters = new Dictionary<string, string>
            {
                ["id"] = null!,
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var found = LambdaArgumentBinder.TryGetFromRoute<int>(ctx, "id", out var value);

        Assert.False(found);
        Assert.Equal(default, value);
    }

    [Fact]
    public void BindFromQuery_returns_invalid_when_conversion_fails()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["page"] = "not-an-int",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_returns_invalid_when_value_is_null()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["page"] = null!,
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_empty_nullable_value_as_bound_default()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["page"] = "",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<int?>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Bound, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_empty_non_nullable_value_as_invalid()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["page"] = "",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<int>(ctx, "page");

        Assert.Equal(LambdaArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromQuery_treats_empty_string_value_as_bound()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["name"] = "",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromQuery<string>(ctx, "name");

        Assert.Equal(LambdaArgumentBindingStatus.Bound, result.Status);
        Assert.Equal("", result.Value);
    }

    [Fact]
    public void BindFromHeader_matches_names_case_insensitively()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Headers = new Dictionary<string, string>
            {
                ["X-Page"] = "42",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromHeader<int>(ctx, "x-page");

        Assert.Equal(LambdaArgumentBindingStatus.Bound, result.Status);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void BindFromHeader_returns_missing_when_header_collection_is_missing()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest());

        var result = LambdaArgumentBinder.BindFromHeader<int>(ctx, "x-page");

        Assert.Equal(LambdaArgumentBindingStatus.Missing, result.Status);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void BindFromHeader_returns_invalid_when_conversion_fails()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Headers = new Dictionary<string, string>
            {
                ["x-page"] = "nope",
            },
        };
        var ctx = LambdaTestHelpers.CreateContext(request);

        var result = LambdaArgumentBinder.BindFromHeader<int>(ctx, "x-page");

        Assert.Equal(LambdaArgumentBindingStatus.Invalid, result.Status);
        Assert.Equal(default, result.Value);
    }
}
