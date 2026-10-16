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
