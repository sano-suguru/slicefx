# Scheduled / cron patterns

SliceFx does not provide a built-in cron abstraction for ASP.NET or Lambda hosts. This guide
covers the recommended patterns for each deployment target.

## Fermyon Cloud / Spin (native)

Use `ISpinCronHandler` from the `SliceFx.Wasi.Spin` package (see
[Spin-specific satellite documentation](../lambda.md#spin)).

## ASP.NET and Lambda: external scheduler + protected endpoint

For ASP.NET Core, Lambda (ASP.NET-hosted), and general WASI without Spin the recommended pattern
is:

1. **Expose a protected POST endpoint** — e.g. `POST /cron/refresh-feeds`.
2. **Protect it with a shared secret** compared in constant time to prevent timing attacks.
3. **Trigger it from an external scheduler** — GitHub Actions `schedule`, AWS EventBridge,
   Cloudflare Workers Cron Triggers, or any cron-capable CI/CD service.

### Constant-time secret comparison

> **NativeAOT / WASI constraint**: `CryptographicOperations.FixedTimeEquals` is unavailable in
> NativeAOT-LLVM WASI builds. Use a manual XOR-accumulation loop instead.

```csharp
// Constant-time comparison safe under NativeAOT-LLVM WASI.
private static bool ConstantTimeEquals(string a, string b)
{
    if (a.Length != b.Length)
        return false;
    var diff = 0;
    for (var i = 0; i < a.Length; i++)
        diff |= a[i] ^ b[i];
    return diff == 0;
}
```

### Example: refresh-all-feeds cron feature

```csharp
// Features/Cron/RefreshAllFeeds.cs
[Feature("POST /cron/refresh-feeds", Summary = "Refresh all feeds (cron-triggered)")]
public static class RefreshAllFeeds
{
    public static async Task<SliceResult> Handle(
        [FromHeader(Name = "X-Cron-Secret")] string? secret,
        ISpinVariables vars,           // or IConfiguration on ASP.NET
        IFeedRefreshService refresher,
        CancellationToken ct)
    {
        var expected = await vars.GetAsync("CRON_SECRET", ct) ?? string.Empty;
        if (!ConstantTimeEquals(secret ?? string.Empty, expected))
            return SliceResult.Unauthorized("Invalid cron secret.");

        await refresher.RefreshAllAsync(ct);
        return SliceResult.NoContent();
    }
}
```

### GitHub Actions trigger example

```yaml
# .github/workflows/cron.yml
on:
  schedule:
    - cron: '0 * * * *'   # every hour

jobs:
  trigger:
    runs-on: ubuntu-latest
    steps:
      - run: |
          curl -sS -f -X POST https://my-app.example.com/cron/refresh-feeds \
            -H "X-Cron-Secret: ${{ secrets.CRON_SECRET }}"
```

### AWS EventBridge (Lambda)

Use an EventBridge rule with a schedule expression to invoke a Lambda function that POSTs to the
cron endpoint, or use a separate Lambda function triggered directly by EventBridge that calls your
shared application logic (no HTTP hop required in that case).

### Cloudflare Workers Cron Triggers

Add a `[triggers]` section to `wrangler.toml`:

```toml
[triggers]
crons = ["0 * * * *"]
```

Implement `scheduled(event, env, ctx)` in `shim.mjs` to POST to the cron endpoint (in the same
Worker process via `env.SELF`) or call the SliceFx WASI handler directly.

## What to avoid

- **Timer-based background tasks inside the ASP.NET host** (`IHostedService` + `PeriodicTimer`):
  these work but complicate AOT trimming and do not translate to Lambda or WASI.
- **Relying on request concurrency for background work**: Lambda and WASI runtimes freeze the
  process between invocations; background threads will not run.
