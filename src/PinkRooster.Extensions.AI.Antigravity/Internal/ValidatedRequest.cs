namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>The kind of answer the caller asked for.</summary>
internal enum ResponseKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Any JSON value, no schema.</summary>
    Json,

    /// <summary>A JSON object matching a schema.</summary>
    JsonSchema,
}

/// <summary>The role of a turn in the conversation part of a prompt.</summary>
internal enum PromptRole
{
    /// <summary>A user turn.</summary>
    User,

    /// <summary>An assistant turn.</summary>
    Assistant,
}

/// <summary>One user or assistant turn, its text parts already joined.</summary>
internal sealed record PromptTurn(PromptRole Role, string Text);

/// <summary>A request that passed <see cref="RequestValidator"/>, reduced to what the prompt builder needs.</summary>
internal sealed record ValidatedRequest(
    IReadOnlyList<string> SystemTexts,
    IReadOnlyList<PromptTurn> Turns,
    ResponseKind ResponseKind,
    string? SchemaText,
    string? SchemaName,
    string? SchemaDescription);
