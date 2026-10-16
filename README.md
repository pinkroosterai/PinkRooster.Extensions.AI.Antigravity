# PinkRooster.Extensions.AI.Antigravity

An [`IChatClient`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) for the Antigravity CLI (`agy`). Each request runs `agy` once, in non-interactive print mode, in a new empty directory, and the answer comes back as a `ChatResponse`. Plain text and JSON-schema answers (including `GetResponseAsync<T>`) are supported.

Status: version 0.1.0, not published yet. Tested against **agy 1.3.5 on Linux** (see [Compatibility](#compatibility)).

## Read this first: usage terms

This library is a wrapper around the `agy` command line. Google's Antigravity terms and FAQ (read 2026-10-16; neither page shows a date) say:

- Additional Terms §6: "Using third party software, tools, or services to access the Service (e.g. using OpenClaw with Antigravity OAuth) is a breach of this Agreement", which "may be grounds for suspension or termination" of your account.
- FAQ: headless mode (`agy -p`) "is intended for direct scripting and CI workflows"; wrapping `agy` inside third-party coding agents or applications "is not supported under consumer plans". For third-party agents the FAQ recommends a Gemini Enterprise or Google AI Studio API key.

What that means for you is for you and your plan to decide; the library does not check which plan you use. **You are responsible for complying with the terms of your plan.** For anything beyond personal scripting, use an API key or Enterprise sign-in as Google recommends. The library never handles credentials: it does not sign in, read token files or change agy's settings. You sign in with `agy` yourself.

## What you need

- .NET 8 or .NET 10.
- The Antigravity CLI **1.3.5 or newer**, installed and signed in once (start `agy` interactively). The library checks the version once per client; an older version raises `AntigravityVersionException`.

```
dotnet add package PinkRooster.Extensions.AI.Antigravity
```

(Once published.) The library depends on `Microsoft.Extensions.AI.Abstractions` 10.3.0 or newer. Use `Microsoft.Extensions.AI` yourself for `GetResponseAsync<T>` and pipelines.

## Quick start

```csharp
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity;

using IChatClient client = new AntigravityChatClient(new AntigravityChatClientOptions
{
    DefaultModelId = "gemini-3.8-flash-low", // see `agy models`
});

ChatResponse reply = await client.GetResponseAsync("Say pong.");
Console.WriteLine(reply.Text);
```

A typed answer:

```csharp
public record Weather(string City, double TemperatureCelsius);

ChatResponse<Weather> typed = await client.GetResponseAsync<Weather>("Weather in Paris, made up is fine.");
Console.WriteLine(typed.Result.City);
```

With dependency injection:

```csharp
services.Configure<AntigravityChatClientOptions>(configuration.GetSection("Antigravity")); // optional
services.AddAntigravityChatClient(o => o.DefaultModelId = "gemini-3.8-flash-low");

IChatClient client = provider.GetRequiredService<IChatClient>();
using IChatClient withMiddleware = client.AsBuilder().UseOpenTelemetry().Build();
```

## Read this before you use it: what agy is

`agy` is an agent. It can read and write files, run commands, call the MCP servers you configured, and browse. A chat client must not cause side effects you did not intend, so the library does what it can, and this section says what it cannot do.

| Measure | Effect |
|---|---|
| New empty working directory per request, deleted afterwards (created owner-only where the platform allows) | A file agy writes in its working directory ends up there and disappears with it. Verified: agy writes files in its working directory by default and `--sandbox` did not stop that. |
| Never auto-approves permissions; never adds directories, resumes conversations or selects a mode | agy keeps its headless defaults: shell commands are denied unless your agy settings allow them. |
| The prompt starts with an instruction not to use tools or write files (`SystemPreamble`) | Lowers tool use. It is an instruction to a model, not enforcement; it worked in the one case tried. |
| Slash commands are switched off and the prompt never starts with `/` | Text in a message cannot run an agy command or skill. |

**What it cannot prevent:**

- **Your MCP servers run on every request** (unless `IsolateAgyHome` applies, see "Isolated agy home"). agy starts every MCP server enabled in your agy settings (four `npx` processes on the test machine) and the model can call their tools. That adds start-up time and side effects beyond files. The library does not read or change agy's settings; use `agy mcp list` and `agy mcp disable` yourself if you want fewer.
- **Your agy settings can widen permissions** (unless `IsolateAgyHome` applies). Allow-rules for commands or file writes in agy's `settings.json` apply to requests made through this library.
- **agy stores a transcript of every request**, including the full prompt and answer text, under `~/.gemini/antigravity-cli/brain/`. The library cannot switch that off.
- **The prompt is visible in the process list** while agy runs, because agy takes the prompt only as a command-line argument.
- **Writes outside the request directory are not blocked.**

## Isolated agy home

On Linux every request runs agy with its own throwaway home directory (`HOME`), created next to the request directory and deleted with it (`IsolateAgyHome`, on by default). The system text (`SystemPreamble`, system messages, `Instructions`) travels in an `AGENTS.md` file in the request directory instead of the prompt, so it also stays off the command line; a system text over 24,000 bytes is refused, because agy truncates a longer rule file.

What changes for you:

- agy does not see your own agy rules (`GEMINI.md`/`AGENTS.md` in your home), skills, plugins, settings or MCP servers. Before this, a global rule file could add text to every answer (a suffix rule was seen in a test) and cost about 5,000 input tokens (indicative).
- Nothing of a request stays in agy's history; the home is deleted afterwards.
- Cost: about 18 MB is written per request (agy unpacks a binary into a fresh home); run time was unchanged in the one test.
- Your sign-in is not stored in the home (verified once on Linux: an empty `HOME` was still signed in). If you sign in with an API key or Application Default Credentials through agy settings, that is untested: set `IsolateAgyHome` to `false`.
- On Windows and macOS isolation is not applied; the option has no effect there until it is verified.

## Tools

Pass `AIFunction` tools in `ChatOptions.Tools` and agy can call them while it works on the request:

```csharp
var options = new ChatOptions { Tools = [AIFunctionFactory.Create((string city) => GetForecast(city), "forecast")] };
ChatResponse response = await client.GetResponseAsync("Will it rain in Utrecht?", options);
```

How it works: for the request the library opens a private endpoint on `127.0.0.1` (random port, random token, valid only for that request), tells agy about it through the request's own agy home, and runs your function in your process whenever agy calls it. The answer comes back as text; **no tool call is returned to you**, so a surrounding `FunctionInvokingChatClient` never runs a tool itself and `FunctionCallContent` never appears in a response or in streamed updates. Tool calls run for as long as `Timeout` allows; a tool that takes minutes needs a larger `Timeout`.

What to know before you use it:

- **Requires `IsolateAgyHome`** (Linux): tool requests are refused where the home is not isolated.
- **No approval step.** agy decides when to call a tool, with arguments the model chose (checked against the tool's JSON schema first). A tool with side effects runs whenever the model asks for it. Treat tool results as untrusted input to the model.
- **`MaxToolCalls`** (default 20) limits the calls per request; further calls get an error result and agy decides what to do. `FunctionInvokingChatClient` cannot do this for you, because agy runs the calls itself.
- A tool that throws gives agy a generic error ("The tool failed.") and is not rethrown to you. Tool arguments and results are never logged; tool names and durations are, at debug level.
- agy may call several tools at the same time; your functions must be safe to run concurrently.
- Cancelling the request, or reaching `Timeout`, ends agy and cancels the token your function received.
- Not supported yet: tools together with a JSON response format, approval-required tools, `RequireAny`/`RequireSpecific`.

## Cost and speed

- Every request costs at least about 17,000 input tokens of agy's own overhead (a one-word answer used 16,878 on agy 1.3.5). `Usage` reports what agy reports, overhead included.
- A trivial request took 2 to 12 seconds with agy 1.3.5. The library's process handling adds about 1 ms over starting the process by hand (measured on Linux with a fake agy; the client's own steps are negligible next to the seconds agy needs).
- One agy model call per request. No retries, no warm-up, and the version check does not call a model.
- `ChatOptions.MaxOutputTokens` is not honored, so it cannot cap cost.

## Options (`AntigravityChatClientOptions`)

Validated and copied when the client is created; later changes to the instance have no effect.

| Option | Default | Meaning |
|---|---|---|
| `ExecutablePath` | null | Path of agy. Null: the `PATH`, then `~/.local/bin` (Windows: `%LOCALAPPDATA%\agy\bin`). Batch files (`.cmd`, `.bat`) are refused. |
| `DefaultModelId` | null | Model used when the request names none. Null leaves it to agy. |
| `DefaultReasoningEffort` | null | Used when the request names none; see below. |
| `Timeout` | 5 minutes | Longest a request may run (at least 1 second). Waiting for a free slot does not count. |
| `MaxConcurrency` | 4 | Most agy processes one client runs at once. |
| `MaxPromptLength` | 100,000 (Windows 30,000) | Longest prompt in characters. A platform limit in bytes also applies; the smaller wins. Over it: `AntigravityPromptTooLargeException`, nothing runs. |
| `SkipVersionCheck` | false | Skips `agy --version`. |
| `EnvironmentVariables` | empty | Added to the host environment for agy; a null value removes a variable. |
| `TemporaryDirectoryRoot` | system temp | Where request directories are created. |
| `AdditionalArguments` | empty | Extra agy arguments. Flags that the library sets or that break safety (`-p`, `--output-format`, `--input-format`, `--json-schema`, `--print-timeout`, `--model`, `--effort`, `--disable-slash-commands`, `--dangerously-skip-permissions`, `--add-dir`, `-c`/`--continue`, `--conversation`, `--mode`, `--project`, `--new-project`, `-i`/`--prompt-interactive`, `--agent`) raise `ArgumentException` when the client is created. |
| `SystemPreamble` | built-in text | Placed at the start of every prompt. Must not be empty. |
| `MaxOutputBytes` | 16 MiB | Most standard output read; more raises `AntigravityOutputException`. |
| `MaxToolCalls` | 20 | Most tool calls agy may make in one request; further calls get an error result. At least 1. See "Tools". |
| `IsolateAgyHome` | true on Linux, false elsewhere | Every request runs agy with its own throwaway home (`HOME`) and sends the system text in an `AGENTS.md` file. agy then sees none of your own agy rules, skills, settings or MCP servers, and keeps no history of the request. Writes about 18 MB per request. Applied on Linux only; `true` elsewhere has no effect. While true on Linux, `EnvironmentVariables` may not set `HOME`. See "Isolated agy home". |

## What each `ChatOptions` member does

Honored: `ModelId`, `ResponseFormat`, `Instructions`, `Reasoning`, and `Tools` with `ToolMode` null, auto or none (see "Tools"). Rejected: the unsupported tool cases below. Everything else is ignored, and a debug log entry names (never shows) the ones you set.

| Member | Behavior |
|---|---|
| `ModelId` | Passed to agy as the model; falls back to `DefaultModelId`, then agy's default. An unknown model raises `AntigravityProcessException`. |
| `ResponseFormat` | `Text`: plain. `Json`: the answer must be one JSON value. `ForJsonSchema`: structured output, see below. |
| `Instructions` | Added to the system text of the prompt, after any system messages. |
| `Reasoning` | `Effort` maps `Low`→low, `Medium`→medium, `High`→high, `ExtraHigh`→xhigh; `None` passes nothing; `Output` is ignored. agy rejects an effort together with a model name that already ends in `-low`, `-medium`, `-high`, `-xhigh` or `-max` (such as `gemini-3.8-flash-low`), so in that case the effort is **not passed** (debug log). Use a base name such as `gemini-3.8-flash` with an effort. |
| `Tools`, `ToolMode` | `AIFunction` tools are offered to agy through a private endpoint (see "Tools"). Refused with `NotSupportedException` before anything runs: approval-required, declaration-only and hosted tools, `ToolMode` RequireAny and RequireSpecific, tools together with a JSON response format, and any tools while `IsolateAgyHome` does not apply; two tools with the same name raise `ArgumentException`. `ToolMode.None` runs a plain request. Silently dropping a tool would give wrong answers. |
| `Temperature`, `TopP`, `TopK`, `Seed`, `FrequencyPenalty`, `PresencePenalty` | Ignored: agy has no such settings. |
| `StopSequences`, `MaxOutputTokens` | Ignored: do not rely on them for limits or cost. |
| `ConversationId`, `ContinuationToken`, `AllowBackgroundResponses` | Ignored: the client is stateless and sends the whole conversation every time. |
| `AllowMultipleToolCalls`, `RawRepresentationFactory`, `AdditionalProperties` | Ignored. |

Messages: system, user and assistant messages become one prompt (a preamble, then the system text, then the turns in order, each under a header with a random per-request code so that text in a message cannot pose as a turn). Text is passed unchanged. Reasoning content in history is dropped. Images (`DataContent` of type png, jpeg, gif or webp in user messages, at most 10 of 10 MiB each) are written as files in the request directory and the prompt points agy at them; agy 1.4.0 was seen reading a jpeg this way (png, gif and webp are unchecked). URIs, other files and tool calls or results raise `NotSupportedException`. A prompt reaches agy as one command-line argument, without a shell.

## Structured output

With `ChatResponseFormat.ForJsonSchema` (and `GetResponseAsync<T>`), the schema is written to a file in the request directory and agy returns a structured result; the library returns that result, **not** agy's free-text answer (which carries extra fields), as compact JSON, after checking it against your schema.

- The schema must be an object schema (`"type": "object"`). `GetResponseAsync<T>` wraps primitives, arrays and enums in an object with a `data` property itself, and unwraps them.
- Draft 2020-12 applies unless the schema declares draft 7 or 2019-09.
- If agy cannot satisfy the schema it still reports success but returns no structured result; the library raises `AntigravityInvalidOutputException` (the raw text is on `RawOutput`, not in the message). The same happens when the result does not match your schema. There is no retry.
- **Not asserted:** `format` is treated as an annotation only (dates and GUIDs are not checked); `contentEncoding` and `contentMediaType` are not decoded or checked; remote `$ref` documents are never fetched; keywords the validator does not know are ignored. Defects in a schema usually raise `ArgumentException` before agy runs; a bad regular expression or an unresolvable `$ref` raises it only when an answer reaches it, after one run.
- `ChatResponseFormat.Json` (no schema): the answer, with one surrounding Markdown code fence removed, must be exactly one JSON value, else `AntigravityInvalidOutputException`.

## Results

`ChatResponse` has one assistant message; `ResponseId` is agy's conversation id; `ModelId` is the model you chose (null when none); `FinishReason` is always `Stop` on success; `Usage` maps agy's token counts (input, output, total, cached input, reasoning). `RawRepresentation` is agy's JSON (`JsonElement`), and `AdditionalProperties` has `agy.num_turns`, `agy.duration_seconds` and, when agy denied something, `agy.denied_actions`.

`GetStreamingResponseAsync` for a plain-text request runs agy with `stream-json` output and yields the answer text as agy writes it: one update per piece of text, then a final update with usage, `ResponseId`, `ModelId`, `FinishReason` and `AdditionalProperties` and no text. The text of every answer step is streamed, so when agy writes text between its own tool calls the joined text can be longer than `GetResponseAsync`'s. A request with a JSON response format (`ChatResponseFormat.Json` or a schema) yields the whole checked answer as **one** update, because it can only be checked when complete. Errors surface on a `MoveNextAsync` call: the first one when nothing was yielded yet, a later one when agy fails after text was sent (the text already yielded stands; nothing is retried). Stopping the enumeration early ends the agy process. A timeout is only visible once the run ends, as for any request; the stream carries no event for it.

`GetService` returns `ChatClientMetadata` (provider `antigravity`, your default model) for `typeof(ChatClientMetadata)`, and the client itself for `typeof(AntigravityChatClient)`.

## Errors

| Exception | When |
|---|---|
| `AntigravityNotFoundException` | agy was not found; lists where it looked. |
| `AntigravityVersionException` | agy is older than 1.3.5, or its version could not be read. |
| `AntigravityNotAuthenticatedException` | agy says it is not signed in. Detection uses the text the official documentation gives ("authentication required"); the real wording has not been captured, so another wording shows as one of the two exceptions below. |
| `AntigravityTimeoutException` | The request exceeded `Timeout`, or agy reported its own print timeout. agy reports its timeout as a success with an empty answer; the library does not. |
| `AntigravityProcessException` | agy ended with a non-zero exit code (bad model, bad flag, killed from outside), or could not be started. |
| `AntigravityRunException` | agy exited with 0 but did not succeed: status other than `SUCCESS` (a content-safety block, for instance, also when a structured result is present), or no answer because agy denied an action. |
| `AntigravityOutputException` | agy's output was not the expected JSON, or exceeded `MaxOutputBytes`. |
| `AntigravityInvalidOutputException` | The structured result is missing, or does not match the schema or JSON format. |
| `AntigravityPromptTooLargeException` | The prompt is over a size limit. |
| `ArgumentException`, `ArgumentNullException`, `NotSupportedException`, `ObjectDisposedException`, `OperationCanceledException` | Bad input or options, unsupported content or options, use after `Dispose`, cancellation. |

Messages never contain the prompt, the answer, the schema or environment values. `StandardError` (up to 4 KB) and `ExitCode` are on the exception, and `RawOutput` on `AntigravityInvalidOutputException`; treat them as sensitive.

Cancelling a request ends the whole agy process tree and deletes the request directory. A request waiting for a free slot can be cancelled without starting anything.

## Logging

Pass an `ILogger<AntigravityChatClient>` (or register one for dependency injection). Entries contain only model names, durations, token counts, prompt length, versions, option names, exit codes and exception type names. Prompts, answers, schemas, standard error, environment values and credentials are never logged. The library does not log at error level; exceptions carry the information.

## Compatibility

| agy | Platform | Status |
|---|---|---|
| 1.3.5 | Linux (Ubuntu) | agy's behavior was verified by hand on this version, and the library is tested against a fake agy. The library itself has not yet been run against the real CLI; that run is planned before release. |
| 1.4.0 and newer | any | Accepted; one information log entry. Not tested. |
| older than 1.3.5 | any | Rejected. |
| any | Windows, macOS | Run in CI with a fake agy; **unverified** with the real CLI (process-tree kill, argument limits, UTF-8 output). |

Where agy's documentation and agy 1.3.5 disagree, the library follows what agy does: for example agy's default print timeout is unlimited, not five minutes, a timeout exits with 0, and a prompt cannot be read from standard input. The library only passes flags that were verified on 1.3.5 and ignores envelope fields it does not know.

## Not supported in 0.1.0

Caller tool calling, streaming of tool steps and JSON-format answers, audio, video, files and image URIs, embeddings, conversation reuse, choosing the working directory, retrying invalid structured output, and agy's own agent, mode and permission settings. `docs/deferred.md` says how each could be added.

## Development

`dotnet build -warnaserror`, then `dotnet test` on the unit and the process test projects, need no agy and no network. A suite against a real agy spends tokens, is not part of CI and is run by the maintainer before a release. The requirements are in `docs/specification.md` and the design in `docs/software-design.md`.

## Licence

MIT. See `LICENSE`.
