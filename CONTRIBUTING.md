# Contributing

Thanks for helping. This is a small library; the rules below keep it that way.

## Build and test

Needs the .NET 10 SDK. Neither suite needs `agy` or the network.

```
dotnet build PinkRooster.Extensions.AI.Antigravity.sln -warnaserror
dotnet test tests/PinkRooster.Extensions.AI.Antigravity.Tests
dotnet test tests/PinkRooster.Extensions.AI.Antigravity.ProcessTests
dotnet pack src/PinkRooster.Extensions.AI.Antigravity -c Release
```

If the build dies with `Internal CLR error (0x80131506)`, run `dotnet build-server shutdown` and retry.

## Rules

- **Do not run `agy -p` against a model, or the opt-in suite (`tests/*.IntegrationTests`), in a PR.** Every call spends tokens. Test with `tools/FakeAgy`. If a change needs a real run, say so in the PR and the maintainer will run it.
- **Warnings are errors.** Every public member needs XML docs. Code style is in `.editorconfig`; the build enforces it.
- **Public API is tracked.** A new or changed public member must be added to `src/PinkRooster.Extensions.AI.Antigravity/PublicAPI.Unshipped.txt` (the build tells you the exact line). At release time the maintainer moves the lines to `PublicAPI.Shipped.txt`. After 1.0, `Shipped` is a contract: removing a line is a breaking change.
- **Never log, or put in an exception message,** a prompt, answer, schema, standard error, environment value or tool argument/result.
- **Facts about agy** are labelled verified, reported or unknown with source and date (`docs/specification.md`, `docs/antigravity-chat-client/research.md`). Do not write an unchecked CLI fact as verified.
- **New package references** go in with `dotnet add package`. The library depends only on the Microsoft.Extensions abstractions and one schema validator; keep it that way.
- A test that must catch a guard is checked once by breaking the guard and seeing it fail.

## Commits and pull requests

- [Conventional commits](https://www.conventionalcommits.org/): `type(scope): description`.
- One logical change per commit and per PR.
- Never commit a secret.

## Releases (maintainer)

The version comes from git tags (MinVer, prefix `v`). Move `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt`, update `CHANGELOG.md`, then push a tag such as `v0.1.0`. `.github/workflows/release.yml` builds, tests, pushes to NuGet.org and creates the GitHub release.

One-time setup: a `nuget` environment in the repository settings, a `NUGET_USER` secret (the nuget.org profile name), and a trusted-publishing policy for this repository on nuget.org.
