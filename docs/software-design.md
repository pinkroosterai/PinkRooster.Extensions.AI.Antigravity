# Software design: `PinkRooster.Extensions.AI.Antigravity` v1

Status: draft for review. Date: 2026-10-16. Inputs: `docs/specification.md` (SPEC), `docs/decisions.md` (DEC), `docs/research-findings.md` (RF), `docs/deferred.md`.
This document says **how** SPEC is met. Requirement ids (FR-, SO-, NFR-, EC-) and fact ids (F-) are SPEC's. Labels: *verified* (run or read, with source), *reported*, *unknown*.
Diagrams are Mermaid. They could not be rendered here; I read each through for syntax (D10) and kept to constructs without special characters in labels.

## 1. Overview

### 1.1 Context

```mermaid
flowchart LR
    App["Host app: IChatClient consumer"] -->|"messages, ChatOptions"| Client["AntigravityChatClient"]
    Client -->|"argv, env, working dir"| Agy["agy process, one per request"]
    Agy -->|"stdout JSON, stderr, exit code"| Client
    Client -->|"ChatResponse or exception"| App
    Client -->|"creates and deletes"| Dir["Request directory in temp"]
    Agy -->|"may write here"| Dir
    Agy --> Svc["Antigravity service and model"]
    Agy --> Home["agy home: sign-in, settings, history"]
    Agy --> Mcp["MCP servers from the user's agy settings"]
```

One request = one `agy` process, started with a fresh empty working directory, JSON output, no shell. The library owns the process lifetime, the timeout and the mapping; agy owns the model call, sign-in and its own history.

### 1.2 Facts established while designing (new in step 4)

All run on 2026-10-16 in `/tmp/agy-scratch` with `gemini-3.8-flash-low`, agy 1.3.5, Linux. F-34 to F-43 are also in SPEC's facts table with the same ids (added in step 5); F-44 to F-47 belong to the design only.

| ID | Fact | Label | Source |
|---|---|---|---|
| F-34 | With `--disable-slash-commands`, `-p "/help"` is sent to the model (it answered in prose) instead of printing the built-in command list. Closes Q6. | verified | run G1 |
| F-35 | A schema the model cannot satisfy (`minimum` 100 with `maximum` 10) gave exit 0, `status:"SUCCESS"`, **no `structured_output`** field, `json_schema` echoed, and the JSON-with-extras in `response`. Closes Q7: agy checks the schema itself and omits the result; it does not fail. | verified | run G2 |
| F-36 | `--json-schema` accepts an absolute and a relative file path, and `--print-timeout 120s` is accepted. | verified | runs J1, J2 |
| F-37 | `--effort` with a level-qualified slug (`gemini-3.8-flash-low --effort high`) exits 1 with `conflicts with --effort`. A base slug (`gemini-3.8-flash --effort low`) and no `--model` with `--effort low` both work. | verified | runs G4, G5, H2 |
| F-38 | A preamble that says "do not use tools and never create files" stopped a file-creation request (no file, no tool use, 2,061 input tokens). One sample only. | verified (n=1) | run G3 |
| F-39 | Each agy run starts every MCP server enabled in the user's agy settings (here 4: `npx` processes, 9 descendants within 3 s) and exposes their tools to the model. They share agy's process group and session. | verified | `agy mcp list`, `ps` in run H1 |
| F-40 | After `kill -9` of only the agy process, none of its 9 descendants was still alive 2 s later (Linux). | verified | run H1 |
| F-41 | agy keeps full conversation transcripts, prompt and answer text included, under `~/.gemini/antigravity-cli/brain/<id>/.system_generated/logs/transcript.jsonl` (found by searching for a test prompt string). | verified | `grep -rl` |
| F-42 | `ChatOptions.Reasoning` is `ReasoningOptions` with `Effort` (enum `ReasoningEffort`: None, Low, Medium, High, ExtraHigh) and `Output`. Providers should map best effort and may ignore. Closes Q8. | verified | Microsoft Learn, v10.9.0 |
| F-43 | NuGet `JsonSchema.Net` 9.x is under the OSMF EULA with `requireLicenseAcceptance=true`; 8.0.5 is MIT. `LateApexEarlySpeed.Json.Schema` 4.2.2 is BSD-3-Clause, supports drafts 7, 2019-09 and 2020-12, depends on `Microsoft.Extensions.Http` 3.1.0 and `System.Text.Json` 9.0.14. Behavior not tested. | verified (nuspec); behavior unknown | api.nuget.org nuspec |
| F-44 | Only the .NET 10 runtime (10.0.12) and SDK 10.0.112 are installed here; `net8.0` tests cannot run locally without another runtime. | verified | `dotnet --list-runtimes` |
| F-45 | The "not signed in" signal is still unknown (Q5). | unknown | not testable without touching sign-in |
| F-46 | Ending a process tree with `Process.Kill(entireProcessTree: true)` works on Windows, Linux and macOS in .NET 8 and 10. | reported (framework documentation as I know it; not re-read) — verify in milestone M3 | |
| F-47 | `ProcessStartInfo.ArgumentList` passes each argument without shell interpretation; on Windows it is joined and quoted for `CreateProcess`, and batch files (`.cmd`, `.bat`) need extra quoting rules. | reported — verify on Windows in M3 | |

## 2. Architecture and components

### 2.1 Components

| ID | Component (visibility) | Responsibility | Main requirements |
|---|---|---|---|
| C1 | `AntigravityChatClient` (public, sealed) | Implements `IChatClient`. Orders the steps of a request: validate, build prompt, ensure agy ready, wait for a slot, create request directory, run, classify, map. Owns the concurrency gate, disposal state and streaming wrapper. | FR-001, 003, 018–020, 025 |
| C2 | `AntigravityChatClientOptions` + `OptionsValidator` (public / internal) | Holds and validates options once at construction; builds a frozen internal copy so later changes by the caller have no effect. | FR-029, EC-31 |
| C3 | `RequestValidator` (internal) | Pure checks before any process: null and empty input, roles, content types, tools, tool mode, schema shape. No I/O. | FR-007, 008, SO-002, SO-008 |
| C4 | `PromptBuilder` (internal) | Turns messages, instructions and response format into one prompt string. Adds preamble, per-request nonce headers, the format section; enforces the length limits. Pure. | FR-006, 012–014, SO-009 |
| C5 | `AgyLocator` and `AgyVersionChecker` (internal) | Resolves the executable; runs `agy --version` once and caches a success; parses and compares the version. | FR-021, 022, 038 |
| C6 | `InvocationBuilder` (internal) | Builds the argument list and environment: model, effort, schema file path, print timeout, flags the library always sets, additional arguments. Single place that knows agy flags. | FR-004, 009–011, 013, 028 |
| C7 | `IAgyProcessRunner` and `AgyProcessRunner` (internal) | Starts the process, drains stdout and stderr, enforces timeout and output cap, kills the tree, returns a plain result. The only code that touches `System.Diagnostics.Process`. | FR-023, 024, 034, 035 |
| C8 | `RequestDirectory` (internal, `IDisposable`) | Creates the owner-only temp directory, writes the schema file, deletes everything on dispose with retries. | FR-003, NFR-001, 007 |
| C9 | `EnvelopeParser` (internal) | Parses stdout into an `AgyEnvelope` record; ignores unknown fields; reports unreadable output. | FR-033, 036 |
| C10 | `ErrorClassifier` with `AuthFailureDetector` (internal) | Decides, from process result plus envelope, whether the run succeeded or which exception applies. The only place that knows agy's error texts, including the unverified sign-in signal. | FR-005, 023, 026, 027, SO-006 |
| C11 | `ResponseMapper` (internal) | Maps a successful envelope to `ChatResponse`: message, ids, usage, raw representation, additional properties. | FR-002, 015–017, SO-005 |
| C12 | `ISchemaValidator`, `StructuredResultChecker` (internal) | Validates `structured_output` against the schema; checks plain-JSON answers; produces compact JSON text. | SO-001, 003, 004, 007, 010 |
| C13 | Exception types (public) | The hierarchy of SPEC section 9.1. | FR-030 |
| C14 | `AntigravityServiceCollectionExtensions` (public) | `AddAntigravityChatClient`. | FR-032 |
| C15 | `LogMessages` (internal) | Source-generated `LoggerMessage` methods with typed, content-free parameters. The only way the library logs. | FR-031, NFR-009 |

### 2.2 Class diagram

```mermaid
classDiagram
    class IChatClient {
        <<interface>>
    }
    class AntigravityChatClient {
        +GetResponseAsync()
        +GetStreamingResponseAsync()
        +GetService()
        +Dispose()
    }
    class AntigravityChatClientOptions
    class OptionsValidator
    class RequestValidator
    class PromptBuilder
    class AgyLocator
    class AgyVersionChecker
    class InvocationBuilder
    class IAgyProcessRunner {
        <<interface>>
        +RunAsync()
    }
    class AgyProcessRunner
    class RequestDirectory
    class EnvelopeParser
    class AgyEnvelope
    class ErrorClassifier
    class AuthFailureDetector
    class ResponseMapper
    class ISchemaValidator {
        <<interface>>
        +Validate()
    }
    class StructuredResultChecker
    class AntigravityException
    class LogMessages

    IChatClient <|.. AntigravityChatClient
    AntigravityChatClient --> AntigravityChatClientOptions
    AntigravityChatClient --> RequestValidator
    AntigravityChatClient --> PromptBuilder
    AntigravityChatClient --> AgyVersionChecker
    AntigravityChatClient --> InvocationBuilder
    AntigravityChatClient --> IAgyProcessRunner
    AntigravityChatClient --> RequestDirectory
    AntigravityChatClient --> EnvelopeParser
    AntigravityChatClient --> ErrorClassifier
    AntigravityChatClient --> ResponseMapper
    AntigravityChatClient --> StructuredResultChecker
    AntigravityChatClient --> LogMessages
    AntigravityChatClientOptions ..> OptionsValidator
    AgyVersionChecker --> AgyLocator
    AgyVersionChecker --> IAgyProcessRunner
    IAgyProcessRunner <|.. AgyProcessRunner
    EnvelopeParser --> AgyEnvelope
    ErrorClassifier --> AuthFailureDetector
    ErrorClassifier ..> AntigravityException
    ResponseMapper --> AgyEnvelope
    StructuredResultChecker --> ISchemaValidator
```

### 2.3 Process-boundary types

```csharp
internal interface IAgyProcessRunner
{
    Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken);
}

internal sealed record AgyProcessRequest(
    string ExecutablePath, IReadOnlyList<string> Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string?> EnvironmentOverrides,
    TimeSpan Timeout, int MaxOutputBytes);

internal sealed record AgyProcessResult(
    int? ExitCode, string StandardOutput, string StandardError,
    bool TimedOut, bool OutputLimitExceeded, TimeSpan Elapsed);
```

Cancellation is the only outcome reported by exception (`OperationCanceledException` after the tree is gone); timeout and cap overflow are results, so the classifier alone decides the exception type. Test assemblies see internals through `InternalsVisibleTo`.

## 3. Key flows

### 3.1 Plain request (FR-002 to FR-006, 009, 015 to 017, 021 to 025)

```mermaid
sequenceDiagram
    participant Caller
    participant Client as AntigravityChatClient
    participant Val as RequestValidator
    participant PB as PromptBuilder
    participant Ver as AgyVersionChecker
    participant Inv as InvocationBuilder
    participant Run as IAgyProcessRunner
    participant Agy as agy process
    participant Cls as ErrorClassifier
    participant Map as ResponseMapper

    Caller->>Client: GetResponseAsync messages, options, ct
    Client->>Client: throw if disposed, ct.ThrowIfCancellationRequested
    Client->>Val: Validate messages, options
    Val-->>Client: ok
    Client->>PB: Build messages, options
    PB-->>Client: prompt text, length checked
    Client->>Ver: EnsureReadyAsync ct
    Note over Ver,Run: first call only: locate agy, run agy --version, compare to 1.3.5, cache result
    Ver-->>Client: executable path
    Client->>Client: wait for concurrency slot, honoring ct
    Client->>Client: create request directory
    Client->>Inv: Build prompt, model, effort, no schema
    Inv-->>Client: arguments, environment
    Client->>Run: RunAsync request, ct
    Run->>Agy: start, stdin closed
    Agy-->>Run: stdout JSON envelope, stderr, exit 0
    Run-->>Client: AgyProcessResult
    Client->>Cls: Classify result
    Cls-->>Client: parsed envelope, success
    Client->>Map: Map envelope, model
    Map-->>Client: ChatResponse
    Client->>Client: delete request directory, release slot
    Client-->>Caller: ChatResponse
```

### 3.2 JSON-schema request (SO-001 to SO-006, SO-008)

```mermaid
sequenceDiagram
    participant Caller
    participant Client as AntigravityChatClient
    participant Val as RequestValidator
    participant Dir as RequestDirectory
    participant Run as IAgyProcessRunner
    participant Agy as agy process
    participant Cls as ErrorClassifier
    participant Chk as StructuredResultChecker

    Caller->>Client: GetResponseAsync with ForJsonSchema format
    Client->>Val: Validate, schema must be an object schema
    Val-->>Client: ok or ArgumentException before any process
    Client->>Dir: Create, write schema.json
    Client->>Run: RunAsync, arguments include the schema file path
    Run->>Agy: start
    Agy-->>Run: envelope with status, structured_output or none
    Run-->>Client: AgyProcessResult
    Client->>Cls: Classify
    alt status is not SUCCESS, even with structured_output
        Cls-->>Client: AntigravityRunException
    else SUCCESS
        Client->>Chk: Check structured_output against schema
        alt field missing, or schema mismatch
            Chk-->>Client: AntigravityInvalidOutputException with raw output
        else valid
            Chk-->>Client: compact JSON text
            Client-->>Caller: ChatResponse with that text
        end
    end
    Client->>Dir: Dispose, delete directory
```

### 3.3 Timeout and cancellation (FR-023, 024, EC-05 to EC-09)

```mermaid
sequenceDiagram
    participant Caller
    participant Client as AntigravityChatClient
    participant Run as AgyProcessRunner
    participant Agy as agy process and children
    participant Cls as ErrorClassifier

    Caller->>Client: GetResponseAsync with ct
    Client->>Run: RunAsync, Timeout 5 min
    Run->>Agy: start
    par timeout path
        Run->>Run: timer fires at Timeout
        Run->>Agy: Kill entire process tree
        Run->>Run: wait for exit, stop readers after grace period
        Run-->>Client: result with TimedOut true
        Client->>Cls: Classify
        Cls-->>Client: AntigravityTimeoutException
        Client-->>Caller: exception, directory deleted
    and cancellation path
        Caller->>Client: ct is cancelled
        Run->>Agy: Kill entire process tree
        Run->>Run: wait for exit, stop readers after grace period
        Run-->>Client: throws OperationCanceledException
        Client-->>Caller: OperationCanceledException, directory deleted
    end
    Note over Client: A token that is already cancelled, or cancelled while waiting for a slot, never reaches Run
```

The two paths in the `par` block are alternatives, never both: whichever fires first wins and the other is disarmed.

### 3.4 agy missing, too old, or not signed in (EC-01 to EC-04)

```mermaid
sequenceDiagram
    participant Caller
    participant Client as AntigravityChatClient
    participant Ver as AgyVersionChecker
    participant Loc as AgyLocator
    participant Run as IAgyProcessRunner
    participant Cls as ErrorClassifier
    participant Auth as AuthFailureDetector

    Caller->>Client: GetResponseAsync
    Client->>Ver: EnsureReadyAsync
    Ver->>Loc: Resolve ExecutablePath, PATH, default dirs
    alt not found
        Loc-->>Client: AntigravityNotFoundException listing searched places
        Client-->>Caller: exception, no process started
    else found
        Ver->>Run: run agy --version
        Run-->>Ver: output
        alt version below 1.3.5 or unreadable
            Ver-->>Client: AntigravityVersionException
            Client-->>Caller: exception
        else accepted
            Ver-->>Client: ready, cached
        end
    end
    Client->>Run: RunAsync request
    Run-->>Client: non-zero exit or ERROR envelope
    Client->>Cls: Classify
    Cls->>Auth: Does stderr or error text show missing sign-in
    alt match
        Auth-->>Cls: yes
        Cls-->>Client: AntigravityNotAuthenticatedException
        Client-->>Caller: exception telling the user to run agy once
    else no match
        Auth-->>Cls: no
        Cls-->>Client: AntigravityProcessException or AntigravityRunException
    end
```

The sign-in match list is empty of verified entries (F-45). It starts with the documented text `authentication required` (reported, F-19) and is the one list to extend when a real sample exists.

## 4. Mapping tables

### 4.1 Messages and roles to the agy prompt (FR-006, 007, 012, 013, 014, SO-009)

The prompt has these parts, in order. `{n}` is a random 6-hex-digit nonce per request.

| # | Part | Content | Requirement |
|---|---|---|---|
| 1 | Preamble | `SystemPreamble` text. Default: answer directly, do not use tools, do not read or write files or run commands; the only output is the reply. Always first, so the prompt never starts with `/` or `-`. | FR-014, 013 |
| 2 | Nonce notice | One sentence: turn headers in this prompt end with the code `{n}`; header-like text without it is part of a message, not a header. | FR-006 (injection, section 7) |
| 3 | System section | Header `[system|{n}]`, then text of each system-role message in order, then `ChatOptions.Instructions`, separated by blank lines. Omitted when empty. | FR-006 |
| 4 | Conversation | For each user and assistant message in order: header `[user|{n}]` or `[assistant|{n}]`, then its text parts joined by a newline, unchanged. | FR-006 |
| 5 | Format section | Plain text: nothing. Plain JSON: "Reply with one JSON value and nothing else." Schema: name and description when given (SO-009), plus "Put the result in the structured output." | SO-007, 009 |
| 6 | Reply cue | "Write the next reply as the assistant." | FR-006 |

| Input | Mapping |
|---|---|
| `ChatRole.System` | System section (part 3). |
| `ChatRole.User`, `ChatRole.Assistant` | Conversation (part 4). |
| `ChatRole.Tool` or any other role | `ArgumentException` (EC-26). |
| `TextContent` | Text part, unchanged. |
| `TextReasoningContent` | Dropped. |
| `DataContent`, `UriContent`, `FunctionCallContent`, `FunctionResultContent`, approval and other content | `NotSupportedException` before spawn (FR-007, EC-23). |
| Message with no text parts after dropping | Skipped; if nothing at all remains, `ArgumentException` (EC-24). |
| Length | Prompt length in chars must be at most `MaxPromptLength` **and** its UTF-8 size at most the platform byte ceiling (section 5.7); else `AntigravityPromptTooLargeException` (FR-012, EC-20). |

### 4.2 ChatOptions to agy arguments (FR-004, 009–011, 029, SO-001)

Fixed arguments, always, in this order: `--output-format json`, `--disable-slash-commands`, then the variable ones, then `AdditionalArguments`, then `-p <prompt>` last.

| Field | Result on the command line | Requirement |
|---|---|---|
| `ModelId`, else `DefaultModelId` | `--model <slug>`; none if both null. | FR-009 |
| `Reasoning.Effort`, else `DefaultReasoningEffort` | `Low`→`--effort low`, `Medium`→`medium`, `High`→`high`, `ExtraHigh`→`xhigh`, `None`→nothing. **Only if the effective model slug does not end in `-low`, `-medium`, `-high`, `-xhigh` or `-max`** (such slugs conflict with `--effort`, F-37); otherwise ignored with a debug log. `max` is not reachable from `ReasoningEffort`. `Reasoning.Output` is ignored. | FR-010 |
| `ResponseFormat` = schema | `--json-schema <absolute path of schema.json in the request directory>` (F-36). | SO-001 |
| `ResponseFormat` = `Text`/`Json` | No schema argument. | SO-007 |
| `Instructions` | Part of the prompt (4.1). | FR-006 |
| `Tools`, `ToolMode` | Rejected before spawn. | FR-008 |
| `Temperature`, `TopP`, `TopK`, `Seed`, `FrequencyPenalty`, `PresencePenalty`, `StopSequences`, `MaxOutputTokens`, `ConversationId`, `ContinuationToken`, `AllowBackgroundResponses`, `AllowMultipleToolCalls`, `RawRepresentationFactory`, `AdditionalProperties` | Nothing; one debug entry lists the names of the non-null ones (never values). | FR-011 |
| `Timeout` option | `--print-timeout <Timeout + 10 s, whole seconds>s` as a backstop (F-36, format `120s` verified). | FR-023 |
| Never produced | `--dangerously-skip-permissions`, `--add-dir`, `-c`, `--continue`, `--conversation`, `--mode`, `--project`, `--new-project`, `-i`, `--prompt-interactive`, `--input-format`, `--sandbox`, `--agent`. | FR-004 |

Environment: the host environment plus `EnvironmentVariables` (a null value removes a variable). Nothing else is added (FR-028).

### 4.3 agy output to ChatResponse (FR-002, 015–017, SO-005)

| agy field | `ChatResponse` | Note |
|---|---|---|
| `response` (text mode, no schema) | One `ChatMessage` (role assistant) with one `TextContent`; one trailing `\n` or `\r\n` removed | FR-002 |
| `structured_output` (schema) | Same message; text = compact JSON of the object | SO-005; `response` is ignored because it carries `toolAction`, `toolSummary` and prose (F-14, F-35) |
| `conversation_id` | `ResponseId` | FR-016 |
| (none) | `ModelId` = requested model, else `DefaultModelId`, else null | envelope has no model |
| (none) | `CreatedAt` = `DateTimeOffset.UtcNow` at completion | |
| `status` = SUCCESS | `FinishReason` = `ChatFinishReason.Stop` | FR-016 |
| `usage.input_tokens` | `Usage.InputTokenCount` | FR-015 |
| `usage.output_tokens` | `Usage.OutputTokenCount` | |
| `usage.total_tokens` | `Usage.TotalTokenCount` | |
| `usage.cache_read_tokens` | `Usage.CachedInputTokenCount` | |
| `usage.thinking_tokens` | `Usage.ReasoningTokenCount` | |
| whole envelope | `RawRepresentation` (a cloned `JsonElement`, so no parser buffers are kept) | FR-017 |
| `num_turns`, `duration_seconds`, `denied_actions` | `AdditionalProperties` keys `agy.num_turns`, `agy.duration_seconds`, `agy.denied_actions` | FR-017, 026 |
| `json_schema`, `error`, unknown fields | Not mapped | FR-036 |

API member names used here (`ChatFinishReason.Stop`, `UsageContent`, `ChatResponseUpdate.Contents`) are from my knowledge of Abstractions 10.x and were not read this session (unverified); M4 checks them against the package before the mapper is written.

Streaming (FR-019): a plain-text request runs with `--output-format stream-json`; `AgyProcessRunner.RunStreamingAsync` hands stdout lines to the client through a channel, `StreamEventReader` turns each into text, model or result, and each text piece becomes a `ChatResponseUpdate` (the trailing line terminator is withheld by `TrailingTerminatorHold` so the joined text equals `ResponseMapper.PlainText`). The `result` event's object goes through `ErrorClassifier` like an envelope and supplies the final update (`UsageContent`, `ResponseId`, `ModelId`, `CreatedAt`, `FinishReason`, no text). `Json` and schema requests take the buffered path and yield one update. The request runs inside the async iterator; leaving it early cancels the run and ends the process tree (EC-34). Full spec: `docs/streaming-responses/spec.md`.

### 4.4 Errors to exceptions (FR-005, 023, 026, 027, 030, 033, SO-003, 004, 006)

`ErrorClassifier` applies these rules in order; the first match wins.

| # | Condition | Exception | EC |
|---|---|---|---|
| 1 | `OutputLimitExceeded` | `AntigravityOutputException` | EC-17 |
| 2 | `TimedOut`, or stderr contains the notice `print timeout after` (case-insensitive) | `AntigravityTimeoutException` | EC-05, 06 |
| 3 | `AuthFailureDetector` matches stderr or the envelope `error` | `AntigravityNotAuthenticatedException` | EC-04 |
| 4 | Exit code is not 0 or process had no exit code (signal) | `AntigravityProcessException` (message from envelope `error` if stdout parses, else first stderr line, max 200 chars) | EC-10, 29 |
| 5 | Stdout is not a JSON object with a string `status` | `AntigravityOutputException` | EC-16 |
| 6 | `status` is not `SUCCESS` | `AntigravityRunException` (envelope `error`, truncated to 200 chars in the message) | EC-11, 12 |
| 7 | Empty `response` and non-empty `denied_actions` (and no `structured_output`) | `AntigravityRunException` (names the action kinds) | EC-13 |
| 8 | Schema requested and `structured_output` absent, not an object, or failing validation | `AntigravityInvalidOutputException` (raw `structured_output` or `response` on `RawOutput`) | EC-18, 19 |
| 9 | JSON format without schema and text does not parse | `AntigravityInvalidOutputException` | SO-007 |
| 10 | Otherwise | success; non-empty `denied_actions` adds one warning log | EC-14, 15 |

Rules 8 and 9 run in `StructuredResultChecker` right after rule 7, since they need the schema. Pre-spawn failures (not found, version, prompt size, bad input, directory creation) come from their own components, not from the classifier. Exceptions carry `StandardError` (up to 4 KB) and `ExitCode` where relevant; neither is placed in the message or logged (FR-031).

## 5. Process management

### 5.1 Start (C7)

1. `ProcessStartInfo`: `FileName` = resolved path, arguments through `ArgumentList` (one list entry per argument, no shell, `UseShellExecute=false`), `WorkingDirectory` = request directory, `CreateNoWindow=true`, redirected stdin, stdout and stderr, `StandardOutputEncoding` and `StandardErrorEncoding` = `new UTF8Encoding(false)`.
2. Environment: start from the inherited block; apply `EnvironmentOverrides`.
3. stdin: close it right after start. agy gets end-of-file immediately, which matches the verified `</dev/null` runs (F-18). Unverified: pipe-closed versus `/dev/null` behave the same; M3 tests it with the fake and the opt-in test with real agy.
4. All three standard streams are redirected, so agy sees no terminal on them; that is the condition verified in F-18. Whether the child still inherits the host's controlling terminal on Unix is unverified and has no known effect. No `setsid` is requested; the tree kill in 5.4 does not depend on it (F-40).

### 5.2 Deadlock avoidance

- stdout and stderr are drained **concurrently** by two tasks started before waiting for exit, each reading into a bounded buffer. The runner never calls `WaitForExit` before the readers finish, and never reads one stream to the end before starting the other.
- The buffer cap is `MaxOutputBytes` (default 16 MiB) for stdout and 64 KiB for stderr. At the stdout cap the runner stops reading, kills the tree and returns `OutputLimitExceeded=true`. For stderr, the first 64 KiB are kept and the rest is discarded while draining continues, so a chatty agy never blocks on a full pipe.
- After a kill, readers get a 2-second grace period to reach end of stream, then their token is cancelled. This covers a grandchild that inherited a pipe handle and would otherwise keep the pipe open (reported .NET behavior, not reproduced here; M3 reproduces it with FakeAgy before relying on the grace period).
- Waiting for exit uses `WaitForExitAsync(token)`; the timeout timer and the caller token are linked into one source whose cancellation triggers the kill, so a single code path ends the process.

### 5.3 Timeout

The runner starts a `CancellationTokenSource.CancelAfter(Timeout)` linked to the caller's token. When it fires and the caller's token is not cancelled, the runner kills the tree and returns `TimedOut=true`. The timeout clock starts after the process has started and does not include time spent waiting for a concurrency slot or the version check (FR-025). agy's own `--print-timeout` is set 10 s later as a backstop; if it fires first, agy exits 0 with the notice in stderr, which rule 2 of 4.4 classifies as a timeout (F-05).

### 5.4 Cancellation and child processes (FR-024, NFR-001)

- On cancellation the runner calls `Process.Kill(entireProcessTree: true)` inside a `try`/`catch` for `InvalidOperationException` (already exited) and `Win32Exception`, then awaits exit with a 5 s guard, then throws `OperationCanceledException`.
- Why a tree kill: agy starts the user's MCP servers as descendants (F-39). On Linux they died with their parent in the test (F-40), but that depends on how the servers watch stdin; the tree kill does not rely on it (F-46, to verify in M3 on all three systems).
- The fake agy used in tests spawns a child that sleeps; the test asserts that neither exists after cancel, timeout and cap overflow on each CI OS (NFR-001).
- Dispose of the client does not cancel running requests (the contract says instances must not be disposed while in use); later calls throw (FR-020).

### 5.5 Encoding

UTF-8 without byte-order mark in both directions. Arguments: .NET encodes `ArgumentList` for the platform (UTF-8 on Unix, UTF-16 on Windows), so characters outside the Basic Multilingual Plane should survive on both (reported framework behavior, F-47; `EncodingTests` in M3 checks it with FakeAgy). Output: invalid byte sequences become U+FFFD instead of failing (EC-32). Whether agy writes UTF-8 on Windows when stdout is a pipe is unknown (F-32); the opt-in test on Windows includes an emoji and non-Latin text and decides whether a fix is needed.

### 5.6 Platform differences

| Topic | Linux | macOS | Windows |
|---|---|---|---|
| Executable lookup | `PATH`, then `~/.local/bin/agy` (verified install location, F-01) | same | `PATH` with `PATHEXT`, then `%LOCALAPPDATA%\agy\bin` (documented) |
| Batch-file shims | n/a | n/a | A resolved path ending in `.cmd` or `.bat` is refused with `ArgumentException` (argument-injection risk through `cmd.exe`, F-47); `.exe` only. If agy ships only a shim, this needs a decision (R-8). |
| Tree kill | `Process.Kill(true)` (F-46) | same, unverified | same, unverified |
| Argument limit | a 150,000-byte argument failed to start (verified, F-11); the kernel's per-argument limit of 128 KiB is known to me but not measured here | unknown | command line about 32,767 UTF-16 units, reported |
| Temp directory | owner-only mode 0700 (to verify in M2) | same, to verify | per-user temp, ACL not changed (unverified that it is private) |
| Exit code on kill | 137 or none (unverified) | same, unverified | any, unverified |
| CI | ubuntu-latest | macos-latest | windows-latest |

### 5.7 Environment and working directory

- Working directory: always the request directory (FR-003). It is created with `Directory.CreateTempSubdirectory` under `TemporaryDirectoryRoot` (default system temp), which creates it owner-only on Unix (to verify in M2). When a custom root is set, the library creates a uniquely named child and applies `UnixFileMode` 700. The host's current directory is never used (NFR-008).
- Deletion: recursive, in `finally`, with up to 3 retries 100 ms apart for locked files on Windows. A failure to delete is logged as a warning with the path (not an exception, since the answer is already known); R-9 records this as a deviation from NFR-001 "no directory remains" in the failing case.
- Prompt byte ceiling: `MaxPromptBytes` is internal and computed: Linux and macOS 120,000 bytes of UTF-8 (a conservative value below the verified failure at 150,000 bytes; the true limit and the macOS limit are unverified, and M3 probes them with FakeAgy and the opt-in test); Windows 30,000 UTF-16 units (below the reported 32,767 limit, unverified). `MaxPromptLength` (characters, default 100,000, Windows default 30,000) applies as well; the smaller limit wins.
- The preamble nonce and the request directory name use `RandomNumberGenerator`, not `Random`.

## 6. Structured-output design

1. **Gate (before spawn).** `RequestValidator` parses `ChatResponseFormatJson.Schema` (a `JsonElement`). Not an object, or the top-level `type` is not `"object"` → `ArgumentException` (SO-002, SO-008). A schema that `System.Text.Json` cannot hold is already a `JsonElement` error at the caller. How `GetResponseAsync<T>` treats a primitive or array `T` is unverified (F-28): the documentation only advises callers to wrap such types in an object (F-27). If the extension sends a non-object schema, this gate rejects it with `ArgumentException` (assumption A-6); the round-trip test (SO-005) in M5 runs the real extension with `int`, `string[]` and a record and decides whether the gate needs an adaptation.
2. **Delivery.** The schema text is written to `schema.json` in the request directory and passed by path (verified, F-36). Nothing about the schema goes through argv or logs.
3. **Result source.** Use `structured_output` only (F-14, F-35).
4. **agy's own check.** agy omits `structured_output` when it cannot produce a matching value, still returning exit 0 and `SUCCESS` (F-35). So a missing field is a normal failure path, not an agy defect: rule 8 of 4.4 → `AntigravityInvalidOutputException`.
5. **Library check.** The result is validated against the same schema by `ISchemaValidator` (SO-003). It guards against differences between agy's validator and ours; it is cheap and local. Draft: as declared in `$schema`, else 2020-12 (SO-010). Unsupported keywords: listed in the README after the validator is chosen (DL-12).
6. **Text.** The object is written back with `Utf8JsonWriter` without indentation (SO-005) so `GetResponseAsync<T>` deserializes it.
7. **Plain JSON format (SO-007).** No `--json-schema`. The prompt asks for one JSON value; the text is trimmed; a single surrounding Markdown code fence (a line starting with three backticks, optionally `json`, and a closing one) is removed; the rest must parse with `JsonDocument` (no trailing content). Otherwise `AntigravityInvalidOutputException`. A schema such as `{}` was not tried with agy (unknown), so it is not used.
8. **No retry** (DEC J3, O-8). One call, one result or one exception.
9. **Errors carry the raw output** (`RawOutput`, string) for the caller to inspect. It is never logged and never in the message.

## 7. Security and safety design

### 7.1 Side effects of agy's own tools (D4)

Layers, strongest first. Each is verified or labelled.

| # | Measure | What it does | Limit |
|---|---|---|---|
| 1 | Fresh empty request directory per request | Any file agy writes in its working directory lands there and is deleted (F-08: writes in cwd are allowed). | Writes outside the directory are not stopped by this. |
| 2 | Never auto-approve; no `--add-dir`; no `--mode` | Keeps agy at its headless defaults: workspace file access allowed, shell commands soft-denied (F-07, F-08). | The user's own agy settings can add allow-rules (Q4). |
| 3 | Preamble | Tells the model not to use tools or write files. Test G3: it worked once (F-38). | A model instruction, not enforcement. |
| 4 | `--disable-slash-commands` and a prompt that never starts with `/` | Caller text cannot run agy commands or skills (F-12, F-34). | |
| 5 | Documentation | README states: agy runs the user's MCP servers on every request and can call their tools (F-39); history is stored with full text (F-41); `--sandbox` did not stop file writes (F-08). | The library cannot switch MCP servers off without editing agy's settings, which FR-028 forbids (R-3, open question Q-D1). |

The design adds a check, not a promise: the opt-in integration test (AC-04) asks real agy to create a file and asserts the caller's directory is unchanged and the request directory was cleaned.

### 7.2 Prompt passing, argument length, injection (D5)

| Threat | Handling |
|---|---|
| Shell interpretation of caller text | None: no shell, `ArgumentList` (F-47, verify on Windows). |
| Argument length | Reject over `MaxPromptLength` and over the platform byte ceiling before spawn (5.7, FR-012). Verified from bash on Linux: starting agy with a 150,000-byte argument fails with `Argument list too long` (exit 126, F-11). How .NET reports the same failure (probably a `Win32Exception`) is unverified; the ceiling avoids reaching it. Windows and macOS limits unverified. |
| Caller text read as an agy flag | The prompt is the value of `-p`, and always begins with the preamble. Verified: a value starting with `--` is taken as the prompt (RF finding 9). |
| Caller text read as an agy slash command or skill | `--disable-slash-commands` (verified effective, F-34) plus the preamble-first rule. |
| Forged conversation turns inside a message | Turn headers contain a per-request nonce unknown to the caller before the call (4.1). Not a security boundary against a hostile model or tool output; `IChatClient` documents that prompt injection is the application's concern (F-25). |
| Schema path injection | The path is generated by the library; the schema text is never placed in argv. |
| Extra arguments from options | Allow-list-free but deny-list validated (FR-029): forbidden flags rejected at construction, matching `--flag` and `--flag=value`, and short forms. |
| Batch-file launch on Windows | Refused (5.6). |
| Environment | Only the host environment and caller-supplied entries; values are never logged. |

### 7.3 Log hygiene and secrets (D9)

- Logging only through `LogMessages` (source-generated). Every method takes typed parameters from this closed list: model id string, version, exit code, durations, token counts, prompt length (number), boolean flags, exception type name, option names. There is no `string message` parameter and no way to pass a prompt, answer, schema, stderr or environment value.
- Levels: Debug for ignored option names and argument count (not arguments, since the prompt is one of them); Information for version above newest tested and request summaries; Warning for denied actions with answer, and temp-directory deletion failure (path only); Error is not used by the library (exceptions carry the information).
- stderr is stored on the exception property only (truncated, 4 KB), never logged. `RawRepresentation` and `RawOutput` are returned to the caller, not logged.
- The library does not read credentials (FR-028). No `ToString` override on options prints environment values.
- A test (`LoggingTests`) uses a capturing logger and sentinel strings (`SECRET-PROMPT`, `SECRET-ANSWER`, `SECRET-ENV`, `SECRET-STDERR`) through success and every error path, then asserts none appears in any log entry, message or `ToString` of exceptions (except the two properties documented to carry raw data).
- Privacy residue the library cannot remove, stated in the README: the prompt is visible in the process list while agy runs (F-11, NFR-009), and agy stores full transcripts (F-41).

## 8. Testing design

### 8.1 Layers

| Layer | Project | What | Needs agy |
|---|---|---|---|
| U | `…Tests` | Pure components and the client with a fake `IAgyProcessRunner` returning canned `AgyProcessResult`s and recorded envelopes. | no |
| P | `…ProcessTests` + `tools/FakeAgy` | The real runner against `FakeAgy`, a small console program that imitates agy by command-line switches or environment variables: print an envelope, print a timeout notice, sleep, spawn a child that sleeps, write 20 MiB, emit non-UTF-8 bytes, emit emoji, exit with code N, create a file in its working directory. Plus real-process tests of the locator and version check. | no |
| I | `…IntegrationTests` | Real agy. Skipped unless `AGY_INTEGRATION_TESTS=1`; model `gemini-3.8-flash-low`; tiny prompts; the owner starts them (they spend tokens). | yes |
| D | review checklist | README items, packaging. | no |

The test framework is xUnit with plain `Assert` (no assertion library with a commercial licence, to keep contributors free of licence questions). Test projects also reference `Microsoft.Extensions.AI` so `GetResponseAsync<T>` can be exercised (SO-005); the library does not.

### 8.2 Recorded fixtures

Stored under `tests/…Tests/Fixtures/`, one file each, taken verbatim from the runs in RF and this step with the conversation id replaced by a fixed value. Fixture provenance is recorded in `Fixtures/README.md`.

| Fixture | From | Used for |
|---|---|---|
| `success-text.json` | RF T-A1 | FR-002, 015–017 |
| `success-schema.json` | RF T-B1 / run J1 | SO-001, 005 |
| `schema-missing-structured.json` | run G2 | SO-004, EC-19 |
| `error-safety-block.json` (status ERROR, structured_output present) | RF T-A3 | SO-006, EC-12 |
| `timeout-empty-success.json` + `timeout.stderr.txt` | RF T-B2 | FR-023, EC-06 |
| `denied-empty.json` and `denied-with-answer.json` + stderr | RF first run, T-C4, run G2 | FR-026, EC-13, 14 |
| `exit1-bad-model.json` + stderr | run G5 / RF | EC-10 |
| `exit1-empty-prompt.json` | RF T-B5 | EC-10 |
| `exit1-bad-schema.stderr.txt` | RF T-B4 | SO-008 |
| `cancel-context-canceled.json` | RF T-D4 | EC-29 |
| `version-1.3.5.txt` | `agy --version` | FR-022 |
| *missing:* not-signed-in output | not obtainable (F-45) | FR-027 uses a **synthetic** fixture built from the documented text and marked as such; an integration test pins the real one when it exists |

### 8.3 Opt-in integration tests

| Test | Checks |
|---|---|
| `Plain_prompt_returns_text` | AC-02, FR-002 |
| `Schema_prompt_roundtrips_into_T` | AC-02, SO-001, 005 |
| `Unsatisfiable_schema_throws_InvalidOutput` | SO-004 with real agy (F-35) |
| `File_creation_request_leaves_no_trace` | AC-04, NFR-006 |
| `Leading_slash_is_not_a_command` | FR-013 |
| `Preamble_blocks_tool_use_sample` | A-5, five repeats, reports the rate; informational |
| `Unicode_roundtrip` | FR-035 |
| `Not_signed_in_signal` | Q5, runs only when the owner points `AGY_TEST_NO_AUTH_HOME` at an agy home without credentials |
| `Reasoning_effort_with_base_model` | FR-010, F-37 |

Each prints the command run and the envelope status, never the prompt in CI logs (not applicable: they are not run in CI).

### 8.4 CI

GitHub Actions: matrix ubuntu, windows, macos; restore, build with warnings as errors, U and P tests on `net10.0` and `net8.0` (the net8.0 runtime is installed in CI by `setup-dotnet`; locally only net10.0 runs, F-44), pack, upload package artifact. Integration tests are not in CI. A scheduled workflow is not planned.

### 8.5 Requirement → component → test

Test ids are SPEC's `T-<id>`. Class names are the proposed test classes.

| Requirement | Component | Test class (level) |
|---|---|---|
| FR-001 | C1 | `ClientTests` (U) |
| FR-002 | C11, C4 | `ResponseMapperTests` (U); `IntegrationTests` (I) |
| FR-003 | C8, C1 | `RequestDirectoryTests` (U); `ProcessCleanupTests` (P) |
| FR-004 | C6 | `InvocationBuilderTests` (U) |
| FR-005 | C10 | `ErrorClassifierTests` (U) |
| FR-006 | C4 | `PromptBuilderTests` (U) |
| FR-007 | C3 | `RequestValidatorTests` (U) |
| FR-008 | C3 | `RequestValidatorTests` (U) |
| FR-009 | C6 | `InvocationBuilderTests` (U) |
| FR-010 | C6 | `InvocationBuilderTests` (U); `IntegrationTests` (I) |
| FR-011 | C6 | `InvocationBuilderTests` (U) |
| FR-012 | C4 | `PromptBuilderTests` (U) |
| FR-013 | C4, C6 | `PromptBuilderTests`, `InvocationBuilderTests` (U); `IntegrationTests` (I) |
| FR-014 | C4 | `PromptBuilderTests` (U); `IntegrationTests` (I) |
| FR-015 | C11 | `ResponseMapperTests` (U) |
| FR-016 | C11 | `ResponseMapperTests` (U) |
| FR-017 | C11 | `ResponseMapperTests` (U) |
| FR-018 | C1 | `ClientTests` (U) |
| FR-019 | C1 | `StreamingTests` (U) |
| FR-020 | C1 | `ClientTests` (U) |
| FR-021 | C5 | `AgyLocatorTests` (U); `LocatorProcessTests` (P) |
| FR-022 | C5 | `VersionCheckerTests` (U) |
| FR-023 | C7, C10 | `ProcessRunnerTests` (P); `ErrorClassifierTests` (U) |
| FR-024 | C7, C1 | `ProcessRunnerTests` (P); `ClientTests` (U) |
| FR-025 | C1 | `ConcurrencyTests` (P) |
| FR-026 | C10 | `ErrorClassifierTests` (U) |
| FR-027 | C10 | `ErrorClassifierTests` (U, synthetic fixture); `IntegrationTests` (I) |
| FR-028 | C6, C7 | `InvocationBuilderTests` (U), `EnvironmentTests` (P) |
| FR-029 | C2 | `OptionsValidatorTests` (U) |
| FR-030 | C13 | `ExceptionHierarchyTests` (U) |
| FR-031 | C15 | `LoggingTests` (U) |
| FR-032 | C14 | `DiExtensionTests` (U) |
| FR-033 | C9 | `EnvelopeParserTests` (U) |
| FR-034 | C7 | `ProcessRunnerTests` (P) |
| FR-035 | C7 | `EncodingTests` (P) |
| FR-036 | C9 | `EnvelopeParserTests` (U) |
| FR-037 | docs | `ReadmeChecklist` (D) |
| FR-038 | C5 | `VersionCheckerTests` (U) |
| SO-001 | C6, C8, C12 | `StructuredOutputTests` (U); `IntegrationTests` (I) |
| SO-002 | C3 | `RequestValidatorTests` (U) |
| SO-003 | C12 | `StructuredOutputTests` (U) |
| SO-004 | C12, C10 | `StructuredOutputTests` (U); `IntegrationTests` (I) |
| SO-005 | C11, C12 | `StructuredRoundTripTests` (U, uses `Microsoft.Extensions.AI`) |
| SO-006 | C10 | `ErrorClassifierTests` (U) |
| SO-007 | C12, C4 | `StructuredOutputTests` (U) |
| SO-008 | C3, C10 | `RequestValidatorTests`, `ErrorClassifierTests` (U) |
| SO-009 | C4 | `PromptBuilderTests` (U) |
| SO-010 | C12 | `SchemaValidatorTests` (U) |
| NFR-001 | C7, C8 | `ProcessCleanupTests` (P) |
| NFR-002 | C7 | `ProcessRunnerTests`, `OptionsValidatorTests` (P, U) |
| NFR-003 | C1 | `ConcurrencyTests` (P) |
| NFR-004 | C1 | `ClientTests` (U): exactly one runner call per request, no retry on any error |
| NFR-005 | C6, C7 | `InvocationBuilderTests` (U): no shell, no auto-approve |
| NFR-006 | C8, C6 | `SideEffectTests` (P: FakeAgy writes a file; caller dir unchanged); `IntegrationTests` (I) |
| NFR-007 | C8, docs | `RequestDirectoryTests` (U, Unix mode); `ReadmeChecklist` (D) |
| NFR-008 | C7, C8 | `WorkingDirectoryTests` (P: host cwd changed, same behavior) |
| NFR-009 | C15, docs | `LoggingTests` (U); `ReadmeChecklist` (D) |
| NFR-010 | C1, C7 | `OverheadTests` (P, measured, reported not gated on shared runners) |
| NFR-011 | all | CI matrix (P on 3 OS) |
| NFR-012 | C5, C15 | `VersionCheckerTests` (U); `ReadmeChecklist` (D) |
| NFR-013 | build | `NoAgyNeededCheck` (CI job with `PATH` stripped of agy) |
| NFR-014 | build | `ReadmeChecklist`, build flags (D) |
| NFR-015 | C7 | `ProcessRunnerTests` (P, output cap) |

### 8.6 Edge-case tests (one per EC id)

| EC | Test (class: method) | Level |
|---|---|---|
| EC-01 | `AgyLocatorTests: Missing_executable_throws_NotFound_listing_places` | U |
| EC-02 | `VersionCheckerTests: Older_than_minimum_throws_Version` | U |
| EC-03 | `VersionCheckerTests: Unreadable_output_throws_Version` | U |
| EC-04 | `ErrorClassifierTests: Documented_auth_text_maps_to_NotAuthenticated` | U |
| EC-05 | `ProcessRunnerTests: Timeout_kills_tree_and_reports_TimedOut` | P |
| EC-06 | `ErrorClassifierTests: Agy_timeout_notice_with_exit0_is_timeout` | U |
| EC-07 | `ClientTests: Cancelled_token_starts_no_process` | U |
| EC-08 | `ProcessRunnerTests: Cancel_kills_tree_within_5s` | P |
| EC-09 | `ConcurrencyTests: Cancel_while_waiting_for_slot` | P |
| EC-10 | `ErrorClassifierTests: Nonzero_exit_maps_to_Process` | U |
| EC-11 | `ErrorClassifierTests: Error_status_exit0_maps_to_Run` | U |
| EC-12 | `ErrorClassifierTests: Error_status_with_structured_output_maps_to_Run` | U |
| EC-13 | `ErrorClassifierTests: Empty_answer_with_denials_maps_to_Run` | U |
| EC-14 | `ErrorClassifierTests: Answer_with_denials_succeeds_and_warns` | U |
| EC-15 | `ResponseMapperTests: Empty_success_gives_empty_text` | U |
| EC-16 | `EnvelopeParserTests: Not_json_throws_Output` | U |
| EC-17 | `ProcessRunnerTests: Output_over_cap_kills_and_reports` | P |
| EC-18 | `StructuredOutputTests: Schema_mismatch_throws_InvalidOutput` | U |
| EC-19 | `StructuredOutputTests: Missing_structured_output_throws_InvalidOutput` | U |
| EC-20 | `PromptBuilderTests: Too_long_in_chars_or_bytes_throws` | U |
| EC-21 | `PromptBuilderTests: Prompt_never_starts_with_slash` | U |
| EC-22 | `RequestValidatorTests: Tools_and_ToolMode_rejected` | U |
| EC-23 | `RequestValidatorTests: Non_text_content_rejected` | U |
| EC-24 | `RequestValidatorTests: Empty_input_rejected` | U |
| EC-25 | `ClientTests: Null_messages_throws_ArgumentNull` | U |
| EC-26 | `RequestValidatorTests: Unknown_role_rejected` | U |
| EC-27 | `RequestValidatorTests: Non_object_schema_rejected` | U |
| EC-28 | `RequestDirectoryTests: Unwritable_root_throws_AntigravityException` | U |
| EC-29 | `ErrorClassifierTests: No_exit_code_maps_to_Process` | U |
| EC-30 | `ClientTests: Use_after_dispose_throws` | U |
| EC-31 | `OptionsValidatorTests: Invalid_options_rejected` | U |
| EC-32 | `EncodingTests: Invalid_bytes_replaced_not_fatal` | P |
| EC-33 | `VersionCheckerTests: Newer_version_accepted_with_info_log` | U |
| EC-34 | `StreamingTests: Failure_surfaces_on_first_MoveNext` | U |

## 9. Compatibility strategy (D6)

| Aspect | Strategy |
|---|---|
| Minimum version | 1.3.5 (`MinimumAgyVersion`), checked once per client (FR-022). Lower is rejected because 1.3.5 is the only version whose behavior is verified (DEC L2). The README lists tested versions; 1.4.0 is untested (Q10). |
| Upper bound | None. A version above the newest tested logs once at Information (NFR-012). |
| Flags | Only flags verified on 1.3.5 are ever produced (4.2): `--output-format json`, `--disable-slash-commands`, `--model`, `--effort`, `--json-schema <path>`, `--print-timeout <n>s`, `-p`. All of them are built in `InvocationBuilder` only. Unknown `--output-format` values are accepted silently by agy (F-17), so the library never builds the value from input. |
| Output format | `EnvelopeParser` requires only a JSON object with string `status`; every other field is optional and read defensively; unknown fields are ignored (FR-036). Missing `usage` gives null `Usage`. Missing `conversation_id` gives null `ResponseId`. |
| Behavior that agy documents but does not do (F-03, F-13, F-19) | The library follows the binary, not the docs (RF §5), and each deviation has a fixture. |
| Error texts | Matched in one class (`AuthFailureDetector`) and one regex for the timeout notice (`ErrorClassifier`). A change in wording degrades to `AntigravityProcessException` or `AntigravityRunException`, never to a silent success, because success requires exit 0, status `SUCCESS` and no notice (FR-005). |
| Drift detection | The opt-in integration suite is the compatibility check; run it after each agy update and record the version in the README. Fixtures carry the agy version they came from. |
| Library versioning | SemVer; pre-1.0 (`0.x`) until Q1 and Q5 are closed. A change of minimum agy version is a minor version bump. |

## 10. Project layout, packaging, versioning, milestones

### 10.1 Layout

```
AgyChatClient/
  PinkRooster.Extensions.AI.Antigravity.sln
  Directory.Build.props        net8.0;net10.0, nullable, warnings as errors, XML docs, SourceLink
  Directory.Packages.props     central package versions
  src/PinkRooster.Extensions.AI.Antigravity/
      AntigravityChatClient.cs, AntigravityChatClientOptions.cs, Exceptions/…
      Internal/ (PromptBuilder, RequestValidator, InvocationBuilder, AgyLocator,
                 AgyVersionChecker, AgyProcessRunner, RequestDirectory, EnvelopeParser,
                 ErrorClassifier, AuthFailureDetector, ResponseMapper,
                 StructuredResultChecker, LogMessages)
      DependencyInjection/AntigravityServiceCollectionExtensions.cs
  tests/PinkRooster.Extensions.AI.Antigravity.Tests/          (U) + Fixtures/
  tests/PinkRooster.Extensions.AI.Antigravity.ProcessTests/   (P)
  tests/PinkRooster.Extensions.AI.Antigravity.IntegrationTests/ (I, opt-in)
  tools/FakeAgy/                                              console, net10.0
  samples/ConsoleSample/
  docs/   README.md   LICENSE (MIT)   .github/workflows/ci.yml
```

### 10.2 Packaging

- One package `PinkRooster.Extensions.AI.Antigravity`, targets `net8.0;net10.0`, symbols (`snupkg`), SourceLink, deterministic build, README in package, MIT licence expression.
- Dependencies: `Microsoft.Extensions.AI.Abstractions` (floor to be set at the lowest version that has `ChatOptions.Reasoning` and `ReasoningOptions`: not yet determined; v10.9.0 is the version read, F-25, F-42), `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, and one schema validator (DL-12).
- No analyzers or source generators are shipped. `LoggerMessage` generation is build-time only.
- Trimming and AOT: not claimed in v1; JSON handling uses `JsonDocument`/`Utf8JsonWriter` so it is likely compatible, but untested.

### 10.3 Versioning

Version in `Directory.Build.props` (`VersionPrefix`), `0.1.0` first; tag `vX.Y.Z` triggers the pack job. 1.0.0 only after AC-07 (open questions that block release are answered).

### 10.4 Implementation order

| M | Milestone | Delivers | Requirements | Done when |
|---|---|---|---|---|
| M0 | Skeleton | Solution, props, CI on 3 OS, empty projects, FakeAgy stub, licence | NFR-011, 014 | CI green on an empty test |
| M1 | Contracts | Options + validator, exceptions, `MinimumAgyVersion`, `IAgyProcessRunner` types | FR-029, 030, EC-31 | `OptionsValidatorTests`, `ExceptionHierarchyTests` pass |
| M2 | Input side | `RequestValidator`, `PromptBuilder`, `InvocationBuilder`, `RequestDirectory`, `AgyLocator` and version parsing | FR-003, 004, 006–014, 021, 022, 038, SO-002, 008, 009 | the U tests for these pass; owner-only directory mode checked |
| M3 | Process runner | `AgyProcessRunner` against FakeAgy: timeout, cancel, tree kill, caps, UTF-8, concurrency drain | FR-023–025, 034, 035, NFR-001–003, 008, 015 | P tests pass on Linux locally and on all three CI systems; F-46 and F-47 resolved |
| M4 | Output side, plain | `EnvelopeParser`, `ErrorClassifier`, `AuthFailureDetector` (documented text), `ResponseMapper`; client assembled; fixtures recorded | FR-001, 002, 005, 015–020, 026, 027, 033, 036, EC-01–EC-34 except schema ones | `AntigravityChatClient` plain request passes all U tests with the fake runner |
| M5 | Structured output | Schema file, validator spike (criteria: licence, dependencies and advisories, passes a set of our fixtures incl. 2020-12 and enum/array/nested), `StructuredResultChecker` | SO-001, 003–007, 010 | `StructuredOutputTests`, round-trip tests pass |
| M6 | Surface | `GetService`, streaming wrapper, DI extension, `LogMessages`, logging tests | FR-018, 019, 031, 032, NFR-009 | `LoggingTests`, `DiExtensionTests` pass |
| M7 | Real agy | Integration tests; owner runs them; fixtures refreshed; Q5, A-5 results recorded | AC-02, 04 | owner reports pass/fail output; results written to `docs/` |
| M8 | Release | README (FR-037 items), sample, package validation, release notes | FR-037, NFR-012, 014, AC-06, 07 | pack succeeds from a clean checkout; `ReadmeChecklist` ticked |

Each milestone is one or a few commits (one logical change per commit, conventional commits, no branch unless asked).

## 11. Decision log (D7)

| ID | Decision | Alternatives considered | Reason |
|---|---|---|---|
| DL-01 | One internal process-boundary interface `IAgyProcessRunner` returning a plain result | (a) Mock `Process` through a wrapper; (b) use CliWrap; (c) call `Process` directly in the client | (a) `Process` is hard to fake faithfully; (b) adds a dependency for 100 lines of code and hides the kill and drain rules the spec cares about; (c) untestable. A narrow interface plus a FakeAgy executable covers both logic and real-process behavior. |
| DL-02 | Timeout and output-cap overflow are returned as result flags; only cancellation throws | Throw from the runner for all three | The classifier is then the single place that chooses exception types and the unit tests need no real process. |
| DL-03 | Close stdin immediately after start | Leave it open; redirect from `NUL`/`/dev/null` | The verified runs used `</dev/null` (EOF at once). An open pipe could make a tool wait for input. Equivalence of a closed pipe is unverified and tested in M3 (5.1). |
| DL-04 | Prompt goes in argv with a byte and character ceiling | Prompt file fallback; always a file; stdin | stdin does not work (F-11). The file route costs an extra tool turn and changes behavior (DEC M4). |
| DL-05 | Turn headers with a per-request nonce | Plain `User:` labels (DEC M1 wording); XML-style tags with escaping; JSON array of messages | Plain labels are forgeable by message text; escaping would change text (FR-006 says unchanged); JSON changes how the model reads history. A nonce keeps the text intact and defeats pre-written fake headers. DEC M1 said "labeled turns"; the nonce is a refinement, not a change. |
| DL-06 | Schema by file path in the request directory | Inline string in argv | Verified both work (RF T-A3, F-36). File avoids argv growth and quoting, and keeps schema text off the process list (NFR-009). |
| DL-07 | `structured_output` only; require status `SUCCESS` | Fall back to parsing `response` when the field is missing | The fallback would accept answers agy itself rejected (F-35) and `response` has extra fields (F-14). |
| DL-08 | Validate with our own validator even though agy checks | Trust agy; or skip validation | DEC J3 requires validation; the cost is small and it makes the guarantee independent of agy internals. |
| DL-09 | Use the library timeout as the authority, agy's `--print-timeout` as backstop at +10 s | Only agy's timeout; only ours | agy's timeout returns exit 0 with an empty answer (F-05); ours kills the tree and raises a typed error. The backstop covers a host that freezes the library's timer. |
| DL-10 | `--disable-slash-commands` always, plus preamble first | One of the two | Flag verified effective (F-34); preamble-first keeps FR-013 true even if a future agy drops the flag. |
| DL-11 | Effort only for base model slugs | Always pass `--effort`; catch exit 1 and retry without; reject the option | Level-qualified slugs make agy exit 1 (F-37); retrying breaks "one process per request" (FR-003, NFR-004); rejecting would fail callers that set Reasoning globally. Suffix rule is a heuristic and documented (R-6). |
| DL-12 | Schema validator behind `ISchemaValidator`; chosen in M5. Preference order: `LateApexEarlySpeed.Json.Schema` (BSD-3) then `JsonSchema.Net` pinned to 8.x (MIT). **Not** `JsonSchema.Net` 9.x | JsonSchema.Net 9.x (best known); a hand-written subset validator; no validator | 9.x needs EULA acceptance, which an MIT library cannot impose on its users (F-43). A hand-written validator risks wrong results on 2020-12. The interface keeps the choice reversible. LateApex pulls `Microsoft.Extensions.Http` 3.1.0, which may raise advisories; M5 checks that. |
| DL-13 | Tree kill on cancel, timeout and cap | Kill only the agy process | agy starts MCP servers as children (F-39); Linux test showed they die with the parent (F-40), but that depends on the servers. |
| DL-14 | Refuse `.cmd`/`.bat` as agy path | Allow through `cmd.exe` | Quoting batch arguments safely is known to be error-prone (F-47). |
| DL-15 | Preserve the host environment, apply overrides | Scrub the environment | agy needs its sign-in variables (`AGY_ADC_AUTH`, `GEMINI_API_KEY`, F-20); scrubbing would break them. |
| DL-16 | Request directory deletion errors are logged, not thrown | Throw | The answer is already obtained; throwing would discard a paid result. Recorded as R-9 against NFR-001. |
| DL-17 | xUnit with plain asserts | FluentAssertions; Shouldly; NUnit | Avoids licence questions in a MIT package's test suite; asserts here are simple. |
| DL-18 | Streaming runs the request inside the iterator (buffered wrapper for JSON-format requests; line-by-line for plain text since `docs/streaming-responses`) | Run eagerly in the method, return a completed enumerable | Iterator semantics give "errors on first `MoveNextAsync`" (FR-019) and cancellation through `[EnumeratorCancellation]`. |
| DL-19 | Options copied at construction and validated once | Read live options | Thread safety (F-25) and no surprises from mutation after construction. |
| DL-20 | Logging through source-generated `LoggerMessage` with typed, content-free parameters | `ILogger.LogDebug` with strings | Makes it structurally impossible to log a prompt (D9). |
| DL-21 | Plain JSON answers: strip one surrounding code fence, then strict parse | Strict only; lenient extraction of the first JSON value | Models often fence JSON; extraction of "first JSON value" could accept garbage (SO-007). |
| DL-22 | Version check through the same runner, once per client, cached on success only | Per request; at startup; none | Spec FR-022. Failures are retried on the next request so a late install works. The version run is not a "request process" (FR-003 now says so). |
| DL-23 | Every request runs in a new empty temp directory, deleted afterwards (DEC S1, asked) | Caller-chosen directory with `--mode plan`; caller-chosen directory with default permissions; a fixed shared scratch directory | agy writes files in its working directory by default and `--sandbox` did not stop it (F-08); `--mode plan` wrote plan files into `~/.gemini` and can end waiting for approval (F-08). A fresh directory confines the verified writes and leaves nothing behind. |
| DL-24 | Concurrency limit per client instance (`SemaphoreSlim`) | One static limit for the whole process; no limit; a limit per machine through a named mutex | The spec asks for a per-instance cap (FR-025) and DI registers one singleton client; a static limit would couple unrelated clients, and a cross-process limit is more machinery than v1 needs. Several instances can exceed the cap together; documented. |
| DL-25 | One short-lived process per request | One long-lived agy process fed through `--input-format stream-json` | The long-lived mode exists (F-02) and would save the start-up of agy and its MCP servers (F-39), but it keeps conversation state inside agy (history grows, files persist) and breaks statelessness, per-request directories and simple kill semantics (DEC X1). Listed as a future optimization. |
| DL-26 | `AdditionalArguments` checked against a deny-list (FR-029) | Allow-list of known-safe flags; no extra arguments at all | agy has many flags (F-02) and gains more; an allow-list would block legitimate ones such as `--log-file`, while no option at all would force a library release for each new flag. The deny-list covers every flag that breaks safety, statelessness or the library's own parsing. |
| DL-27 | Target `net8.0;net10.0`, depend only on abstraction packages plus one validator (DEC P3, P5) | Single target `net10.0`; also `netstandard2.0`; depend on `Microsoft.Extensions.AI` | net8.0 is the long-term-support runtime many consumers still run; netstandard2.0 would limit APIs used for process and path handling; the full `Microsoft.Extensions.AI` package is not needed to implement the interface and would force its version on consumers. |
| DL-28 | `FinishReason` is always `Stop` on success (DEC R2) | Infer `Length` from `thinking_tokens` or text length; map the safety-block error to `ContentFilter` | agy gives no reliable signal (F-06 shows a block as exit 0 with `ERROR`); inference would be a guess presented as fact. Deferred item O-10. |
| DL-29 | Keep preamble, nonce and flag as layered measures; do not use `--sandbox` or `--mode` | Add `--sandbox`; add `--mode plan` | `--sandbox` did not prevent file writes in the working directory and was not verified to add anything (F-08); `--mode plan` has the side effects in DL-23. Revisit when agy documents the sandbox semantics. |
| DL-30 | Phase 5 outcome for DL-12: `LateApexEarlySpeed.Json.Schema` 4.2.2 with every standard `format` made annotation-only; schema defects that only show when an answer reaches them raise `ArgumentException` from the check | `JsonSchema.Net` 8.0.5 (not run through the cases); a hand-written subset validator; asserting `format` (the validator's default) | Licence BSD-3 and no advisories; passes the schema cases in `docs/antigravity-chat-client/research.md`; asserting `format` would reject answers that typed deserialization accepts (`"2026-10-16"` for a `DateTime`). Cost: about 11 old Microsoft.Extensions.* packages in consumers' graphs. |

## 12. Risks, mitigations, open questions

### 12.1 Risks

| ID | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| R-1 | Usage terms: wrapping `agy` in a third-party application is "not supported under consumer plans" (F-23). | High / high for publication | README states the position; recommends API key or Enterprise; no credential handling; Q1 with the owner. |
| R-2 | agy changes output or flags | Medium / medium | Section 9: minimal parsing, one flag builder, fixtures, integration suite, minimum version. |
| R-3 | The user's MCP servers and tools are available to every request, with start-up cost (4 `npx` processes here) and possible side effects through their tools (F-39). | High on this machine / medium | Preamble; documentation; tree kill. No technical fix without editing agy settings (FR-028). Q-D1. |
| R-4 | Full prompts and answers are stored by agy on disk (F-41). | Certain / privacy | README; cannot be prevented (O-11). |
| R-5 | Prompt visible in the process list while running (argv only). | Certain / privacy on shared machines | README; Q3. |
| R-6 | `--effort` suffix rule is a heuristic and may mis-detect a future model name. | Low / low | Debug log of the decision; fixture and integration test; fallback is no effort. |
| R-7 | Preamble is a model instruction: one passing sample (F-38). | Medium / medium | Layered measures (7.1); five-repeat informational test; not claimed as a guarantee. |
| R-8 | agy on Windows may be a `.cmd` shim, or write non-UTF-8; unknown (F-32). | Unknown | `.exe` only with a clear error; Windows CI with FakeAgy; owner runs integration tests on Windows if used there. |
| R-9 | Temp directory may survive a failed deletion (locked file). | Low / low | Retries, warning with path, request directories use a recognizable prefix for later cleanup. |
| R-10 | A trivial prompt was once blocked by agy's content filter, giving exit 0 with `ERROR` (F-06). | Low-medium / medium | Treated as `AntigravityRunException`, not retried (cost). Message carries agy's text. |
| R-11 | Fixed ~17K-token overhead and 2–12 s latency per call, plus MCP start-up (F-10, F-39). | Certain / cost | Documented (FR-037); `MaxOutputTokens` ignored is documented. |
| R-12 | Sign-in detection signal unknown (F-45). | Certain / medium | Isolated in `AuthFailureDetector`; a failed sign-in still surfaces as `AntigravityProcessException` or `AntigravityRunException` with agy's text, never as success. |
| R-13 | Validator dependency has licence or advisory problems (F-43). | Medium / medium | DL-12 criteria in M5; interface keeps it swappable. |
| R-14 | Only the net10.0 runtime is installed locally (F-44). | Certain / low | net8.0 tests run in CI; locally optionally install the 8.0 runtime. |

### 12.2 Open questions that remain

| ID | Question | Owner | Blocks |
|---|---|---|---|
| Q1 | **Closed:** publish with the README warning (answered 2026-10-16). | — | — |
| Q5 | Real not-signed-in signal (F-45). | User runs it, or permits a run with a separate agy home | release |
| Q-D1 | **Closed:** warn only, opt-in empty home deferred as SPEC O-13 (answered 2026-10-16). Question was: should the README only warn about MCP servers (default), or should the library offer an opt-in option that runs agy against a separate, empty agy home (by environment) so none of the user's MCP servers load? An earlier test with `HOME` changed still used the real sign-in, so this is not known to work (RF §6 item 1). Same item as SPEC section 11. | User, then assistant tests | no |
| Q2, Q3, Q4, A-1, A-3 | As in SPEC section 11. | User | no |
| A-6 | `GetResponseAsync<T>` with primitive or array `T` (6, step 1 of the gate). | Assistant, milestone M5 | no |
| Q9, Q10 | Windows/macOS argument limits; agy 1.4.0. | Assistant, CI / after update | no |

### 12.3 Conflicts and gaps between the design and SPEC

Found in step 4. **Step 5 applied C-1 to C-8 and C-10 to SPEC** (and added F-34 to F-43 to its facts table); this table is the record of why. C-9 was applied to `docs/decisions.md` as an amendment note. Nothing here is still in conflict.

| ID | Where | Conflict or gap | Proposed fix |
|---|---|---|---|
| C-1 | FR-012, EC-20, 9.2 `MaxPromptLength` | SPEC counts characters; the operating-system limit is in bytes (UTF-8) per argument on Linux and UTF-16 units on Windows (F-11). 100,000 non-ASCII characters can exceed 128 KiB. | Add: "prompt UTF-8 size at most the platform ceiling"; same exception. Design applies both limits (5.7). |
| C-2 | NFR-006, NFR-008, Q4 | SPEC says the only host configuration reaching agy is the environment plus agy's settings and sign-in. True, but agy's settings include MCP servers, which agy starts on every run and whose tools the model can call (F-39). The side-effect surface is wider than "files". | Reword NFR-006 and NFR-008 to name MCP servers; extend Q4; add Q-D1. |
| C-3 | FR-029 | Lists flags the library sets itself as print, output format, input format, schema, print timeout. The design also sets `--model`, `--effort`, `--disable-slash-commands`. | Add them to the rejected list. |
| C-4 | FR-003 | "Exactly one agy process per request" is contradicted by the one-time `agy --version` run per client. | Add "(not counting the one-time version check)". |
| C-5 | FR-010, 9.2 | `DefaultReasoningEffort` has no type in SPEC; `--effort` fails for level-qualified model slugs (F-37). | Type it as `ReasoningEffort?` (the library's own use of the Microsoft type is fine); document the suffix rule (DL-11). |
| C-6 | NFR-007, NFR-009 | SPEC says agy "stores each conversation"; it stores full prompt and answer text (F-41). | Say so in NFR-007 and in the README item of FR-037. |
| C-7 | Q6, Q7, Q8, A-5 | Answered by runs in this step (F-34, F-35, F-42, F-38). | Close Q6–Q8; A-5 stays open until the five-repeat test. |
| C-8 | SO-004 | Not a conflict: F-35 shows the case really occurs (SUCCESS without the field). | Add F-35 to SPEC's facts table. |
| C-9 | DEC M1 | DEC says "labeled turns (`User:` / `Assistant:`)"; the design uses nonce headers (DL-05). | Recorded as an amendment in `docs/decisions.md`; the user can revert to plain labels. |
| C-10 | FR-034 | The stderr cap (64 KiB) is a design value not in SPEC. | Kept as a design detail (user-visible only through the 4 KB `StandardError` property, FR-033). |
| C-11 | FR-033 vs EC-10 vs 4.4 | FR-033 said 200 characters of stderr on a property; EC-10 and the design said 4 KB. | FR-033 now says up to 4 KB on `StandardError`. |
| C-12 | NFR-001 vs DL-16 | SPEC said no directory remains in any case; the design logs a failed deletion. | NFR-001 and FR-003 now state the exception. |
