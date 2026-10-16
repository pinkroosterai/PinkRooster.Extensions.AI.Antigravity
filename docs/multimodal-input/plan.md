# Plan: Multimodal input (images)

**Goal** — A user message may carry `DataContent` images. The library writes them as files in the request directory and the prompt points agy at them. Unit and process suites pass. Whether agy reads those files and the model sees the image is unverified until the owner's paid run.
**Status** — done; verified once against real agy 1.4.0 (jpeg)
**Date** — 2026-10-16

## Decisions (assumed, redline freely)

| # | Decision | Why |
|---|---|---|
| 1 | Only `DataContent` with media type `image/png`, `image/jpeg`, `image/gif` or `image/webp`, in **user** messages. Everything else non-text keeps throwing `NotSupportedException` before any process starts. | The four types are the common image types; agy support for each is unverified. User-only because an assistant image has no meaning as input. |
| 2 | `UriContent` stays unsupported, with a clearer message ("pass the bytes as `DataContent`"). | Fetching a caller URL means the library makes network requests for the caller (server-side request risk) and agy cannot read a URL as an image. |
| 3 | Fixed limits: at most 10 images per request, at most 10 MiB each. Over a limit → `ArgumentException`. No public option yet. | Keeps the public surface unchanged; both are constants that can become options later. |
| 4 | Files are named by the library (`attachments/image-N.<ext>`, N counted over the whole request), never by the caller. | No path traversal from caller data. |
| 5 | In the turn text, the image appears as `[attachment: attachments/image-N.ext]` at its position. | Keeps order of text and images; agy reads the file by relative path from its working directory. |
| 6 | The default preamble forbids reading files. When a request has images, the default preamble is replaced by one that allows reading the attachment files only (also when tools are present). A caller-set preamble is kept as is. | Same pattern as the existing tool preamble. |
| 7 | JSON and schema responses work with images (the schema is a separate file). | No conflict. |
| 8 | No new agy permission rule is written. If agy denies the read, it shows up as `denied_actions` and is logged as today. | The permission syntax for file reads is unverified; guessing a rule would be an unchecked CLI claim. |

## Tasks

- [x] `ValidatedRequest` carries `Attachments`; `RequestValidator` accepts, checks and names images — `Internal/ValidatedRequest.cs`, `Internal/RequestValidator.cs`.
- [x] One place chooses the effective preamble — `AgentsFile.EffectivePreamble`, used by `AgentsFile.Build` and `PromptBuilder`.
- [x] `RequestDirectory.WriteAttachments` writes the files owner-only — `Internal/RequestDirectory.cs`.
- [x] Both the buffered and the streaming path write them before agy starts — `AntigravityChatClient.cs`.
- [x] FakeAgy lists the attachment files it finds (`FAKE_AGY_ECHO_ATTACHMENTS`), so a process test can prove the bytes arrive — `tools/FakeAgy/Program.cs`.
- [x] Tests with the code: validator, prompt builder, request directory, client (nothing leaks into logs or arguments, buffered and streaming), process test; break each guard once.
- [x] Opt-in real-agy test and the owner's run entry; do not run it — `tests/…IntegrationTests/RealAgyTests.cs`, `docs/antigravity-chat-client/owner-run.md`.
- [x] Documents: `docs/specification.md` (O-3, FR-007, EC-23), `docs/software-design.md`, `docs/decisions.md` M3, `docs/deferred.md` row 3, `README.md`, `CLAUDE.md` product line.

**Done when** — `dotnet build PinkRooster.Extensions.AI.Antigravity.sln -warnaserror`, the unit suite and the process suite exit 0; the mutation checks are in the Log; the opt-in test exists and waits for the owner.

## Owner's paid check (not run by me)

One call with the rooster photo (`assets/images/rooster-profile.jpg`): does agy read `attachments/image-1.png` and describe it? A denied read shows as `denied_actions`; a model that cannot see images answers without the content. The exact command is in `docs/antigravity-chat-client/owner-run.md`.

## Log
- 2026-10-16 · Built as planned, no deviations. `AgentsFile.EffectivePreamble` is the one place that picks the preamble (the first draft only swapped it when tools were present; the client test caught that `AGENTS.md` still carried the default one). Unit 517 passed (+17 new in `AttachmentTests`, two old tests changed from png to pdf as the unsupported example), process 46 passed (+1), `dotnet build -warnaserror` 0 warnings. Mutation checks, each reverted: role check removed → 1 test failed; media-type check removed → 6 failed; streaming path not writing the files → 1 failed. Not done: the opt-in test `A_real_agy_describes_an_attached_image` is written and skips; agy reading `attachments/image-1.png` and the model seeing it is unverified (`owner-run.md`, run 1). Nothing delegated.
- 2026-10-16 · Paid check run with the owner's go-ahead (one call, `A_real_agy_describes_an_attached_image`, agy 1.4.0, `gemini-3.8-flash-low`, isolated home): passed. Answer "Rooster", no denied actions, 1 turn, 26,697 input tokens (the usual overhead is about 17,000, so the photo cost roughly 10,000, an estimate from one call). So agy reads `attachments/image-1.jpg` with no extra permission rule and the model sees the pixels. Verified for jpeg only; png, gif and webp are unchecked.
