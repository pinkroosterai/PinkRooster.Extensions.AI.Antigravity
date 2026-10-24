## What and why

<!-- One logical change per PR. Link the issue if there is one. -->

## Checklist

- [ ] `dotnet build PinkRooster.Extensions.AI.Antigravity.sln -warnaserror` is clean
- [ ] Unit and process tests pass (`dotnet test tests/...Tests`, `...ProcessTests`)
- [ ] New public API is in `PublicAPI.Unshipped.txt` and has XML docs
- [ ] No prompt, answer, schema, standard error, environment value or tool argument/result is logged or put in an exception message
- [ ] I did not run the real `agy` against a model; any claim about agy behaviour is labelled verified, reported or unknown
