# Research: tool calling through an MCP bridge

Checked 2026-10-16 (agy 1.4.0, Linux). Feeds `spec.md`. Harvested from `docs/tool-calling-options.md` and its critique (both deleted after this file was written), plus the experiments of 2026-10-16 named below. Labels: **verified** (run here or read in official documentation), **reported** (secondary source), **unknown**.

## agy behaviour (local runs and agy docs)

- **MCP config locations.** Global `~/.gemini/config/mcp_config.json` (`mcpServers`: `command`/`args`/`env`/`cwd` for stdio; `serverUrl`, `headers` for remote; `disabled`, `disabledTools`), workspace `.agents/mcp_config.json`. `agy mcp add/remove/list/enable/disable` edit the global file; there is no per-session flag. Remote transports: Streamable HTTP, SSE, WebSocket. Verified (help, local file, [MCP docs](https://antigravity.google/docs/mcp?tab=cli), no page date).
- **A workspace config in a new, untrusted directory is loaded in print mode.** agy started the server, sent `server/discover`, then `initialize` (protocol 2025-11-25), `notifications/initialized`, `tools/list`. Verified (stdio stub, 2 runs).
- **Tools are not exposed by name.** agy has one generic tool `call_mcp_tool(ServerName, ToolName, Arguments)`; the model first reads the tool's schema from a cache file (`<home>/.gemini/antigravity-cli/mcp/<server>/<tool>.json`) with `view_file`, then calls. Verified (stdio stub).
- **Without a permission, print mode denies the MCP call.** Exit 0, `status` `SUCCESS`, empty `response`, `denied_actions: [{"action":"mcp","display_name":"CallMcpTool"}]`, stderr text naming the missing `mcp` permission; the server never received `tools/call`. Verified (real home, no flags).
- **With `--dangerously-skip-permissions`** (scratch dir only) the call succeeded; the tool output appears as `tool_info.output` of the `call_mcp_tool` step in stream-json. Verified.
- **Per-request agy home works.** `HOME=<dir>` moves agy's whole config tree: `HOME=<dir> agy mcp list` showed no servers; `agy mcp add` wrote `<dir>/.gemini/config/mcp_config.json` and left the real file untouched; `GEMINI_HOME` had no effect. Sign-in survived the redirect (a call under an empty `HOME` was answered; no token file was created in the new home, so sign-in is not stored under `HOME`; where it lives is unknown). Verified on Linux; Windows (`USERPROFILE`?) and macOS unknown.
- **Allow rule in the temp home is honoured.** With `HOME` redirected, `mcp_config.json` in the temp home and `{"permissions":{"allow":["mcp(spike/*)"]}}` in `<home>/.gemini/antigravity-cli/settings.json`, the MCP call succeeded in print mode with no skip-permissions flag, no `denied_actions`, no stderr. 1 run, stdio server. Verified. Tokens that run: 39,925 input, 10.7 s, no cache reads.
- **`AGENTS.md` in the working directory acts as a system message.** Applied with no file-read step (+~120 input tokens: 11,957 vs 11,835). A rule "answer in French, end with PINEAPPLE-7" beat a conflicting `-p` instruction ("English only, no extra lines") once. n=1. Docs: always-on rules are "injected into the system prompt on every turn"; a single rule file is truncated above 24,000 bytes; active global and always-on rules share a 20,000-token budget; workspace files `AGENTS.md`, `GEMINI.md`, `.agents/AGENTS.md`, `.agents/rules/*.md` ([rules](https://antigravity.google/docs/rules/), no page date). Verified (run) / verified (docs).
- **The user's global rules leak into library answers.** `~/.gemini/config/GEMINI.md` is a symlink to this user's `CLAUDE.md` (6,605 bytes); earlier outputs ended with "State … Next step …", the rule written there. Under an empty `HOME` a plain question got a clean answer and 11,835 input tokens against ~16,900 under the real home (different prompts; indicative). Verified (leak seen in outputs), indicative (token difference).
- **A fresh home costs ~18 MB per request**, 17 MB of it an unpacked `webm_encoder` binary under `<home>/.gemini/antigravity-cli/bin`; run time unchanged (~2 s). Verified.
- **Reads outside the workspace.** In run 1 (no flags) agy read its own cache file outside the working directory with `view_file`. Whether it can read arbitrary paths in default print mode is unknown.
- **Permission rules.** `mcp(server/tool)`, `mcp(server/*)`, `mcp(*)`; precedence Deny > Ask > Allow; MCP tools default to Ask; rules in the global `settings.json` ([permissions](https://antigravity.google/docs/permissions/)). Issue [#548](https://github.com/google-antigravity/antigravity-cli/issues/548) (opened 2026-07-06, last comment 2026-10-09, no maintainer fix) reports `permissions.allow` ignored on some versions and platforms, one `mcp(<server>/*)` success on 1.1.27 on Windows, conflicting reports on 1.2.11, and project grants in `~/.gemini/config/projects/<project-id>.json`. Reported.
- **stream-json.** Output has `tool` steps with `tool_name` and `tool_info{name, parameters, output, error}`; input accepts only `user` text events (no tool-result event). Timeout shows only as a stderr notice with exit 0 and `SUCCESS`. Verified (docs and earlier captures).
- **Not tested.** HTTP `serverUrl` with `headers` in print mode (every MCP run used stdio); cancellation reaching a running MCP tool when agy is killed or times out; agy's own wait limit on a pending `tools/call`; `--json-schema` combined with MCP tools; Windows and macOS.

## Microsoft.Extensions.AI (v10.9.0 docs; the library compiles against the 10.3.0 floor)

- `FunctionInvokingChatClient` (FICC) invokes the `AIFunction` in `Tools` for each `FunctionCallContent` in the inner response and loops; continuation depends on the presence of invocable calls, not on `FinishReason`. Defaults: 40 iterations, 3 consecutive errors. At the last iteration it strips function declarations and calls the inner client again, passing any call the model still makes back to the caller. Verified ([reference](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient); [source at b10f9c0](https://github.com/dotnet/extensions/blob/b10f9c0a081b5dbb7755b8f5592e1d3c3f550a3a/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs), summarising fetches, last ~2,000 characters unread).
- A `FunctionCallContent` is marked `InformationalOnly` and not invoked when the same response contains a `FunctionResultContent` with the same `CallId`; in streaming FICC buffers once it sees a call so it can detect such pairs. `InformationalOnly`: "already processed … should be ignored by components that process function calls". Verified (same sources; [property](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.functioncallcontent.informationalonly)).
- `ApprovalRequiredAIFunction` calls become `ToolApprovalRequestContent`; the caller answers with `ToolApprovalResponseContent`. A call whose tool is only an `AIFunctionDeclaration` passes back to the caller. Verified.
- `ChatFinishReason.ToolCalls` exists at 10.9.0. Whether `InformationalOnly`, the approval types and `ToolCalls` exist at the 10.3.0 floor: unknown (check by compiling).
- `HostedMcpServerTool` is a service-side tool; agy cannot receive it from the caller. Verified.

## MCP

- Current revision 2026-07-28: stateless requests, per-request capability metadata, no protocol-level sessions, no GET stream. Streamable HTTP: servers MUST validate `Origin`, SHOULD bind to 127.0.0.1, SHOULD authenticate; closing the response stream is cancellation; stdio uses `notifications/cancelled`. Verified ([spec](https://modelcontextprotocol.io/specification/latest), [Streamable HTTP](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http)).
- Official C# SDK: ASP.NET Core hosting (`AddMcpServer().WithHttpTransport(...)`, stateless default), tool handlers get a `CancellationToken` that fires on client cancellation; `McpClientTool` is an `AIFunction`. A direct `AIFunction` → server tool path was not seen in the pages read. Verified (docs via Context7) / unknown (the direct path).

## Reviewer concerns carried into the spec

- A's HTTP + bearer design was untested (stdio only). Security of C was overstated. The permission blocker was solved by the per-request home (above), which the first options document had missed.
- Writing a settings file in a temp home needs an explicit amendment of FR-028 ("does not read, copy or write agy credentials or settings").
- ASP.NET Core hosting in a class library implies a framework reference; decision P5 of the library allows only Abstractions, logging abstractions and one schema validator.

## Ruled out

- **Surface-and-resume (park a `tools/call`, return the call to the caller):** needs a kept-alive agy process between `IChatClient` calls; reverses the one-process-per-request decision; resume fails when history reaches another client instance or after a restart; agy's wait limit unknown.
- **Schema-constrained loop without MCP (one agy run per turn):** best contract fit, but untested schema adherence, ≥17,000 input tokens per turn, no streamed text with tools. Not chosen for this spec by the owner's request; remains the fallback if the bridge fails.
- **`--dangerously-skip-permissions`:** forbidden by decisions L4/S2; also approves file writes and commands.
- **Editing the user's own `settings.json` or `mcp_config.json`:** forbidden by FR-028.

## Not checked

Windows/macOS home redirection (`USERPROFILE`, keychain sign-in); concurrent requests each with its own home; whether agy honours `headers` on `serverUrl`; the cost of unpacking `webm_encoder` per request at scale.

## HTTP bridge run (2026-10-16, agy 1.4.0, Linux)

One paid run: `HOME` redirected to an empty directory; `mcp_config.json` there with `{"serverUrl":"http://127.0.0.1:<port>/mcp","headers":{"Authorization":"Bearer <token>"}}`; `settings.json` there with `mcp(spike/*)` allowed; a stand-in HTTP MCP server on 127.0.0.1 in Python. Result `SUCCESS`, the tool output reached the model (`ZEBRA-4417 for Ada`), 39,468 input tokens, 9.4 s. Verified (n=1).

What agy's HTTP client (Go) did, in order, from the stand-in's log:
1. `POST /mcp` `server/discover` with `Mcp-Protocol-Version: 2026-07-28`, `Mcp-Method`, `Accept: application/json, text/event-stream`, `Authorization: Bearer <token>`. The stand-in answered 404 with a JSON-RPC "method not found" error; agy then fell back.
2. `POST /mcp` `initialize` (protocol 2025-11-25, no `Mcp-Protocol-Version` header yet), then `GET /mcp` with `Accept: text/event-stream` (answered 405; agy went on), then `notifications/initialized`.
3. Two `GET /.well-known/oauth-protected-resource[/mcp]` probes **without** the `Authorization` header (agy probing for OAuth); the stand-in's default 404 was accepted.
4. `tools/list`, `notifications/roots/list_changed`, `tools/call` (all with `Authorization` and `Mcp-Protocol-Version: 2025-11-25`).
- No `Origin` header was sent (a non-browser client). The `Authorization` header was sent on every MCP POST and GET.

Consequences: a bridge needs to serve the 2025-11-25 form (`initialize` + notifications + `tools/list` + `tools/call`) and to answer `server/discover` with a 404 JSON-RPC error, a standalone GET with 405 and the `.well-known` probes with 404 without requiring the bearer token on them. A bearer header in the config works; the token does not have to be in the URL.

## FICC and a call cap (2026-10-16)

`FunctionInvokingChatClient.MaximumIterationsPerRequest` (default 40) counts rounds of FICC's own loop; calls marked `InformationalOnly` never enter that loop (`CopyFunctionCalls` skips them) and do not advance its counters. Tool calls the bridge runs for agy are therefore invisible to FICC, so FICC cannot cap them. The cap has to live in the client's own options. Source: FICC source at b10f9c0, read 2026-10-16 (summarising fetch).

## Planning, 2026-10-16

Read from the repo (no external search; every external unknown was already in this file or is carried into a `Settle first`).

- **Today's validator rejects tools.** `RequestValidator.Validate` throws `NotSupportedException` for non-empty `Tools` or any `ToolMode` other than null or `None`, and for any content other than text (`Internal/RequestValidator.cs`). Phase 2 replaces the tool refusal; the content refusal stays (function content never appears in a request this design sees, because calls are never returned to the caller).
- **System text is part of the prompt.** `PromptBuilder.Build` writes the preamble, a nonce explanation, one `[system|nonce]` turn per system text, then the user and assistant turns (`Internal/PromptBuilder.cs`). Moving system text to `AGENTS.md` changes this builder, its tests and the turn-forgery reasoning of design DL-05, which was about system versus user turns sharing one prompt. The guard against a prompt starting with `/` or `-` (FR-013) must keep working with a prompt that now starts with the first conversation turn.
- **The request directory is owner-only and has one owner class** (`Internal/RequestDirectory.cs`: creation under `TemporaryDirectoryRoot`, deletion with retries and a warning naming the path). The home needs the same creation and deletion rules.
- **Environment reaches the process through `AgyProcessRequest.EnvironmentOverrides`**, applied after the host environment (`Internal/AgyProcessRunner.cs`). The caller's `AntigravityChatClientOptions.EnvironmentVariables` flow through the same dictionary, so a caller who sets `HOME` collides with the library's redirect: precedence must be decided.
- **No new package is allowed** (spec, Depends on). The library already references `LateApexEarlySpeed.Json.Schema` (arguments can be validated with it), `Microsoft.Extensions.AI.Abstractions`, logging, options and DI abstractions (`PinkRooster.Extensions.AI.Antigravity.csproj`).
- **Test seams.** Unit tests use `FakeProcessRunner` (scripted results; cannot act as an MCP client) and reference `Microsoft.Extensions.AI`, so a real `FunctionInvokingChatClient` can be put over the client in a unit test. Process tests run `tools/FakeAgy`, which can only print and exit today; phase 2 needs it to act as an MCP client over HTTP.
- **Streaming spec interplay.** `StreamEventReader` already treats tool steps as ignorable events (`Other`), so hidden tool calls need no reader change; a test should pin that.
- **Documents that say "tools rejected" or "FR-028 never writes settings":** `docs/specification.md` (O-1, FR-008, FR-028, NFR-005/006/008, Q4, Q-D1 and O-13), `docs/decisions.md` (A1, S3, M1, M3, L3, Q-D1 follow-ups), `docs/software-design.md` (C-components, DL-05, DL-25, R-3), `docs/deferred.md` (rows 1 and the home), `README.md`, `CLAUDE.md` (the "Caller tools throw NotSupportedException" sentence and the Single-owners list), `docs/antigravity-chat-client/owner-run.md`.

Unknowns carried into phases as `Settle first`: whether `AIFunction.JsonSchema`, the approval types and `ChatFinishReason` members used exist at the 10.3.0 floor (phase 2); whether a loopback listener can be opened without elevated rights through the BCL types the executor chooses (phase 2); how a caller-set `HOME` in `EnvironmentVariables` is treated (phase 1).

## Home precedence (2026-10-16, phase 1)

Decision: the library's redirect wins, and a caller who wants to control the home variable must turn isolation off. `OptionsValidator` rejects a `HOME` entry in `EnvironmentVariables` when `IsolateAgyHome` is true and the platform is Linux (the only platform where isolation is applied); no silent override and no silent drop. The redirect variable is `HOME` on Linux; on other platforms isolation is not applied (spec), so no variable is chosen for them. `IsolateAgyHome = true` on an unverified platform is accepted and has no effect, with one debug log entry at client creation. Why: a silent override would hide the caller's setting; a validator error names the fix.

## AGENTS.md size (2026-10-16, phase 1)

agy truncates a single rule file above 24,000 bytes ([rules docs](https://antigravity.google/docs/rules/), no page date, checked 2026-10-16; the page measures after `@[label](path)` includes expand, which the library never writes). The library measures the UTF-8 byte count of the whole `AGENTS.md` content it writes (preamble, system texts, and in phase 2 the tool instruction) against 24,000 and refuses with `AntigravityPromptTooLargeException` before any process starts. Bytes, not characters, because the docs say bytes. The `MaxPromptLength` character limit keeps applying to the prompt only.
