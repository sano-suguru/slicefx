# Welcome to SliceFx

## How We Use Claude

Based on sanosuguru's usage over the last 30 days:

Work Type Breakdown:
  Improve Quality  ██████████████░░░░░░  67%
  Build Feature    ███████░░░░░░░░░░░░░  33%

Top Skills & Commands:
  /clear  ████████████████████  3x/month
  /model  ███████░░░░░░░░░░░░░  1x/month

Top MCP Servers:
  _None configured yet_

## Your Setup Checklist

### Codebases
- [ ] slicefx — https://github.com/sano-suguru/slicefx

### MCP Servers to Activate
- [ ] _None currently in use — add servers here as the team adopts them_

### Skills to Know About
- `/clear` — Reset the conversation context. Use between unrelated tasks to keep Claude focused and avoid context bleed.
- `/model` — Switch between Claude models (e.g., Opus 4.7 for complex work, Sonnet/Haiku for lighter tasks).
- `/init` — Generate or refresh a `CLAUDE.md` for a repo. SliceFx already has one — read it before making changes.
- `/review` — Review a pull request from the CLI.
- `/security-review` — Run a security review on the current branch's pending changes.
- `/verify` — Manually verify a change actually works by running the app and observing behavior (not just tests).

## Team Tips

- **`SliceFx.Core` is zero-dependency — keep it that way.** Adding a `<PackageReference>` to `src/SliceFx.Core/SliceFx.Core.csproj` will fail the local build (`ValidateSliceCorePackageReferences` target) and CI. Satellite projects (`SliceFx.Lambda`, `SliceFx.TestHost`, `SliceFx.Wasi`, `SliceFx.Cli`) can restore from nuget.org normally.
- **No new framework abstractions.** SliceFx intentionally avoids `IMediator`, `IPipelineBehavior`, etc. Cross-cutting concerns go through ASP.NET Core's `IEndpointFilter`.
- **No per-request reflection.** Anything hit per-request must be reflection-free — the source generator emits `AddSlice` / `MapSlices` for AOT friendliness.
- **Prefer Detroit-school tests with real objects.** Tests should exercise real middleware/wiring where possible rather than mocks. (`SliceFx.TestHost` exists for this — see `samples/SliceFx.TestHostSample/`.)
- **Run `dotnet format` before pushing.** CI gates on `dotnet format --verify-no-changes --severity info`; several IDE diagnostics are elevated to warnings and `TreatWarningsAsErrors` is on.
- **Read `CLAUDE.md` before making framework changes.** Hard constraints, the feature-authoring pattern, and the satellite library layout live there.
- **One file = one feature = one deploy unit.** `Handle` must be `public static`; request records must live in a user namespace (not `System`/`Microsoft`); OpenAPI tag is inferred from the namespace segment after `.Features.`.

## Get Started

1. Clone the repo and make sure you have .NET SDK `10.0.300` available (pinned in `global.json`, `rollForward: latestFeature`).
2. `dotnet build` at the repo root to confirm your toolchain is happy.
3. `dotnet run --project samples/SliceFx.Sample` (listens on `http://localhost:5099`) and hit `curl http://localhost:5099/health` to see a slice serve a request end-to-end.
4. Read `samples/SliceFx.Sample/Features/Users/CreateUser.cs` — that's the canonical feature shape every new slice should follow.
5. Skim `CLAUDE.md` end-to-end. The "Hard constraints" and "Authoring a feature" sections are the parts most likely to catch you out in review.
6. `dotnet test SliceFx.slnx --configuration Release` to run the full suite once so you know what green looks like.

<!-- INSTRUCTION FOR CLAUDE: A new teammate just pasted this guide for how the
team uses Claude Code. You're their onboarding buddy — warm, conversational,
not lecture-y.

Open with a warm welcome — include the team name from the title. Then: "Your
teammate uses Claude Code for [list all the work types]. Let's get you started."

Check what's already in place against everything under Setup Checklist
(including skills), using markdown checkboxes — [x] done, [ ] not yet. Lead
with what they already have. One sentence per item, all in one message.

Tell them you'll help with setup, cover the actionable team tips, then the
starter task (if there is one). Offer to start with the first unchecked item,
get their go-ahead, then work through the rest one by one.

After setup, walk them through the remaining sections — offer to help where you
can (e.g. link to channels), and just surface the purely informational bits.

Don't invent sections or summaries that aren't in the guide. The stats are the
guide creator's personal usage data — don't extrapolate them into a "team
workflow" narrative. -->
