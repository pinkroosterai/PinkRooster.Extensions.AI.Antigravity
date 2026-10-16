# Can the library capture and parse agy's log file?

Date: 2026-10-16. agy 1.4.0. Evidence: `agy --help`, one free `agy models` run in a scratch `HOME` with `--log-file`, and reading the existing logs in `~/.gemini/antigravity-cli/log/` (163 files, 5.4 MB). No model call was made.

## Answer

Capturing: yes, cheaply. Parsing for useful information: only a little, and not reliably. The log is a Go server log, not a run report.

## Verified

| # | Fact | Source |
|---|---|---|
| 1 | `--log-file <path>` ("Override CLI log file path") exists in `agy --help` and is honored: a run with `HOME` in a scratch directory wrote the log to the given path. | `agy --help`; scratch run |
| 2 | Default logs go to `~/.gemini/antigravity-cli/log/cli-<timestamp>.log`, with `cli.log` as a symlink to the latest. With the library's isolated home they would be created (and deleted) inside that home. | directory listing |
| 3 | Format is glog: `I1016 17:40:36.447005  50 server.go:1687] message`. Level letter, date, time, thread, source file and line, message. Lines are not structured. | log files |
| 4 | One run produces about 160 lines. Most are start-up and background noise (language server ports, config loading, auth cache, remote-control, slash-command reload). | log files |
| 5 | Messages that carry run information: `latency_breakdown.go` (a `key:value` map with `cascade_id`, `trajectory_id`, `status`, `total_latency_ms`, `media_count`); `http_helpers.go` (the model endpoint URL, a trace ID and a `ResponseID` per call); `model_config_manager.go` (the selected model label). | log files |
| 6 | The "not signed in" text appears in the log (`You are not logged into Antigravity`). It is the only auth wording seen in a real file, and it comes from a run that never reached the model. | log files |
| 7 | Logs can contain prompt and schema text: lines from other sessions show a JSON schema and its descriptions in plain text. | log files |

## Not found

No token counts, finish reasons, safety-block markers, retry or rate-limit lines, tool-call lines or denied-action lines were found by keyword search. Only logs from earlier runs were available, so a real run's tool and error lines are unknown. Checking it needs one paid run with `--log-file`, which is the owner's to start.

## What it could add over the JSON envelope

| Possible gain | Realistic? |
|---|---|
| Per-call model `ResponseID`s and the per-call count (how many model calls a request made) | Yes, from `http_helpers` lines |
| Latency split (`total_latency_ms`) | Only the send part; not model time |
| Auth-failure wording | Yes, but the envelope and stderr already carry it |
| Finish reason, safety block, retries | Not seen; unknown until a paid capture |
| Token usage | Already in the envelope |

## Risks

- Format is an internal glog stream with no stability promise. Parsing it makes the library depend on source file names and line numbers that change between agy releases (agy updates itself).
- Content risk (fact 7). `LogMessages` may never carry content. If the library reads the log, it must extract only named fields and never forward or log lines.
- The project rule "code follows behavior, only flags verified on 1.3.5 or 1.4.0" applies to `--log-file` too: it is verified on 1.4.0 only.

## Recommendation

1. Do not parse the log for features now. The envelope and the stream already hold what the library maps; the log adds little that is verified.
2. Add an opt-in `AgyLogDirectory` option that passes `--log-file` to a caller-chosen path, for diagnosis. It is a low-effort change in `InvocationBuilder` and needs no parsing. It must be documented as possibly containing prompt content.
3. If finish reason or safety detection (deferred item 10) is wanted, spend one owner-run paid capture with `--log-file` to see whether such lines exist before designing a parser.
