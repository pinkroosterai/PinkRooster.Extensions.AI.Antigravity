# Fixtures

Outputs of agy 1.3.5 used by the parser, classifier and mapper tests.

**Provenance.** Files marked *transcribed* were copied by hand from agy output recorded in the working session of
2026-10-16 (the run ids T-.. and G/J/H.. refer to `docs/research-findings.md` and `docs/software-design.md` §1.2); the
conversation ids were replaced with fixed values. The original scratch files were deleted, so the transcriptions were not
compared byte for byte with a file. They are replaced by files captured from a new run in plan phase 7. Files marked
*synthetic* are not agy output at all.

| File | Provenance | Run |
|---|---|---|
| `version-1.3.5.txt` | Captured: `agy --version` | |
| `success-text.json` | Transcribed | T-A1 |
| `success-schema.json` | Transcribed | J1 |
| `schema-missing-structured.json` | Transcribed: SUCCESS without `structured_output`, with `denied_actions` | G2 |
| `denied-with-answer.json` | Transcribed: same run as above (non-empty answer plus denial) | G2 |
| `error-safety-block.json` | Transcribed: exit 0, status ERROR, `structured_output` present | T-A3 |
| `timeout-empty-success.json`, `timeout.stderr.txt` | Transcribed | T-B2 |
| `denied-empty.json`, `denied.stderr.txt` | Transcribed: empty answer, denied command | T-C4 |
| `exit1-bad-model.json`, `exit1-bad-model.stderr.txt` | Transcribed: exit code 1 | G5 |
| `exit1-empty-prompt.json` | Transcribed: exit code 1 | T-B5 |
| `exit1-bad-schema.stderr.txt` | Transcribed: exit code 1, empty stdout | T-B4 |
| `cancel-context-canceled.json`, `cancel-context-canceled.stderr.txt` | Transcribed: exit code 1 after SIGTERM | T-D4 |
| `not-signed-in.SYNTHETIC.json`, `not-signed-in.SYNTHETIC.stderr.txt` | **Synthetic**: built from the text the official documentation gives ("authentication required"); no real output exists (open question Q5) | none |
