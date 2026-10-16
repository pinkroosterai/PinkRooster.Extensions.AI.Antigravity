using System.Text.Json;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>The token counts of an agy run.</summary>
internal sealed record AgyUsage(long? InputTokens, long? OutputTokens, long? TotalTokens, long? CachedInputTokens, long? ThinkingTokens);

/// <summary>
/// agy's JSON output for one run. Only <see cref="Status"/> is required; every other member is read defensively
/// and is null (or empty) when absent or of an unexpected type.
/// </summary>
internal sealed record AgyEnvelope(
    string Status,
    string? Response,
    string? Error,
    string? ConversationId,
    double? DurationSeconds,
    int? NumTurns,
    AgyUsage? Usage,
    JsonElement? StructuredOutput,
    IReadOnlyList<string> DeniedActionKinds,
    JsonElement Raw)
{
    public const string SuccessStatus = "SUCCESS";

    public bool IsSuccess => string.Equals(Status, SuccessStatus, StringComparison.Ordinal);
}
