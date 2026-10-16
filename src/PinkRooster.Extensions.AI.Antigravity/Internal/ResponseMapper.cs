using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Maps a successful <see cref="AgyEnvelope"/> to a <see cref="ChatResponse"/>.</summary>
internal static class ResponseMapper
{
    public const string NumTurnsKey = "agy.num_turns";
    public const string DurationKey = "agy.duration_seconds";
    public const string DeniedActionsKey = "agy.denied_actions";

    /// <summary>The answer text of a plain request: agy's response without the one line terminator it appends.</summary>
    public static string PlainText(AgyEnvelope envelope)
    {
        string text = envelope.Response ?? string.Empty;
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }

    public static ChatResponse Map(AgyEnvelope envelope, string text, string? modelId, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(text);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ResponseId = envelope.ConversationId,
            ModelId = modelId,
            CreatedAt = createdAt,
            FinishReason = ChatFinishReason.Stop,
            Usage = MapUsage(envelope.Usage),
            RawRepresentation = envelope.Raw,
        };

        response.AdditionalProperties = new AdditionalPropertiesDictionary();
        if (envelope.NumTurns is int turns)
        {
            response.AdditionalProperties[NumTurnsKey] = turns;
        }

        if (envelope.DurationSeconds is double seconds)
        {
            response.AdditionalProperties[DurationKey] = seconds;
        }

        if (envelope.DeniedActionKinds.Count > 0)
        {
            response.AdditionalProperties[DeniedActionsKey] = envelope.DeniedActionKinds;
        }

        return response;
    }

    private static UsageDetails? MapUsage(AgyUsage? usage) =>
        usage is null
            ? null
            : new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.TotalTokens,
                CachedInputTokenCount = usage.CachedInputTokens,
                ReasoningTokenCount = usage.ThinkingTokens,
            };
}
