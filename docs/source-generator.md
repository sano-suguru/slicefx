# Source generator and route manifest

[日本語](ja/source-generator.md)

`SliceFx.SourceGenerator` is the registration path for SliceFx features. It discovers `[Feature]` classes at compile time and emits explicit ASP.NET Core Minimal API registrations into the `SliceFx` namespace.

## Generated registration shape

The generated `MapSlices()` method maps each feature with `MapMethods`, then attaches validation, feature filters, and endpoint metadata:

```csharp
public static IEndpointRouteBuilder MapSlices(this IEndpointRouteBuilder app)
{
    app.MapMethods(
            "/users",
            new[] { "POST" },
            new Func<CreateUser.Request, IUserStore, CancellationToken, Task<CreateUser.Response>>(CreateUser.Handle))
        .AddEndpointFilterFactory(__CreateDataAnnotationsValidationFactory_CreateUser)
        .WithTags("Users")
        .WithName("Users.CreateUser");

    return app;
}
```

Generated validation is emitted only when supported DataAnnotations rules are present (`Required`, length/range rules, `EmailAddress`, `Url`, and `RegularExpression`). Support is shape-conditional: `StringLength` is generated only for `string` properties, `Range` only for numeric types (`int`, `long`, `double`, `float`, `decimal`), and any attribute carrying a resource or localized error message is treated as unsupported regardless of type. Unsupported reflection-based validation such as custom `ValidationAttribute`, type-level validation, `IValidatableObject`, resource-based messages, or supported attribute types in unsupported shapes is reported with `SLICE010` for ASP.NET registrations so default registrations stay reflection-free and trimming-friendly; move those rules to `ISliceValidator<TRequest>`.

## Multi-assembly apps

Feature assemblies expose generated module helpers and assembly markers. Host assemblies emit the user-facing `AddSlice()` / `MapSlices()` extensions and can explicitly aggregate directly referenced Slice modules without runtime scanning.

Class library projects default to module helpers only. Executable hosts default to the public extension surface and map only features compiled into the host project. Set `SliceFxRole` to `Host`, `Feature`, or `Both` only when you need to override that default.

Hosts can control referenced module aggregation with MSBuild properties:

```xml
<PropertyGroup>
  <!-- Preferred: aggregate only these referenced assembly simple names. -->
  <SliceFxReferencedAssemblies>FeatureLib;SharedSlices</SliceFxReferencedAssemblies>

  <!-- Optional migration switch: aggregate every directly referenced Slice module. -->
  <SliceFxAggregateReferences>true</SliceFxAggregateReferences>
</PropertyGroup>
```

If a host references Slice feature assemblies but sets neither `SliceFxReferencedAssemblies` nor `SliceFxAggregateReferences`, the generator reports `SLICE050` and keeps the host local-only. Set `SliceFxAggregateReferences=false` to make that local-only choice explicit and suppress the diagnostic.

The generator validates endpoint-name uniqueness across local features and aggregated referenced modules before emitting host registrations.

## Route manifest

The generator emits route metadata for tooling and deployment experiments, including empty manifests for projects that do not define features yet. The manifest includes:

- HTTP method and route pattern
- Feature type, tag, endpoint name, and summary
- Request type and return type
- Handler parameter names
- Referenced filter type names
- Portability status: `portable`, `partial`, or `aspnet-only`

The manifest is string-based so tools can consume route shape without adding dependencies to `SliceFx.Core`. `slicefx openapi` uses the manifest as an offline OpenAPI projection for portable tooling; hosted ASP.NET apps should use `Microsoft.AspNetCore.OpenApi` for the authoritative runtime document.

## Diagnostics

Invalid feature shapes are reported at compile time with `SLICE###` diagnostics. The prefix is intentionally kept as the domain term for feature slices, while the framework/package identity is `SliceFx`.

Diagnostic IDs are grouped into reserved ranges so new rules can be added without renumbering existing IDs:

| Range | Area |
| --- | --- |
| `SLICE001`-`SLICE009` | Core feature shape, routing, endpoint metadata, and filters |
| `SLICE010`-`SLICE019` | Validation |
| `SLICE020`-`SLICE029` | WASI portability |
| `SLICE030`-`SLICE039` | Lambda function-per-feature |
| `SLICE040`-`SLICE049` | JSON context overrides |
| `SLICE050`-`SLICE059` | Cross-assembly aggregation |
| `SLICE060`-`SLICE069` | Minimal API migration overlap |

<!-- diagnostics-reference:start -->
| ID | Severity | Area | Meaning | Suggested fix |
| --- | --- | --- | --- | --- |
| `SLICE001` | Error | Core feature shape | Feature type has no `Handle` method. | Add exactly one `public static Handle(...)` method. See [Why are Feature classes and `Handle` methods static?](design-decisions.md#why-are-feature-classes-and-handle-methods-static) |
| `SLICE002` | Error | Core feature shape | `Handle` exists but is not public and static. | Make the handler `public static`. See [Why are Feature classes and `Handle` methods static?](design-decisions.md#why-are-feature-classes-and-handle-methods-static) |
| `SLICE003` | Error | Core feature shape | Multiple `Handle` methods make the feature ambiguous. | Keep one handler method per feature type. |
| `SLICE004` | Error | Routing | Route is not in `METHOD /path` form. | Use a supported HTTP method followed by an absolute route path. |
| `SLICE005` | Error | Endpoint metadata | Two features produce the same endpoint name. | Rename one feature or set `FeatureAttribute.Name` / `FeatureAttribute.Tag`. |
| `SLICE006` | Info | Endpoint metadata | Tag inference could not find a `.Features.` namespace segment. | Move the feature under a `.Features.<Tag>` namespace or set `FeatureAttribute.Tag`. |
| `SLICE007` | Warning | Filters | `[FilterOrderHint]` conflicts with the declared filter order within a layer. | Reorder filter attributes so hinted dependencies run first. |
| `SLICE008` | Warning | Filters | `[FilterOrderHint]` references a filter from the opposite execution layer — neutral (`[SliceFilter<T>]`) and ASP.NET (`[Filter<T>]`) filters run in separate stages and cannot be ordered relative to each other. | Remove the cross-layer hint; use hints only within the same filter type. |
| `SLICE010` | Error | Validation | ASP.NET generated registrations would need reflection-bound DataAnnotations validation. | Use supported generated validation attributes or move the rule to `ISliceValidator<T>`. |
| `SLICE011` | Error | Validation | `ISliceValidator<T>` implementation cannot be generated safely. | Make the validator a closed, constructible implementation for a concrete request type. |
| `SLICE012` | Error | Validation | More than one `ISliceValidator<T>` targets the same request. | Combine the rules into one validator for that request type. |
| `SLICE013` | Error | Validation | Validator target type does not match a discovered Slice request parameter. | Remove the validator or target a request type used by a feature handler. |
| `SLICE020` | Info | WASI portability | Return type is ASP.NET-specific and excluded from the WASI route table. | Return a POCO, `SliceResult`, `WasiResponse`, `Task<T>`, or `ValueTask<T>`. |
| `SLICE021` | Warning | WASI portability | WASI JSON serialization metadata cannot be generated safely. | Provide an appropriate `JsonSerializerContext` for the WASI target. |
| `SLICE022` | Warning | WASI portability | WASI route would need reflection-bound DataAnnotations validation. | Use supported generated validation attributes or `ISliceValidator<T>`. |
| `SLICE023` | Warning | WASI portability | Parameter cannot be bound by the WASI route table. | Use supported route/query/header/body shapes or move the feature to ASP.NET-only. |
| `SLICE024` | Warning | WASI portability | Concrete request-like parameter on a body-method (POST/PUT/PATCH) is not registered in the JSON context and is treated as a DI service. | If it is a body parameter, add `[JsonSerializable(typeof(T))]` to the `[SliceJsonContext]` class; if it is a service, add `[FromServices]` to silence this warning. |
| `SLICE030` | Info | Lambda function-per-feature | Return type is not supported by generated per-feature Lambda handlers. | Return a POCO, `Task<T>`, `ValueTask<T>`, or `APIGatewayHttpApiV2ProxyResponse`. |
| `SLICE031` | Info | Lambda function-per-feature | Feature uses endpoint filters, which are not available in the per-feature Lambda path. | Remove the filter for that path or keep the feature on hosted ASP.NET/Lambda. |
| `SLICE032` | Warning | Lambda function-per-feature | Lambda JSON serialization metadata cannot be generated safely. | Use Lambda-supported body/response types and generated JSON metadata. |
| `SLICE033` | Warning | Lambda function-per-feature | Parameter cannot be bound by the per-feature Lambda handler. | Use supported route/query/header/body parameter shapes. |
| `SLICE034` | Warning | Lambda function-per-feature | Lambda route would need reflection-bound DataAnnotations validation. | Use supported generated validation attributes or `ISliceValidator<T>`. |
| `SLICE035` | Error | Lambda function-per-feature | `[LambdaFunctionStartup]` type is invalid. | Use a public parameterless type implementing `ILambdaFunctionPerFeatureStartup`. |
| `SLICE036` | Error | Lambda function-per-feature | Two features produce the same Lambda artifact ID. | Change the feature name, endpoint name, or tag to make artifact IDs unique. |
| `SLICE037` | Warning | Parameter binding | `[FromKeyedServices]` key constant cannot be re-emitted as a C# literal in generated WASI/Lambda dispatch. | Use a string, numeric, bool, char, enum, or `typeof` key; or use `[FromServices]` and register the keyed service under its type directly. |
| `SLICE040` | Error | JSON context overrides | Multiple explicit JSON context overrides target the same Slice adapter. | Keep exactly one `[SliceJsonContext]` override per target. |
| `SLICE041` | Error | JSON context overrides | Explicit JSON context override is not a `JsonSerializerContext`. | Point the override at a type deriving from `JsonSerializerContext`. |
| `SLICE050` | Warning | Cross-assembly aggregation | Referenced Slice modules exist but aggregation is not explicitly configured. | Set `SliceFxReferencedAssemblies`, `SliceFxAggregateReferences=true`, or `SliceFxAggregateReferences=false`. |
| `SLICE051` | Error | Cross-assembly aggregation | `SliceFxAggregateReferences` has an unsupported value. | Use `true`/`false`, `1`/`0`, or `yes`/`no`. |
| `SLICE060` | Warning | Minimal API migration overlap | Raw Minimal API route literal overlaps a generated Slice route. | Remove one mapping or make the overlap an intentional migration choice. |
| `SLICE061` | Warning | Minimal API migration overlap | Raw Minimal API endpoint name overlaps a generated Slice endpoint name. | Change one endpoint name or set `FeatureAttribute.Name`. |
<!-- diagnostics-reference:end -->
