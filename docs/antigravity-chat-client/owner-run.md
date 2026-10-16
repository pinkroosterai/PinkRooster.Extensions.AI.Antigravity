# Owner run: the tests against the real agy

Written 2026-10-16 for plan phase 7. These runs call the model, so they spend tokens and are yours to start; nothing in the build, the unit suite, the process suite or CI runs them.

## Before you start

- agy installed and signed in. `agy --version` printed **1.4.0** when this was written (it was 1.3.5 in the research; the CLI updated itself). The library accepts it and logs once that it is newer than the tested 1.3.5, so this run is also the first test of 1.4.0 (SPEC Q10).
- The solution builds: `dotnet build -warnaserror` from the repository root.
- Cost, estimated and not measured: about 18 model calls (13 in `RealAgyTests`, 5 in the capture), each around 17,000 to 22,000 input tokens on `gemini-3.8-flash-low`, so roughly 300,000 to 400,000 input tokens. The unsatisfiable-schema and essay-length cases are the longest. Each test also leaves one more conversation in `~/.gemini/antigravity-cli/brain/`.

## Run 1: the library against the real agy (16 calls)

```
AGY_INTEGRATION_TESTS=1 dotnet test tests/PinkRooster.Extensions.AI.Antigravity.IntegrationTests \
  --filter "FullyQualifiedName~RealAgyTests" --logger "console;verbosity=detailed"
```

Expected: 11 tests pass and 1 is skipped (the not-signed-in one needs run 3). A failure prints what was observed.

| Test | Calls | Should show | A failure looks like |
|---|---|---|---|
| `Plain_prompt_returns_text` | 1 | Answer contains "pong"; response id set; input tokens above 0; model id `gemini-3.8-flash-low` | An exception (read its type and message); empty answer |
| `Schema_prompt_roundtrips_into_T` | 1 | `Paris` as the city | `AntigravityInvalidOutputException` (agy returned no structured result), a `AntigravityRunException` with a content-safety text (seen once on a trivial prompt, so a rerun is fair) |
| `Unsatisfiable_schema_throws_InvalidOutput` | 1 | `AntigravityInvalidOutputException`, as agy 1.3.5 behaved (SUCCESS without a structured result) | Any other exception type: agy 1.4.0 behaves differently, which is a finding |
| `File_creation_request_leaves_no_trace` | 1 | No `agy-integration-created.txt` and no other new entry in the working directory; the scratch root empty; the output says what agy answered | The file or a leftover directory exists: the isolation failed (report this first) |
| `Leading_slash_is_not_a_command` | 1 | A prose answer, not agy's list of commands | The command list is printed (`/changelog<TAB>Show release notes…`) |
| `Preamble_blocks_tool_use_sample` | 5 | A summary line "N of 5 runs had a denied action, M of 5 used more than one turn"; informational | Only the caller's directory is asserted; a changed directory fails it |
| `Unicode_roundtrip` | 1 | The answer contains `héllo 日本語 😀` | Garbled characters |
| `Streaming_yields_text_updates_that_join_to_the_answer` | 1 | Several updates (the output line says how many and when the first and last arrived: the first should come well before the last); text contains "twenty"; usage and response id set | One update only (agy sent the text in one piece, or the stream reader dropped events); an `AntigravityOutputException` (the stream shape differs from `docs/streaming-responses/capture/`) |
| `A_real_agy_calls_a_caller_tool_through_the_bridge_without_skip_permissions` | 1 | The answer contains `ZEBRA-4417`; the output says the tool body ran at least once; no skip-permissions flag was used | `AntigravityRunException` or an empty answer (agy denied the MCP permission: the allow rule in the temp home was not honoured on this agy version — report it); the tool body ran 0 times; a timeout (agy could not reach the bridge or did not read its schema) |
| `Isolated_home_keeps_sign_in_and_follows_an_agents_md_rule` | 1 | No exception (sign-in survives the per-request `HOME`); the answer contains `PINEAPPLE-7` (the system text arrived through `AGENTS.md`); the answer has no trailing lines from your own global agy rules | `AntigravityNotAuthenticatedException` (sign-in does not survive the redirect on this machine: set `IsolateAgyHome` to false and report); no `PINEAPPLE-7` (the `AGENTS.md` rule was ignored); user-rule suffix lines still present |
| `Reasoning_effort_with_base_model` | 2 | Both answers contain "ok" (the effort is passed for `gemini-3.8-flash` and left out for `gemini-3.8-flash-low`) | `AntigravityProcessException` mentioning `conflicts with --effort` |

## Run 2: capture fresh fixtures (3 free, 5 paid calls)

```
mkdir -p /tmp/agy-captured
AGY_INTEGRATION_TESTS=1 AGY_CAPTURE_DIR=/tmp/agy-captured dotnet test \
  tests/PinkRooster.Extensions.AI.Antigravity.IntegrationTests \
  --filter "FullyQualifiedName~FixtureCaptureTests" --logger "console;verbosity=detailed"
```

It only writes files into `/tmp/agy-captured` (raw agy output, no prompt text beyond the tiny test prompts). Copy nothing yourself: send me the folder name or paste the files and I will replace the transcribed fixtures.

## Run 3 (optional but it closes an open question): the real "not signed in" output

Needs agy **not** signed in on the machine that runs it. Easiest is a machine or container with agy installed and never signed in; signing out here (`/logout` inside agy) clears your credentials and keyring entries, so only do it if you are happy to sign in again.

```
AGY_INTEGRATION_TESTS=1 AGY_TEST_UNAUTHENTICATED=1 dotnet test \
  tests/PinkRooster.Extensions.AI.Antigravity.IntegrationTests \
  --filter "FullyQualifiedName~Not_signed_in_signal" --logger "console;verbosity=detailed"
```

It prints the exception type, message, exit code and standard error. It passes only if the library already recognizes the wording; if it fails, the printed text is exactly what I need to add to the detector (one call).

## Windows and macOS

If you will use the library there, run runs 1 and 2 on that system too and send the output; the process-tree kill, the argument-size limit and UTF-8 output on those systems are unverified until then.

## What to send back

The console output of each run (or its path). I record it in `plan.md`'s Log, refresh fixtures, fix what the run shows, update the README's compatibility table (1.4.0) and close the open items in the specification.
