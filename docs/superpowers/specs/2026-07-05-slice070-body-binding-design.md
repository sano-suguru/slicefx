# SLICE070 / body-binding: "at most one body" invariant

- **Date:** 2026-07-05
- **Status:** Approved (design)
- **Scope:** `src/SliceFx.SourceGenerator` compile-time parameter binding + SLICE070/023/033 diagnostics
- **Origin:** Dogfooding follow-up (C6). `slicefx-shortlink` had to document a workaround — "use interfaces for all DI services and wrap `IConfiguration` in a settings interface" — to avoid SLICE070 under `[assembly: SliceAspNetAot]`.

## Problem

Under the compile-time binding paths (`[assembly: SliceAspNetAot]`, WASI, Lambda function-per-feature), a handler parameter is classified as the request **body** when it is a concrete, request-like, non-simple type on a body verb (`POST`/`PUT`/`PATCH`) **and** is registered in a `[SliceJsonContext]`. Interfaces/abstract types and concrete types *not* in the JSON context are resolved from DI.

The heuristic cannot probe the DI container at compile time (documented limitation), so it uses JSON-context membership as its body/DI discriminator. This misclassifies a legitimate case: when a handler injects a **concrete type that is also registered in the JSON context** (e.g. a settings POCO, or any serialized concrete type) alongside its request DTO, both parameters resolve to `Body`. The emitter then reports SLICE070 (`AspNetAotRegistrationEmitter.cs`, the `bodyCount > 1` branch) with the reason `"multiple body parameters are not supported"` — which does not tell the author how to fix it.

`slicefx-shortlink` worked around this by making every DI service an interface and wrapping `IConfiguration` in `IShortLinkSettings`. That is friction the framework should absorb: a handler binds **at most one request body**, and the binder should reflect that.

Current binding logic:

- `SourceGenerationHelpers.ResolveConventionBinding` — per-parameter decision. On a body verb, a request-like non-simple type is `Body` if in `knownSerializableTypes`, else `Services`.
- `SourceGenerationHelpers.IsRequestLikeParameter` — already excludes interfaces/abstract, framework types, simple types, and explicitly-bound (`route`/`query`/`header`/`services`/`keyedServices`/`parameters`) parameters.
- `JsonContextPlanner` already contains a nested-`Request`-record detector (`JsonContextRootHelpers.IsNestedTypeOf`) used for JSON-root detection — the same signal we reuse for binding.

## Goal

Make idiomatic SliceFx handlers compile without the interface/`[FromServices]` workaround, by selecting **one** body parameter per handler from the whole signature, and treating every other request-like concrete parameter as DI. Keep SLICE070 only for genuinely undecidable cases, with an actionable message. Apply the same rule consistently across the three compile-time paths (ASP.NET-AOT, WASI, Lambda function-per-feature).

This is a pre-1.0 behavior change, accepted because it is a correctness improvement (the compile-time heuristic moves closer to ASP.NET Minimal API's runtime `IServiceProviderIsService` semantics) rather than churn.

## Design

### Body selection rule

Introduce a single authority: `SourceGenerationHelpers.SelectBodyParameter(FeatureModel feature, HashSet<string>? knownSerializableTypes) -> HandleParamModel?`. It examines the full parameter list and returns the one body parameter, or `null` if the handler has no body. Precedence, evaluated over parameters that are not already bound to `route`/`query`/`header`/`services`/`keyedServices` and are not `CancellationToken`/framework types/interfaces/abstract:

1. **`[FromBody]` explicit** — if any parameter carries `[FromBody]`, it is the body. (More than one `[FromBody]` remains a user error → falls through to the ambiguity diagnostic.)
2. **Convention** — otherwise, if a parameter's type is a **nested type of the feature class** (`IsNestedTypeOf`), it is the body. (The canonical SliceFx `Request` record.)
3. **Sole serializable candidate** — otherwise, if exactly one remaining request-like candidate exists **and it is registered in the JSON context** (`knownSerializableTypes.Contains`), it is the body. Covers the non-nested shared-contract request pattern (`docs/cli.md`) used by Blazor clients.
4. Otherwise → no body from these candidates; each such parameter binds as `Services` (DI).

A parameter binds to `Body` **iff** it is the parameter returned by `SelectBodyParameter`. All other request-like concrete parameters bind to `Services`.

Key consequence: JSON-context membership is demoted from *the* body/DI discriminator to a *necessary condition for serving as a body* (a body must be serializable). Positive body identification comes from `[FromBody]` or the nesting convention. This removes the "concrete service accidentally in the JSON context" misclassification entirely.

When `knownSerializableTypes` is `null` (the non-AOT ASP.NET path, which binds via reflection at runtime), behavior is unchanged: that path does not use `SelectBodyParameter` for registration and defers to Minimal API.

### Wiring

`SelectBodyParameter` becomes the single source of truth. Refactor callers to delegate:

- `SourceGenerationHelpers.FindBodyParameters` / `FindSingleBodyParameter` → return the result of `SelectBodyParameter` (0 or 1 element).
- `ResolveParameterBinding` for a specific parameter → returns `Body` only when that parameter equals the selected body parameter; otherwise the request-like concrete type resolves to `Services`. (Implementation may pass the pre-selected body parameter down, or have `ResolveConventionBinding` consult `SelectBodyParameter` — chosen at implementation time to keep a single evaluation per feature and preserve incremental-generator caching.)
- All four emitters currently run their own `bodyCount > 1` loop over `ResolveParameterBinding` (`AspNetAotRegistrationEmitter.cs:157`, `WasiRegistrationEmitter.cs:194`, `LambdaFunctionPerFeatureEmitter.cs:307`, `RouteManifestEmitter.cs:307`) plus their own `FindBodyParam`. Converge all of them onto `SelectBodyParameter`, removing the duplicated body-count logic so ambiguity is decided in exactly one place.

### Diagnostics

The ambiguity case (2+ candidates survive selection — e.g. two nested request-like types, or multiple `[FromBody]`) keeps its diagnostic but with an actionable reason. SLICE070's message format embeds a reason (`{3}`); replace the reason string:

> `parameter '{1}' of type '{2}' is a second request-body candidate; a handler binds at most one request body. Annotate the intended body with [FromBody], mark injected services with [FromServices] (or use an interface/abstract type), so only one candidate remains.`

- ASP.NET-AOT: `AspNetAotRegistrationEmitter.cs` `bodyCount > 1` branch reason updated (now driven by `SelectBodyParameter` returning ambiguity).
- WASI (`SLICE023`, `UnsupportedParameterForWasi`) and Lambda FPF (`SLICE033`) multi-body reasons aligned to the same wording.
- `helpLinkUri` unchanged (`docs/aot.md`); a new binding-rule section is added there (§Docs).
- `AnalyzerReleases.Unshipped.md`: SLICE070 ID and severity are unchanged; update only descriptive text if present. No new diagnostic ID is introduced.

## Files touched

- `src/SliceFx.SourceGenerator/SourceGenerationHelpers.cs` — add `SelectBodyParameter`; refactor `ResolveConventionBinding`, `FindBodyParameters`, `FindSingleBodyParameter`, `ResolveParameterBinding` to delegate. Make the nested-type check (`IsNestedTypeOf`, currently in `JsonContextRootHelpers`) reachable from here (share or relocate the helper).
- `src/SliceFx.SourceGenerator/Emit/AspNetAotRegistrationEmitter.cs` — body-count eligibility uses `SelectBodyParameter`; SLICE070 reason updated.
- `src/SliceFx.SourceGenerator/Emit/WasiRegistrationEmitter.cs` — delegate to `SelectBodyParameter`; align the SLICE023 multi-body reason.
- `src/SliceFx.SourceGenerator/Emit/LambdaFunctionPerFeatureEmitter.cs` — delegate to `SelectBodyParameter`; align the SLICE033 multi-body reason.
- `src/SliceFx.SourceGenerator/Emit/RouteManifestEmitter.cs` — delegate to `SelectBodyParameter` so portability classification matches the emitters (currently duplicates the `bodyCount > 1` check and its "multiple body parameters (SLICE023)" string).
- `src/SliceFx.SourceGenerator/AnalyzerReleases.Unshipped.md` — descriptive text only.

## Testing

xUnit in the source-generator suite, using in-memory Roslyn compilation. Per repo convention, in-memory compilations have **no global usings** — test sources must `using System.ComponentModel.DataAnnotations;` and add `using Microsoft.AspNetCore.Mvc;` (or fully-qualify) for `[FromBody]`/`[FromServices]`.

Cases (asserted across ASP.NET-AOT, WASI, and Lambda-FPF where each path applies):

1. **Regression (shortlink):** nested `Request` record + a concrete settings type registered in the JSON context, on `POST` → `Request` binds body, settings binds DI, **no SLICE070**.
2. **Shared contract:** single non-nested request record (in JSON context), on `POST` → binds body.
3. **`[FromBody]` override:** non-nested type with `[FromBody]` → binds body even when a nested type also exists (documents precedence 1 > 2).
4. **`[FromServices]` / interface:** concrete `[FromServices]` and interface parameters → DI, never body.
5. **Genuine ambiguity:** two nested request-like types (or two `[FromBody]`) → SLICE070 with the new reason text.
6. **Consistency:** the same handler shape yields the same body/DI classification on all three compile-time paths.

Whole-solution gate: `dotnet test SliceFx.slnx --configuration Release`. Build the affected samples green: `SliceFx.AotSample`, `SliceFx.WasiSample` (build only), `SliceFx.LambdaFunctionPerFeatureSample`.

## Docs

- `docs/aot.md` (near Limitations) and `docs/guides/parameter-binding.md` — document the body-selection precedence table and that idiomatic handlers no longer need the all-interfaces workaround.
- Note in framework docs that the `slicefx-shortlink` README workaround (all-interface DI services, `IConfiguration` wrapping) is no longer required after this change. Updating the shortlink repo itself is out of scope for this spec and tracked separately.

## Out of scope

- B4 (WasiSample demonstrating the KV/HttpClient satellites) — separate spec/plan cycle.
- Runtime (non-AOT) ASP.NET binding — unchanged; Minimal API handles it.
- Auto-adding `[FromServices]` via a Roslyn code fix — considered and dropped; the new binding rule removes the friction without needing a code fix.

## Risks

- A concrete, non-interface type that is genuinely a DI service, is registered in the JSON context, and is the *sole* request-like parameter on a body verb would be selected as body (precedence 3). This matches today's behavior (already misclassified) — not a regression — and is resolved by `[FromServices]` or (preferred) using an interface. Called out in docs.
- Shared binder change touches all three compile-time paths; mitigated by the cross-path consistency tests (case 6) and the full-solution + sample-build gate.
