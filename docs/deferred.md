# Deferred and out-of-scope topics

Living list. Each row: what is out of v1, why, and how it could be added. The specification (step 3) will copy this into its out-of-scope section.
Date: 2026-10-16. Evidence refs point to `docs/research-findings.md` (F#n = §1 finding n).

| # | Topic | v1 behavior | Why deferred | How to add later |
|---|---|---|---|---|
| 1 | Caller tool calling (AIFunction) | `Tools`/`ToolMode` and tool content → `NotSupportedException` | agy only runs its own tools; it has no hook to return a call to the client (stream-json events show agy's tools, not caller's). | Expose caller functions through an MCP server that agy connects to (`agy mcp add`), or a prompt-level JSON protocol with a function-invoking wrapper; needs a design of its own. |
| 2 | Streaming of tool steps, thinking and JSON-format answers | Plain-text requests stream text (`docs/streaming-responses/spec.md`); `Json` and schema requests yield one update; tool steps and thinking are not content | The schema applies to agy's final result only, and a plain-JSON answer is checked when complete. | Surface `tool` steps as function-call content if callers need them; stream a schema answer only if agy adds partial structured output. Shape seen in F-run T-E3. |
| 3a | Reusable template home | Each request writes ~18 MB into a fresh agy home (a binary agy unpacks) | Not needed for correctness. | Seed a library-owned template and copy or link it per request; measure first. |
| 3b | API-key and Application Default Credentials sign-in with an isolated home | Untested; such users may see "not signed in" and should set `IsolateAgyHome` to false | Their sign-in lives in agy settings (F-20), which the isolated home lacks. | Let the caller supply authentication settings to write into the per-request home. |
| 3 | Multimodal input | `DataContent`/`UriContent` → `NotSupportedException` | No documented way to pass images to print mode. | Write files into the temp directory and reference them in the prompt (agy can read workspace files); verify model support first. |
| 4 | Embeddings | No `IEmbeddingGenerator` | agy has no embeddings command. | Out of reach with agy; would need another backend. |
| 5 | Session reuse | Stateless; history resent every call | `--continue`/`--conversation` persist state and bind to the working directory (S1). | Map `ChatOptions.ConversationId` to `--conversation <id>` and send only the new turn; needs a stable working directory per conversation. |
| 6 | Caller-chosen working directory / agent editing files | Always an empty temp directory | A chat client must not change caller files (S1). | Separate opt-in "agent" client type with explicit permission rules (`permissions.allow`) and `--add-dir`. |
| 7 | Prompt file fallback for long prompts | Over `MaxPromptLength` → exception | Adds an extra tool turn and changes behavior. | Write `prompt.md` in the temp dir and send "read prompt.md and follow it"; measure cost first. |
| 8 | Retry on invalid structured output | Throws, no retry | Hidden second paid call. | Opt-in `MaxSchemaRetries` that resends with the validation error. |
| 9 | Own telemetry (`ActivitySource`, metrics) | Use `UseOpenTelemetry()` | Not needed for a first release. | Add an `ActivitySource` following the OTel GenAI conventions. |
| 10 | Mapping `FinishReason.Length`/`ContentFilter` | `Stop` only | agy exposes no reliable signal (F2). | Parse `error` text for the safety-block phrase once its format is stable. |
| 11 | Preventing agy's stored conversations | Not prevented | No flag found (F5). | Re-check each agy release; if a no-persist flag appears, pass it. |
| 12 | `--mode`, `--effort` beyond Reasoning mapping, `--agent`, `--project` | Rejected in `AdditionalArguments` (L4) | Break statelessness or safety. | Add typed options after a safety review. |
| 13 | Opt-in run without the user's MCP servers | README warns only (decided 2026-10-16) | They add start-up cost and tools with side effects (F-39). | Test an environment variable that gives agy a separate home; if it works, add an option that sets it. |
