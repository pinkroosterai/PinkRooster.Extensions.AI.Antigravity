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

## Execution, Phase 1, 2026-10-16

### Phase gate

The user ran `/execute-plan antigravity-chat-client` on 2026-10-16 after being told in the plan hand-over that the project `CLAUDE.md` forbids code while its phase is "documentation". Treated as approval to move to implementation; the phase line of `CLAUDE.md` is updated in the same commit as the first code. Source: user's command, this session.

### Package floor

`Microsoft.Extensions.AI.Abstractions` 10.2.0 and older have no `ReasoningOptions` or `ReasoningEffort`; 10.3.0 is the first stable version that has them (checked 10.0.0, 10.1.0, 10.1.1, 10.2.0 absent; 10.3.0, 10.5.0 to 10.9.0 and 10.11.0 present; 10.4.x not opened). Floor for the library: 10.3.0, to be compiled against the floor so no later API is used by accident. Whether 10.3.0 already has `ReasoningEffort.ExtraHigh` is settled by the compile. Source: the `net8.0` assembly of each nupkg from api.nuget.org, searched for the type names, checked 2026-10-16.

### Test framework

xunit 2.9.3 (stable v2 line) with Microsoft.NET.Test.Sdk and plain asserts; xunit.v3 4.0.2 is available and was not chosen because v2 is the established, widely supported runner and the suites here need nothing v3 adds. Revisit if a later phase needs v3 features. Decision made by the executor as a default, 2026-10-16.

### Local net8

The user chose ".NET 10 only for now". Only the .NET 10 runtime is installed. The library still targets `net8.0;net10.0` as DEC P3 says (building for net8.0 needs only the targeting pack, restored from NuGet); tests are built for and run on `net10.0` locally. The `net8.0` test run is left to CI and stays unrun until a remote exists. Interpretation of "just .NET 10": run locally on 10 only, not change the library targets; to be corrected if the user meant single-target. Source: user answer, 2026-10-16.

### Remote

No GitHub remote exists. The workflow file is written; creating or pushing a remote stays with the user. Recorded 2026-10-16.

## Execution, Phase 2, 2026-10-16

### Experimental members of `ChatOptions`

At Abstractions 10.3.0, `ChatOptions.ContinuationToken` and `ChatOptions.AllowBackgroundResponses` are marked experimental (diagnostic MEAI001), which `-warnaserror` turns into a build error even for a read. The library only reads them to name them in a debug entry for ignored options, so the single method that does so suppresses MEAI001 locally; nothing experimental is exposed or used. Other members read so far (`Instructions`, `Reasoning`'s enum `ReasoningEffort`, `ResponseFormat`, `ToolMode`) compile without suppression at the floor version. Source: compiler output of the Phase 2 build against 10.3.0, 2026-10-16.

### Test tooling observation

Tests that must prove a guard exists were checked once by breaking the guard (prompt-start rule in `PromptBuilder`): four tests failed, then passed again after restoring it. Source: local run, 2026-10-16.

## Execution, Phase 3, 2026-10-16

### Process behavior

Linux only (Ubuntu, kernel 7.0.0-38, .NET 10.0.12); Windows and macOS are not settled because no CI exists (no remote) and no such machine is at hand, so they carry into Phase 7.

- **Largest single argument** `Process.Start` accepts through `ArgumentList`: 131,071 ASCII characters (bytes); 131,072 fails at start. .NET reports the failure as a `Win32Exception`, which the runner turns into `AntigravityProcessException` ("agy could not be started"). The library's ceiling of 120,000 bytes sits below this and starts a process fine. Matches the kernel's per-argument limit of 128 KiB including the terminator; the earlier 150,000-byte observation (SPEC F-11) is consistent. Source: `ArgumentSizeTests` output, 2026-10-16.
- **Closing standard input right after start** gives the child end-of-file at once (`stdin-length:0`), the same as the verified `</dev/null` runs. Source: `Standard_input_is_closed…` test, 2026-10-16.
- **Argument passing**: arguments containing newlines, quotes, `$()`, `;`, `&`, `|`, backticks, a leading `--model`, emoji and trailing spaces arrive unchanged, one list entry each. Source: `Each_argument_arrives_unchanged…` test, 2026-10-16.
- **Tree kill**: ending the root with `entireProcessTree: true` also ended a sleeping child that shares the standard streams, on timeout, cancellation and output overflow. With `entireProcessTree: false` the same three tests fail (the child survives), so the tests do detect a missing tree kill. Source: `ProcessCleanupTests` and a mutation run, 2026-10-16.
- **Descendant that holds the pipes after the root exited**: without a bound the readers would wait for it; with the 2-second grace period the runner returned in about 2 s with the output read so far. The runner does not end such a descendant: once the root has exited it can no longer be found as the root's child. Not reproduced with the real agy; its MCP servers ended with it in the earlier Linux test (SPEC F-40). Source: `A_descendant_that_keeps_the_pipes_open…` test, 2026-10-16.
- **Pipe inheritance hang, sequential reading**: reading standard output to the end before starting to read standard error hangs when the child writes 2 MiB to standard error first; the concurrent drain does not. Checked by mutating the runner: the test did not finish within 250 s. Source: mutation run, 2026-10-16.
- **Exit code of a process ended by a signal** (Linux, `kill -9` of itself): reported as a non-zero code, not a timeout. Value not recorded.
- **Runner overhead** over a bare `Process.Start` of the fake agy: measured by `OverheadTests` (median of 7 rounds), written to the test output; the ceiling in the test is 250 ms. Linux figure from the run: see the test output of the commit's CI-less run, not recorded here.

### Windows agy shape

Not settled: only the owner can check whether the Windows installer places `agy.exe` or a batch shim. The runner never launches batch files (DESIGN DL-14). Carried to Phase 7.

## Execution, Phase 4, 2026-10-16

### Fixtures

The plan expected the step-1 agy outputs to be gone and fixtures to be mostly synthetic. Most of those outputs were still in the session record verbatim, so 13 of the 14 fixture groups are transcriptions of real agy 1.3.5 output (conversation ids replaced, not compared byte for byte with the deleted files) and only the not-signed-in pair is synthetic (built from the documented text). Provenance per file: `tests/PinkRooster.Extensions.AI.Antigravity.Tests/Fixtures/README.md`. A newly captured set is still to replace the transcribed files in Phase 7. Source: session record of the runs T-A1, T-A3, T-B2, T-B4, T-B5, T-C4, T-D4, G2, G5, J1, 2026-10-16.

### Names the mapper uses, at the floor version

`ChatFinishReason.Stop`, `UsageContent`, `ChatResponseUpdate(ChatRole?, IList<AIContent>?)` with `ResponseId`, `ModelId`, `CreatedAt`, `FinishReason`, `RawRepresentation`, `AdditionalProperties`, `ChatResponse.Usage`, `ChatOptions.Instructions`, `ChatOptions.Reasoning.Effort` and `ChatClientMetadata(providerName, providerUri, defaultModelId)` all compile against Microsoft.Extensions.AI.Abstractions 10.3.0 (net8.0 and net10.0) without suppressions. This settles the "unverified" note of DESIGN §4.3 for the floor version. Source: build output of Phase 4, 2026-10-16.

### Behavior choices made while building the client

- `GetService` returns the client only for `typeof(AntigravityChatClient)`, as SPEC FR-018 says, not for `IChatClient` or `object` (the usual convention in other clients returns it for any type the client implements). Kept as specified.
- A response format other than plain text is refused with `NotSupportedException` until Phase 5 adds structured output; no test locks this in.
- Tests were checked to bite by two mutations: logging the prompt makes 6 logging tests fail; adding a retry on process failure makes the "never retried" test fail.

## Execution, Phase 5, 2026-10-16

### Schema validator

**Chosen: `LateApexEarlySpeed.Json.Schema` 4.2.2** (BSD-3-Clause), the first candidate of DESIGN DL-12. `JsonSchema.Net` 9.x stays excluded (EULA acceptance by consumers); 8.0.5 (MIT) was only checked for dependencies and advisories, not run through the cases, and remains the fallback.

- Licence: BSD-3-Clause expression in the nuspec. Advisories: `dotnet list package --vulnerable --include-transitive` on a scratch project and on the library → none. Source: api.nuget.org nuspec and `dotnet list`, 2026-10-16.
- Cost: only a `netstandard2.1` asset, which pulls `Microsoft.Extensions.Configuration`, `DependencyInjection`, `Http`, `Logging`, `Options`, `Primitives` 3.1.0 and `LateApexEarlySpeed.Nullability.Generic` 1.0.4 into the consumer's graph (about 11 packages for a guard check). Newer versions of the shared ones win where the consumer already has them (Microsoft.Extensions.AI needs 10.x). Accepted; revisit if the weight is a complaint, with the hand-written subset validator or `JsonSchema.Net` 8.0.5 as alternatives.
- Behavior checked in a scratch project (Linux, .NET 10): required, additionalProperties false, integer vs 5.0 vs 5.5, minimum/maximum, enum, array items, `["string","null"]`, anyOf, oneOf, const, pattern, minLength, uniqueItems, if/then, dependentRequired, unevaluatedProperties, nested objects, `$ref` to `$defs` and `#/definitions`, a recursive `$ref`, a declared draft 7 and 2020-12, an unsatisfiable schema, and a schema generated by `AIJsonUtilities.CreateJsonSchema` (property names are camelCase by default). All gave the expected result. 10,000 validations on one validator took 40 ms.
- Defaults: draft 2020-12 when `$schema` is absent; an unknown `$schema` URI is accepted silently as the default draft; unknown keywords and unknown formats are ignored.
- **Surprise 1:** `format` is asserted by default (`date-time`, `email`, `uuid`, `uri`, `ipv4` reject bad values). `AIJsonUtilities` emits `format` for dates and GUIDs, and the typed deserialization accepts `"2026-10-16"` for a `DateTime`, so asserting would reject answers that deserialize fine. All standard formats are therefore set to annotation-only, as JSON Schema 2020-12 does by default.
- **Surprise 2:** a bad regular expression or an unresolvable `$ref` raises only when an answer reaches that part of the schema (construction succeeds); a wrong type of a keyword (`"required":"a"`, `"type":5`) raises at construction. The library maps both to `ArgumentException`; the second kind can therefore appear after one agy run.
- A remote `$ref` is not fetched; it fails as "cannot find schema".
- Keywords or behavior **not asserted** (for the README, SO-010): `format` (annotation only, by choice); `contentEncoding` / `contentMediaType` (the library's README says content decoding is not supported; a bad base64 value was accepted); remote `$ref` and external documents; anything the library does not know is ignored without notice.

Source: scratch project `/tmp/vspike` (deleted with the machine's temporary files), the library's README at github.com/lateapexearlyspeed/Lateapexearlyspeed.JsonSchema, and the nuspec, all 2026-10-16.

### Typed extension round trip

`GetResponseAsync<T>` works against the client for a record with strings, numbers, an array and an enum, and for `int`, `string[]` and an enum wrapped in `{"data": ...}`, with the schema path and with `useJsonSchemaResponseFormat: false` (plain JSON path; the extension then appends the schema to the prompt). This closes SPEC A-6 with a test, not only with the source reading. Source: `StructuredRoundTripTests`, 2026-10-16.
