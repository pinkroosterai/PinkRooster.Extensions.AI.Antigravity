using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// Turns agy's structured result, or a plain JSON answer, into the response text, or raises
/// <see cref="AntigravityInvalidOutputException"/> (docs/software-design.md section 4.4 rules 8 and 9, section 6).
/// </summary>
internal static class StructuredResultChecker
{
    private static readonly JsonWriterOptions CompactOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The result of a schema request: <c>structured_output</c> only, checked against the schema, as compact JSON.</summary>
    public static string CheckSchemaResult(AgyEnvelope envelope, ICompiledSchema schema)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(schema);

        if (envelope.StructuredOutput is not JsonElement result)
        {
            // agy checks the schema itself and leaves the field out when it cannot produce a matching value.
            throw new AntigravityInvalidOutputException("agy returned no structured result for the requested schema.", envelope.Response);
        }

        if (!schema.IsValid(result))
        {
            throw new AntigravityInvalidOutputException(
                "The structured result does not match the requested schema.", result.GetRawText());
        }

        return Compact(result);
    }

    /// <summary>
    /// The answer to a request for JSON without a schema: the text, with one surrounding Markdown code fence removed,
    /// must be exactly one JSON value.
    /// </summary>
    public static string CheckPlainJson(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        string text = StripFence(answer.Trim());
        try
        {
            using JsonDocument _ = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new AntigravityInvalidOutputException("The answer is not valid JSON.", answer);
        }

        return text;
    }

    private static string Compact(JsonElement element)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, CompactOptions))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // A reply of the form ```json ... ``` (or ``` ... ```) is common even when asked for bare JSON.
    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal) || text.Length < 6)
        {
            return text;
        }

        string inner = text[3..^3];
        int firstLineEnd = inner.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return text; // no line break: not a fence around a block
        }

        string info = inner[..firstLineEnd].Trim();
        if (info.Length > 0 && !info.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return inner[(firstLineEnd + 1)..].Trim();
    }
}
