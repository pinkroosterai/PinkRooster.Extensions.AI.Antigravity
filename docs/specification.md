# Specification: `PinkRooster.Extensions.AI.Antigravity` v1

Status: draft for review. Date: 2026-10-16. Inputs: `docs/research-findings.md` (RF), `docs/decisions.md` (DEC), `docs/deferred.md`.
This document says **what** the library does. How it does it is for `docs/software-design.md`.
Priorities: **Must** (v1 fails without it), **Should** (v1 ships without it only with a recorded reason), **Could**.

## 1. Purpose, scope, non-goals

**Purpose.** A .NET class library that implements `Microsoft.Extensions.AI.IChatClient` on top of the Antigravity CLI (`agy`). A request is answered by running `agy` once in non-interactive print mode; its output is mapped back to `ChatResponse`. First consumer: the owner's own .NET apps; then an open-source NuGet release (DEC P1; see open question Q1 about usage terms).

**In scope for v1**
- Plain generated text responses.
- Responses constrained to a JSON schema (`ChatResponseFormat.ForJsonSchema` and `GetResponseAsync<T>`).
- Configuration of agy location, model, timeout, concurrency and environment.
- Safe-by-default behavior: a chat request must not change the caller's files.

**Non-goals.** Being an agent runner; exposing agy's own tools, files, MCP servers or sessions to the caller; signing the user in; managing agy installation or updates.

### 1.1 Out of scope for v1 (each with a future-extension note)

| # | Item | v1 behavior | Future-extension note |
|---|---|---|---|
| O-1 | Caller tool calling (`AIFunction`) | **Delivered 2026-10-16** for invocable `AIFunction`s with no approval, hidden from the response (`docs/tool-calling-mcp-bridge/spec.md`); approval-required tools, `RequireAny`/`RequireSpecific` and tools with a JSON format stay refused (FR-008) | Surfacing calls to the caller (surface-and-resume) or a schema-constrained loop, if callers need the standard flow. |
| O-2 | Streaming of tool steps, thinking and JSON-format answers | Text of plain requests streams; JSON-format requests give one update (FR-019) | Surface tool steps as content; stream a schema answer only if agy adds partial structured output. |
| O-3 | Non-image input, image URIs | Rejected (FR-007). Images as `DataContent` are supported (`docs/multimodal-input/plan.md`). | Other file types: verify agy and model support first. URIs: the library would have to fetch them. |
| O-4 | Embeddings | No `IEmbeddingGenerator` | Not possible with agy. |
| O-5 | Session reuse | Stateless; history resent each call | Map `ConversationId` to agy's conversation resume with a stable directory. |
| O-6 | Caller-chosen working directory / file-editing agent | Always an empty per-request directory (FR-003) | Separate opt-in "agent" client with explicit permission rules. |
| O-7 | Long-prompt file fallback | Prompt over limit rejected (FR-012) | Pass a prompt file and ask agy to read it; measure cost first. |
| O-8 | Retry on invalid structured output | Error, no retry (SO-003) | Opt-in retry count that resends with the validation error. |
| O-9 | Own telemetry (`ActivitySource`, metrics) | Use `UseOpenTelemetry()` of Microsoft.Extensions.AI | Add an activity source following the GenAI conventions. |
| O-10 | `FinishReason.Length` / `ContentFilter` | Always `Stop` on success (FR-016) | Parse agy's safety-block error once its format is stable. |
| O-11 | Preventing agy's stored conversation history | Not prevented (NFR-007) | Re-check each agy release for a no-persist flag. |
| O-13 | Opt-in run of agy with an empty agy home (no user MCP servers) | **Delivered 2026-10-16** as `IsolateAgyHome` (default on for Linux): `HOME` gives agy a separate home (verified on Linux) | Windows and macOS: verify the home variable and sign-in location. |
| O-12 | Typed options for agy's `--mode`, `--agent`, `--project`, and permission rules | Rejected in `AdditionalArguments` (FR-029) | Add typed options after a safety review. |

## 2. Glossary and facts

| Term | Meaning |
|---|---|
| agy | The Antigravity CLI binary. |
| Print mode | `agy -p`: one prompt in, one answer out, then exit. |
| Envelope | The JSON object agy prints in JSON output mode. |
| Request | One call of `GetResponseAsync` or `GetStreamingResponseAsync`. |
| Request directory | The new empty temporary directory a request runs in. |
| Run failure | An agy run that ends with an envelope status other than `SUCCESS`. |

**Facts table.** Label: *verified* (checked on this machine or in official documentation), *reported* (secondary source only), *unknown*. All dated 2026-10-16. "T-xx" are test runs recorded in RF; "RF §n" are sections of `docs/research-findings.md`. Facts from official web pages (F-19 to F-23, F-27) were read through a page-fetch tool that summarizes; quotes were not re-checked in a browser. F-34 to F-43 were added in step 4 and have the same ids in `docs/software-design.md`.

| ID | Fact | Label | Source or command |
|---|---|---|---|
| F-01 | Installed agy version is 1.3.5 at `~/.local/bin/agy`; 1.4.0 exists in the changelog. | verified | `agy --version`, `agy changelog` |
| F-02 | Print-mode flags exist: `--output-format text|json|stream-json`, `--input-format`, `--json-schema`, `--model`, `--effort low|medium|high|xhigh|max`, `--print-timeout`, `--sandbox`, `--mode`, `--disable-slash-commands`, `--dangerously-skip-permissions`, `--add-dir`, `-c/--continue`, `--conversation`. | verified | `agy --help` |
| F-03 | `--print-timeout` default is `0s` (wait for the turn) in `--help`; the official page says 5 minutes. | verified (disagreement) | `agy --help`; headless doc |
| F-04 | JSON envelope fields: `conversation_id`, `status`, `response`, `error`, `duration_seconds`, `num_turns`, `usage{input_tokens,output_tokens,thinking_tokens,cache_read_tokens,total_tokens}`, `structured_output`, `json_schema`, and `denied_actions` (not in the docs). | verified | T-A1, T-A3, T-C4; headless doc |
| F-05 | Print timeout: exit 0, `status:"SUCCESS"`, empty `response`, only a stderr line `[agy] print timeout after 1s with turn in progress; returning partial output`. | verified | T-B2, T-B3 |
| F-06 | A run can return exit 0 with `status:"ERROR"` and a populated `structured_output` (content-safety block). | verified | T-A3 |
| F-07 | A tool denied by permissions gives exit 0, empty text output, `denied_actions` in the envelope, and a stderr notice. | verified | first run, T-C4 |
| F-08 | agy created a file in its working directory in headless default mode; `--sandbox` did not prevent it; `--mode plan` wrote plan files under `~/.gemini/antigravity-cli/brain/`. | verified | T-C4, T-E1, T-E2 |
| F-09 | Every run stores a conversation under `~/.gemini/antigravity-cli/brain/<conversation_id>/`; no flag to disable was found. | verified / unknown (flag) | `ls`; `--help` |
| F-10 | A one-word reply consumed 16,878 input tokens; trivial runs took 2–12 s. | verified | T-A1, T-E3, runs G2, J1 |
| F-11 | The prompt is accepted only as a command-line argument; stdin and `-p -` do not carry it. A 150 KB single argument fails at process start on Linux. | verified | T-B5, T-C1, T-D1, T-C3 |
| F-12 | Slash commands run in print mode (`-p "/help"` prints the command list). | verified | T-D3 |
| F-13 | `--json-schema` accepts a JSON string or a file path; invalid JSON exits 1 without a model call; primitive type names (`string`) are rejected on 1.3.5 although documented. | verified | T-B4; headless doc |
| F-14 | With a schema, `structured_output` holds the clean object; `response` holds the JSON plus extra fields `toolAction` and `toolSummary`. | verified | T-A3, T-B1 |
| F-15 | Exit codes seen: 0 on success, timeout, soft-deny and model-side error; 1 on bad model, bad schema, empty prompt, cancel; 2 on unknown flag or missing flag value. | verified | T-runs |
| F-16 | SIGTERM during a run gives exit 1 with `status:"ERROR"`, `error:"context canceled"`; the run had child processes while active. | verified | T-D4 |
| F-17 | An unknown `--output-format` value is accepted and runs a model turn. | verified | first run |
| F-18 | stdout carries only the answer and diagnostics go to stderr; no TTY is needed on 1.3.5 (stdin and stdout redirected). | verified | T-A1, T-A2 |
| F-19 | A non-interactive unauthenticated run "exits with an `authentication required` error instead of hanging". Not reproduced here. | reported (official doc) / unknown locally | headless doc |
| F-20 | Sign-in methods in the docs: personal OAuth (OS keyring), business/Gemini Enterprise, `gemini-api-key` (needs `modelProvider: gemini` in settings plus `GEMINI_API_KEY`), Application Default Credentials (`AGY_ADC_AUTH=true`). API key and ADC are described as suited to headless and CI. | verified (docs) | antigravity.google/docs/install?tab=cli |
| F-21 | On this machine a token file `~/.gemini/antigravity-cli/antigravity-oauth-token` exists, while the docs say credentials live in the OS keyring. Which one agy actually uses is not established. | verified (file exists) / unknown (use) | `ls` |
| F-22 | Antigravity Additional Terms §6: "Using third party software, tools, or services to access the Service (e.g. using OpenClaw with Antigravity OAuth) is a breach of this Agreement"; may lead to suspension or termination. No effective date shown. | verified | antigravity.google/terms/ |
| F-23 | Antigravity FAQ: headless mode `agy -p` "is intended for direct scripting and CI workflows"; wrapping `agy` inside third-party coding agents or applications, or automating account rotation, "is not supported under consumer plans"; for third-party agents it recommends a Gemini Enterprise or AI Studio API key. No page date. | verified | antigravity.google/docs/faq |
| F-24 | Forum answers disagree on whether a local wrapper around `agy -p` is allowed; they are not policy. | reported | discuss.ai.google.dev threads (search, not opened) |
| F-25 | `IChatClient : IDisposable` has `GetResponseAsync`, `GetStreamingResponseAsync`, `GetService(Type, object?)`, `Dispose`. Members must be thread-safe. Package `Microsoft.Extensions.AI.Abstractions` v10.9.0. | verified | Microsoft Learn API reference |
| F-26 | `ChatOptions` has 20 properties: AdditionalProperties, AllowBackgroundResponses, AllowMultipleToolCalls, ContinuationToken, ConversationId, FrequencyPenalty, Instructions, MaxOutputTokens, ModelId, PresencePenalty, RawRepresentationFactory, Reasoning, ResponseFormat, Seed, StopSequences, Temperature, ToolMode, Tools, TopK, TopP. | verified | Microsoft Learn, v10.9.0 |
| F-27 | `ChatResponseFormat` has `Text`, `Json`, and three `ForJsonSchema` overloads; docs warn many services need a top-level `type=object`. | verified | Microsoft Learn |
| F-28 | `GetResponseAsync<T>` is in package `Microsoft.Extensions.AI`, defaults `useJsonSchemaResponseFormat` to true. How it wraps primitives and reports bad JSON was not read. | verified / unknown | Microsoft Learn |
| F-29 | `UsageDetails` has Input/Output/Total/CachedInput/Reasoning token counts and `AdditionalCounts`. | verified | Microsoft Learn |
| F-30 | *Superseded by F-42.* The shape of `ChatOptions.Reasoning` was not read when step 3 was written. | superseded | — |
| F-31 | No published `IChatClient` that wraps a CLI subprocess was found in two searches (a negative search result, not proof that none exists). | verified (search result only) | web search |
| F-32 | Windows and macOS behavior of agy (process tree, quoting, encoding, argument limit) was not tested. | unknown | needs a run on those systems |
| F-33 | `--output-format` and `--json-schema` in print mode date from agy 1.1.8. | reported | web search summary |
| F-34 | With `--disable-slash-commands`, `-p "/help"` is sent to the model (it answered in prose) instead of printing the built-in command list. | verified | run G1 (step 4) |
| F-35 | A schema the model cannot satisfy gave exit 0, `status:"SUCCESS"`, **no `structured_output`** field, `json_schema` echoed, and JSON-with-extras in `response`. agy checks the schema itself and omits the result. | verified | run G2 |
| F-36 | `--json-schema` accepts an absolute and a relative file path; `--print-timeout 120s` is accepted. | verified | runs J1, J2 |
| F-37 | `--effort` with a level-qualified slug (`gemini-3.8-flash-low --effort high`) exits 1 (`conflicts with --effort`); a base slug (`gemini-3.8-flash --effort low`) and `--effort low` without `--model` both work. | verified | runs G4, G5, H2 |
| F-38 | A preamble saying "do not use tools and never create files" stopped a file-creation request once (n=1). | verified (n=1) | run G3 |
| F-39 | Each agy run starts every MCP server enabled in the user's agy settings (here 4, with 9 descendant processes within 3 s) and exposes their tools to the model. | verified | `agy mcp list`, `ps` in run H1 |
| F-40 | After `kill -9` of only the agy process, none of its 9 descendants was alive 2 s later (Linux). | verified | run H1 |
| F-41 | agy stores full conversation transcripts, prompt and answer text included, under `~/.gemini/antigravity-cli/brain/<id>/.system_generated/logs/transcript.jsonl`. | verified | `grep -rl` for a test prompt |
| F-42 | `ChatOptions.Reasoning` is `ReasoningOptions` (`Effort`: `ReasoningEffort` None, Low, Medium, High, ExtraHigh; and `Output`). Providers should map best effort and may ignore it. | verified | Microsoft Learn, v10.9.0 |
| F-43 | NuGet `JsonSchema.Net` 9.x is under the OSMF EULA with licence acceptance required; 8.0.5 is MIT. `LateApexEarlySpeed.Json.Schema` 4.2.2 is BSD-3-Clause (behavior untested). | verified (nuspec) | api.nuget.org |

## 3. Functional requirements

| ID | Requirement (testable statement) | Priority |
|---|---|---|
| FR-001 | `AntigravityChatClient` implements `IChatClient`: `GetResponseAsync`, `GetStreamingResponseAsync`, `GetService`, `Dispose`. | Must |
| FR-002 | For a text request that succeeds, the result has exactly one assistant message whose text equals agy's answer with agy's single trailing line terminator removed. | Must |
| FR-003 | Each request starts exactly one agy process (the one-time version check of FR-022 is not a request) in a new, empty request directory. That directory is deleted when the request ends by success, failure, timeout or cancellation. If deletion fails, a warning naming the path is logged and the request result is not changed. | Must |
| FR-004 | The agy command line never contains the flags that auto-approve permissions, add directories, resume or continue conversations, or select a mode (`--dangerously-skip-permissions`, `--add-dir`, `--continue`/`-c`, `--conversation`, `--mode`, `--project`, `--new-project`, interactive flags), whatever the caller configures. | Must |
| FR-005 | A request counts as successful only if agy exits 0, the envelope status is `SUCCESS`, and no print-timeout notice was written. Any other combination ends in an exception (section 8). | Must |
| FR-006 | The conversation is turned into one prompt in this order: preamble (FR-014), system text (system-role messages in order, then `ChatOptions.Instructions`), then user and assistant messages in their original order, each marked with its role. Every text part reaches agy unchanged and in order. | Must |
| FR-007 | Content other than text and images is rejected with `NotSupportedException` before any process starts: `UriContent`, `DataContent` that is not `image/png`, `image/jpeg`, `image/gif` or `image/webp` or is not in a user message, and function call, function result and approval content. An image is written as `attachments/image-N.<ext>` in the request directory (at most 10, at most 10 MiB each, else `ArgumentException`) and the turn text points at it; agy 1.4.0 was seen reading a jpeg this way on 2026-10-16 (png, gif, webp unchecked). Reasoning content in history is dropped. | Must |
| FR-008 | **Amended 2026-10-16:** invocable `AIFunction` tools with `ToolMode` null, auto or none are served through the tool bridge (`docs/tool-calling-mcp-bridge/spec.md`). These are rejected with `NotSupportedException` before any process starts: approval-required, declaration-only and hosted tools; `ToolMode` RequireAny and RequireSpecific; tools with a JSON response format; any tools while the agy home is not isolated. Duplicate tool names raise `ArgumentException`. | Must |
| FR-009 | `ChatOptions.ModelId` selects the agy model; if null, `DefaultModelId` of the options is used; if both are null, agy's own default is used. | Must |
| FR-010 | `ChatOptions.Reasoning.Effort` (else the `DefaultReasoningEffort` option) selects the agy effort level: `Low`→low, `Medium`→medium, `High`→high, `ExtraHigh`→xhigh, `None`→no level requested. It is applied only when the effective model is unset or its slug does not end in `-low`, `-medium`, `-high`, `-xhigh` or `-max` (agy rejects that combination, F-37); otherwise it is ignored and a debug entry names the option only. `Reasoning.Output` is ignored. The mapping is published in the README. | Should |
| FR-011 | The options listed as "ignored" in section 4 cause no exception and no change to the agy command line. | Must |
| FR-012 | A prompt longer than `MaxPromptLength` characters, or whose size exceeds the platform's per-argument ceiling (UTF-8 bytes on Linux and macOS, UTF-16 units on Windows; the smaller limit wins), is rejected with `AntigravityPromptTooLargeException` before any process starts. | Must |
| FR-013 | No prompt can start with `/`, so no caller text can invoke an agy slash command. | Must |
| FR-014 | The prompt begins with a preamble telling agy to answer directly, without tools and without writing files. The preamble is on by default and replaceable through options. | Must |
| FR-015 | `ChatResponse.Usage` maps agy token counts: input, output, total, cache-read → cached input, thinking → reasoning. | Must |
| FR-016 | The response has `ResponseId` = agy conversation id, `ModelId` = requested model else default else null, `CreatedAt` = completion time, `FinishReason` = `Stop`. | Must |
| FR-017 | `RawRepresentation` is the parsed envelope. `AdditionalProperties` carries the keys `agy.num_turns`, `agy.duration_seconds` and, when present, `agy.denied_actions`. The raw stdout is not retained. | Must |
| FR-018 | `GetService(typeof(ChatClientMetadata))` with a null key returns metadata with provider name `antigravity` and the default model id; `GetService(typeof(AntigravityChatClient))` returns the client; any other type or a non-null key returns null. | Must |
| FR-019 | `GetStreamingResponseAsync` for a plain-text request yields text updates as agy produces them (agy `stream-json` output), then a final update with usage and the same identifiers as the non-streaming result; joined text equals the `GetResponseAsync` text when agy ran no tools. A JSON-format request (plain or schema) yields exactly one update. Errors surface on a `MoveNextAsync` call (the first when nothing was yielded). Spec: `docs/streaming-responses/spec.md`. | Must |
| FR-020 | After `Dispose`, any member except `Dispose` throws `ObjectDisposedException`; `Dispose` may be called repeatedly. | Must |
| FR-021 | The agy executable is found from the `ExecutablePath` option; if null, from the system search path, then the default install directory of the platform. If not found, `AntigravityNotFoundException` is thrown before any request process starts. | Must |
| FR-022 | The agy version is read once per client instance. A version below 1.3.5 raises `AntigravityVersionException`. A newer version is accepted. The check can be skipped by option. The version check never calls a model. | Must |
| FR-023 | The library enforces `Timeout` (default 5 minutes) per request and raises `AntigravityTimeoutException` when it passes, killing the process. An agy print-timeout notice is treated as a timeout, not as an empty answer. | Must |
| FR-024 | When the caller's cancellation token is cancelled, the process and all its child processes are terminated, the request directory is deleted, and `OperationCanceledException` is thrown. A token that is already cancelled starts no process. | Must |
| FR-025 | At most `MaxConcurrency` (default 4) agy processes run at once per client instance; further requests wait. Waiting does not consume the request timeout, and a waiting request can be cancelled. | Must |
| FR-026 | An empty answer together with denied actions raises `AntigravityRunException` naming the denied action kinds. A non-empty answer with denied actions succeeds, with the denials in `AdditionalProperties` and one warning log entry. | Must |
| FR-027 | When agy reports that it is not signed in, `AntigravityNotAuthenticatedException` is thrown and its message tells the user to start `agy` once interactively. | Must |
| FR-028 | The library does not read, copy or change the user's own agy credentials or settings, and never starts a sign-in. **Amended 2026-10-16 (`docs/tool-calling-mcp-bridge/spec.md`):** it may write files inside an agy home directory that it created for one request and deletes afterwards. agy receives the host environment plus `EnvironmentVariables` from the options, and, when `IsolateAgyHome` applies (Linux), a `HOME` pointing at that home. | Must |
| FR-029 | `AdditionalArguments` containing any flag forbidden by FR-004, or the flags the library sets itself (print, output format, input format, schema, print timeout, model, effort, slash-command switch-off), makes the options invalid (`ArgumentException` at client construction). | Must |
| FR-030 | Every library-raised failure derives from `AntigravityException`, except `ArgumentException`, `NotSupportedException`, `ObjectDisposedException` and `OperationCanceledException`, which keep their standard meaning. | Must |
| FR-031 | Logging uses an optional `ILogger`. With no logger nothing is logged. Entries never contain prompt text, answer text, schema text, environment values or credentials. | Must |
| FR-032 | A dependency-injection extension registers the client as `IChatClient` (singleton) with options bound from configuration or a delegate. | Should |
| FR-033 | Output that is not a readable envelope (not JSON, missing `status`) raises `AntigravityOutputException` with up to 4 KB of stderr on the `StandardError` property, not in the message. | Must |
| FR-034 | Output of at least 1 MiB on stdout or stderr is read completely without blocking the agy process. Output beyond the output cap (assumption A-4: 16 MiB) raises `AntigravityOutputException`. | Must |
| FR-035 | Text is exchanged with agy as UTF-8 without a byte-order mark, including characters outside the Basic Multilingual Plane. | Must |
| FR-036 | Unknown envelope fields are ignored. | Must |
| FR-037 | The README states: tested agy versions, the minimum version, the usage-terms position (section 6), the side-effect residue (NFR-007) and the token overhead (NFR-004). | Must |
| FR-038 | An unrecognised version string from `agy --version` raises `AntigravityVersionException`, unless the version check is skipped. | Should |

## 4. Behavior of IChatClient members and ChatOptions fields

### 4.1 Members

| Member | Behavior | Reason |
|---|---|---|
| `GetResponseAsync(messages, options, ct)` | Supported: FR-002 to FR-009, FR-012 to FR-017, FR-021 to FR-027. Throws `ArgumentNullException` for null `messages`. `options` null = defaults. The caller's `ChatOptions` instance is not modified. | Core |
| `GetStreamingResponseAsync(messages, options, ct)` | Supported: incremental text for plain requests, one update for JSON-format requests (FR-019). | Streaming of tool steps is out (O-2). |
| `GetService(Type, object?)` | FR-018. | Convention for metadata discovery. |
| `Dispose()` | FR-020. No agy process outlives a request, so nothing else is released. | One process per request. |
| `GetResponseAsync<T>` (extension, consumer side) | Works through `ResponseFormat` (section 5). The library does not reference the package that defines it. | F-28 |

### 4.2 ChatOptions fields (all 20)

| Field | Behavior | Reason |
|---|---|---|
| `ModelId` | Honored (FR-009). Unknown model → `AntigravityProcessException`. | agy `--model` exists (F-02). |
| `ResponseFormat` | `Text`: plain. `Json` without schema: answer must be valid JSON (SO-007). `ForJsonSchema`: section 5. | Scope of v1. |
| `Instructions` | Honored as system text (FR-006). | Maps naturally. |
| `Reasoning` | Honored when mappable (FR-010), otherwise ignored. | agy effort levels exist (F-02); shape known (F-42); conflict with level-qualified model slugs (F-37). |
| `Tools` | Invocable `AIFunction`s are served through the tool bridge; other cases → `NotSupportedException` (FR-008). | Silent dropping gives wrong answers. |
| `ToolMode` | Other than null or none → `NotSupportedException`. | Same. |
| `Temperature`, `TopP`, `TopK`, `Seed`, `FrequencyPenalty`, `PresencePenalty` | Ignored. | agy has no such flags (F-02); they only tune quality. |
| `StopSequences`, `MaxOutputTokens` | Ignored. | No agy flag; the caller must not rely on them for limits or cost. Documented. |
| `ConversationId`, `ContinuationToken`, `AllowBackgroundResponses` | Ignored. | Stateless client (O-5). |
| `AllowMultipleToolCalls` | Ignored. | No tools. |
| `RawRepresentationFactory`, `AdditionalProperties` | Ignored. | No provider-specific options in v1. |

## 5. Structured-output requirements

| ID | Requirement | Priority |
|---|---|---|
| SO-001 | For `ResponseFormat` created with `ForJsonSchema`, the schema reaches agy and the library returns agy's structured result, not its free-text answer (F-14). | Must |
| SO-002 | A schema whose top level is not `type: object` is rejected with `ArgumentException` before any process starts. | Must |
| SO-003 | The returned object is validated against the supplied schema. Invalid or mismatching output raises `AntigravityInvalidOutputException`, with the raw output on a property and not in the message. No second agy call is made. | Must |
| SO-004 | A run with status `SUCCESS` but without a structured result raises `AntigravityInvalidOutputException`. This is the normal outcome when agy cannot satisfy the schema (F-35). | Must |
| SO-005 | The response text is the structured result as compact JSON. `GetResponseAsync<T>` called on this client deserializes it into `T` for a schema generated from `T` (record with strings, numbers, nested object, array, enum). | Must |
| SO-006 | If the envelope status is not `SUCCESS`, `AntigravityRunException` is raised even when a structured result is present (F-06). | Must |
| SO-007 | For `ResponseFormat.Json` without a schema, the answer text must parse as JSON, else `AntigravityInvalidOutputException`. | Should |
| SO-008 | A schema that is not valid JSON causes `ArgumentException`; a schema agy itself rejects causes `AntigravityProcessException`. | Must |
| SO-009 | The schema name and description given in `ChatResponseFormat` are made available to the model. | Could |
| SO-010 | Schema validation uses the draft the schema declares; with no declaration, 2020-12. Unsupported keywords are ignored and listed in the README. | Should |

## 6. Authentication and usage terms

| Item | Position | Label, source |
|---|---|---|
| Who signs in | The user, in agy, outside this library (FR-028). | Decision DEC A1 |
| Modes the library works with | Any mode agy itself accepts in print mode: personal OAuth, business sign-in, Gemini API key, Application Default Credentials. The library cannot tell them apart and does not configure them. | F-20 verified (docs); which of them works headless here: unknown |
| Recommended for non-interactive use | API key or ADC (docs: "suits headless and CI runs"). | F-20 |
| Unsupported by the library | Passing credentials as options; sign-in flows; credential refresh; account rotation; sharing one sign-in between several users or machines; reading keyring or token files. | DEC A1, F-23 |
| Usage terms | Terms §6 forbids using third-party software to access the service, giving OpenClaw with OAuth as the example. The FAQ says `agy -p` is "intended for direct scripting and CI workflows" and that wrapping `agy` inside third-party applications is "not supported under consumer plans", and recommends an API key or Enterprise for third-party agents. This library is such a wrapper. | F-22, F-23 verified, read 2026-10-16; neither page shows a date |
| Consequence | The README and package description must state the position above, say that the user is responsible for complying with their plan's terms, and recommend an API key or Enterprise sign-in for anything beyond personal scripting. The library does not check which plan is in use. | FR-037; open question Q1 |
| Not signed in | FR-027; the detection signal is unknown (F-19, Q5). | |

## 7. Non-functional requirements

| ID | Area | Requirement | Priority |
|---|---|---|---|
| NFR-001 | Reliability | After any request outcome (success, each exception, cancellation) no agy process and no request directory created by it remains, checked with a fake agy that spawns a child. The only exception is a directory the operating system refuses to delete (for example a locked file on Windows): it is retried, then reported by a warning that names the path (FR-003). | Must |
| NFR-002 | Timeouts | Default request timeout 5 minutes; settable from 1 second upwards; a timeout is never reported as success (F-05). | Must |
| NFR-003 | Concurrency | One client instance is safe for concurrent use. Ten simultaneous requests with `MaxConcurrency` 4 never run more than 4 agy processes. | Must |
| NFR-004 | Cost control | One agy model call per request. No retry, no warm-up and no preflight call to a model. Documented: each call costs at least ~17,000 input tokens (F-10), and `MaxOutputTokens` is not honored. | Must |
| NFR-005 | Security | The prompt is passed to the process without a shell, so no caller text is interpreted by a shell. agy never runs with auto-approval (FR-004). The library reads no secrets. | Must |
| NFR-006 | Side-effect safety | A request must not create, change or delete files outside its request directory through the library's own actions. agy's own settings on the host can widen what agy may do: allow-rules for commands and files, and the MCP servers agy starts on every run, whose tools the model can call (F-39). The library does not read or override them and the README says so. Since 2026-10-16 (`IsolateAgyHome`, on by default on Linux) a request runs in its own empty agy home, so the user's settings and MCP servers do not reach it; the text above still describes requests that run in the user's own home. | Must |
| NFR-007 | Residue | agy stores each conversation, with the full prompt and answer text, in its own history folder (F-09, F-41). The library cannot prevent this in the user's own home; with `IsolateAgyHome` the history lives in the per-request home, which is deleted with the request. The README states both. The request directory is created readable only by its owner where the platform allows. | Must |
| NFR-008 | Isolation from host config | Behavior does not depend on the current directory of the host process. The only host configuration that reaches agy is the host environment and agy's own settings, sign-in and MCP servers (F-39); with `IsolateAgyHome` (Linux default) agy's settings, rules, skills and MCP servers come from the per-request home instead, and only the sign-in and the host environment reach it. | Must |
| NFR-009 | Privacy | The library never writes the prompt or the answer to disk or logs. The prompt is visible to other local users in the process list while agy runs (command-line argument), and agy itself keeps the full text in its history (NFR-007); the README says both. With `IsolateAgyHome` the system text no longer travels on the command line but in `AGENTS.md` in the request directory. | Must |
| NFR-010 | Performance | Library overhead outside agy's own run time, measured with a fake agy that answers at once: under 100 ms per request on the CI runners, after the one-time version check. Observed agy latency of 2–12 s for a trivial prompt is outside the library's control (F-10). | Should |
| NFR-011 | Platforms | Targets `net8.0` and `net10.0`. Linux x64 is verified. Windows and macOS are supported and run in CI with a fake agy, but real-agy behavior there is "unverified" in the README (F-32). | Must |
| NFR-012 | Compatibility | The tested agy version list is in the README. Behavior on agy newer than the newest tested version is logged once at information level. | Must |
| NFR-013 | Testability | All unit and fake-agy tests run without agy installed and without network. Tests against real agy are opt-in and not part of CI. | Must |
| NFR-014 | Packaging | MIT license; XML documentation on every public member; builds without warnings; no dependency other than those in section 9.4. | Must |
| NFR-015 | Memory | Memory per request is bounded by the output cap (FR-034) plus the prompt. | Should |

## 8. Error and edge-case catalog

"Before spawn" means no agy process was started. In every row the request directory is removed and no process is left (NFR-001). No row logs prompt or answer text (FR-031).

| ID | Case | Observable behavior |
|---|---|---|
| EC-01 | agy not found | `AntigravityNotFoundException` naming the searched locations; before spawn. |
| EC-02 | agy version below minimum | `AntigravityVersionException` with found and required versions; before the request process. |
| EC-03 | Version output unreadable | `AntigravityVersionException` (FR-038). |
| EC-04 | Not signed in | `AntigravityNotAuthenticatedException` (FR-027). Signal unknown, Q5. |
| EC-05 | Timeout (library) | Process killed; `AntigravityTimeoutException` stating the configured timeout. |
| EC-06 | agy's own print-timeout notice, exit 0 | Same as EC-05, never an empty success (F-05). |
| EC-07 | Cancelled before start | `OperationCanceledException`; no process. |
| EC-08 | Cancelled while running | Process tree terminated; `OperationCanceledException` promptly (within 5 s on Linux with the fake agy). |
| EC-09 | Cancelled while waiting for a concurrency slot | `OperationCanceledException`; no process. |
| EC-10 | Non-zero exit (bad model, bad flag, rejected schema, empty prompt) | `AntigravityProcessException` with exit code and the envelope or stderr error text (first line, at most 200 characters) in the message; full stderr (4 KB) on a property. |
| EC-11 | Exit 0, status `ERROR` | `AntigravityRunException` with the envelope `error` text (F-06). |
| EC-12 | Exit 0, status `ERROR`, structured result present | Same as EC-11 (SO-006). |
| EC-13 | Exit 0, empty answer, denied actions | `AntigravityRunException` listing denied action kinds (F-07). |
| EC-14 | Exit 0, non-empty answer, denied actions | Success; denials in `AdditionalProperties`; one warning. |
| EC-15 | Exit 0, empty answer, no denial, no timeout notice | Success with empty text (agy reported success). |
| EC-16 | Stdout not a readable envelope | `AntigravityOutputException` (FR-033). |
| EC-17 | Output beyond the cap | `AntigravityOutputException`; process killed. |
| EC-18 | Schema-invalid output | `AntigravityInvalidOutputException` (SO-003). |
| EC-19 | No structured result with a schema | `AntigravityInvalidOutputException` (SO-004). |
| EC-20 | Prompt too long (characters or platform byte ceiling) | `AntigravityPromptTooLargeException` with length and the limit that was hit; before spawn. |
| EC-21 | Prompt text starts with `/` | Runs as a normal prompt; never an agy command (FR-013). |
| EC-22 | Tools or tool mode supplied | `NotSupportedException` naming the option; before spawn. |
| EC-23 | File, URI, non-image, assistant/system image or tool content in messages | `NotSupportedException`; before spawn. Images in user messages are written as files (FR-007). |
| EC-24 | Empty message list, or no text at all | `ArgumentException`; before spawn. |
| EC-25 | Null `messages` | `ArgumentNullException`. |
| EC-26 | Unknown chat role | `ArgumentException`. |
| EC-27 | Schema top level not an object | `ArgumentException` (SO-002). |
| EC-28 | Request directory cannot be created | `AntigravityException` (base type) naming the directory root; before spawn. |
| EC-29 | agy killed from outside (signal) | `AntigravityProcessException` with exit code. |
| EC-30 | Client disposed | `ObjectDisposedException` (FR-020). |
| EC-31 | Invalid options (negative timeout, concurrency below 1, forbidden extra argument) | `ArgumentException` at construction. |
| EC-32 | Non-UTF-8 bytes in agy output | Invalid sequences replaced; request does not fail on that alone. |
| EC-33 | Agy newer than newest tested | Accepted; one information log (NFR-012). |
| EC-34 | Streaming call that fails | Exception thrown on first `MoveNextAsync` when nothing was yielded, otherwise on the next one; text already yielded stands. |

## 9. Public API surface (outline)

### 9.1 Types

| Type | Kind | Outline |
|---|---|---|
| `AntigravityChatClient` | sealed class : `IChatClient` | `AntigravityChatClient(AntigravityChatClientOptions? options = null, ILogger<AntigravityChatClient>? logger = null)`; const `MinimumAgyVersion = "1.3.5"`; members of `IChatClient`. |
| `AntigravityChatClientOptions` | class | See 9.2. |
| `AntigravityServiceCollectionExtensions` | static class | `AddAntigravityChatClient(this IServiceCollection, Action<AntigravityChatClientOptions>? configure = null)`; registers a singleton `IChatClient`. Chat pipelines (`AsBuilder`) are built by the consumer. |
| `AntigravityException` | class : `Exception` | Base type; `StandardError` (string?, truncated), `ExitCode` (int?) on subtypes that have them. |
| `AntigravityNotFoundException`, `AntigravityVersionException`, `AntigravityNotAuthenticatedException`, `AntigravityTimeoutException`, `AntigravityProcessException`, `AntigravityRunException`, `AntigravityOutputException`, `AntigravityInvalidOutputException`, `AntigravityPromptTooLargeException` | classes : `AntigravityException` | Per sections 3 and 8. `AntigravityInvalidOutputException.RawOutput` (string?). |

### 9.2 Options

| Property | Default | Notes |
|---|---|---|
| `ExecutablePath` | null (search) | FR-021 |
| `DefaultModelId` | null | FR-009 |
| `DefaultReasoningEffort` | null (type `ReasoningEffort?`) | FR-010 |
| `Timeout` | 5 min | FR-023 |
| `MaxConcurrency` | 4 | FR-025 |
| `MaxPromptLength` | 100,000 chars; 30,000 on Windows (reported command-line limit, unverified, Q9) | FR-012 |
| `SkipVersionCheck` | false | FR-022 |
| `EnvironmentVariables` | empty; a null value removes the variable | FR-028 |
| `TemporaryDirectoryRoot` | system temp | FR-003 |
| `AdditionalArguments` | empty | FR-029 |
| `SystemPreamble` | built-in text | FR-014 |
| `MaxOutputBytes` | 16 MiB | FR-034, assumption A-4 |

### 9.3 Usage sketch

```csharp
IChatClient client = new AntigravityChatClient(new() { DefaultModelId = "gemini-3.8-flash-low" });
ChatResponse<Weather> r = await client.GetResponseAsync<Weather>("Weather in Paris?");
```

### 9.4 Dependencies (assumption A-3)

`Microsoft.Extensions.AI.Abstractions`; `Microsoft.Extensions.Logging.Abstractions`; `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` (for FR-032); one JSON-schema validator (chosen in the design).

## 10. Acceptance criteria and traceability

### 10.1 Acceptance criteria for v1

| ID | Criterion |
|---|---|
| AC-01 | With a fake agy, every row of section 8 produces the stated behavior. |
| AC-02 | With real agy (opt-in run by the owner): a text request returns the expected answer; a JSON-schema request returns an object that `GetResponseAsync<T>` deserializes. |
| AC-03 | No test leaves a process or request directory behind (NFR-001), on Linux, Windows and macOS CI. |
| AC-04 | Running a prompt that asks agy to create a file leaves the caller's working directory unchanged with real agy (opt-in run). |
| AC-05 | Every Must requirement has a passing test; every Should has a passing test or a recorded reason. |
| AC-06 | Build is warning-free; the package packs; the README contains everything FR-037 lists. |
| AC-07 | Open questions marked "blocks release" in section 11 are answered. |

### 10.2 Requirement → test traceability

Test ids follow `T-<requirement id>`; the design (step 4) maps each to a test class. Levels: **U** unit with fake process runner, **P** fake-agy executable (real process), **I** opt-in real agy, **D** document review.

| Requirement | Test id | Level | Acceptance |
|---|---|---|---|
| FR-001 | T-FR-001 | U | AC-05 |
| FR-002 | T-FR-002 | U, I | AC-02 |
| FR-003 | T-FR-003 | U, P | AC-03 |
| FR-004 | T-FR-004 | U | AC-05 |
| FR-005 | T-FR-005 | U | AC-01 |
| FR-006 | T-FR-006 | U | AC-05 |
| FR-007 | T-FR-007 | U | AC-01 |
| FR-008 | T-FR-008 | U | AC-01 |
| FR-009 | T-FR-009 | U | AC-05 |
| FR-010 | T-FR-010 | U | AC-05 |
| FR-011 | T-FR-011 | U | AC-05 |
| FR-012 | T-FR-012 | U | AC-01 |
| FR-013 | T-FR-013 | U, I | AC-01 |
| FR-014 | T-FR-014 | U, I | AC-04 |
| FR-015 | T-FR-015 | U | AC-05 |
| FR-016 | T-FR-016 | U | AC-05 |
| FR-017 | T-FR-017 | U | AC-05 |
| FR-018 | T-FR-018 | U | AC-05 |
| FR-019 | T-FR-019 | U | AC-01 |
| FR-020 | T-FR-020 | U | AC-01 |
| FR-021 | T-FR-021 | U, P | AC-01 |
| FR-022 | T-FR-022 | U | AC-01 |
| FR-023 | T-FR-023 | U, P | AC-01 |
| FR-024 | T-FR-024 | P | AC-03 |
| FR-025 | T-FR-025 | P | AC-05 |
| FR-026 | T-FR-026 | U | AC-01 |
| FR-027 | T-FR-027 | U, I | AC-01 |
| FR-028 | T-FR-028 | U | AC-05 |
| FR-029 | T-FR-029 | U | AC-01 |
| FR-030 | T-FR-030 | U | AC-05 |
| FR-031 | T-FR-031 | U | AC-05 |
| FR-032 | T-FR-032 | U | AC-05 |
| FR-033 | T-FR-033 | U | AC-01 |
| FR-034 | T-FR-034 | P | AC-01 |
| FR-035 | T-FR-035 | P | AC-05 |
| FR-036 | T-FR-036 | U | AC-05 |
| FR-037 | T-FR-037 | D | AC-06 |
| FR-038 | T-FR-038 | U | AC-01 |
| SO-001 | T-SO-001 | U, I | AC-02 |
| SO-002 | T-SO-002 | U | AC-01 |
| SO-003 | T-SO-003 | U | AC-01 |
| SO-004 | T-SO-004 | U | AC-01 |
| SO-005 | T-SO-005 | U, I | AC-02 |
| SO-006 | T-SO-006 | U | AC-01 |
| SO-007 | T-SO-007 | U | AC-05 |
| SO-008 | T-SO-008 | U | AC-01 |
| SO-009 | T-SO-009 | U | AC-05 |
| SO-010 | T-SO-010 | U | AC-05 |
| NFR-001 | T-NFR-001 | P | AC-03 |
| NFR-002 | T-NFR-002 | U, P | AC-05 |
| NFR-003 | T-NFR-003 | P | AC-05 |
| NFR-004 | T-NFR-004 | U | AC-05 |
| NFR-005 | T-NFR-005 | U | AC-05 |
| NFR-006 | T-NFR-006 | P, I | AC-04 |
| NFR-007 | T-NFR-007 | U, D | AC-06 |
| NFR-008 | T-NFR-008 | P | AC-05 |
| NFR-009 | T-NFR-009 | U, D | AC-06 |
| NFR-010 | T-NFR-010 | P | AC-05 |
| NFR-011 | T-NFR-011 | P (3-OS CI) | AC-03 |
| NFR-012 | T-NFR-012 | U, D | AC-06 |
| NFR-013 | T-NFR-013 | U | AC-05 |
| NFR-014 | T-NFR-014 | D | AC-06 |
| NFR-015 | T-NFR-015 | P | AC-05 |

Edge cases EC-01 to EC-34 are covered by AC-01; each EC row maps to the requirement named in its row, and the design lists one test per EC id.

## 11. Open questions and assumptions

| ID | Item | Type | Owner | Blocks release |
|---|---|---|---|---|
| Q1 | **Closed (step 5): publish with the warning of section 6.** Question was: FAQ says wrapping `agy` in third-party applications is not supported on consumer plans (F-22, F-23). Publish to NuGet anyway with the warning of section 6 (my default), recommend API key/Enterprise only, or keep it private for your own use? | Closed | User (answered 2026-10-16) | No |
| Q2 | Which sign-in mode do you use, and should the integration tests run under it? Docs say keyring; this machine has a token file (F-21). | Decision | User | No |
| Q3 | Is the prompt visible in the process list (NFR-009) acceptable, given agy takes the prompt only as an argument? | Decision | User | No |
| Q4 | Is it acceptable that the host's agy settings can allow shell commands, and that agy starts the user's MCP servers on every run so the model can call their tools (NFR-006, F-39)? The alternative is reading or overriding agy settings, which contradicts FR-028. | Decision | User | No |
| Q-D1 | **Closed (step 5): warn in the README only; opt-in empty agy home deferred (O-13).** Question was: should the library only warn about MCP servers (default), or later offer an opt-in way to run agy without them (for example a separate empty agy home through the environment)? A test with a changed `HOME` still used the real sign-in, so such a route is not known to work. | Closed | User (answered 2026-10-16) | No |
| Q5 | Exact "not signed in" signal (exit code, text). Needs a run without credentials; I will not touch your sign-in. | Open fact | User (runs it) or assistant with permission | Yes |
| Q6 | **Closed (step 4):** `--disable-slash-commands` stops `/help` being run as a command (F-34). FR-013 is met by the flag plus never starting the prompt with `/`. | Closed | — | No |
| Q7 | **Closed (step 4):** with an unsatisfiable schema agy returns `SUCCESS` without `structured_output` (F-35). | Closed | — | No |
| Q8 | **Closed (step 4):** shape of `ReasoningOptions` read (F-42); mapping in FR-010. F-30 is superseded. | Closed | — | No |
| Q9 | Argument limit on Windows and macOS; default `MaxPromptLength` there. | Open fact | Assistant (CI) | No |
| Q10 | Does agy 1.4.0 change any behavior used here? | Open fact | Assistant, after you update agy | No |
| A-1 | Type name `AntigravityChatClient`. | Assumption | User confirms | No |
| A-2 | `ChatResponse.Text` for an empty successful answer is the empty string (EC-15). | Assumption | Assistant | No |
| A-3 | Extra dependencies for DI and options (section 9.4), beyond DEC P5. | Assumption | User confirms | No |
| A-4 | Output cap 16 MiB and the option `MaxOutputBytes`. | Assumption | Assistant (design) | No |
| A-5 | The preamble (FR-014) lowers tool use enough; one sample worked (F-38), the repeat test is planned in the integration suite. | Assumption | Owner runs the integration suite | No |
| A-6 | `GetResponseAsync<T>` handling of primitive and array `T` is unknown (F-28). If it sends a non-object schema, SO-002 rejects it and `GetResponseAsync<int>` fails with `ArgumentException`; the round-trip test (SO-005) settles this. | Assumption | Assistant (milestone M5) | No |
