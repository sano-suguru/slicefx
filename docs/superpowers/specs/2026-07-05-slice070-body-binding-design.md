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

Introduce a single authority: `SourceGenerationHelpers.SelectBodyParameter(FeatureModel feature, HashSet<string>? knownSerializableTypes) -> HandleParamModel?`. It examines the full parameter list and returns the one body parameter, or `null` if the handler has no body.

First, form the **candidate set**: parameters that are not already bound to `route`/`query`/`header`/`services`/`keyedServices`/`parameters`, are not `CancellationToken`/framework types/interfaces/abstract, and are not `[FromBody]` (handled separately in precedence 1). Then:

1. **`[FromBody]` explicit** — if exactly one parameter carries `[FromBody]` (`BindingSource == "body"`), it is the body, on **any** verb (explicit intent). Two or more `[FromBody]` → ambiguity (diagnostic). This step is evaluated before and independently of the candidate set.
2. **Convention (body verbs only)** — otherwise, **only when `IsInferredBodyMethod(feature.HttpMethod)` is true** (`POST`/`PUT`/`PATCH`): if exactly one candidate's type is a **nested type of the feature class** (`IsNestedTypeOf`), it is the body. (The canonical SliceFx `Request` record.) If two or more candidates are nested types → ambiguity (diagnostic).
3. **Sole serializable candidate (body verbs only)** — otherwise, **only when `IsInferredBodyMethod` is true**: if exactly one candidate exists **and it is registered in the JSON context** (`knownSerializableTypes?.Contains`), it is the body. Covers the non-nested shared-contract request pattern (`docs/cli.md`) used by Blazor clients.
4. Otherwise (including **all** non-body verbs `GET`/`DELETE`/… with no `[FromBody]`) → no body; each candidate binds as `Services` (DI).

A parameter binds to `Body` **iff** it is the parameter returned by `SelectBodyParameter`. All other candidate parameters bind to `Services`.

**Verb gate is load-bearing.** The current per-parameter binder gates body inference on `IsInferredBodyMethod` (`ResolveConventionBinding`, `SourceGenerationHelpers.cs:326`), so on `GET`/`DELETE` a nested/complex type falls through to `Services` today. Precedence 2 and 3 MUST preserve that gate; only precedence 1 (`[FromBody]`) is verb-independent. Dropping the gate would flip `GET` nested-type parameters to `Body` — a regression that also triggers SLICE071 (missing JSON root).

**Multiple nested types** on a body verb are treated as ambiguous (→ diagnostic), not "first wins". This differs from the JSON-root phase-1a heuristic (`JsonContextPlanner.cs:475`, which breaks on the first nested type for *root registration*); binding selection is stricter because picking a body arbitrarily would silently mis-dispatch.

Key consequence: JSON-context membership is demoted from *the* body/DI discriminator to a *necessary condition for serving as a body* (a body must be serializable). Positive body identification comes from `[FromBody]` or the nesting convention. This removes the "concrete service accidentally in the JSON context" misclassification entirely.

**Non-AOT runtime dispatch is unaffected**, but the route manifest is not. The non-AOT ASP.NET registration path (`RegistrationEmitter.cs`) binds via Minimal API reflection at runtime and does not consult `SelectBodyParameter` — genuinely unchanged. However, `RouteManifestEmitter` classifies **every** feature's portability and computes its `RequestType` using `FindSingleBodyParameter` / `ClassifyPortability` with the serializable set `BuildUnionSerializableTypes(wasiPlan, lambdaPlan)` (`RouteManifestEmitter.cs:22,199,239`) — the **WASI+Lambda union, not the AspNet-AOT set**, which is `null` only when both are empty. Because `FindSingleBodyParameter` will delegate to `SelectBodyParameter`, the manifest's portability classification and `RequestType` change for affected features **even on the null path**. This is intended: the multi-body case that previously classified a route `partial` (`"multiple body parameters (SLICE023)"`, `RouteManifestEmitter.cs:307`) now resolves to a single body and classifies `portable`. `slicefx routes` / `openapi` / generated clients reflect the improved classification. The change is a correctness improvement, not a silent regression, and is asserted by a dedicated test (Testing case 7).

The CLI's source-scan fallback (`RouteCatalog.FindRequestType`, `tools/SliceFx.Cli/Internal/RouteCatalog.cs:290`) is a **separate** heuristic (used only when the project has not been built and no manifest exists); it keys on `[FromBody]` and `Request`/`*.Request` names and does not converge on `SelectBodyParameter`. Aligning it is out of scope (see Out of scope) — the CLI prefers the generated manifest whenever present.

One more non-AOT consumer changes subtly: `SliceFeatureGenerator.FindSliceRequestTypeFqns` collects the request-type set (used to match `ISliceValidator<T>`) via `FindBodyParameters(model)` with a **null** serializable set. For the canonical nested-`Request` feature this is unchanged, because precedence 2 selects the nested type without consulting the set. It differs only for a feature with **two or more non-nested request-like parameters** (the genuinely ambiguous case): the old null path returned all of them as bodies; `SelectBodyParameter`'s null-arity fallback returns no body (ambiguous), so no request type is registered for that feature. This is more correct — such a feature was never bindable — but it means the earlier "non-AOT is unchanged" phrasing applies to *runtime dispatch* only, not to validator request-type collection. Guarded by a test (Testing case: validator matches nested request on a plain ASP.NET app).

Note on set nullness: on the AOT path `knownSerializableTypes` is **never null** — `JsonContextPlan.GetSerializableTypesSet()` returns an empty `HashSet` when no `[SliceJsonContext]` is defined. `SelectBodyParameter` handles the empty set correctly because precedence 2 (nested convention) runs before the membership-based precedence 3. The null-arity fallback is reached only by the manifest's `BuildUnionSerializableTypes` when both the WASI and Lambda plans are empty.

### Wiring

`SelectBodyParameter` becomes the single source of truth. Refactor callers to delegate:

- `SourceGenerationHelpers.FindBodyParameters` / `FindSingleBodyParameter` → return the result of `SelectBodyParameter` (0 or 1 element).
- `ResolveParameterBinding` for a specific parameter → returns `Body` only when that parameter equals the selected body parameter; otherwise the request-like concrete type resolves to `Services`. (Implementation may pass the pre-selected body parameter down, or have `ResolveConventionBinding` consult `SelectBodyParameter` — chosen at implementation time to keep a single evaluation per feature and preserve incremental-generator caching.)
- All four emitters currently run their own `bodyCount > 1` loop over `ResolveParameterBinding` (`AspNetAotRegistrationEmitter.cs:157`, `WasiRegistrationEmitter.cs:194`, `LambdaFunctionPerFeatureEmitter.cs:307`, `RouteManifestEmitter.cs:307`) plus their own `FindBodyParam`. Converge all of them onto `SelectBodyParameter`, removing the duplicated body-count logic so ambiguity is decided in exactly one place.
- **Two loops per emitter, not one.** Each emitter has (a) an *eligibility* loop (the `bodyCount > 1` check above) and (b) an *argument-emission* loop that re-calls `ResolveParameterBinding` per parameter to emit each argument (`AspNetAotRegistrationEmitter.cs:523-606`, `WasiRegistrationEmitter.cs:242`, `LambdaFunctionPerFeatureEmitter.cs:105`). Both must agree on which parameter is the body. Since `ResolveParameterBinding` will return `Body` only for the selected parameter (it internally consults `SelectBodyParameter`), the emission loops stay correct without threading extra state — but the implementation MUST verify that every emission loop routes non-body serializable concrete parameters through the `Services` (`GetRequiredService`) branch, so eligibility (one body) and emission (per-parameter) cannot diverge. This is the single most error-prone part of the change; cover it with the cross-path consistency test.

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
7. **Non-body verbs (regression guard for the verb gate):** `GET` and `DELETE` handlers with a nested type parameter and with a JSON-context-registered concrete type → bind `Services` (DI), **never body**, and produce no SLICE071. This is the primary guard for precedence 2/3's verb gate.
8. **Manifest / portability (null-path regression):** a project that references neither WASI nor Lambda (union serializable set null) with the shortlink-shaped handler → `slicefx routes` / manifest classifies the route `portable` (previously `partial` "multiple body parameters"), and `RequestType` resolves to the selected body. Guards the corrected non-AOT claim.
9. **`SliceResult<T>` + nested `Request`:** body selection picks `Request`; `T` is still registered as the JSON root (payload), not the wrapper (interaction with `CollectRoots`).
10. **Record vs class request type:** nested `Request` as both a `record` and a `class` → identical body selection (documents that `IsNestedTypeOf` / `IsRequestLikeParameter` are shape-agnostic).

Whole-solution gate: `dotnet test SliceFx.slnx --configuration Release`. Build the affected samples green: `SliceFx.AotSample`, `SliceFx.WasiSample` (build only), `SliceFx.LambdaFunctionPerFeatureSample`.

## Docs

- `docs/aot.md` (near Limitations) and `docs/guides/parameter-binding.md` — document the body-selection precedence table and that idiomatic handlers no longer need the all-interfaces workaround.
- Note in framework docs that the `slicefx-shortlink` README workaround (all-interface DI services, `IConfiguration` wrapping) is no longer required after this change. Updating the shortlink repo itself is out of scope for this spec and tracked separately.

## Out of scope

- B4 (WasiSample demonstrating the KV/HttpClient satellites) — separate spec/plan cycle.
- Runtime (non-AOT) ASP.NET binding — unchanged; Minimal API handles it (the route manifest it emits *does* change; see Design).
- The CLI source-scan fallback `RouteCatalog.FindRequestType` (`tools/SliceFx.Cli/Internal/RouteCatalog.cs`) — a separate no-build heuristic; not converged onto `SelectBodyParameter`. The CLI prefers the generated manifest whenever the project is built, so divergence only shows for unbuilt projects. Revisit only if it causes reported confusion.
- Auto-adding `[FromServices]` via a Roslyn code fix — considered and dropped; the new binding rule removes the friction without needing a code fix.

## Risks

- A concrete, non-interface type that is genuinely a DI service, is registered in the JSON context, and is the *sole* request-like parameter on a body verb would be selected as body (precedence 3). This matches today's behavior (already misclassified) — not a regression — and is resolved by `[FromServices]` or (preferred) using an interface. Called out in docs.
- A nested type of the feature class that is actually a DI service (e.g. a nested helper) is selected as body by precedence 2 (name-based `IsNestedTypeOf`, which does not distinguish `Request`/`Response`/other nested types). Not a regression (today's body-verb path misclassifies it too); resolved by `[FromServices]` or a non-nested/interface type. Documented in `docs/guides/parameter-binding.md`.
- **Compile-time-error → runtime-error shift:** a concrete serializable type that previously tripped SLICE070 (compile error) may now bind to `Services`. If the author never registered it in DI, the code compiles but fails at DI resolution (`GetRequiredService`, `AspNetAotRegistrationEmitter.cs:604`) at startup / first request. This matches ASP.NET Minimal API's own behavior for an unregistered service and affects only the rare "in JSON context but not DI-registered" case; documented as expected.
- Shared binder change touches all three compile-time paths; mitigated by the cross-path consistency tests (cases 6–8) and the full-solution + sample-build gate.
