# Decisions (step 2)

Date: 2026-10-16. Inputs: `docs/research-findings.md` (cited as F#n, "finding n" of its §1, or §section).
**Asked** = you answered in the interview. **Default** = I chose it; strike or change any line. Deferred and out-of-scope items are tracked in `docs/deferred.md`.

## 1. Purpose and packaging

| ID | Decision | Source |
|---|---|---|
| P1 | First consumer: your own .NET apps; then an open-source NuGet release. v1 is done when plain-text and JSON-schema requests work against real agy, unit tests on a fake process pass, and a README with a sample exists. | Asked |
| P2 | Package id and root namespace: `PinkRooster.Extensions.AI.Antigravity`. Main type `AntigravityChatClient : IChatClient` (type name is my default). | Asked / Default |
| P3 | Targets `net10.0` only (changed 2026-10-16 from `net8.0;net10.0`); C# = default language version of the newest SDK; nullable and implicit usings as the SDK default; XML docs on all public API. | Default |
| P4 | License MIT (matches Microsoft.Extensions.AI). | Default |
| P5 | Depends on `Microsoft.Extensions.AI.Abstractions` only (not `Microsoft.Extensions.AI`; `GetResponseAsync<T>` lives there and the consumer references it themselves) plus `Microsoft.Extensions.Logging.Abstractions`. One JSON-schema validator package is allowed (choice in the design). | Default |
| P6 | Platforms: Linux verified; Windows and macOS supported by design and CI, but behavior is "unverified" in the docs until tested there (F§2, finding 7, 10). | Default |

## 2. Finding and configuring agy

| ID | Decision | Source |
|---|---|---|
| L1 | Executable: `AntigravityChatClientOptions.ExecutablePath`; if null, search `PATH`, then the default install dir (`~/.local/bin`, `%LOCALAPPDATA%\agy\bin`). Not found → `AntigravityNotFoundException`. | Default |
| L2 | Version: run `agy --version` once per client instance (cached). Minimum **1.3.5**; older → `AntigravityVersionException`. Newer is allowed; log once if above the newest tested version. Option `SkipVersionCheck`. | Asked |
| L3 | Caller options: `ExecutablePath`, `DefaultModelId`, `DefaultReasoningEffort`, `Timeout` (default 5 min), `MaxConcurrency` (default 4), `MaxPromptLength` (default 100,000 chars; lower default on Windows), `SkipVersionCheck`, `EnvironmentVariables` (added to the inherited environment), `TemporaryDirectoryRoot` (default system temp), `AdditionalArguments`, `SystemPreamble` (see S3). | Default |
| L4 | `AdditionalArguments` rejects flags that break safety or statelessness: `--dangerously-skip-permissions`, `--add-dir`, `--continue`/`-c`, `--conversation`, `--mode`, `--project`, `--new-project`, `-i`, `--prompt-interactive`, `-p/--print/--prompt`, `--output-format`, `--input-format`, `--json-schema`, `--print-timeout`. | Default |
| L5 | The library never passes a flag value it has not tested (F14): output format is always `json`. | Default (F14) |

## 3. Authentication

| ID | Decision | Source |
|---|---|---|
| A1 | The library never signs in, reads token files, or touches agy settings. It inherits the environment and the user's existing agy session. | Default |
| A2 | Not signed in → `AntigravityNotAuthenticatedException`, message says to run `agy` once interactively. Detection (exit code and stderr text such as "authentication required") is unverified (§6 item 1); the design must isolate it in one place and the integration test must pin the real text. | Default |
| A3 | No preflight sign-in check; the first request finds out. | Default |

## 4. Process model, timeout, cancellation

| ID | Decision | Source |
|---|---|---|
| X1 | One agy process per request; no long-lived process. stdin is closed or empty; stdout and stderr are read concurrently (no deadlock); UTF-8. | Default |
| X2 | Concurrency capped by a semaphore (`MaxConcurrency`). Time waiting for a slot counts for cancellation, not for the timeout. | Default |
| X3 | The library enforces the timeout itself and throws `AntigravityTimeoutException`. It also passes `--print-timeout` = timeout + 10 s as backstop, and treats agy's stderr line `print timeout … returning partial output` as a timeout, never as an empty success (F1). | Default |
| X4 | Cancellation: kill the whole process tree, wait for exit, delete the temp directory, throw `OperationCanceledException`. Tree-kill with agy's tool processes is unverified (§6 item 5). | Default |
| X5 | Exit code 0 is not success (F1–F3): success needs exit 0 **and** envelope `status` = `SUCCESS` **and** no timeout notice. Non-zero exit → `AntigravityProcessException`. `status` ≠ `SUCCESS` → `AntigravityRunException` with the envelope `error`, even if `structured_output` is present (F2). | Default |
| X6 | Empty response with `denied_actions` → `AntigravityRunException` (permission denied); a non-empty response with `denied_actions` succeeds, the denial goes into `AdditionalProperties` and a warning log (F3). | Default |

## 5. Safety of agy's own tools

| ID | Decision | Source |
|---|---|---|
| S1 | Every request runs in a **new empty temp directory** under `TemporaryDirectoryRoot`, deleted afterward (also on cancel and failure). The caller cannot choose the working directory in v1. | Asked |
| S2 | No `--dangerously-skip-permissions`, no `--add-dir`, no `--continue`/`--conversation`; shell commands stay denied by agy's headless default. | Default |
| S3 | The prompt starts with a preamble telling agy to answer directly and not use tools or write files (`SystemPreamble`, on by default, can be replaced). This reduces tool use; it is not a guarantee. | Default |
| S4 | Known residue, documented as such: agy stores every conversation under `~/.gemini/antigravity-cli/brain/` and the library cannot prevent it (F5). `--mode plan` is not used because it writes plan files there and can end in "waiting for approval" (F4). | Default (F4, F5) |
| S5 | Add `--disable-slash-commands` once tested (open: does it stop `/help`? §6 item 2). Until tested, a prompt that starts with `/` gets the preamble prepended, so it never starts with `/`. | Default |

## 6. Message and prompt mapping

| ID | Decision | Source |
|---|---|---|
| M1 | All messages and `ChatOptions.Instructions` are flattened into **one prompt**: preamble, then system text, then the conversation as labeled turns (`User:` / `Assistant:`), ending with a request for the next assistant reply. Stateless; history is always resent. | Default |
| M2 | `ChatRole.System` and `Instructions` → one system section (messages first, then `Instructions`). Unknown custom roles → `ArgumentException`. | Default |
| M3 | Content: `TextContent` mapped; `TextReasoningContent` in history dropped; `DataContent` images (png, jpeg, gif, webp, user messages) → files in the request directory (amended 2026-10-16, `docs/multimodal-input/plan.md`); `UriContent`, other `DataContent`, and any function call/result/approval content → `NotSupportedException`. Empty message list or no text → `ArgumentException`. | Default |
| M4 | Prompt goes in argv (no shell, so no escaping). Longer than `MaxPromptLength` → `AntigravityPromptTooLargeException`; no file fallback in v1. | Asked |

## 7. ChatOptions mapping

| Option | Behavior | Reason |
|---|---|---|
| `ModelId` | Honored → `--model` (else `DefaultModelId`, else agy default). Unknown slug → agy exit 1 → `AntigravityProcessException`. | F2 |
| `ResponseFormat` | `Text`: plain. `Json` without schema: honored by instruction plus JSON parse check. `JsonSchema`: honored, see section 8. | Asked |
| `Instructions` | Honored (M1). | |
| `Reasoning` | Honored → `--effort` (mapping low/medium/high/xhigh/max in design); unmappable → ignored with debug log. | agy flag exists |
| `Tools`, `ToolMode` | Non-empty `Tools`, or `ToolMode` other than null/None → `NotSupportedException`. | Asked |
| `Temperature`, `TopP`, `TopK`, `Seed`, `FrequencyPenalty`, `PresencePenalty`, `StopSequences`, `MaxOutputTokens` | Ignored, debug log of the option names only. No agy flag exists. | Asked |
| `ConversationId`, `ContinuationToken`, `AllowBackgroundResponses` | Ignored with debug log (stateless; sessions out). | Default |
| `AllowMultipleToolCalls`, `RawRepresentationFactory`, `AdditionalProperties` | Ignored. | Default |

## 8. Structured output

| ID | Decision | Source |
|---|---|---|
| J1 | Schema is written to a file in the request's temp directory and passed with `--json-schema <path>` (no argv-size or quoting issue). Top-level must be `type: object`; otherwise `ArgumentException` (agy rejects primitives, F13; M.E.AI wraps primitives itself). | Default |
| J2 | Result is taken from the envelope's `structured_output`, never from `response` (it carries extra `toolAction`/`toolSummary` fields, F11). The returned `ChatMessage` text is that object as compact JSON. | Default |
| J3 | The library validates the object against the schema. Missing, not JSON, or schema mismatch → `AntigravityInvalidOutputException` with the raw output on a property. **No retry.** | Asked |
| J4 | Validator package: chosen in the design (candidate: JsonSchema.Net); must work without reflection-heavy startup and be AOT-friendly if possible. | Default |

## 9. Response mapping

| ID | Decision | Source |
|---|---|---|
| R1 | One assistant `ChatMessage` with `TextContent`. `ResponseId` = `conversation_id`. `ModelId` = requested model, else `DefaultModelId`, else null (the envelope has no model). `CreatedAt` = completion time. | Default |
| R2 | `FinishReason` = `Stop` on success. No `Length` or `ContentFilter` mapping in v1 (agy gives no reliable signal; a safety block shows as `ERROR`, F2). | Default |
| R3 | `Usage`: input → `InputTokenCount`, output → `OutputTokenCount`, total → `TotalTokenCount`, `cache_read_tokens` → `CachedInputTokenCount`, `thinking_tokens` → `ReasoningTokenCount`. Documented: input counts include ~17K tokens of agy overhead (F6). | Default |
| R4 | `RawRepresentation` = the parsed envelope (`JsonElement`); `AdditionalProperties` gets `num_turns`, `duration_seconds`, `denied_actions`. Raw stdout is not stored. | Default |
| R5 | `GetService`: returns `ChatClientMetadata("antigravity", null, DefaultModelId)` and the client itself; otherwise null. | Default |
| R6 | `GetStreamingResponseAsync` runs the normal request and yields **one** `ChatResponseUpdate` with the full text, usage and metadata. | Asked |
| R7 | `Dispose` is a no-op for the process (none is long-lived) and marks the client disposed; later calls throw `ObjectDisposedException`. | Default |

## 10. Errors, observability, tests, docs

| ID | Decision | Source |
|---|---|---|
| E1 | Exception base `AntigravityException`; subtypes: `NotFound`, `NotAuthenticated`, `Version`, `Timeout`, `Process` (non-zero exit), `Run` (status ≠ SUCCESS or denied), `OutputParse` (envelope unreadable), `InvalidOutput` (schema), `PromptTooLarge`. Cancellation uses `OperationCanceledException`; bad input uses `ArgumentException`/`NotSupportedException`. | Default |
| E2 | Exception messages and logs never contain the prompt, response, schema contents, environment values or token data. stderr is kept (truncated to 4 KB) on an exception property, not in the message and not logged by default. | Default (D9) |
| O1 | Logging via optional `ILogger<AntigravityChatClient>` (null logger by default): metadata only (model, duration, exit code, token counts, prompt length, version). OpenTelemetry comes from wrapping with `UseOpenTelemetry()`; no own `ActivitySource` in v1. | Default |
| T1 | Process boundary behind one internal interface (`IProcessRunner`); unit tests use a fake runner returning canned stdout/stderr/exit/timing. A tiny fake-agy console program covers real-process behavior: cancel/tree-kill, large output, UTF-8, timeout. | Default |
| T2 | Integration tests against real agy are opt-in (`AGY_INTEGRATION_TESTS=1`), use `gemini-3.8-flash-low`, never run in CI, and are started by you (they cost tokens). | Default |
| T3 | CI: GitHub Actions on ubuntu, windows, macos: build, unit tests, fake-agy tests, pack. | Default |
| D1 | Docs: README (install, quick start, structured-output sample, safety posture, error table, compatibility and minimum version), XML docs, one sample console app. | Default |
| D2 | Compatibility: tested agy version list kept in the README; unknown envelope fields ignored; missing required fields → `OutputParse`. | Default |

## 11. Out of scope for v1

Real streaming, caller tools, multimodal input, embeddings, session reuse, caller-chosen working directory, `--agent`/`--add-dir`/permission rules, prompt file fallback, retry on invalid output, own telemetry. Each has a future-extension note in `docs/deferred.md`.

## 12. Amendments (step 5, 2026-10-16)

Made while reviewing `specification.md` and `software-design.md`; nothing you answered in the interview was changed.

| ID | Change | Why |
|---|---|---|
| M1 | Turn labels carry a per-request random code (for example `[user|3f9a1c]`) instead of bare `User:` / `Assistant:`. Revert to plain labels if you prefer. | Message text could forge a bare label (design DL-05). |
| S5 | `--disable-slash-commands` is always passed. | Tested: it stops `/help` running as a command (F-34). The preamble-first rule stays as a second layer. |
| L3/L4 | `DefaultReasoningEffort` is typed `ReasoningEffort?`; `AdditionalArguments` also rejects `--model`, `--effort`, `--disable-slash-commands`. | F-37, FR-029. |
| M4 | The prompt limit applies to characters and to the platform's per-argument size, the smaller winning. | Operating-system limits are in bytes (FR-012). |
| X5/J3 | A `SUCCESS` run without `structured_output` is a normal failure path, not an agy defect. | F-35. |

## 13. Amendments (2026-10-16, `docs/tool-calling-mcp-bridge/spec.md`)

| ID | Change | Why |
|---|---|---|
| A1 / FR-028 | The library still never reads, copies or changes the user's own agy files, but writes files inside an agy home it created for one request. | Lets each request run with its own settings and MCP servers (O-13) without touching the user's. |
| L3 | New option `IsolateAgyHome` (default true on Linux, false elsewhere; no effect off Linux). While true on Linux, `EnvironmentVariables` may not set `HOME`. | Per-request home; the verified platform is Linux. |
| S3 / M1 | With an isolated home the preamble and all system text go into `AGENTS.md` in the request directory and out of the prompt; a text over 24,000 bytes (agy's limit for one rule file) is refused with `AntigravityPromptTooLargeException`. Without isolation both stay in the prompt as before. | `AGENTS.md` acts as a system-level rule and left the command line; agy truncates longer files silently. |
| M3 | Unchanged by phase 1; phase 2 of the same plan replaces the tool refusal. | |
