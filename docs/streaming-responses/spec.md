# Spec: Real streaming responses

**In one sentence** — `GetStreamingResponseAsync` yields the answer's text as agy produces it, instead of one update after the run ends, for developers using `AntigravityChatClient` as an `IChatClient`.
**Status** — buildable
**Coverage** — 8/9 clear · 1 partial · 0 missing
**Confirmed** — 15/15 decisions · 0 assumed, unconfirmed
**Research** — `research.md`

## Outcome

A caller that streams sees the first text of an answer while agy is still working, not after the whole run ends. Joined together, the streamed text is the text of every answer step of the run. When agy ran no tools that is the same text `GetResponseAsync` returns; when it did, it also holds the text agy wrote between tool calls, so it can be longer. Failures still surface as the same typed exceptions.

## Who it is for, and what they do with it

- **Library user** (a .NET developer with an `IChatClient`): calls `GetStreamingResponseAsync`, reads updates with `await foreach`, shows or forwards text as it arrives. May stop early (break or cancel) and expects the agy process to end.
- **Library user, structured output**: calls with a JSON-schema response format and gets the one-update behavior of today.
- **Owner** (the maintainer): runs the paid capture that settles the unknown stream-json shape (see `How it works`).

## Scope

**In** — incremental `TextContent` updates for plain-text requests; a final update with usage, identifiers and finish reason; the existing error classification, timeout, output limit, concurrency slot and cleanup applied to the streaming run.
**Out** — streaming of schema (structured) requests; agy tool steps and thinking text as content; caller tools; changing `GetResponseAsync` or its output format; new public options; multi-turn `--input-format stream-json`; retry after a mid-stream failure.

## How it works

A streaming request for plain text runs agy with `--output-format stream-json` (`InvocationBuilder` stays the only owner of flags). The runner hands stdout lines to the client as they arrive instead of after exit. Each line is one JSON event. Text from `step_update` events becomes `ChatResponseUpdate` with `TextContent`. The final `result` event is classified by `ErrorClassifier` and supplies usage, finish reason and metadata for the last update.

Which text is streamed: the text of every `agent_response` step, in order, with nothing inserted between steps. Mid-run text agy writes around its own tool calls is therefore in the stream.

Event shape, from two captures on agy 1.4.0 (`capture/`, `research.md § Captured stream-json shape`): each line is `{"event": "init"|"step_update"|"result", <same key>: {...}}`. `text_delta` is incremental, and the `DONE` event of a step can carry the last delta. Steps with `step_type` `agent_response` and no `text_delta` exist and yield nothing. The `result` event carries `status`, `response`, `usage` (summed over steps), `num_turns`, `duration_seconds`, `conversation_id`; it has no model, which is in `init.init.model`, so `ModelId` comes from `init`. Not captured: the timeout notice and `denied_actions` in a stream.

A streaming request for a JSON-schema response format runs as today (`--output-format json`) and yields one update, because agy applies the schema to the final result only.

Update layout: text updates carry `TextContent` and `ResponseId` only; the last update carries `UsageContent`, `ModelId`, `CreatedAt`, `FinishReason`, `RawRepresentation`, `AdditionalProperties`.

## Domain and terms

| Term | Meaning |
|---|---|
| Streaming request | One call of `GetStreamingResponseAsync`. A kind of Request (existing term). |
| Stream event | One line of agy's `stream-json` output: `init`, `step_update` or `result`. |
| Text update | A `ChatResponseUpdate` whose content is `TextContent` from one stream event. |
| Final update | The last `ChatResponseUpdate`: usage, finish reason, metadata. |
| Result event | The last stream event; the stream counterpart of the Envelope. |

States of a streaming request: not started → running (events arriving) → finished (result event read, final update yielded) | failed (exception) | abandoned (caller stopped; process ended).

## Qualities and constraints

**Appetite** — one small phase.

- Library delay: a text update is yielded as soon as its line is read, with no buffering by us. Checked by a test (the update arrives before the fake agy exits), not by a time limit. agy's own start-up (2–9 s) is not ours to change.
- Memory stays bounded: the existing `MaxOutputBytes` still caps what is read. Streaming must not keep every event.
- No content in logs or exception messages (project rule); stream events are content.
- Library compiles against Abstractions 10.3.0; warnings are errors; public members documented.

## Depends on

- **agy `stream-json` output**: needed for text. If the event shape differs from what the parser expects, the request fails with the existing "unreadable output" exception type, not a hang; text already yielded stays yielded.
- **`AgyProcessRunner`**: needs a line-by-line reading mode next to the buffered one; remains the only code touching `Process`.
- **agy version**: stream-json seen on 1.3.5, current is 1.4.0; the version check already warns on unknown versions.

## Edge cases and failure modes [THIN: timeout notice and denied_actions not seen in a stream-json run]

| Case | Behavior |
|---|---|
| Failure before any text (bad model, not signed in, start-up failure) | Exception on the first `MoveNextAsync`, same type as non-streaming. |
| Failure after text was yielded (timeout notice, `ERROR` status, crash) | Exception on the next `MoveNextAsync`; earlier updates stand; no retry. |
| Caller cancels or breaks out of `await foreach` | agy process tree is ended, concurrency slot released, request directory removed. |
| Output over `MaxOutputBytes` | Same overflow exception as today, mid-stream. |
| Timeout | Process ended, timeout exception; text already yielded stands. |
| Run ends without a `result` event | Treated as a failed run (exit code decides the type). |
| Stdout line that is not valid JSON | Failure as above; line content never logged. |
| Run succeeds but produced no text | One final update, no `TextContent`. |
| Two streaming calls at once | Share `MaxConcurrency` with other requests; a slot is held until the enumeration ends or the `Timeout` runs out, so a caller that stops reading without disposing cannot hold it for longer than `Timeout`. |
| Caller never enumerates | Nothing starts (the request runs inside the iterator). |

## Success criteria

- A process test against `tools/FakeAgy` emitting events with delays shows the first text update before the fake exits (the latency criterion).
- Memory: a process test with a large stream stays within `MaxOutputBytes` and does not retain events.
- Logging: the existing logging-discipline test also covers the streaming path.
- For a fake run with one answer step, joining all text updates equals the text of `GetResponseAsync`; for a fake run with several answer steps, it equals all of them in order.
- Appetite: the change lands as one phase of the plan.
- Each row of the edge-case table has a unit or process test; guard tests checked once by breaking the guard.
- Breaking out of the enumeration leaves no agy process and no request directory (process test).
- A schema request still yields exactly one update, with the structured text.
- `dotnet build -warnaserror`, the unit suite and the process suite pass. The real-agy shape is confirmed by the owner's capture (`owner-run.md`).

## Deferred questions

- **Stream agy tool steps or thinking as content?** — would change: update kinds and what `ToChatResponse()` returns. Blocked by it: nothing.
- **Stream schema requests via the final result only?** — would change: nothing for v1; revisit if agy adds partial structured output. Blocked by it: nothing.

## Decisions

### 2026-10-16 (readback)

- `user` Q: close the timeout/denied-action stream gap with a third paid run, or leave it to the plan? → A: "Leave it to the plan, stop here (Recommended)" — ruled out: a third capture before planning; the plan's first task settles the timeout and `denied_actions` shape. confirmed 2026-10-16

### 2026-10-16 (capture)

- `sourced` **How it works** — `text_delta` is incremental; `result` has `status`, `response`, `usage`, no model; model is in `init` — `research.md § Captured stream-json shape`. Joining the deltas of both captures equals `result.response`. confirmed 2026-10-16
- `sourced` **How it works** — steps without `text_delta` yield nothing — `research.md § Captured stream-json shape`. confirmed 2026-10-16

### 2026-10-16

- `user` Q: which agent text is streamed when agy talks, runs tools, then talks again? → A: "All agent_response steps (Recommended)" — ruled out: streaming only the final step; streamed text always equalling `GetResponseAsync` text. confirmed 2026-10-16
- `user` Q: do the stream-json capture before planning? → A: "Yes, I run the capture first (Recommended)" — ruled out: planning on the documented shape alone. confirmed 2026-10-16
- `user` Q: appetite? → A: "One small phase (Recommended)" — ruled out: a second phase, tool steps as content. confirmed 2026-10-16
- `user` Q: library delay figure? → A: "Pass-through, no figure (Recommended)" — ruled out: a millisecond limit and a timing test. confirmed 2026-10-16
- `assumed` **How it works** — no separator is inserted between the text of consecutive answer steps; text is concatenated as emitted — ruled out: inserting a blank line or newline between steps. confirmed 2026-10-16

### 2026-10-16 (draft)

- `assumed` **How it works** — streaming requests use `--output-format stream-json`; non-streaming requests stay on `json` — ruled out: switching `GetResponseAsync` to stream-json, a streaming flag in options. confirmed 2026-10-16
- `assumed` **Scope** — JSON-schema streaming requests keep today's single-update behavior — ruled out: streaming schema text; per `agy --help` 1.4.0 the schema applies to the final result only (`research.md`). confirmed 2026-10-16
- `assumed` **Edge cases** — a failure after text was yielded is thrown on the next `MoveNextAsync`, earlier updates stand, no retry — ruled out: retracting text, silent truncation, retry. confirmed 2026-10-16
- `assumed` **How it works** — the `result` event goes through the existing `ErrorClassifier` rules — ruled out: a second classifier for streams. confirmed 2026-10-16
- `assumed` **Edge cases** — stopping the enumeration ends the agy process tree — ruled out: letting agy finish in the background. confirmed 2026-10-16
- `assumed` **Scope** — no new public options; `Timeout`, `MaxOutputBytes`, `MaxConcurrency` apply as they do now — ruled out: a separate streaming timeout or buffer option. confirmed 2026-10-16
- `assumed` **How it works** — only the last update carries usage and metadata; text updates carry `TextContent` and `ResponseId` — ruled out: usage on every update, cumulative text per update. confirmed 2026-10-16
