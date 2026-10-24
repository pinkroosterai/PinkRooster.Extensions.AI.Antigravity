# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [Semantic Versioning](https://semver.org/); before 1.0 a minor version may break the API.

## [Unreleased]

First release (0.1.0) is not published yet. Contents so far:

- `AntigravityChatClient`, an `IChatClient` on top of `agy` print mode: text and JSON-schema responses, streaming, image input, caller tools through a loopback MCP bridge, a per-request agy home on Linux.
- `AddAntigravityChatClient` for dependency injection and a set of `AntigravityException` types.
- Targets `net10.0` only.
