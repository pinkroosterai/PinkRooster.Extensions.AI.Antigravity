# IChatClient gap analysis: Antigravity client vs an OpenAI-style client

Date: 2026-10-16. Source: `RequestValidator`, `InvocationBuilder`, `ResponseMapper`, `AntigravityChatClient` and `docs/deferred.md`. agy was not run; nothing here is a new measurement.

Goal tested against: the client is a drop-in replacement for the OpenAI chat client.

## Verdict

A drop-in for plain chat, structured output and server-side tool use. Not a drop-in for anything that depends on the model API itself: sampling, images, token limits, caller-visible tool calls. Some gaps cannot be closed because agy does not offer the feature.

## Supported

- `GetResponseAsync` and `GetStreamingResponseAsync`, with incremental text for plain-text requests.
- `ChatResponseFormat.ForJsonSchema` and `GetResponseAsync<T>`; the schema is validated and the request throws on an invalid answer.
- System, user and assistant text, and `ChatOptions.Instructions`.
- `ModelId` and `Reasoning.Effort`.
- `AIFunction` tools, run through a loopback MCP bridge.
- Usage and response ID mapping, `ChatClientMetadata`, and the standard DI, `UseOpenTelemetry` and `UseLogging` wrappers.

## Gaps

| Area | Today | Can it be closed? | Impact on "drop-in" |
|---|---|---|---|
| Sampling options (`Temperature`, `TopP`, `TopK`, `Seed`, penalties, `StopSequences`, `MaxOutputTokens`) | Ignored, with a log line naming them | No. agy has no such flags. | Medium. Output does not change and only the log warns. |
| Multimodal input (`DataContent`, `UriContent`) | `NotSupportedException` | Maybe, by writing files into the temp directory (unverified). | High for vision code |
| Tool calls visible in the response (`FunctionCallContent` / `FunctionResultContent`) | The caller's `AIFunction` delegate does run, in process, through the MCP bridge (`McpToolServer.cs:160`) and its result goes back to agy. The response holds only the final text, with no call content. | Yes, by mapping the bridge's calls to content. It only becomes a record: agy still owns the loop, so the caller cannot veto or replace a call. | Low. Tools work. Only code that reads call content from the response differs. |
| Tool approval (`ApprovalRequiredAIFunction`) | `NotSupportedException` | No | Medium |
| `ToolMode` RequireAny / RequireSpecific | `NotSupportedException` | No. agy decides when to call a tool. | Medium |
| Tools combined with a JSON or schema response | `NotSupportedException` | Maybe, with a two-step flow | Medium |
| Hosted tools and declaration-only functions | `NotSupportedException` | Declaration-only: no. Hosted: unknown. | Low to medium |
| Tool or function-call content in message history | `NotSupportedException` | Partly, by flattening to text | Medium for multi-turn agent code |
| Tool use on Windows and macOS | Refused; per-request home isolation exists on Linux only | Needs per-OS work | High if run elsewhere |
| `FinishReason` | Always `Stop` | Partly, by parsing the safety-block text | Low to medium |
| `ConversationId`, `ContinuationToken`, background responses | Ignored; full history resent, each call stateless | Yes, via `--conversation` | Low (slower, costlier) |
| Streaming of JSON or schema responses | One update at the end | Only if agy adds partial structured output | Low |
| Streaming of tool steps and reasoning | Not surfaced | Yes | Low |
| `TextReasoningContent` in the response | Not returned | Unknown | Low |
| `AllowMultipleToolCalls` | Ignored | n/a | Low |
| Prompt length | Over `MaxPromptLength` throws | Yes, via a prompt-file fallback | Medium for long context |
| Latency, cost, concurrency | One process per call; about 17k input tokens overhead per call; about 18 MB written per home; `MaxConcurrency` throttles | Partly, via a template home | High for chat-heavy apps |
| Retry on invalid structured output | Throws | Yes, opt-in | Low to medium |
| Embeddings (`IEmbeddingGenerator`) | None | No | Separate interface |
| Caller-set `HOME` | Rejected on Linux | n/a | Low |
| API-key / ADC sign-in with an isolated home | Untested; likely "not signed in" | Yes, with supplied settings | Low |

## Reading of the gaps

- Closable with effort: Windows and macOS tool support, multimodal, tools with a JSON format, `FinishReason`, conversation reuse, prompt-file fallback.
- Not closable with agy: sampling control, caller-side control of the tool loop (approval, forced modes), token limits, embeddings.
- The silent ignore of sampling options and `MaxOutputTokens` is the most likely surprise on a swap from OpenAI. An opt-in strict mode that throws would make it visible. How many callers rely on these options was not checked.

## Proposed order if the goal stays "drop-in"

1. Multimodal input: the biggest hard failure today.
2. Strict-options mode.
3. `FinishReason` mapping.

Anything beyond that (non-Linux tools, caller control of the tool loop) is a redesign and goes through `/plan-work`.
