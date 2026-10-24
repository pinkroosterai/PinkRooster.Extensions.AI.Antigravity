# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [Semantic Versioning](https://semver.org/); before 1.0 a minor version may break the API.

## [Unreleased]

## [0.1.1] - 2026-10-24

- Fix package README logo rendering on NuGet.org using standard Markdown image syntax.
- Update package README install instructions.

## [0.1.0] - 2026-10-24

First release of PinkRooster.Extensions.AI.Antigravity.

- `AntigravityChatClient`, an `IChatClient` on top of `agy` print mode: text and JSON-schema responses, streaming, image input, caller tools through a loopback MCP bridge, a per-request agy home on Linux.
- `AddAntigravityChatClient` for dependency injection and a set of `AntigravityException` types.
- Targets `net10.0`.
