# SLICE070 body-binding "at most one body" — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the compile-time parameter binder select **one** request body per handler (via `[FromBody]` > nested-`Request` convention > sole serializable candidate) so idiomatic SliceFx handlers stop tripping SLICE070, consistently across the ASP.NET-AOT, WASI, and Lambda function-per-feature paths.

**Architecture:** Introduce a single authority `SourceGenerationHelpers.SelectBodyParameter` returning a `BodySelectionResult` (chosen body + ambiguity signal). Every emitter's `bodyCount > 1` eligibility loop and every per-parameter binding call routes body decisions through it; `ResolveParameterBinding` reflects the pre-selected body instead of re-inferring it. Diagnostics keep their IDs (SLICE070/023/033) but gain an actionable reason.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator (`netstandard2.0`), xUnit v3 in-memory Roslyn compilation tests.

## Global Constraints

- `src/SliceFx.Core/SliceFx.Core.csproj` must never gain a `<PackageReference>` (not touched here, but do not add references anywhere that would trip `ValidateSliceCorePackageReferences`).
- Source generator target framework stays `netstandard2.0`; no per-request reflection anywhere in generated code.
- Style is CI-enforced: file-scoped namespaces, `var`, 4-space indent, final newline, LF. `dotnet format SliceFx.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591 xUnit1004` must pass.
- `TreatWarningsAsErrors` is on; build must be warning-clean.
- In-memory Roslyn test compilations have **no global usings** — every test source string must declare its own `using` directives (`System.ComponentModel.DataAnnotations`, `Microsoft.AspNetCore.Mvc` for `[FromBody]`/`[FromServices]`, etc.).
- Diagnostic IDs SLICE070/SLICE023/SLICE033 keep their current severity (Error / Info / Warning respectively) and `helpLinkUri`. No new diagnostic ID is introduced.
- Body selection precedence (verbatim from the spec):
  1. `[FromBody]` explicit — any verb.
  2. Nested type of the feature class (`IsNestedTypeOf`) — **body verbs only** (`IsInferredBodyMethod`).
  3. Sole serializable candidate (in the JSON context) — **body verbs only**.
  4. Otherwise (incl. all non-body verbs) → each candidate binds `Services` (DI).
  - Precedence 2 does **not** require JSON-context membership (serializability is enforced downstream by SLICE071/SLICE021). Precedence 3 does.

---

## File Structure

- `src/SliceFx.SourceGenerator/SourceGenerationHelpers.cs` — home of `SelectBodyParameter`, `BodySelectionResult`, and the refactored `ResolveConventionBinding` / `ResolveParameterBinding` / `FindBodyParameters` / `FindSingleBodyParameter`. Single source of truth for the body decision.
- `src/SliceFx.SourceGenerator/Diagnostics/SliceDiagnostics.cs` — SLICE070/023/033 reason wording.
- `src/SliceFx.SourceGenerator/Emit/AspNetAotRegistrationEmitter.cs`, `WasiRegistrationEmitter.cs`, `LambdaFunctionPerFeatureEmitter.cs`, `RouteManifestEmitter.cs` — converge each emitter's eligibility loop and argument-emission loop onto `SelectBodyParameter`.
- `tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs` — new compile tests.
- `docs/guides/parameter-binding.md`, `docs/aot.md` — document the precedence and residual risks.
- `src/SliceFx.SourceGenerator/AnalyzerReleases.Unshipped.md` — SLICE070 description text.

Shared helper `JsonContextRootHelpers.IsNestedTypeOf(paramTypeFqn, featureTypeFqn)` already exists in `src/Shared/JsonContextRootHelpers.cs` and is compiled into the generator; call it directly.

---

## Task 1: `SelectBodyParameter` core + binder delegation

**Files:**
- Modify: `src/SliceFx.SourceGenerator/SourceGenerationHelpers.cs` (`ResolveParameterBinding:167`, `ResolveConventionBinding:312`, `FindBodyParameters:99`, `FindSingleBodyParameter:126`)
- Test: `tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs`

**Interfaces:**
- Produces:
  - `internal readonly record struct BodySelectionResult(HandleParamModel? Body, HandleParamModel? AmbiguousWith);` — `Body` is the chosen body param (or null for none); `AmbiguousWith` is non-null **iff** selection is ambiguous (drives the multi-body diagnostic), and its value is the offending second candidate.
  - `public static BodySelectionResult SelectBodyParameter(FeatureModel feature, HashSet<string>? knownSerializableTypes)`
  - `ResolveParameterBinding(HandleParamModel parameter, string httpMethod, string pattern, HashSet<string>? knownSerializableTypes = null, HandleParamModel? selectedBody = null)` — new trailing optional `selectedBody`; returns `Body` for the convention path only when `parameter.Name == selectedBody?.Name`.
  - `FindBodyParameters` / `FindSingleBodyParameter` return the result of `SelectBodyParameter(...).Body`.

- [ ] **Step 1: Write the failing regression test (shortlink shape, AOT path)**

Add to `SourceGeneratorCompileTests.cs`. This is the core dogfooding regression: a nested `Request` record plus a concrete settings type that is registered in the JSON context, on `POST`, must NOT produce SLICE070; `Request` binds body, settings binds DI.

```csharp
[Fact]
public void AspNetAot_selects_nested_request_as_body_and_binds_serializable_service_as_di()
{
    var source = """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using System.Text.Json.Serialization.Metadata;
        using Microsoft.AspNetCore.Http;
        using SliceFx;

        [assembly: SliceAspNetAot]

        namespace AotBodyApp
        {
            public sealed record AppSettings(string Region);

            namespace Features.Orders
            {
                [Feature("POST /orders")]
                public static class CreateOrder
                {
                    public sealed record Request(string Sku);
                    public sealed record Response(string Id);

                    public static Response Handle(Request req, global::AotBodyApp.AppSettings settings)
                        => new Response(req.Sku + settings.Region);
                }
            }

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(global::AotBodyApp.Features.Orders.CreateOrder.Request))]
            [JsonSerializable(typeof(global::AotBodyApp.Features.Orders.CreateOrder.Response))]
            [JsonSerializable(typeof(global::AotBodyApp.AppSettings))]
            public sealed partial class AotJsonContext : JsonSerializerContext { }
        }
        """;

    var compilation = CreateHostCompilation("AotBodyApp", source);
    GeneratorDriver driver = CreateDriver();
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, TestContext.Current.CancellationToken);

    Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "SLICE070");
    Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    var aotSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");
    // AppSettings is resolved from DI, not read as a body.
    Assert.Contains("GetRequiredService(typeof(global::AotBodyApp.AppSettings))", aotSource, StringComparison.Ordinal);
}
```

> If the exact generated-source substrings differ (JSON-context class shape, `[SliceJsonContext]` partial requirements, generated file suffix), first run an existing AOT test to copy the working scaffold from `SourceGeneratorCompileTests.cs`; keep the assertions behavioral (`SLICE070` absent, `GetRequiredService(...AppSettings...)` present).

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~AspNetAot_selects_nested_request_as_body" -c Release`
Expected: FAIL — current code classifies both `Request` and `AppSettings` as body (`AppSettings` is in the JSON context), emitting SLICE070.

- [ ] **Step 3: Add `BodySelectionResult` and `SelectBodyParameter`**

In `SourceGenerationHelpers.cs`, add the result struct (near the other binding types at the bottom of the file) and the selector. Full implementation:

```csharp
public static BodySelectionResult SelectBodyParameter(
    FeatureModel feature,
    HashSet<string>? knownSerializableTypes)
{
    var parameters = feature.GetParams();

    // Precedence 1: explicit [FromBody], on any verb.
    HandleParamModel? explicitBody = null;
    foreach (var p in parameters)
    {
        if (p.BindingSource == "body")
        {
            if (explicitBody is not null)
            {
                return new BodySelectionResult(explicitBody, p); // 2+ [FromBody] → ambiguous
            }

            explicitBody = p;
        }
    }

    if (explicitBody is not null)
    {
        return new BodySelectionResult(explicitBody, null);
    }

    // Precedences 2 & 3 apply only on inferred-body verbs (POST/PUT/PATCH).
    if (!IsInferredBodyMethod(feature.HttpMethod))
    {
        return new BodySelectionResult(null, null);
    }

    // Candidate set: request-like concrete params (IsRequestLikeParameter already excludes
    // route/query/header/services/keyedServices/parameters, CancellationToken, framework,
    // interface/abstract, and simple types).
    var candidates = ImmutableArray.CreateBuilder<HandleParamModel>();
    foreach (var p in parameters)
    {
        if (IsRequestLikeParameter(p))
        {
            candidates.Add(p);
        }
    }

    if (candidates.Count == 0)
    {
        return new BodySelectionResult(null, null);
    }

    // Precedence 2: nested type of the feature class (canonical Request record).
    // Does NOT require JSON-context membership; serializability is enforced downstream.
    HandleParamModel? nested = null;
    foreach (var p in candidates)
    {
        if (JsonContextRootHelpers.IsNestedTypeOf(p.TypeFqn, feature.FullyQualifiedTypeName))
        {
            if (nested is not null)
            {
                return new BodySelectionResult(nested, p); // 2+ nested → ambiguous
            }

            nested = p;
        }
    }

    if (nested is not null)
    {
        return new BodySelectionResult(nested, null);
    }

    // Precedence 3: a body must be serializable. When the set is known, the body is the sole
    // candidate registered in the JSON context; 2+ registered → ambiguous; 0 → all DI.
    if (knownSerializableTypes is not null)
    {
        HandleParamModel? serializable = null;
        foreach (var p in candidates)
        {
            if (knownSerializableTypes.Contains(p.TypeFqn))
            {
                if (serializable is not null)
                {
                    return new BodySelectionResult(serializable, p);
                }

                serializable = p;
            }
        }

        return new BodySelectionResult(serializable, null);
    }

    // Unknown serializable set (e.g. manifest union empty): fall back to arity, matching the
    // pre-change null-path behavior (single candidate = body; multiple = ambiguous).
    return candidates.Count == 1
        ? new BodySelectionResult(candidates[0], null)
        : new BodySelectionResult(null, candidates[1]);
}
```

And the struct (place beside `HandlerParameterBinding` at file end):

```csharp
internal readonly record struct BodySelectionResult(
    HandleParamModel? Body,
    HandleParamModel? AmbiguousWith);
```

Confirm `using System.Collections.Immutable;` and access to `JsonContextRootHelpers` are already present in the file (they are — `FindBodyParameters` uses `ImmutableArray`).

- [ ] **Step 4: Delegate the per-parameter binder to the selection**

Change `ResolveParameterBinding` to accept the pre-selected body and pass it to the convention resolver:

```csharp
public static HandlerParameterBinding ResolveParameterBinding(
    HandleParamModel parameter,
    string httpMethod,
    string pattern,
    HashSet<string>? knownSerializableTypes = null,
    HandleParamModel? selectedBody = null)
{
    var wireName = string.IsNullOrWhiteSpace(parameter.BindingName) ? parameter.Name : parameter.BindingName!;
    return parameter.BindingSource switch
    {
        "body" => ResolveExplicitBody(parameter, wireName),
        "route" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Route),
        "query" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Query),
        "header" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Header),
        "services" => new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null),
        "keyedServices" => new HandlerParameterBinding(HandlerParameterBindingSource.KeyedServices, wireName, null, parameter.BindingKeyLiteral),
        "parameters" => Unsupported(wireName, "[AsParameters] is not supported in generated WASI/Lambda dispatch; ASP.NET routes are unaffected"),
        _ => ResolveConventionBinding(parameter, wireName, selectedBody),
    };
}
```

Replace `ResolveConventionBinding` (lines 312-349) so the body decision is reflected from `selectedBody`, not re-inferred. The signature keeps `pattern` (needed by the unchanged route/query branch) and drops `httpMethod` and `knownSerializableTypes` (the verb/serializability logic now lives entirely in `SelectBodyParameter`):

```csharp
private static HandlerParameterBinding ResolveConventionBinding(
    HandleParamModel parameter,
    string pattern,
    string wireName,
    HandleParamModel? selectedBody)
{
    if (parameter.TypeFqn == "global::System.Threading.CancellationToken")
    {
        return new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
    }

    if (!IsSimpleType(parameter.TypeFqn))
    {
        // Body is chosen centrally by SelectBodyParameter; here we only reflect it.
        return selectedBody is not null && parameter.Name == selectedBody.Name
            ? new HandlerParameterBinding(HandlerParameterBindingSource.Body, wireName, null)
            : new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
    }

    // Route/query branch: unchanged from the original.
    return IsRouteParam(wireName, pattern)
        ? new HandlerParameterBinding(HandlerParameterBindingSource.Route, wireName, null)
        : new HandlerParameterBinding(HandlerParameterBindingSource.Query, wireName, null);
}
```

The call site in `ResolveParameterBinding` (the `_ =>` arm) becomes `ResolveConventionBinding(parameter, pattern, wireName, selectedBody)`.

- [ ] **Step 5: Point `FindBodyParameters` / `FindSingleBodyParameter` at the selection**

```csharp
public static ImmutableArray<HandleParamModel> FindBodyParameters(
    FeatureModel feature,
    HashSet<string>? knownSerializableTypes = null)
{
    var body = SelectBodyParameter(feature, knownSerializableTypes).Body;
    return body is null ? [] : [body];
}

public static HandleParamModel? FindSingleBodyParameter(
    FeatureModel feature,
    HashSet<string>? knownSerializableTypes = null)
    => SelectBodyParameter(feature, knownSerializableTypes).Body;
```

- [ ] **Step 6: Run the regression test — expect PASS**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~AspNetAot_selects_nested_request_as_body" -c Release`
Expected: PASS.

> The emitters still contain their own `bodyCount > 1` loops calling the old `ResolveParameterBinding` 4-arg overload (no `selectedBody`), which now returns `Services` for every non-simple param. That is corrected in Task 2. The regression test passes already because `Request` (nested) is picked by `SelectBodyParameter`, and the AOT emitter's argument loop — verified in Task 2 — will emit `Request` as body and `AppSettings` via `GetRequiredService`. If Step 6 fails on the `GetRequiredService(...AppSettings...)` assertion, proceed to Task 2 (which threads `selectedBody` through the emission loop) and re-run this test at Task 2 Step 6.

- [ ] **Step 7: Add the remaining Task-1 binding tests**

Add these `[Fact]`s using the same scaffold (behavioral assertions; adjust generated-source substrings to the copied working scaffold):

1. `AspNetAot_binds_from_body_override_even_with_nested_type` — handler `Handle([FromBody] global::AotBodyApp.External payload, Request notThis)` where `External` is a top-level record in the JSON context and `Request` is nested. Assert no SLICE070 and that `External` is the body (precedence 1 > 2). (Construct so exactly one is `[FromBody]`.)
2. `AspNetAot_binds_shared_contract_when_sole_serializable_candidate` — single **non-nested** request record in the JSON context on `POST` → body; no SLICE070.
3. `AspNetAot_binds_interface_and_fromservices_as_di` — `Handle(Request req, IClock clock, [FromServices] global::AotBodyApp.Concrete c)` → no SLICE070; `Request` body, `IClock` and `Concrete` DI.
4. `AspNetAot_request_record_vs_class_bind_identically` — same feature with `Request` declared once as `record` and once as `class` (two feature classes) → both bind body, no SLICE070.

- [ ] **Step 8: Run the Task-1 test group**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~AspNetAot" -c Release`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SliceFx.SourceGenerator/SourceGenerationHelpers.cs tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs
git commit -m "feat(sourcegen): single-body selection via SelectBodyParameter

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Converge the four emitters + actionable diagnostics

**Files:**
- Modify: `src/SliceFx.SourceGenerator/Emit/AspNetAotRegistrationEmitter.cs` (eligibility loop `~137-179`, argument-emission loop `~523-606`)
- Modify: `src/SliceFx.SourceGenerator/Emit/WasiRegistrationEmitter.cs` (`~178-198` eligibility, `~242` emission)
- Modify: `src/SliceFx.SourceGenerator/Emit/LambdaFunctionPerFeatureEmitter.cs` (`~292-307` eligibility, `~105-172` emission)
- Modify: `src/SliceFx.SourceGenerator/Diagnostics/SliceDiagnostics.cs` (SLICE070 message `~355`; SLICE023 `~184`; SLICE033 `~228`)
- Test: `tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs`

**Interfaces:**
- Consumes: `SourceGenerationHelpers.SelectBodyParameter`, `BodySelectionResult`, and the new `selectedBody` parameter of `ResolveParameterBinding` (Task 1).

- [ ] **Step 1: Write the failing ambiguity + consistency tests**

```csharp
[Fact]
public void AspNetAot_reports_actionable_slice070_for_two_nested_body_candidates()
{
    var source = """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Microsoft.AspNetCore.Http;
        using SliceFx;

        [assembly: SliceAspNetAot]

        namespace AmbigApp.Features.Orders
        {
            [Feature("POST /orders")]
            public static class CreateOrder
            {
                public sealed record Request(string A);
                public sealed record Extra(string B);
                public static string Handle(Request req, Extra extra) => req.A + extra.B;
            }
        }
        """;
    var compilation = CreateHostCompilation("AmbigApp", source);
    GeneratorDriver driver = CreateDriver();
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags, TestContext.Current.CancellationToken);

    var slice070 = Assert.Single(diags, d => d.Id == "SLICE070");
    Assert.Contains("at most one request body", slice070.GetMessage(), StringComparison.Ordinal);
    Assert.Contains("[FromBody]", slice070.GetMessage(), StringComparison.Ordinal);
}
```

(Both `Request` and `Extra` are nested in `CreateOrder` → precedence 2 ambiguity.)

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~reports_actionable_slice070" -c Release`
Expected: FAIL — current message is "multiple body parameters are not supported".

- [ ] **Step 3: Update the SLICE070 reason string**

In `SliceDiagnostics.cs`, the SLICE070 descriptor message format keeps `{0}`/`{1}`/`{2}`/`{3}`. The `{3}` reason is supplied by the emitter. Update the emitter-supplied reason (Step 4) to:

```
is a second request-body candidate; a handler binds at most one request body. Annotate the intended body with [FromBody], mark injected services with [FromServices] (or use an interface/abstract type), so only one candidate remains
```

No change to the descriptor's static text is required beyond confirming it renders the reason (it does: `... cannot be bound in NativeAOT registration mode: {3}. ...`). For SLICE023 (`WasiRegistrationEmitter`) and SLICE033 (`LambdaFunctionPerFeatureEmitter`), align their multi-body reason strings to the same wording.

- [ ] **Step 4: Rewrite the AOT eligibility loop to use `SelectBodyParameter`**

In `AspNetAotRegistrationEmitter.GetAotSkipDiagnostic` (`~136-179`), replace the manual `bodyCount` loop with:

```csharp
var selection = SourceGenerationHelpers.SelectBodyParameter(feature, serializableTypes);
if (selection.AmbiguousWith is not null)
{
    return EquatableDiagnostic.Create(
        SliceDiagnostics.UnsupportedParameterForAspNetAot,
        feature.GetDiagnosticLocationModel(),
        feature.TypeName,
        selection.AmbiguousWith.Name,
        SourceGenerationHelpers.TrimGlobalAlias(selection.AmbiguousWith.TypeFqn),
        "is a second request-body candidate; a handler binds at most one request body. Annotate the intended body with [FromBody], mark injected services with [FromServices] (or use an interface/abstract type), so only one candidate remains");
}
```

Keep the pre-existing per-parameter `Unsupported`-source check (IFormFile/BindAsync/multi-value query) for the **non-body** params: iterate params and, for each, call `SourceGenerationHelpers.ResolveParameterBinding(p, feature.HttpMethod, feature.Pattern, serializableTypes, selection.Body)`; if `binding.Source == Unsupported`, return the existing SLICE070 with `binding.UnsupportedReason`. Do not re-count bodies.

- [ ] **Step 5: Thread `selectedBody` through the AOT argument-emission loop**

In the argument-emission loop (`~523-606`), compute `var selection = SourceGenerationHelpers.SelectBodyParameter(feature, serializableTypes);` once, then for each parameter call `ResolveParameterBinding(p, feature.HttpMethod, feature.Pattern, serializableTypes, selection.Body)`. The existing branches (`Body` → read/deserialize; else → `GetRequiredService`) are unchanged; they now receive at most one `Body`. Verify a non-body serializable concrete param (`AppSettings` from Task 1) reaches the `GetRequiredService` branch.

- [ ] **Step 6: Repeat the convergence for WASI and Lambda emitters**

- `WasiRegistrationEmitter.cs`: replace the `bodyCount` loop (`~178-198`) with the `SelectBodyParameter` + `AmbiguousWith` pattern, emitting SLICE023 with the aligned reason; thread `selection.Body` into the argument-emission loop (`~242`). Use `wasiPlan`'s serializable set (the set already passed as `serializableTypes` there).
- `LambdaFunctionPerFeatureEmitter.cs`: same for the `bodyCount` loop (`~292-307`, SLICE033) and the emission loop (`~105-172`).

- [ ] **Step 7: Add the cross-path consistency test**

```csharp
[Fact]
public void Body_selection_is_consistent_across_aspnetaot_wasi_and_lambda()
{
    // Same nested-Request + concrete-serializable-service shape compiled with all three
    // hosting references; none may emit a body-related error, and each must resolve the
    // service from DI rather than as a body.
    // (Reuse CreateHostCompilation with includeWasiReference + Lambda FPF opt-in; assert
    // SLICE070/023/033 all absent and each generated file resolves the service via DI.)
}
```

Fill this in following the existing WASI + Lambda test scaffolds already in the file (`Generator_infers_shared_wasi_body_contracts` for WASI; search for `LambdaFunctionPerFeature` tests for the Lambda opt-in shape). Assert `SLICE070`, `SLICE023`, `SLICE033` are all absent and each path's generated source resolves the concrete service via `GetRequiredService`.

- [ ] **Step 8: Run the Task-2 tests + full generator suite**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests -c Release`
Expected: all PASS (including the pre-existing binding tests around `SourceGeneratorCompileTests.cs:861,945,969`).

- [ ] **Step 9: Commit**

```bash
git add src/SliceFx.SourceGenerator/Emit/ src/SliceFx.SourceGenerator/Diagnostics/SliceDiagnostics.cs tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs
git commit -m "feat(sourcegen): converge emitters on SelectBodyParameter, actionable SLICE070/023/033

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Verb-gate regression + manifest/portability + SliceResult<T>

**Files:**
- Modify: `src/SliceFx.SourceGenerator/Emit/RouteManifestEmitter.cs` (`bodyCount > 1` at `~295-310`, `FindRequestType:235`, `ClassifyPortability:245`)
- Test: `tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs`

**Interfaces:**
- Consumes: `SelectBodyParameter` (Task 1). `RouteManifestEmitter` already calls `FindSingleBodyParameter` (now delegating) and `ResolveParameterBinding`.

- [ ] **Step 1: Write the non-body-verb regression test (the verb gate)**

```csharp
[Fact]
public void NonBody_verbs_never_select_a_body_parameter()
{
    var source = """
        using Microsoft.AspNetCore.Http;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using SliceFx;

        [assembly: SliceAspNetAot]

        namespace VerbApp
        {
            public sealed record Filter(string Q);

            namespace Features.Items
            {
                [Feature("GET /items/{id}")]
                public static class GetItem
                {
                    public sealed record Request(string Id);   // nested, but GET → not a body
                    public static string Handle(string id, global::VerbApp.Filter filter) => id + filter.Q;
                }
            }

            [SliceJsonContext(SliceJsonTarget.AspNet)]
            [JsonSerializable(typeof(global::VerbApp.Filter))]
            public sealed partial class AotJsonContext : JsonSerializerContext { }
        }
        """;
    var compilation = CreateHostCompilation("VerbApp", source);
    GeneratorDriver driver = CreateDriver();
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags, TestContext.Current.CancellationToken);

    Assert.DoesNotContain(diags, d => d.Id == "SLICE070");
    Assert.DoesNotContain(diags, d => d.Id == "SLICE071");
    var aotSource = GetGeneratedSource(driver, "SliceRegistrations.g.cs");
    Assert.Contains("GetRequiredService(typeof(global::VerbApp.Filter))", aotSource, StringComparison.Ordinal);
}
```

(`Filter` on a `GET` must be DI, never body; the `id` binds from route. `Request` is nested but the verb gate prevents body inference — precedence 2/3 skipped for GET.)

- [ ] **Step 2: Run to confirm PASS (verb gate already in Task 1)**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~NonBody_verbs_never_select" -c Release`
Expected: PASS — `SelectBodyParameter` returns no body for non-body verbs (Task 1 Step 3). This test locks the behavior against future regressions. If it FAILS, the verb gate in `SelectBodyParameter` (the `!IsInferredBodyMethod` early return) is missing or wrong — fix in `SourceGenerationHelpers.cs`.

- [ ] **Step 3: Converge the manifest emitter's body-count loop**

In `RouteManifestEmitter.cs`, replace the `bodyCount > 1` loop (`~295-310`, currently returning `"feature has multiple body parameters (SLICE023)"`) with:

```csharp
var selection = SourceGenerationHelpers.SelectBodyParameter(feature, serializableTypes);
if (selection.AmbiguousWith is not null)
{
    return "feature has multiple body parameters (SLICE023)";
}
```

`FindRequestType` and `ClassifyPortability` already call `FindSingleBodyParameter` (now delegating to `SelectBodyParameter`), so they update automatically.

- [ ] **Step 4: Write the manifest/portability regression test (null-path improvement)**

```csharp
[Fact]
public void Manifest_classifies_shortlink_shape_portable_when_no_wasi_or_lambda()
{
    // A plain ASP.NET app (no WASI/Lambda reference → union serializable set empty/null):
    // nested Request + concrete serializable service on POST classifies portable, not partial.
    var source = """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Microsoft.AspNetCore.Http;
        using SliceFx;

        namespace ManifestApp
        {
            public sealed record AppSettings(string Region);

            namespace Features.Orders
            {
                [Feature("POST /orders")]
                public static class CreateOrder
                {
                    public sealed record Request(string Sku);
                    public static string Handle(Request req, global::ManifestApp.AppSettings s) => req.Sku + s.Region;
                }
            }
        }
        """;
    var compilation = CreateHostCompilation("ManifestApp", source);
    GeneratorDriver driver = CreateDriver();
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags, TestContext.Current.CancellationToken);

    var manifest = GetGeneratedSource(driver, "SliceRouteManifest.g.cs");
    // RequestType resolves to the nested Request; portability is not the multi-body 'partial'.
    Assert.Contains("CreateOrder.Request", manifest, StringComparison.Ordinal);
    Assert.DoesNotContain("multiple body parameters", manifest, StringComparison.Ordinal);
}
```

> Confirm the generated manifest file suffix (`SliceRouteManifest.g.cs`) and how portability appears in the emitted attribute by reading an existing manifest test; adjust the assertions to the real emitted strings while keeping them behavioral (Request resolved, no multi-body reason).

- [ ] **Step 5: Write the `SliceResult<T>` + nested Request test**

```csharp
[Fact]
public void SliceResult_of_t_registers_payload_root_and_selects_nested_request_body()
{
    // POST handler returning Task<SliceResult<CreateOrder.Response>> with a nested Request body
    // and an injected concrete serializable service: Request is the body, Response is the JSON
    // root (payload, not the wrapper), service is DI, no SLICE070.
}
```

Fill following an existing `SliceResult<T>` test scaffold (search `SliceResult` in `SourceGeneratorCompileTests.cs`). Assert `SLICE070` absent, the payload type is a JSON root, and the service resolves via DI.

- [ ] **Step 6: Run the Task-3 tests**

Run: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~Manifest_classifies|FullyQualifiedName~NonBody_verbs|FullyQualifiedName~SliceResult_of_t_registers" -c Release`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SliceFx.SourceGenerator/Emit/RouteManifestEmitter.cs tests/SliceFx.SourceGenerator.Tests/SourceGeneratorCompileTests.cs
git commit -m "feat(sourcegen): converge route manifest on SelectBodyParameter; verb-gate + SliceResult tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Documentation

**Files:**
- Modify: `docs/guides/parameter-binding.md`
- Modify: `docs/aot.md` (Limitations section, `~94`, `~216`)
- Modify: `src/SliceFx.SourceGenerator/AnalyzerReleases.Unshipped.md` (SLICE070 row)

- [ ] **Step 1: Document the precedence in `parameter-binding.md`**

Add a section "Body selection (compile-time paths)" stating the precedence table:

```markdown
## Body selection (AOT / WASI / Lambda paths)

On the compile-time binding paths, a handler binds **at most one request body**.
The body is chosen by, in order:

1. `[FromBody]` — the explicitly annotated parameter (any HTTP method).
2. **Convention** — on `POST`/`PUT`/`PATCH`, the parameter whose type is nested in the
   feature class (the canonical `Request` record).
3. **Sole serializable candidate** — on `POST`/`PUT`/`PATCH`, the single remaining
   request-like parameter registered in a `[SliceJsonContext]` (covers non-nested shared
   contracts used by generated clients).

Every other concrete parameter is resolved from DI. Interfaces/abstract types and
`[FromServices]` parameters are always DI. `GET`/`DELETE` handlers never infer a body.

You no longer need to make all injected services interfaces or wrap `IConfiguration`
to avoid a second-body error: an injected concrete type alongside a nested `Request`
resolves from DI automatically. If two parameters are genuinely body-shaped and
undecidable, SLICE070/023/033 asks you to disambiguate with `[FromBody]`/`[FromServices]`.

**Residual cases:** a concrete type that is a DI service *and* registered in a
`[SliceJsonContext]` and is the *only* request-like parameter on a body verb is treated
as the body — annotate it `[FromServices]` or use an interface. A concrete type that is
selected as the body but was never registered in DI compiles, then fails at DI resolution
at runtime (same as ASP.NET Minimal API).
```

- [ ] **Step 2: Update `docs/aot.md` Limitations**

Replace any statement implying a concrete type in the JSON context on a body verb must use `[FromServices]`/interfaces with a pointer to the new precedence: idiomatic handlers (nested `Request` + injected concrete services) now compile; only genuinely ambiguous multi-body handlers report SLICE070.

- [ ] **Step 3: Update `AnalyzerReleases.Unshipped.md`**

Ensure the SLICE070 row's description reflects "second request-body candidate; disambiguate with `[FromBody]`/`[FromServices]`". Keep the ID/severity columns unchanged.

- [ ] **Step 4: Commit**

```bash
git add docs/guides/parameter-binding.md docs/aot.md src/SliceFx.SourceGenerator/AnalyzerReleases.Unshipped.md
git commit -m "docs: document at-most-one-body selection precedence

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Full-solution verification gate

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution (Release, warnings are errors)**

Run: `dotnet build SliceFx.slnx -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run the whole test suite**

Run: `dotnet test SliceFx.slnx -c Release`
Expected: all pass. Pay attention to `IncrementalCacheTests` (caching must remain green — `SelectBodyParameter` is a pure function over equatable models, so no cache node should change) and the CLI tests (`SliceFx.Cli.Tests`, which assert `slicefx routes` portability output — update any snapshot that legitimately improved from `partial` to `portable`, and confirm the change is the expected multi-body→single-body reclassification, not an accident).

- [ ] **Step 3: Build the affected samples**

Run:
```bash
dotnet build samples/SliceFx.AotSample -c Release
dotnet build samples/SliceFx.WasiSample -c Release
dotnet build samples/SliceFx.LambdaFunctionPerFeatureSample -c Release
```
Expected: all succeed.

- [ ] **Step 4: Format gate (matches CI)**

Run: `dotnet format SliceFx.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591 xUnit1004`
Expected: no changes.

- [ ] **Step 5: Final commit if the format gate or snapshot updates required edits**

```bash
git add -A
git commit -m "chore: format + snapshot updates for body-binding change

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review notes (author)

- **Spec coverage:** precedence rule (Task 1), verb gate (Task 1 + Task 3 Step 1 guard), emitter convergence + diagnostics (Task 2), manifest/portability correction (Task 3), SliceResult<T> (Task 3), record-vs-class (Task 1 Step 7.4), docs incl. residual risks (Task 4), full gate (Task 5). Spec Testing cases 1–10 map to: 1→T1S1, 2→T1S7.2, 3→T1S7.1, 4→T1S7.3, 5→T2S1, 6→T2S7, 7→T3S1, 8→T3S4, 9→T3S5, 10→T1S7.4.
- **Placeholder scan:** the three "fill following the existing scaffold" steps (T2S7, T3S5, and the substring-confirmation notes) reference concrete existing tests to copy from and give exact behavioral assertions; they are not open-ended TODOs. The `ResolveConventionBinding` route/query branch is explicitly called out to keep `IsRouteParam(wireName, pattern)` unchanged (the placeholder expression in the code block is flagged NOT to be used).
- **Type consistency:** `BodySelectionResult(Body, AmbiguousWith)`, `SelectBodyParameter(FeatureModel, HashSet<string>?)`, and `ResolveParameterBinding(..., HandleParamModel? selectedBody)` are used consistently across Tasks 1–3.
