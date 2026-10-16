using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Checks a request before any process starts. Pure: no input or output, no state.</summary>
internal static class RequestValidator
{
    /// <summary>The most images one request may carry.</summary>
    public const int MaxAttachments = 10;

    /// <summary>The largest single image, in bytes.</summary>
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;

    /// <summary>The folder in the request directory that holds the images.</summary>
    public const string AttachmentFolder = "attachments";

    private static readonly Dictionary<string, string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/gif"] = "gif",
        ["image/webp"] = "webp",
    };

    public static ValidatedRequest Validate(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(messages);

        IReadOnlyList<AIFunction> tools = ValidateTools(options);

        var systemTexts = new List<string>();
        var turns = new List<PromptTurn>();
        var attachments = new List<Attachment>();
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
                    case DataContent data:
                        parts.Add(AddAttachment(attachments, data, message.Role));
                        break;
                    case UriContent:
                        throw new NotSupportedException(
                            "UriContent is not supported: the library does not fetch URLs. Pass the image bytes as DataContent.");
                    default:
                        throw new NotSupportedException(
                            $"Message content of type {content.GetType().Name} is not supported: only text and images (DataContent) are accepted.");
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

        if (tools.Count > 0 && kind != ResponseKind.Text)
        {
            throw new NotSupportedException(
                "Tools cannot be combined with a JSON response format: agy applies a schema to its final result only, so this combination is not supported.");
        }

        return new ValidatedRequest(systemTexts, turns, kind, schemaText, schemaName, schemaDescription, tools, attachments);
    }

    // ApprovalRequiredAIFunction is experimental (MEAI001) in Microsoft.Extensions.AI.Abstractions; it is only type-tested here, to refuse it.
#pragma warning disable MEAI001
    // Tools reach agy only through the bridge, which runs invocable functions. Everything the bridge cannot honour is refused
    // before any process starts, because dropping it silently would give wrong answers.
    private static IReadOnlyList<AIFunction> ValidateTools(ChatOptions? options)
    {
        if (options is null)
        {
            return [];
        }

        ChatToolMode? mode = options.ToolMode;
        if (mode is not null && mode is not AutoChatToolMode && !mode.Equals(ChatToolMode.None))
        {
            throw new NotSupportedException(
                "ChatOptions.ToolMode RequireAny and RequireSpecific are not supported: agy decides itself whether to call a tool.");
        }

        if (options.Tools is not { Count: > 0 } || mode is not null && mode.Equals(ChatToolMode.None))
        {
            return []; // no tools, or the caller asked for none: the request runs as a plain request
        }

        var functions = new List<AIFunction>(options.Tools.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (AITool tool in options.Tools)
        {
            if (tool is ApprovalRequiredAIFunction)
            {
                throw new NotSupportedException("Tools that require approval are not supported: agy runs the tool loop, so the library cannot ask the caller first.");
            }

            if (tool is not AIFunction function)
            {
                throw new NotSupportedException(
                    $"A tool of type {tool.GetType().Name} is not supported: only invocable AIFunction tools can be offered to agy.");
            }

            if (!names.Add(function.Name))
            {
                throw new ArgumentException($"Two tools are named '{function.Name}': tool names must be unique.", nameof(options));
            }

            functions.Add(function);
        }

        return functions;
    }
#pragma warning restore MEAI001

    // The image becomes a file the library names itself, and a pointer in the turn text at the image's position.
    private static string AddAttachment(List<Attachment> attachments, DataContent data, ChatRole role)
    {
        if (role != ChatRole.User)
        {
            throw new NotSupportedException("Images are only accepted in user messages.");
        }

        if (!ImageExtensions.TryGetValue(data.MediaType ?? string.Empty, out string? extension))
        {
            throw new NotSupportedException(
                $"DataContent of media type '{data.MediaType}' is not supported: only image/png, image/jpeg, image/gif and image/webp are accepted.");
        }

        if (data.Data.IsEmpty)
        {
            throw new ArgumentException("An image attachment has no data.", "messages");
        }

        if (data.Data.Length > MaxAttachmentBytes)
        {
            throw new ArgumentException($"An image is {data.Data.Length} bytes; the limit is {MaxAttachmentBytes} bytes.", "messages");
        }

        if (attachments.Count >= MaxAttachments)
        {
            throw new ArgumentException($"A request may carry at most {MaxAttachments} images.", "messages");
        }

        string path = $"{AttachmentFolder}/image-{attachments.Count + 1}.{extension}";
        attachments.Add(new Attachment(path, data.Data));
        return $"[attachment: {path}]";
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
