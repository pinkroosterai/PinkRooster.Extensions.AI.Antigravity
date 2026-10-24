using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Keywords;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>A response schema prepared for repeated checks.</summary>
internal interface ICompiledSchema
{
    /// <summary>True when the instance satisfies the schema. Throws <see cref="ArgumentException"/> when the schema itself proves unusable.</summary>
    bool IsValid(JsonElement instance);
}

/// <summary>Prepares schemas. The one place that knows which JSON Schema library is used (docs/software-design.md DL-12).</summary>
internal interface ISchemaValidator
{
    /// <summary>
    /// Prepares the schema. Draft 2020-12 applies unless the schema declares another supported draft.
    /// Throws <see cref="ArgumentException"/> when the schema cannot be used, so most unusable schemas fail before agy is run;
    /// a defect that only shows when an answer reaches it (a bad regular expression, a reference that cannot be resolved)
    /// raises the same exception from <see cref="ICompiledSchema.IsValid"/>.
    /// </summary>
    ICompiledSchema Compile(string schemaText);
}

internal sealed class SchemaValidator : ISchemaValidator
{
    public static SchemaValidator Instance { get; } = new();

    public ICompiledSchema Compile(string schemaText)
    {
        ArgumentNullException.ThrowIfNull(schemaText);
        try
        {
            // The validator reads $ref only inside the schema or the standard meta-schemas; a remote $ref is reported as
            // unresolvable, never fetched.
            return new Compiled(new JsonValidator(schemaText, NewOptions()));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NotSupportedException
                                       or System.Text.RegularExpressions.RegexParseException)
        {
            throw new ArgumentException("The response schema cannot be used to check the answer: " + FirstLine(ex.Message), nameof(schemaText), ex);
        }
    }

    // JSON Schema 2020-12 treats "format" as an annotation unless the format-assertion vocabulary is requested, and the
    // validator asserts it by default. A model that writes a date as "2026-10-16" where the generated schema says "date-time"
    // would be rejected although the typed deserialization accepts it, so every standard format is made annotation-only.
    private static readonly string[] StandardFormats =
    [
        "date-time", "date", "time", "duration", "email", "idn-email", "hostname", "idn-hostname", "ipv4", "ipv6",
        "uri", "uri-reference", "iri", "iri-reference", "uuid", "uri-template", "json-pointer", "relative-json-pointer", "regex",
    ];

    private static JsonValidatorOptions NewOptions()
    {
        JsonValidatorOptions options = new JsonValidatorOptions();
        foreach (string format in StandardFormats)
        {
            options.FormatRegistry.SetFormat(format, static () => new AnnotationOnlyFormat());
        }

        return options;
    }

    private sealed class AnnotationOnlyFormat : FormatValidator
    {
        public override bool Validate(string value) => true;
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        string line = end < 0 ? text : text[..end];
        return line.Length <= 200 ? line : line[..200];
    }

    // Some defects of a schema (an invalid regular expression, a reference that cannot be resolved) only show when an answer
    // reaches the part of the schema that uses them. They are still the caller's schema's fault, not the answer's.
    private sealed class Compiled(JsonValidator validator) : ICompiledSchema
    {
        [SuppressMessage("Usage", "CA2208", Justification = "The exception reaches the caller of the public API, whose schema is the cause.")]
        public bool IsValid(JsonElement instance)
        {
            try
            {
                return validator.Validate(instance).IsValid;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NotSupportedException
                                           or System.Text.RegularExpressions.RegexParseException)
            {
                throw new ArgumentException("The response schema cannot be used to check the answer: " + FirstLine(ex.Message), "schema", ex);
            }
        }
    }
}
