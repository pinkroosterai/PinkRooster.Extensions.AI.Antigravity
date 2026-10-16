# Review rubric

Used in step 5 of the documentation phase to review `docs/specification.md` and `docs/software-design.md`. Each check is pass or fail; a document passes when every check passes. Moved here unchanged from `CLAUDE.md`.

Steps 3 and 4 write to this rubric and step 5 applies it. Each check is pass or fail. A document passes when every check passes.

Specification checks
- S1 Every requirement has an id, a testable statement and a priority.
- S2 Every IChatClient member and every ChatOptions field has a stated behavior (honored, ignored or rejected, with the reason).
- S3 JSON-schema behavior is specified, including what happens when the returned JSON is invalid or does not match the schema.
- S4 Behavior for caller-supplied tools and tool-related content matches the interview decision.
- S5 Every CLI and library fact carries a label (verified, reported, unknown) and a source or command.
- S6 The out-of-scope list is present, with a future-extension note for each item.
- S7 The error catalog covers: agy missing, agy not signed in, timeout, cancellation, nonzero exit, unparsable output, schema-invalid output.
- S8 Acceptance criteria exist and each requirement appears in the traceability table.
- S9 The specification states what the library does, not how; implementation choices live in the design.

Design checks
- D1 Every functional requirement maps to a component and to at least one test.
- D2 Sequence diagrams exist for: plain request, JSON-schema request, timeout and cancellation, agy missing or not signed in.
- D3 Process management addresses deadlock avoidance, killing the process tree on cancellation, encoding, and Windows, Linux and macOS differences.
- D4 Side-effect safety for agy's own tools is designed (sandbox or permission posture, working directory).
- D5 Prompt passing addresses argument-length limits and injection or escaping.
- D6 A compatibility strategy covers changes in agy flags or output format, with a documented minimum version.
- D7 The decision log records alternatives and reasons for each significant decision.
- D8 Terms and behavior are consistent with the specification, with no contradictions.
- D9 Prompts, responses and credentials are never logged by default.
- D10 Diagrams are read through for valid Mermaid syntax (they cannot be rendered here, so say so).
