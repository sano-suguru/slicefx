# OpenAPI integration

Slice has two OpenAPI paths with different fidelity and runtime assumptions.

## ASP.NET Core runtime document

For hosted ASP.NET Core apps, use Microsoft's OpenAPI integration as the authoritative document. Slice emits standard Minimal API registrations with endpoint names, tags, summaries, typed delegates, and filters, so the runtime OpenAPI generator can inspect the actual endpoint metadata.

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
```

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapSlices();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
```

`MapOpenApi()` does not need to come after `MapSlices()` for ordering reasons; the important part is that both are mapped by the host. Keeping it behind `IsDevelopment()` is the ASP.NET template default. Expose it differently when your deployment requires a public or authenticated OpenAPI endpoint.

## Manifest projection with `slice openapi`

`slice openapi` generates an OpenAPI JSON document from Slice's build-time route manifest:

```bash
slice openapi --output openapi.json
slice openapi --title MyApi --version 1.2.0 --output openapi.json
```

This command does not start the ASP.NET host or fetch `/openapi/v1.json`. That keeps it usable in CI, WASI, Lambda per-feature, and other contexts where the app host may not run locally or may have environment-dependent side effects.

The generated document is stamped with `x-slice-source: "manifest"`. It is a projection of manifest data, not a full replacement for the ASP.NET runtime document. It includes route paths, methods, operation ids, tags, summaries, route/query/header parameters, request bodies, successful response schemas, portability metadata, and DTO schemas available from built assemblies.

DTO schemas are read from build output metadata without loading user assemblies. The projection honors common `System.Text.Json` contract metadata:

- `[JsonPropertyName]` controls emitted property names.
- `[JsonIgnore]` removes properties; `JsonIgnoreCondition.Never` keeps them, while `WhenWritingNull` and `WhenWritingDefault` keep them but do not mark them required.
- C# `required` members and `[JsonRequired]` are emitted in OpenAPI `required`.
- `JsonStringEnumConverter` on an enum or enum property emits string enum schemas.
- Binary DTO members such as `byte[]`, `Memory<byte>`, and `ReadOnlyMemory<byte>` emit `type: string` with `format: byte`.

Handler parameter metadata includes nullable annotations and common Minimal API binding attributes such as `[FromQuery(Name = "...")]`, `[FromRoute(Name = "...")]`, `[FromHeader(Name = "...")]`, and `[FromBody]`.

By default, `slice openapi` includes `portable` and `partial` routes. `aspnet-only` routes are omitted because the manifest cannot reliably describe `IResult` response shapes. Omitted routes are listed in `x-slice-omitted` and written as warnings. Use `--include-aspnet-only` only when you want those operations included with explicit portability metadata and incomplete schemas.

## Fidelity boundary

The manifest is deliberately string-based so tooling can read route shape without adding runtime dependencies to `Slice.Core`. That means `slice openapi` should not guess metadata it cannot know. Prefer the ASP.NET Core runtime document when you need middleware-aware behavior, rich result metadata, authentication/security schemes, multiple status codes, custom content types, XML documentation details, OpenAPI transformers, custom `JsonConverter` behavior, polymorphism metadata, `JsonExtensionData`, `JsonNumberHandling` schema effects, or runtime-configured naming policies.

Use `slice openapi` when you need a portable baseline contract without launching the host.
