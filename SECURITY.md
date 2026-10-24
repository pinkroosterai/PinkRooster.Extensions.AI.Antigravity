# Security policy

## Reporting a vulnerability

Please report it privately through [GitHub private vulnerability reporting](https://github.com/pinkroosterai/AgyChatClient/security/advisories/new), not as a public issue. Include the library and agy versions, the operating system, and the smallest steps that show the problem. Expect an answer within a few days; this is a volunteer project.

## What is in scope

The library starts a process (`agy`), runs a loopback MCP server for caller tools, writes files in a per-request directory and passes the prompt as a command-line argument. Reports about these are welcome, for example:

- text in a prompt or tool result that runs an agy command, escapes the request directory or reaches a tool the caller did not supply;
- the loopback tool bridge accepting a connection or request it should refuse;
- a prompt, answer, schema, standard error, environment value or tool argument leaking into a log or exception message;
- a temporary directory or agy home left behind, or created with wider permissions than documented.

Known limits are listed in the README under "Read this before you use it" (for example: the prompt is visible in the process list, and agy keeps its own transcript). They are not vulnerabilities in the library.

## Supported versions

Only the latest release gets fixes. Before 1.0, a fix may change the public API.
