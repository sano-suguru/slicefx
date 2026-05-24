# Source generator and route manifest

`Slice.SourceGenerator` is the registration path for Slice features. It discovers `[Feature]` classes at compile time and emits explicit ASP.NET Core Minimal API registrations into the `Slice` namespace.

## Generated registration shape

The generated `MapSlices()` method maps each feature with `MapMethods`, then attaches validation, feature filters, and endpoint metadata:

```csharp
public static IEndpointRouteBuilder MapSlices(this IEndpointRouteBuilder app)
{
    app.MapMethods(
            "/users",
            new[] { "POST" },
            new Func<CreateUser.Request, IUserStore, CancellationToken, Task<CreateUser.Response>>(CreateUser.Handle))
        .AddEndpointFilterFactory(DataAnnotationsValidationFilter.CreateFilterFactory)
        .WithTags("Users")
        .WithName("Users.CreateUser");

    return app;
}
```

That keeps startup registration reflection-free and trimming-friendly.

## Multi-assembly apps

Feature assemblies expose generated module helpers and assembly markers. Host assemblies emit the user-facing `AddSlice()` / `MapSlices()` extensions and can aggregate directly referenced Slice modules without runtime scanning.

Class library projects default to module helpers only. Executable hosts default to the public extension surface and aggregate referenced Slice modules. Set `SliceRole` to `Host`, `Feature`, or `Both` only when you need to override that default.

Hosts can control referenced module aggregation with MSBuild properties:

```xml
<PropertyGroup>
  <!-- Default: true. Set false to map only features compiled into this project. -->
  <SliceAggregateReferences>false</SliceAggregateReferences>

  <!-- Optional allow-list. When set, only these referenced assembly names are aggregated. -->
  <SliceReferencedAssemblies>FeatureLib;SharedSlices</SliceReferencedAssemblies>
</PropertyGroup>
```

The generator validates endpoint-name uniqueness across local features and aggregated referenced modules before emitting host registrations.

## Route manifest

The generator emits route metadata for tooling and deployment experiments, including empty manifests for projects that do not define features yet. The manifest includes:

- HTTP method and route pattern
- Feature type, tag, endpoint name, and summary
- Request type and return type
- Handler parameter names
- Referenced filter type names
- Portability status: `portable`, `partial`, or `aspnet-only`

The manifest is string-based so tools can consume route shape without adding dependencies to `Slice.Core`. `slice openapi` uses the manifest as an offline OpenAPI projection for portable tooling; hosted ASP.NET apps should use `Microsoft.AspNetCore.OpenApi` for the authoritative runtime document.

## Diagnostics

Invalid feature shapes are reported at compile time with `SLICE###` diagnostics. Common checks include missing `Handle` methods, non-public or non-static handlers, ambiguous handler overloads, unsupported WASI routes, filter order hints, and Lambda per-feature eligibility.
