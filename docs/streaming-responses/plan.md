# Plan: Real streaming responses

**Goal** — `GetStreamingResponseAsync` on a plain-text request yields agy's answer as several text updates followed by one final update with usage and metadata, joined text equals `GetResponseAsync` text, and `dotnet build -warnaserror`, the unit suite and the process suite pass.
**Status** — not started
**Research** — `research.md`

## Context

Spec: `spec.md` (buildable, 15/15 decisions confirmed). Today `GetStreamingResponseAsync` runs the normal request and yields one update; the runner returns only after agy exits and `InvocationBuilder` always passes `--output-format json`. agy 1.4.0's `stream-json` output is captured in `capture/` and described in `research.md § Captured stream-json shape`. The spec sets the appetite at one small phase, so this is one phase. Carried from the spec unchanged: schema requests keep the single update; tool steps as content stay deferred (spec Deferred questions). Plan default not in the spec: a `ChatResponseFormat.Json` request (no schema) also keeps the single-update path, because its whole text is validated after the run (`research.md § Planning, 2026-10-16`). Project rules that bite here: only `AgyProcessRunner` touches `Process`, only `InvocationBuilder` knows flags, `LogMessages` is the only logging and carries no content, warnings are errors, and no `agy -p` is run without the owner.

## Phase 1 — Streaming plain-text responses

**Status** — not started
**Rests on** — `capture/one-step.ndjson` and `capture/with-tool.ndjson` exist and match the shape in `research.md § Captured stream-json shape`; `IAgyProcessRunner` in `Internal/AgyProcessTypes.cs` still has the single buffered `RunAsync`; `ErrorClassifier.Classify` still takes an `AgyProcessResult`; `GetStreamingResponseAsync` in `AntigravityChatClient.cs` still wraps `GetResponseAsync`; the unit and process suites pass before starting.
**Settle first** —
- How agy's own timeout notice, a non-`SUCCESS` status and `denied_actions` appear in `stream-json` output (spec Edge cases THIN mark; answer in `research.md § Stream failure shapes`). Needs one paid agy run, so the owner starts it: agy with a short `--print-timeout` and a long task, stream-json output, in an empty scratch directory, output saved under `capture/`. Ask the owner for the command's go-ahead; if declined, build the failure handling from the documented envelope shape and record in the `Log` that it is unverified.
**Tasks**
- [ ] Add a line-by-line run mode to the process boundary that hands out stdout lines while agy runs and still reports timeout, output overflow, cancellation and exit code, with the same tree kill and bounded memory — interface `Internal/AgyProcessTypes.cs`, implementation `Internal/AgyProcessRunner.cs`, test double `tests/…Tests/Support/FakeProcessRunner.cs`.
- [ ] Let `InvocationBuilder` produce the stream-json form of an invocation for streaming requests, with the non-streaming form unchanged — `Internal/InvocationBuilder.cs`, flag facts in `docs/specification.md` F-02.
- [ ] Add a stream event reader that turns one stdout line into text, model (from `init`) or result, tolerating unknown events and steps without text, failing on a line that is not a JSON event — event shape in `research.md § Captured stream-json shape`, fixtures from `capture/`; follow `Internal/EnvelopeParser.cs` for defensive reading.
- [ ] Make the streaming call yield text updates as lines arrive, then one final update with usage, model, finish reason and metadata; withhold a trailing line terminator so joined text equals `GetResponseAsync` text — spec § How it works and § Edge cases, `ResponseMapper.PlainText`, `ResponseMapper.Map`.
- [ ] Send the result event through the existing classification so failures, timeouts, overflow, denied actions and a missing result event raise the same exception types, before-text on the first `MoveNextAsync` and after-text on the next — `Internal/ErrorClassifier.cs`; failure rows in spec § Edge cases.
- [ ] Keep the concurrency slot, request directory, version check and log entries around the streaming run as in the buffered path, and end agy and release everything when the caller stops enumerating — `AntigravityChatClient.cs`, `LogMessages.cs`; no prompt, answer or stream line in any log or exception message.
- [ ] Keep `Json` and `JsonSchema` response formats on the single-update path, unchanged — spec § How it works, Context above.
- [ ] Teach FakeAgy to emit stdout lines with delays between them, so a test can see an update arrive before the process exits — `tools/FakeAgy/Program.cs` (env-variable style, header comment lists them).
- [ ] Write the tests with the code: unit tests extending `StreamingTests` for text updates, final update, joined-equals-direct, the trailing terminator, steps without text, each failure row, schema and plain-JSON single update, nothing-runs-until-enumerated; process tests for first-update-before-exit, a large stream within `MaxOutputBytes`, break-out leaves no process and no request directory — suites `tests/…Tests`, `tests/…ProcessTests`; break each guard once and see its test fail, per CLAUDE.md.
- [ ] Extend the logging-discipline test to the streaming path — `tests/…Tests/LoggingDisciplineTests.cs`.
- [ ] Add an opt-in real-agy streaming test and tell the owner how to run it and what it should show; do not run it — `tests/…IntegrationTests/RealAgyTests.cs`, `docs/antigravity-chat-client/owner-run.md` for the format.
- [ ] Bring the documents in line: `README.md` (streaming sections), `docs/specification.md` (O-2, FR-019, EC-34, facts), `docs/software-design.md` (§4.3, DL-18), `docs/deferred.md` row 2, the one-update sentence in `CLAUDE.md`, and the spec's Decisions with the plain-JSON default.
**Done when** — `dotnet build PinkRooster.Extensions.AI.Antigravity.sln -warnaserror`, `dotnet test tests/PinkRooster.Extensions.AI.Antigravity.Tests` and `dotnet test tests/PinkRooster.Extensions.AI.Antigravity.ProcessTests` exit 0; a process test shows the first text update before FakeAgy exits; the mutation checks are in the Log; `grep -rn "System.Diagnostics.Process" src` finds it only in `AgyProcessRunner.cs`; no document under `README.md` or `docs/` still says streaming yields one update, except for schema and plain-JSON requests; the opt-in streaming test exists and is waiting for the owner's run.

## Log

<Empty at hand-over. `/execute-plan` appends: date, phase, what changed and why.>
