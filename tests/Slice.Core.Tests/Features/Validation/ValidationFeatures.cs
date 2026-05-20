using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Slice.Core.Tests.Features.Validation;

[Feature("POST /validated-items")]
public static class CreateValidatedItem
{
    public sealed record Request([Required, MinLength(2)] string Name);

    public static IResult Handle(Request request) => Results.Ok(request);
}

public static class UsesServiceParameter
{
    public static IResult Handle(TestDependency dependency) => Results.Ok(dependency);
}

public sealed class TestDependency;
