# Research findings (step 1)

Date: 2026-10-16. Machine: Linux 7.0.0-38 x86_64, bash. agy 1.3.5 at `~/.local/bin/agy`.

Labels: **verified** = run on this machine or read in official docs; **reported** = secondary source only; **unknown** = not established, with how to check.
Scratch dir for all runs: `/tmp/agy-scratch` (outside any project). All runs used `--model gemini-3.8-flash-low`, stdin `</dev/null` unless noted, stdout and stderr redirected to files (so non-TTY). Run outputs are in that directory (`A1.out` …).

## 1. Things that change the design

| # | Finding | Label |
|---|---|---|
| 1 | **A timeout is not an error.** `--print-timeout 1s` returned exit 0, `status":"SUCCESS"`, empty `response`, zero usage. The only signal is a stderr line: `[agy] print timeout after 1s with turn in progress; returning partial output`. | verified (T-B2, T-B3) |
| 2 | **`status:"ERROR"` can come with exit 0.** A schema run returned exit 0, `status":"ERROR"`, an `error` text (content-safety block, retries), yet `structured_output` was filled. The library must read `status`/`error`, not just the exit code. | verified (T-A3) |
| 3 | **Denied permissions are not an error either.** Exit 0, empty stdout in text mode; in json mode `response:""` plus `denied_actions:[{"action":"command","display_name":"RunCommand"}]`; stderr says `jetski: no output produced — a tool required the "command" permission …`. | verified (T-first-run, T-C4) |
| 4 | **agy has side effects by default, even headless.** Asked to create a file, it wrote `hello.txt` into the working directory (exit 0) while a command was denied. `--sandbox` did not stop file writes in cwd (`sb.txt` created). `--mode plan` did not write the file but wrote a plan document to `~/.gemini/antigravity-cli/brain/<conversation-id>/` — outside cwd. | verified (T-C4, T-E1, T-E2) |
| 5 | **Every call stores a conversation** under `~/.gemini/antigravity-cli/brain/<conversation_id>/` (191 directories there now, including mine). No flag found to disable it. | verified (`ls`); no-persist flag: unknown — not in `--help` |
| 6 | **Large fixed overhead per call.** A one-word reply cost 16,878 input tokens (agent system prompt and ~60 tools listed in the `init` event). Latency 2–9 s for trivial prompts on flash-low. | verified (T-A1, T-E3) |
| 7 | **The prompt can only go in argv.** `-p ""` with the prompt on stdin → exit 1, `empty prompt`. `-p -` sends the literal text `-`, not stdin. `--input-format text` with stdin did not help. Linux limit hit at 150 KB single argument: `Argument list too long` (exit 126, from the shell, before agy ran). Windows limit (~32 K chars) not tested. | verified (T-B5, T-C1, T-D1, T-C3); Windows: unknown |
| 8 | **Slash commands run in print mode.** `-p "/help"` printed the command list instead of calling the model. `--disable-slash-commands` exists in `--help`; I did not test it. A user message starting with `/` is therefore an injection path. | verified (T-D3); flag effect: unknown |
| 9 | A prompt that starts with `--` is treated as the prompt value (`-p "--help Reply…"` worked). | verified (T-D2) |
| 10 | **SIGTERM mid-run**: agy exited 1 with `{"status":"ERROR","error":"context canceled",…}` and the process had 3 child processes after 2 s. After the kill no agy children of mine were left. Process-tree kill on Linux therefore needs a check on a run that has started a shell tool. | verified (T-D4); tree behavior with tool processes: unknown |
| 11 | **Structured output goes through a "finish" tool.** With `--json-schema`, `response` contains the JSON *plus two extra fields* (`toolAction`, `toolSummary`); `structured_output` holds the clean object. Use `structured_output`, not `response`. | verified (T-A3, T-B1) |
| 12 | **Schema is advisory, not checked by agy.** `--json-schema` compiles the schema string (`invalid --json-schema: schema is not valid JSON`, exit 1, no model call) but I could not make the model violate it, so what agy does with a non-matching answer is unknown. Docs say nothing either. | verified (invalid JSON); mismatch behavior: unknown — needs a deliberately hard schema |
| 13 | **Primitive type names as schema (`string`, `number`…) are rejected by 1.3.5** (`--json-schema string` → exit 1) although the official page lists them. | verified (T-B4) vs official doc (disagreement, §5) |
| 14 | **Unknown flag values are not always rejected.** `--output-format bogus` was accepted and ran a real model turn (exit 0). The library must only pass known values. | verified (T-first-run) |

## 2. agy CLI facts

| Fact | Label | Source |
|---|---|---|
| Version 1.3.5. `agy changelog` lists 1.4.0 as newer, so an update exists. | verified | `agy --version`, `agy changelog` (2026-10-16) |
| Flags (from `agy --help`): `-p/--print/--prompt`, `--output-format text\|json\|stream-json`, `--input-format text\|stream-json` (stream-json requires `--output-format stream-json`), `--json-schema <string\|path>`, `--model`, `--effort low\|medium\|high\|xhigh\|max`, `--print-timeout`, `--sandbox`, `--mode accept-edits\|auto\|plan`, `--dangerously-skip-permissions`, `--disable-slash-commands`, `--add-dir`, `--agent`, `-c/--continue`, `--conversation <id>`, `--project`, `--new-project`, `--log-file`. | verified | `agy --help` |
| `--print-timeout` default is `0s` = wait until turn ends. | verified | `agy --help` (official doc says 5m, see §5) |
| `-p` takes a value; `agy -p` alone exits 2. Unknown flag exits 2. Bad `--model` exits 1 and lists valid names. | verified | T-runs |
| `agy models` works without a prompt and prints `slug<TAB>name`: gemini-3.8-flash-high/medium/low, claude-sonnet-4-6, claude-opus-4-6-thinking, gpt-oss-120b-medium. | verified | `agy models` |
| JSON envelope fields seen: `conversation_id`, `status`, `response`, `error` (only on failure), `duration_seconds`, `num_turns`, `usage{input_tokens,output_tokens,thinking_tokens,cache_read_tokens,total_tokens}`, `structured_output` and `json_schema` (only with schema), `denied_actions` (only when something was denied; **not in the docs**). | verified | T-A1, T-A3, T-C4; docs list the rest |
| Status values per docs: `SUCCESS`, `ERROR`, `CANCELED`, `INTERRUPTED`, `INVALID`, `WAITING`, `RUNNING`. Seen locally: `SUCCESS`, `ERROR`. | verified (docs) / partly seen | headless doc |
| stdout carries only the response; diagnostics go to stderr. Confirmed with redirected stdout and `</dev/null`: no hang, no TTY needed on 1.3.5. | verified | T-A1/A2 |
| `stream-json` emits NDJSON: `init` (cwd, tools, permission_mode `request-review`), `step_update` (step_index, state ACTIVE/DONE, step_type, `text_delta`, …), final `result`. | verified (init and step_update seen; `result` per docs) | T-E3, headless doc |
| Default headless permissions: file read/write in workspace auto-allowed; shell commands soft-denied unless allowed in `permissions.allow` in `~/.gemini/antigravity-cli/settings.json`. | reported→partly verified (matches T-C4: file written, command denied); settings path not opened by me | headless doc |
| Exit codes: 0 on success **and** on timeout/soft-deny/model-side `ERROR`; 1 on bad flag value, bad schema, bad model, empty prompt, cancel; 2 on missing flag argument/unknown flag. | verified | T-runs |
| Gemini CLI shut down 2026-06-18; agy is its successor. | reported (CLAUDE.md brief; I did not source it) | — |
| Auth: token file `~/.gemini/antigravity-cli/antigravity-oauth-token` exists on this machine (name only; I did not read it). Docs list `auth.type`: `oauth-personal`, `oauth-business`, `gemini-api-key`, `agent-platform`. Changelog mentions `AGY_ADC_AUTH` (Application Default Credentials). Keyring use: not seen here. | verified (file exists, docs, changelog) | `ls`, getting-started page, `agy changelog` |
| **Not signed in behavior**: not tested. A run with `HOME=/tmp/agy-home` still worked (agy kept using the real credentials), so that did not simulate sign-out. Docs: a non-interactive unauthenticated run "exits with an `authentication required` error instead of hanging". | unknown locally; reported by official doc | headless doc |
| Early 1.0.x hangs without a TTY | reported; 1.3.5 does not hang | — |
| 1.3.5 changelog items relevant to us: Auto mode (`--mode auto`); stalled model connection now cancelled after 5 min and retried (1.3.6); headless `-p` runs skip subagent questions (1.3.6). 1.3.4: `--model` without `--effort` picks a default level. | verified | `agy changelog` |
| Minimum agy version for `--output-format` and `--json-schema` in print mode: 1.1.8. | reported (search summary; not opened) | web search 2026-10-16 |
| Windows install path `…\AppData\Local\agy\bin`; Linux `~/.local/bin/agy`; Linux needs glibc ≥ 2.28. Behavior on Windows/macOS (process tree, encoding, quoting) not tested. | verified (docs) / unknown (behavior) | getting-started page |

## 3. Microsoft.Extensions.AI facts

Sources: Microsoft Learn API pages, package `Microsoft.Extensions.AI.Abstractions` **v10.9.0** (view net-11.0-pp), read 2026-10-16. Raw source files could not be fetched (GitHub raw URL gave 404).

| Fact | Label |
|---|---|
| `IChatClient : IDisposable` has `GetResponseAsync(IEnumerable<ChatMessage>, ChatOptions?, CancellationToken)`, `GetStreamingResponseAsync(...)` → `IAsyncEnumerable<ChatResponseUpdate>`, `GetService(Type, object? key)`, `Dispose()`. | verified |
| All members must be thread-safe for concurrent use; implementations may mutate the passed `ChatOptions`. Applications must handle prompt injection and data-size risks unless the implementation documents safeguards. | verified |
| `ChatOptions` properties (v10.9.0): AdditionalProperties, AllowBackgroundResponses, AllowMultipleToolCalls, ContinuationToken, ConversationId, FrequencyPenalty, **Instructions**, MaxOutputTokens, ModelId, PresencePenalty, RawRepresentationFactory, **Reasoning**, ResponseFormat, Seed, StopSequences, Temperature, ToolMode, Tools, TopK, TopP; `Clone()`, copy constructor. (Instructions, Reasoning, ContinuationToken, AllowBackgroundResponses are newer than most blog posts.) | verified |
| `ChatResponseFormat`: `Text`, `Json`, `ForJsonSchema(JsonElement, name?, description?)`, `ForJsonSchema<T>(JsonSerializerOptions?, …)`, `ForJsonSchema(Type, …)`. Docs warn that many services need a top-level `type=object`; primitives/arrays should be wrapped. | verified |
| `GetResponseAsync<T>` extensions live in `Microsoft.Extensions.AI` (not Abstractions), take `useJsonSchemaResponseFormat` (default true) and set `ResponseFormat` with a schema derived from `T`. How it wraps primitives and how it reports bad JSON (exception vs `TryGetResult`): I could not read the source. | verified (signature, default) / unknown (wrapping and failure behavior — check `ChatClientStructuredOutputExtensions.cs` in dotnet/extensions or decompile the package) |
| `UsageDetails`: InputTokenCount, OutputTokenCount, TotalTokenCount, CachedInputTokenCount, ReasoningTokenCount, AdditionalCounts, plus text/audio splits, `Add()`. Maps cleanly from agy's `usage`. | verified |
| `ChatClientMetadata(providerName?, providerUri?, defaultModelId?)`. Leaf-client `GetService` convention (return metadata when the type matches and key is null): not read this session. | verified (members) / unknown (convention) |
| Patterns: `DelegatingChatClient`, `ChatClientBuilder` with `Use…` extension methods, DI via `AddChatClient`. A new `RoutingChatClient` also derives from `IChatClient` in 10.9.0. | verified |

## 4. Existing implementations that wrap a CLI process

Two web searches (2026-10-16) found **no** published `IChatClient` that wraps a CLI subprocess (Claude Code, Gemini CLI, Codex). Closest: a hand-written `IChatClient` adapter over the GitHub Copilot SDK (blog, not code I read), and github/copilot-sdk issue #99 asking for one. Nothing to learn about process lifetime, cancellation or test fakes from them, so those decisions are ours. I did not run a third search; more would not change a design decision.
Implication for tests: wrap the process launch behind our own small interface and test with a fake script/fake process; no precedent to follow.

## 5. Disagreements between sources

| Topic | Official headless doc | agy 1.3.5 here |
|---|---|---|
| `--print-timeout` default | 5m | `0s` (wait until turn ends) in `--help` |
| `--effort` values | low, medium, high | low, medium, high, xhigh, max |
| `--json-schema` primitive names | accepted (`string`, `number`, …) | rejected as invalid JSON |
| Stdin prompts | "`--input-format text` … reads prompts from stdin" | `-p ""` + stdin → `empty prompt` error |
| Timeout outcome | not stated | exit 0, SUCCESS, empty response, stderr notice |
| Schema failure | "not described" | see open item 3 |
| `denied_actions` field | absent | present |

Rule for the design: where the doc and the binary disagree, trust the binary and pin a minimum version.

## 6. Open items (need your call or a further run)

1. **Not-signed-in behavior and error text** — needs a sign-out or a clean credentials location; I did not touch your sign-in. Check: run with the token file moved aside, or ask me to find how agy picks the home directory.
2. **Does `--disable-slash-commands` stop `/help`?** Free to test; run next if you agree.
3. **What agy does when the answer violates the schema** (retry, `ERROR`, or pass-through). Needs a schema the model cannot satisfy; a few cents of tokens.
4. **How to stop agy writing files and conversation state** (`--mode plan` blocks file creation but writes plan files to `~/.gemini`). Candidates to test: an empty scratch working directory per call, `permissions.deny` rules, `--sandbox` with a stricter settings file. Decision for the interview.
5. **Process-tree kill** with a running shell tool, and Windows/macOS behavior: not tested (Linux only here).
6. **Upgrade to 1.4.0** and re-run the tests? Changelog shows no print-mode change, but I did not diff.
7. `GetResponseAsync<T>` wrapping and failure behavior (§3).
8. The 150 KB prompt limit applies to a single argv string on Linux (128 KB); Windows limit unverified.

## 7. Sources

- Official: Antigravity headless mode, https://antigravity.google/docs/cli/headless/ (no page date shown; read 2026-10-16)
- Official: Antigravity getting started (CLI tab), https://antigravity.google/docs/getting-started?tab=cli (read 2026-10-16)
- Local: `agy --version`, `agy --help`, `agy models`, `agy agents`, `agy changelog`, `agy help models`, `agy mcp` (2026-10-16)
- Microsoft Learn, IChatClient, ChatOptions, UsageDetails, ChatResponseFormat.ForJsonSchema, ChatClientStructuredOutputExtensions.GetResponseAsync, ChatClientMetadata (via the Learn MCP tools and a raw-source fetch of ChatResponseFormat.cs and ChatClientMetadata.cs), v10.9.0, read 2026-10-16
- Microsoft Learn, Use the IChatClient interface, https://learn.microsoft.com/dotnet/ai/ichatclient (2026-10-16)
- Web search (secondary), 2026-10-16: print-mode summary incl. "added in v1.1.8" (computingforgeeks, NousResearch/hermes-agent PR #101086 — summaries only, pages not opened); IChatClient CLI-wrapper searches (see §4).

## 8. Side effects of this research (state left behind)

- `/tmp/agy-scratch/` holds my test output plus `sb.txt`; `/tmp/agy-home/` holds a `.gemini` stub; `/tmp/src-m/` is empty. Remove with `rm -rf /tmp/agy-scratch /tmp/agy-home /tmp/src-m`.
- ~20 agy conversations from my runs are now in `~/.gemini/antigravity-cli/brain/` (including one `--mode plan` document). agy offers no delete command I found; they are harmless but they are there.
- One test (`--output-format bogus`, T-first-run) started a real model turn I did not intend. Total spend: about 15 small flash-low calls (~17–22 K input tokens each).
- No settings, sign-in or project files were changed.
