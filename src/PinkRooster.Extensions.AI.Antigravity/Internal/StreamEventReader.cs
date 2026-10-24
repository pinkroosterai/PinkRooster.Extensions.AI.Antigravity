using System.Text.Json;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>What one line of agy's <c>stream-json</c> output means to the library.</summary>
internal enum StreamEventKind
{
    /// <summary>An event the library does not use (a tool step, a step without text, an unknown event).</summary>
    Other,

    /// <summary>The <c>init</c> event; <see cref="StreamEvent.Model"/> and <see cref="StreamEvent.ConversationId"/> may be set.</summary>
    Init,

    /// <summary>A piece of answer text in <see cref="StreamEvent.Text"/>.</summary>
    Text,

    /// <summary>The last event; <see cref="StreamEvent.ResultJson"/> is the result object, which has the fields of the JSON envelope.</summary>
    Result,
}

internal sealed record StreamEvent(StreamEventKind Kind, string? Text = null, string? Model = null, string? ConversationId = null, string? ResultJson = null);

/// <summary>
/// Reads one line of <c>stream-json</c> output (docs/streaming-responses/research.md, "Captured stream-json shape").
/// A line is <c>{"event": "&lt;kind&gt;", "&lt;kind&gt;": {...}}</c>; unknown kinds and members are ignored.
/// </summary>
internal static class StreamEventReader
{
    private const string AnswerStepType = "agent_response";

    /// <summary>Returns false when the line is not a JSON object with a string <c>event</c> member.</summary>
    public static bool TryRead(string line, out StreamEvent? streamEvent)
    {
        streamEvent = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            streamEvent = new StreamEvent(StreamEventKind.Other);
            return true;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("event", out JsonElement kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            streamEvent = kind.GetString() switch
            {
                "init" => ReadInit(root),
                "step_update" => ReadStep(root),
                "result" when root.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Object =>
                    new StreamEvent(StreamEventKind.Result, ResultJson: result.GetRawText()),
                _ => new StreamEvent(StreamEventKind.Other),
            };
            return true;
        }
    }

    private static StreamEvent ReadInit(JsonElement root)
    {
        string? model = root.TryGetProperty("init", out JsonElement init) ? String(init, "model") : null;
        return new StreamEvent(StreamEventKind.Init, Model: model, ConversationId: String(root, "conversation_id"));
    }

    private static StreamEvent ReadStep(JsonElement root)
    {
        if (!root.TryGetProperty("step_update", out JsonElement step) || step.ValueKind != JsonValueKind.Object)
        {
            return new StreamEvent(StreamEventKind.Other);
        }

        string? delta = String(step, "text_delta");
        return String(step, "step_type") == AnswerStepType && !string.IsNullOrEmpty(delta)
            ? new StreamEvent(StreamEventKind.Text, Text: delta, ConversationId: String(step, "conversation_id"))
            : new StreamEvent(StreamEventKind.Other, ConversationId: String(step, "conversation_id"));
    }

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Withholds the line terminator at the end of the text so far, because <see cref="ResponseMapper.PlainText"/> drops one terminator from
/// a finished answer and the streamed text must join to the same string.
/// </summary>
internal sealed class TrailingTerminatorHold
{
    private string _held = string.Empty;

    /// <summary>Takes the next piece of text and returns what can be passed on now (possibly empty).</summary>
    public string Push(string piece)
    {
        string text = _held + piece;
        int keep = text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : text.EndsWith('\n') || text.EndsWith('\r') ? 1 : 0;
        _held = text[^keep..];
        return text[..^keep];
    }

    /// <summary>What is still held when the answer is complete: a lone carriage return is text, a terminator is not.</summary>
    public string Finish() => _held == "\r" ? _held : string.Empty;
}
