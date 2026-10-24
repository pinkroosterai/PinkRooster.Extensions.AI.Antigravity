<img src="https://raw.githubusercontent.com/pinkroosterai/PinkRooster/a3815ce62a2491a8ee1c7401e1e48aa43ef20a85/assets/logo.png" alt="PinkRooster logo" width="120" />

# PinkRooster.Extensions.AI.Antigravity

Microsoft.Extensions.AI chat client for Google Antigravity (`agy`).

Status: preview (`0.1.0-preview.1`), developed against the .NET 10 SDK.

```text
dotnet add package PinkRooster.Extensions.AI.Antigravity --prerelease
```

## Quickstart

```csharp
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity;

IChatClient client = new AntigravityChatClient();
ChatResponse response = await client.GetResponseAsync("What is the capital of the Netherlands?");
Console.WriteLine(response.Text);
```

## Features

- **Standard `IChatClient` pipeline**: works with Microsoft.Extensions.AI abstractions, middleware, and dependency injection.
- **Structured output**: support for `GetResponseAsync<T>` and `ChatResponseFormat.ForJsonSchema`.
- **Streaming**: incremental text streaming for chat completions.
- **Local MCP tools**: automatic loopback bridge for your `AIFunction` tools into agy.
- **Image inputs**: multimodal `DataContent` support for image prompt inputs.
- **Request isolation**: temporary throwaway environments for every invocation on Linux.

## Compatibility

- .NET 10 (and .NET 8)
- Google Antigravity CLI (`agy`) 1.3.5 or newer installed and signed in.
