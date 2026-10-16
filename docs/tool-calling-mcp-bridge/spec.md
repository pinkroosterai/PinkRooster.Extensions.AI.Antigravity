# Spec: Tool calling through an in-process MCP bridge

**In one sentence** — Every request to `AntigravityChatClient` runs agy once in a throwaway agy home, and a request that carries caller `AIFunction` tools also gets a library-hosted MCP server exposing them, so agy calls the tools itself and the caller gets the final answer, for .NET developers who use the client as an `IChatClient`.
**Status** — buildable
**Coverage** — 9/9 clear · 0 partial · 0 missing
**Confirmed** — 21/21 decisions · 0 assumed, unconfirmed
**Research** — `research.md`

## Outcome

A developer who passes `AIFunction` tools in `ChatOptions.Tools` gets an answer in which agy used those tools, with one `GetResponseAsync` call and no extra code. The tools run in the developer's own process, and the response holds only the answer, never a tool call to run. Every request, with or without tools, runs without the developer's own agy rules, skills, settings and MCP servers, and nothing of the request stays on disk afterwards.

## Who it is for, and what they do with it

- **Library user** (a .NET developer with an `IChatClient`): registers tools, calls `GetResponseAsync` or `GetStreamingResponseAsync`, reads the final text. May wrap the client in `UseFunctionInvocation()` (a pipeline copied from OpenAI code) and expects nothing to break. May cancel.
- **Tool author** (same person, other hat): writes an `AIFunction` and expects it to run with the arguments the model chose, return a result, and see a cancelled token when the request is cancelled or times out.
- **Owner** (the maintainer): runs the paid checks that settle the unknowns marked in this spec, and decides the packaging.

## Scope

**In** — a per-request agy home for every request; the system text of every request in `AGENTS.md`; requests whose `ChatOptions.Tools` hold invocable `AIFunction`s and whose response format is text, served by a library-hosted MCP server per request that agy calls; the tool calls kept out of the response; cleanup.
**Out** — surfacing calls to the caller for the caller to run (no tool calls come back through `GetResponseAsync` or streaming); a record of the calls in the response; `ApprovalRequiredAIFunction`; tools that are declarations only; hosted tools; `ToolMode` `RequireAny` and `RequireSpecific`; tools together with a JSON response format (plain or schema); a stdio bridge helper; Windows and macOS verification; a reusable template home; caller-chosen limits on agy's own tools; session reuse. The second of the two phases takes no further item from this list; the owner chose none.

## How it works

Every request:

1. Validate the request; refuse the out-of-scope cases below before any process starts.
2. Create the request directory (as today) and, beside it, an agy home directory. Write the system text (system messages, `Instructions`, the `SystemPreamble`) into `AGENTS.md` in the request directory; the conversation turns stay in the prompt. The system text must fit agy's limit of 24,000 bytes per rule file.
3. Run agy as today, with the home's directory in `HOME` (a per-platform variable). Without tools the home is empty apart from what agy creates.
4. Classify and map the result as today; delete the home and the request directory.

A request with tools adds, in order, between steps 2 and 3 and around the run:

a. Write into the home `…/.gemini/config/mcp_config.json` (one server entry named `agy-tools`, pointing at this request's bridge) and `…/.gemini/antigravity-cli/settings.json` with the single rule `mcp(agy-tools/*)` under `permissions.allow`; add a short tool instruction to `AGENTS.md`.
b. Start a bridge for this request: an MCP server on 127.0.0.1, ephemeral port, a random per-request token, answering the surface described below for the request's tools only.
c. Run agy in the output mode the request kind already uses (json for `GetResponseAsync`, stream-json for streaming). agy connects to the bridge, reads the tool schemas, and calls tools; each call is run by the library on the caller's `AIFunction`. Tool steps in agy's stream are not mapped to content.
d. Stop the bridge before deleting the home.

Platforms where the home redirection is unverified (Windows, macOS) do not redirect: requests run in the user's real home exactly as before and requests with tools are refused with `NotSupportedException`. The client options get `IsolateAgyHome` (default true on Linux, false elsewhere); false has the same effect.

The bridge connection from agy (verified, `research.md § HTTP bridge run`): agy sends the configured `Authorization` header on every MCP request, tries `server/discover` first, falls back to `initialize` with protocol 2025-11-25, opens a standalone `GET` for an event stream, probes `/.well-known/oauth-protected-resource` without the header, and sends no `Origin`. So the bridge serves the 2025-11-25 form (`initialize`, `notifications/*`, `tools/list`, `tools/call`); answers `server/discover` with a 404 JSON-RPC "method not found"; answers the standalone `GET` with 405; answers the `.well-known` probes with 404; requires the bearer token on every MCP POST; and rejects a request that carries an `Origin` header it does not recognise.

Packaging: the bridge is a hand-written minimal MCP server inside the core package, behind an internal interface so the official SDK could replace it later. It adds no package and no ASP.NET Core framework reference (decision P5 stands). It serves only the surface listed above.

Permission and settings: the library writes agy configuration files only inside the home it created for that request and never reads, copies or changes the user's own agy files. This amends decision FR-028 (which says the library "does not read, copy or write agy credentials or settings"); the amendment is made in the specification and decisions documents when this feature is built.

Hidden calls: the response and the streamed updates carry no `FunctionCallContent` or `FunctionResultContent`; a surrounding `FunctionInvokingChatClient` never sees a call and so never runs a tool itself. The caller can still see which tools ran through the tool bodies they supplied. The library logs tool names and durations only (see Qualities).

Streaming: a streaming request with tools yields text updates as agy writes them and the final update (usage, ids, finish reason), as in the streaming spec; tool steps produce no update.

Limit on tool calls: the client options get a `MaxToolCalls` setting (default 20). The bridge refuses a `tools/call` beyond it with an MCP error result. `FunctionInvokingChatClient` cannot do this: calls run by the bridge never enter its loop (`research.md § FICC and a call cap`).

## Domain and terms

| Term | Meaning |
|---|---|
| Tool request | A request whose `ChatOptions.Tools` holds at least one invocable `AIFunction` and whose `ToolMode` is null or auto. Other requests are Requests as before (both kinds run in an agy home). |
| Bridge | The MCP server the library starts for one tool request. |
| Agy home | The per-request directory used as `HOME` for that request's agy process. |
| Bridge token | The random secret that identifies one tool request to its bridge. |
| Tool body | The `AIFunction` the caller supplied, as run by the bridge. |

States of a tool request: not started → home and bridge ready → agy running (tool calls arriving) → finished (result classified, home deleted) | failed (exception) | abandoned (caller stopped; agy ended; bridge and home removed).

## Qualities and constraints

**Appetite** — two phases.

- Security: the bridge listens on loopback only, refuses a request without the right token, validates the `Origin` header, and validates arguments against the function's JSON schema before running the tool body. A test shows a wrong token, a foreign `Origin` and malformed arguments are all rejected and the tool body does not run.
- Side effects: tools run whenever the model asks, with the arguments it chose after schema validation; the library has no approval step (approval-required tools are refused), so a tool with side effects is the caller's risk and the README says so.
- Content never in logs or exception messages: tool arguments and results count as content (project rule); tool names and durations may be logged.
- Cost of the per-request home: about 18 MB written per request, every request (measured once: 18 MB, 17 MB of it one binary agy unpacks) and removed afterwards; no home directory remains after any outcome. A reusable template home that avoids the repeated unpacking is out of scope.
- Tool-call cap per request: `MaxToolCalls`, default 20; the 21st call gets an MCP error result (test).
- Library overhead per tool request outside agy and the tool bodies (home, bridge start and stop): no figure was set and none is measured; it is checked only by the cleanup criterion below.
- Time: time spent inside a tool body counts against the request `Timeout` (default 5 minutes); a long tool needs a larger `Timeout`. No separate tool timeout.

## Depends on

- **agy 1.4.0** and its MCP client in print mode (verified for stdio and for HTTP with a bearer header, one run each): without it the tool request fails with the existing run or process exception. Behavior on other versions is unverified; the version check already warns above the newest tested version.
- **A way to hand agy a home** (`HOME` on Linux, verified; Windows and macOS unverified): where unverified, no redirection happens and tool requests are refused (see How it works).
- **agy's permission rule `mcp(agy-tools/*)` in the home's settings**: verified once on 1.4.0 (stdio); if a future agy ignores it, the call is denied and the request fails with the run exception (`denied_actions`).
- **No new package.** The bridge is the library's own code; the verified agy behaviour above is the interface it must meet, and the fake-agy tests pin it.
- **Sign-in** stays the user's, outside the library; it survived a `HOME` redirect on Linux, location unknown.
- **FR-028 amendment** (decided; to be written into the specification and decisions documents at build time).

## Edge cases and failure modes

| Case | Behavior |
|---|---|
| `Tools` contains an `ApprovalRequiredAIFunction`, a declaration-only tool or a hosted tool | `NotSupportedException` before any process starts. |
| `ToolMode` is `RequireAny` or `RequireSpecific` | `NotSupportedException`. `ToolMode.None`: the request runs as a plain request (no bridge; it still runs in an agy home). |
| Tools together with `ChatResponseFormat.Json` or a schema | `NotSupportedException`. |
| Two tools with the same name | `ArgumentException` before any process starts. |
| agy never connects to the bridge | The request ends at the timeout with the timeout exception; bridge and home are removed. |
| agy calls a tool that is not registered | The bridge returns an MCP error; the request goes on. |
| Arguments do not match the tool's schema | The bridge returns an MCP error result without running the tool body. |
| A tool body throws | The bridge returns an MCP error result with a generic message (no exception text); agy decides what to do; the exception is not rethrown to the caller. |
| A tool body runs longer than the remaining `Timeout` | The request times out as any request does: the process tree is ended, the tool body's token is cancelled, the timeout exception is thrown. |
| The caller cancels or the request times out while a tool body runs | The agy process tree is ended, the tool body's token is cancelled, the home and bridge are removed. |
| agy is denied the MCP permission | Existing classification: `denied_actions` with `mcp` and an empty answer throws the run exception. |
| Two tool requests at once | Each has its own home, bridge, port and token; they share `MaxConcurrency`. |
| agy makes more than `MaxToolCalls` calls | The extra calls get an MCP error result ("limit reached"); agy decides how to answer; the request is not failed by the library. |
| The bridge cannot bind a loopback port | The request fails before agy starts with the process exception; the home is removed. |
| Another process on the machine connects to the bridge | Rejected: wrong token, or foreign `Origin`. |
| The system text is longer than 24,000 bytes (agy truncates a longer rule file) | Refused with the existing prompt-too-large exception before any process starts; never silently truncated. |
| The user signs in to agy in a way that lives in agy's settings rather than the sign-in store (API key, Application Default Credentials) | Not tested; the request may fail as not signed in. The caller sets `IsolateAgyHome` to false to run as before (tool requests are then refused). |
| `IsolateAgyHome` is false, or the platform is Windows or macOS | No home, no `HOME` change, `AGENTS.md` not used: the system text stays in the prompt as before; requests with tools are refused. |
| Home cannot be deleted | Same handling as the request directory: retry, then a warning that names the path. |

## Success criteria

- A process test with a fake agy that acts as an MCP client over the same connection a real agy uses runs a tool body and returns its result in the response, with no `agy` involved.
- A unit or process test shows each row of the edge-case table.
- A test shows wrong token, foreign `Origin` and invalid arguments never reach the tool body (guard checked once by breaking it).
- A test with a real `FunctionInvokingChatClient` over the client shows the tool is run exactly once (by the bridge), the response and the streamed updates contain no `FunctionCallContent` or `FunctionResultContent`, and the wrapper never invokes the tool itself.
- A process test with a fake agy that echoes its `HOME` and working directory shows every request, with and without tools, runs with a `HOME` inside a fresh directory that is gone afterwards, and that the system text arrives in `AGENTS.md` and not in the command line.
- A test shows a system text over 24,000 bytes is refused before any process starts.
- After every outcome (success, each exception, cancellation) no agy process, request directory, agy home or open listener remains (process test).
- No log line or exception message contains a tool argument or result (logging test over the tool path).
- The owner's opt-in run shows a real agy calling a real tool through the bridge and shows the real agy honours the permission rule.
- `dotnet build -warnaserror`, the unit suite and the process suite pass.

## Deferred questions

- **A reusable template home** — would change: avoids writing ~18 MB (a binary agy unpacks) on every request now that every request has a home. Blocked by it: nothing.
- **API-key and Application Default Credentials sign-in with an isolated home** — would change: whether the library should copy a caller-supplied authentication setting into the home. Blocked by it: nothing (opt-out exists).
- **Tools together with a JSON response format** — would change: whether agy's schema finishing works with MCP tools. Blocked by it: nothing.
- **Approval-required tools** — would change: needs the caller to take part, which this design (agy runs the loop) cannot offer. Blocked by it: nothing.
- **`RequireAny`/`RequireSpecific`** — would change: instruction-only support that cannot be enforced. Blocked by it: nothing.
- **Windows and macOS** — would change: the home variable, sign-in location, `HOME` semantics. Blocked by it: nothing.

## Decisions

### 2026-10-16 (round 3)

- `assumed` **Qualities** — no approval step for tool calls; side-effecting tools run on the model's request and the README warns — ruled out: an approval flow in the library (impossible while agy runs the loop). confirmed 2026-10-16

- `user` Q: which Out items does the second phase take? → A: "To be clear -> per-request home is for ALL requests, tools provided to the chat client are made available through the mcp server and are invoked when the mcp tools are invoked by agy - NO tool calls through getresponseasync." Confirmed by the user ("Confirmed, resume") after my restatement — ruled out: the home only for tool requests (reverses the draft entry "the per-request home and `AGENTS.md` are used only for tool requests", which stays as history); surfacing calls to the caller. No other Out item moves in. confirmed 2026-10-16
- `user` Q: do tool calls appear in the response? → A: "Confirmed, resume" to a restatement offering hidden or an informational record; read as hidden because "NO tool calls through getresponseasync" and the earlier "hiding is acceptable" — ruled out: the informational record (reverses the draft entry "the response and streaming carry informational call/result pairs", which stays as history). To be confirmed in the readback. confirmed 2026-10-16
- `assumed` **How it works** — the system text of every request goes into `AGENTS.md` and out of the command line — ruled out: keeping system text in the prompt; evidence: `AGENTS.md` beat a conflicting `-p` instruction once (`research.md`, n=1). confirmed 2026-10-16
- `assumed` **How it works** — client option `IsolateAgyHome`, default true on Linux and false elsewhere; false (or an unverified platform) means no redirection and no tool requests — ruled out: redirecting on platforms where sign-in behaviour is untested. confirmed 2026-10-16

### 2026-10-16 (round 2)

- `user` Q: how is the MCP endpoint built and shipped? → A: "Hand-written minimal server inside the core package (Recommended)" — ruled out: the official SDK with ASP.NET Core in the core package, a separate opt-in package. confirmed 2026-10-16
- `user` Q: does tool-body time count against the request Timeout? → A: "Yes, one Timeout for everything (Recommended)" — ruled out: a separate per-tool timeout option. confirmed 2026-10-16
- `user` Q: a number for the library's overhead per tool request? → A: "No figure; a test checks only that everything is removed (Recommended)" — ruled out: a millisecond limit and a timing test. confirmed 2026-10-16

### 2026-10-16

- `user` Q: may FR-028 be amended to allow writing agy configuration inside a library-created temp home? → A: "Yes, amend: library-created temp home only (Recommended)" — ruled out: dropping the feature; editing the user's own agy files. confirmed 2026-10-16
- `user` Q: may I run one paid HTTP bridge test? → A: "Yes, run it now (Recommended)" — ruled out: assuming the token must go in the URL. Result: `sourced`, `research.md § HTTP bridge run` (bearer header works). confirmed 2026-10-16
- `user` Q: appetite? → A: "Two phases" — ruled out: one phase, none set. Opens: which Out items the second phase takes. confirmed 2026-10-16
- `user` Q: tool-call cap? → A: "I think this is a configuration that you can set in the chat client or the function calling chat client - research and verify if not possible go for option 1" — verified not possible in FICC (`research.md § FICC and a call cap`), so option 1 applies: `MaxToolCalls` on the client options, default 20 — ruled out: no cap, a default of 50, capping through FICC. confirmed 2026-10-16
- `sourced` **How it works** — agy's HTTP behaviour (header sent on every request, `server/discover` fallback, standalone GET, `.well-known` probes, no `Origin`) fixes what the bridge must serve — `research.md § HTTP bridge run`. confirmed 2026-10-16

### 2026-10-16 (draft)

- `assumed` **Scope** — the per-request home and `AGENTS.md` are used only for tool requests — ruled out: changing every request in this feature. *Reversed 2026-10-16 (round 3): the home is for all requests.* confirmed 2026-10-16
- `assumed` **How it works** — one bridge per request on its own port and token — ruled out: a shared listener per client with a request registry. confirmed 2026-10-16
- `assumed` **How it works** — the response and streaming carry informational call/result pairs — ruled out: hiding tool calls entirely. *Reversed 2026-10-16 (round 3): calls are hidden.* confirmed 2026-10-16
- `assumed` **Scope** — approval-required, declaration-only and hosted tools, `RequireAny`/`RequireSpecific` and tools with a JSON format are refused with `NotSupportedException` — ruled out: best-effort support that gives wrong answers silently. confirmed 2026-10-16
- `assumed` **Edge cases** — a throwing tool body becomes an MCP error with a generic message and is not rethrown — ruled out: failing the whole request on the first tool exception, putting exception text in the result. confirmed 2026-10-16
- `assumed` **How it works** — the permission is the single rule `mcp(agy-tools/*)` in the home's settings, with a fixed server name — ruled out: skip-permissions flag, editing the user's settings. confirmed 2026-10-16
- `assumed` **Qualities** — tool names and durations may be logged; arguments and results never — ruled out: logging tool content. confirmed 2026-10-16
- `assumed` **Scope** — Linux is the supported platform for tool requests; Windows and macOS are documented as unsupported until verified — ruled out: shipping untested home redirection. confirmed 2026-10-16
