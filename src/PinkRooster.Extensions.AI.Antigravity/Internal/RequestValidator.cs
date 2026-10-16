using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Checks a request before any process starts. Pure: no input or output, no state.</summary>
internal static class RequestValidator
{
    public static ValidatedRequest Validate(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (options is not null)
        {
            if (options.Tools is { Count: > 0 })
            {
                throw new NotSupportedException("ChatOptions.Tools is not supported: the caller's tools are not forwarded to agy.");
            }

            if (options.ToolMode is not null && !options.ToolMode.Equals(ChatToolMode.None))
            {
                throw new NotSupportedException("ChatOptions.ToolMode is not supported: the caller's tools are not forwarded to agy.");
            }
        }

        var systemTexts = new List<string>();
        var turns = new List<PromptTurn>();
        bool anyText = false;

        foreach (ChatMessage message in messages)
        {
            ArgumentNullException.ThrowIfNull(message, nameof(messages));

            var parts = new List<string>();
            foreach (AIContent content in message.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        if (!string.IsNullOrEmpty(text.Text))
                        {
                            parts.Add(text.Text);
                        }

                        break;
                    case TextReasoningContent:
                        break; // reasoning in history is dropped
                    default:
                        throw new NotSupportedException(
                            $"Message content of type {content.GetType().Name} is not supported: only text is accepted.");
                }
            }

            if (message.Role == ChatRole.System)
            {
                if (parts.Count > 0)
                {
                    systemTexts.Add(string.Join("\n", parts));
                    anyText = true;
                }
            }
            else if (message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            {
                if (parts.Count > 0)
                {
                    turns.Add(new PromptTurn(
                        message.Role == ChatRole.User ? PromptRole.User : PromptRole.Assistant,
                        string.Join("\n", parts)));
                    anyText = true;
                }
            }
            else
            {
                throw new ArgumentException($"Chat role '{message.Role}' is not supported: use system, user or assistant.", nameof(messages));
            }
        }

        if (!anyText)
        {
            throw new ArgumentException("The messages contain no text.", nameof(messages));
        }

        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            systemTexts.Add(options.Instructions);
        }

        ResponseKind kind = ResponseKind.Text;
        string? schemaText = null;
        string? schemaName = null;
        string? schemaDescription = null;

        if (options?.ResponseFormat is ChatResponseFormatJson json)
        {
            if (json.Schema is null)
            {
                kind = ResponseKind.Json;
            }
            else
            {
                JsonElement schema = json.Schema.Value;
                if (!IsObjectSchema(schema))
                {
                    throw new ArgumentException(
                        "The response schema must be a JSON object schema with a top-level \"type\": \"object\".",
                        nameof(options));
                }

                kind = ResponseKind.JsonSchema;
                schemaText = schema.GetRawText();
                schemaName = json.SchemaName;
                schemaDescription = json.SchemaDescription;
            }
        }

        return new ValidatedRequest(systemTexts, turns, kind, schemaText, schemaName, schemaDescription);
    }

    private static bool IsObjectSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in schema.EnumerateObject())
        {
            if (property.NameEquals("type"u8))
            {
                return property.Value.ValueKind == JsonValueKind.String && property.Value.ValueEquals("object"u8);
            }
        }

        return false;
    }
}
