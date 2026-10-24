using System.Text.Json;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Reads agy's standard output as an <see cref="AgyEnvelope"/>.</summary>
internal static class EnvelopeParser
{
    /// <summary>Returns false when the text is not one JSON object with a string <c>status</c>.</summary>
    public static bool TryParse(string? standardOutput, out AgyEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(standardOutput.Trim());
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out JsonElement status)
                || status.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            JsonElement raw = root.Clone();
            envelope = new AgyEnvelope(
                status.GetString()!,
                String(root, "response"),
                String(root, "error"),
                String(root, "conversation_id"),
                Double(root, "duration_seconds"),
                Int(root, "num_turns"),
                ReadUsage(root),
                root.TryGetProperty("structured_output", out JsonElement structured) && structured.ValueKind != JsonValueKind.Null
                    ? structured.Clone()
                    : null,
                ReadDeniedActionKinds(root),
                raw);
            return true;
        }
    }

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Double(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d)
            ? d
            : null;

    private static int? Int(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i)
            ? i
            : null;

    private static long? Long(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long l)
            ? l
            : null;

    private static AgyUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new AgyUsage(
            Long(usage, "input_tokens"),
            Long(usage, "output_tokens"),
            Long(usage, "total_tokens"),
            Long(usage, "cache_read_tokens"),
            Long(usage, "thinking_tokens"));
    }

    // Each denied action is an object with an "action" kind (for example "command"). Kinds are listed once, in order.
    private static List<string> ReadDeniedActionKinds(JsonElement root)
    {
        if (!root.TryGetProperty("denied_actions", out JsonElement denied) || denied.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> kinds = new List<string>();
        foreach (JsonElement item in denied.EnumerateArray())
        {
            string kind = item.ValueKind == JsonValueKind.Object ? String(item, "action") ?? "unknown" : "unknown";
            if (!kinds.Contains(kind))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }
}
