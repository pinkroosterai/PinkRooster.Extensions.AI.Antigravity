# Research: streaming responses

Checked 2026-10-16. Feeds `spec.md`. No web search: the domain is this repo's own earlier research.

## What agy offers for streaming

`agy --help` on 1.4.0 lists `--output-format text|json|stream-json`; `--input-format stream-json` needs `--output-format stream-json` and is a separate (multi-turn) mode. `--json-schema` says "for stream-json, only applicable to the final result". So a schema request cannot stream its structured JSON.

Source: `agy --help`, agy 1.4.0, run locally 2026-10-16 (free command).

## What is known about the stream-json events

Seen once on 1.3.5 (run T-E3): an `init` event (cwd, tools, permission_mode), `step_update` events (step_index, state ACTIVE/DONE, step_type, `text_delta`), and a final `result` event. The `result` event is known from the headless doc only, not seen. No capture of stream-json output is stored in this repo (searched `tests/`, `docs/`). Whether `text_delta` is incremental or cumulative, which `step_type` values exist, and whether the `result` event carries `status`, `usage`, `response`, `denied_actions` and the timeout notice are all **unknown**.

Source: `docs/research-findings.md` (facts table, T-E3), `docs/deferred.md` row 2; repo search 2026-10-16. A capture needs a paid `agy -p` run, which the owner starts.

## What the current code does

`InvocationBuilder` always passes `--output-format json`. `AgyProcessRunner.RunAsync` buffers stdout and returns only after exit. `GetStreamingResponseAsync` calls `GetResponseAsync` and yields one update (FR-019, DL-18). Limits already in place: `Timeout` (5 min), `MaxOutputBytes` (16 MiB), `MaxConcurrency`.

Source: `src/PinkRooster.Extensions.AI.Antigravity/AntigravityChatClient.cs:156`, `Internal/InvocationBuilder.cs:35`, `AntigravityChatClientOptions.cs`.

## Not checked

The `ChatResponseUpdate` conventions in Abstractions 10.3.0 (one update per delta, usage in a trailing `UsageContent` update, `ToChatResponse()` merging). From memory only; the plan must read the package.

## Captured stream-json shape

Two runs on agy 1.4.0, model gemini-3.8-flash-low, new empty directory, exit 0, empty stderr, files in `capture/` (`one-step.ndjson`, 5 lines; `with-tool.ndjson`, 18 lines). Run 2026-10-16 at the owner's request.

- Line format: `{"event": "<kind>", "<kind>": {...}}` with kinds `init`, `step_update`, `result`. `init` holds `model`, `cwd`, `tools`.
- `step_update` has `step_index`, `state` (`ACTIVE`/`DONE`), `step_type` (`user_input`, `agent_response`, `tool`), optional `text_delta`, and on `DONE` `duration_seconds` and `usage`. `tool` steps carry `tool_name`, `tool_info`.
- `text_delta` is incremental. The `DONE` event of a step may carry the final piece (a newline, or the last sentence).
- In the tool run, `agent_response` steps 1 and 3 (before each tool call) had **no** text; only step 5 (after the tools) had text. So the narration-between-tools case did not occur in this run; whether it ever does is unknown.
- `result` has `conversation_id`, `status` (`SUCCESS`), `response`, `duration_seconds`, `num_turns`, `usage` (summed over steps). `result.response` equalled the joined deltas in both runs.
- Not seen: `denied_actions`, `error`, the timeout notice, any non-SUCCESS status, `structured_output`.
