using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>One answer of the bridge: an HTTP status and, except for a notification, a JSON-RPC body.</summary>
internal sealed record McpReply(int HttpStatus, string? Body);

/// <summary>
/// The MCP side of the bridge: the part of the protocol agy was observed to use (<c>docs/tool-calling-mcp-bridge/research.md</c>,
/// "HTTP bridge run"), over the caller's functions. Knows nothing about sockets. Content (arguments, results, exception text)
/// never reaches an error message or the call callback.
/// </summary>
internal sealed class McpToolServer
{
    public const string ServerName = "agy-tools";
    public const string DefaultProtocolVersion = "2025-11-25";

    private static readonly string[] KnownProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    private readonly Dictionary<string, Tool> _tools;
    private readonly IReadOnlyList<AIFunction> _order;
    private readonly int _maxCalls;
    private readonly Action<string, long, bool>? _onCall;
    private readonly CancellationToken _stop;
    private int _calls;

    private sealed record Tool(AIFunction Function, ICompiledSchema Schema);

    /// <param name="tools">The caller's functions; names must be unique and each schema usable (else <see cref="ArgumentException"/>).</param>
    /// <param name="maxCalls">The most calls run; further calls get an error result.</param>
    /// <param name="validator">Prepares the argument schemas.</param>
    /// <param name="onCall">Called after each run call with the tool name, the milliseconds it took and whether it failed.</param>
    /// <param name="stop">Cancelled when the request ends; passed to every tool body.</param>
    public McpToolServer(
        IReadOnlyList<AIFunction> tools,
        int maxCalls,
        ISchemaValidator validator,
        Action<string, long, bool>? onCall,
        CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(validator);
        _order = tools;
        _maxCalls = maxCalls;
        _onCall = onCall;
        _stop = stop;
        _tools = new Dictionary<string, Tool>(StringComparer.Ordinal);
        foreach (AIFunction function in tools)
        {
            _tools[function.Name] = new Tool(function, validator.Compile(function.JsonSchema.GetRawText()));
        }
    }

    public async Task<McpReply> HandleAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("method", out JsonElement methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
        {
            return Error(null, 400, -32600, "Invalid request.");
        }

        string method = methodElement.GetString()!;
        bool hasId = message.TryGetProperty("id", out JsonElement id) && id.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        if (!hasId)
        {
            return new McpReply(202, null); // a notification
        }

        switch (method)
        {
            case "server/discover":
                // agy tries this newer form first and falls back to initialize on a 404 (research.md, "HTTP bridge run").
                return Error(id, 404, -32601, "Method not found.");
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = NegotiatedVersion(message),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0.0" },
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = ToolList() });
            case "tools/call":
                return await CallAsync(id, message, cancellationToken).ConfigureAwait(false);
            default:
                return Error(id, 200, -32601, "Method not found.");
        }
    }

    private static string NegotiatedVersion(JsonElement message)
    {
        if (message.TryGetProperty("params", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out JsonElement version) && version.ValueKind == JsonValueKind.String)
        {
            string asked = version.GetString()!;
            if (Array.IndexOf(KnownProtocolVersions, asked) >= 0)
            {
                return asked;
            }
        }

        return DefaultProtocolVersion;
    }

    private JsonArray ToolList()
    {
        JsonArray list = new JsonArray();
        foreach (AIFunction function in _order)
        {
            list.Add(new JsonObject
            {
                ["name"] = function.Name,
                ["description"] = function.Description,
                ["inputSchema"] = JsonNode.Parse(function.JsonSchema.GetRawText()),
            });
        }

        return list;
    }

    private async Task<McpReply> CallAsync(JsonElement id, JsonElement message, CancellationToken cancellationToken)
    {
        JsonElement parameters = message.TryGetProperty("params", out JsonElement p) && p.ValueKind == JsonValueKind.Object ? p : default;
        string? name = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out JsonElement n)
            && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

        if (name is null || !_tools.TryGetValue(name, out Tool? tool))
        {
            return ToolError(id, "Unknown tool.");
        }

        if (Interlocked.Increment(ref _calls) > _maxCalls)
        {
            return ToolError(id, "The tool call limit for this request was reached.");
        }

        JsonElement arguments = parameters.TryGetProperty("arguments", out JsonElement a) && a.ValueKind == JsonValueKind.Object
            ? a
            : JsonDocument.Parse("{}").RootElement;
        if (!tool.Schema.IsValid(arguments))
        {
            return ToolError(id, "The arguments do not match the tool's schema.");
        }

        Dictionary<string, object?> dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in arguments.EnumerateObject())
        {
            dictionary[property.Name] = property.Value.Clone();
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_stop, cancellationToken);
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            object? result = await tool.Function.InvokeAsync(new AIFunctionArguments(dictionary), linked.Token).ConfigureAwait(false);
            _onCall?.Invoke(name, watch.ElapsedMilliseconds, false);
            return Result(id, new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ResultText(tool.Function, result) }),
                ["isError"] = false,
            });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            throw; // the request ended or the connection went away
        }
        catch (Exception)
        {
            // The exception text is content (it can echo arguments); agy only learns that the tool failed.
            _onCall?.Invoke(name, watch.ElapsedMilliseconds, true);
            return ToolError(id, "The tool failed.");
        }
    }

    private static string ResultText(AIFunction function, object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
        JsonElement json => json.GetRawText(),
        _ => JsonSerializer.Serialize(result, function.JsonSerializerOptions),
    };

    private static McpReply Result(JsonElement id, JsonNode result) =>
        new(200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["result"] = result }.ToJsonString());

    private static McpReply ToolError(JsonElement id, string text) =>
        Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = true,
        });

    public static McpReply Error(JsonElement? id, int httpStatus, int code, string message) =>
        new(httpStatus, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } value ? JsonNode.Parse(value.GetRawText()) : null,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString());
}
