# Research: Antigravity IChatClient library

Checked 2026-10-16. Feeds `plan.md`. The product research is in `docs/research-findings.md`; the requirements are in `docs/specification.md` (SPEC), the design in `docs/software-design.md` (DESIGN). This file holds only what planning added. Corpus check: `docs/research/` does not exist, so nothing was reused from it. The lookups were small and run inline, not delegated.

## Does `GetResponseAsync<T>` wrap primitive and array types?

Yes. For a type whose schema is not an object, the extension wraps the schema in an object with one required property `data`, sets `IsWrappedInObject` on the result, and sends that wrapped schema as `ChatOptions.ResponseFormat`. So the library's "top-level must be an object" gate (SO-002) does not break `GetResponseAsync<int>`; the library returns `{"data": ...}` like any other object. With `useJsonSchemaResponseFormat: false` the extension instead sets `ChatResponseFormat.Json` and appends a user message containing the schema text. That second path is what SO-007 (plain JSON) must serve, and the returned text must be bare JSON for the extension to deserialize it. This closes SPEC assumption A-6 and DESIGN risk in §6 step 1.

Source: `ChatClientStructuredOutputExtensions.cs` at dotnet/extensions commit b10f9c0 (the commit Learn lists for Abstractions v10.9.0), fetched through raw.githubusercontent.com, checked 2026-10-16.

## Names the mapper uses (DESIGN §4.3 flagged them unverified)

`ChatFinishReason` is a struct with static `Stop`, `Length`, `ToolCalls`, `ContentFilter`. `UsageContent` exists and holds `UsageDetails Details`. `ChatResponseUpdate` has `Contents`, `ResponseId`, `ModelId`, `CreatedAt`, `FinishReason`, `Role`, `RawRepresentation`, `AdditionalProperties`. `ChatResponse` has `Messages`, `Usage`, `ResponseId`, `ModelId`, `CreatedAt`, `FinishReason`, `RawRepresentation`, `AdditionalProperties`. Resolves the "unverified" note in DESIGN §4.3.

Source: files under `src/Libraries/Microsoft.Extensions.AI.Abstractions` at commit b10f9c0, checked 2026-10-16.

## Tree kill semantics

`Process.Kill(entireProcessTree: true)` terminates descendants; processes the caller lacks permission to inspect are silently skipped; `AggregateException` is thrown when not all descendants could be terminated; `Win32Exception` when the process is already terminating. `WaitForExit` and `HasExited` report only the root process, so they can return while descendants are still exiting. Consequence: tests for "no process left" (NFR-001) must poll for the descendants, not trust `WaitForExit`. Replaces the "reported" label of DESIGN F-46 with "documented" for the API behavior; behavior on macOS and Windows with a real agy stays untested.

Source: [Process.Kill](https://learn.microsoft.com/dotnet/api/system.diagnostics.process.kill?view=net-10.0), checked 2026-10-16.

## Request directory permissions

`Directory.CreateTempSubdirectory` creates the directory with owner-only permissions (700) on Unix-like systems; the parent temp directory may be shared. Resolves the "to verify" in DESIGN §5.7 for the default root. A custom `TemporaryDirectoryRoot` is not covered by this and still needs its own permission step.

Source: [Directory.CreateTempSubdirectory](https://learn.microsoft.com/dotnet/api/system.io.directory.createtempsubdirectory?view=net-10.0), checked 2026-10-16.

## Current package versions (for central version management)

Latest stable on NuGet: Microsoft.Extensions.AI.Abstractions 10.11.0 (the API pages read were v10.9.0); Microsoft.Extensions.Logging.Abstractions, DependencyInjection.Abstractions and Options 10.0.13; xunit 2.9.3; xunit.v3 4.0.2; Microsoft.NET.Test.Sdk 18.11.0; Microsoft.SourceLink.GitHub 10.0.402. Which Abstractions version is the lowest with `ChatOptions.Reasoning` was not checked.

Source: api.nuget.org flat-container index per package, checked 2026-10-16.

## Environment facts the plan relies on

SDK 10.0.112 and runtime 10.0.12 only (no .NET 8 runtime), agy 1.3.5 at `~/.local/bin/agy`, `gh` logged in as `pinkroosterai`, git repo on branch `master` with no commits and no remote. All checked 2026-10-16 with `dotnet --list-runtimes`, `agy --version`, `gh auth status`, `git remote -v`.

## Ruled out

- **Dispatching the research to workers** — five small lookups, all answered with sources I already held; a worker's cold start would have cost more than the lookups.
- **`JsonSchema.Net` 9.x as validator** — licence needs EULA acceptance by consumers (`docs/software-design.md` F-43).

## Still open

- **Lowest Abstractions version that has `ChatOptions.Reasoning`** — carried into Phase 1 `Settle first`; needs a package-by-package check.
- **Schema validator choice** — carried into Phase 5 `Settle first`; needs a build and a fixture run.
- **Real "not signed in" output, real-agy behavior on Windows and macOS, agy 1.4.0** — carried into Phase 7; need the owner's machine, sign-in or account.
