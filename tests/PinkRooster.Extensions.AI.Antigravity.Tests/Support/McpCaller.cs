using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PinkRooster.Extensions.AI.Antigravity.Tests.Support;

/// <summary>Plays the part of agy toward a tool bridge: reads the home's MCP config and makes the calls agy was observed to make.</summary>
internal sealed class McpCaller : IDisposable
{
    private readonly HttpClient _http = new();
    private int _id;

    public McpCaller(string url, string token)
    {
        Url = url;
        Token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public string Url { get; }

    public string Token { get; }

    /// <summary>Reads <c>serverUrl</c> and the bearer token that a request wrote into its agy home.</summary>
    public static McpCaller FromHome(string home)
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, ".gemini", "config", "mcp_config.json")));
        JsonElement server = config.RootElement.GetProperty("mcpServers").GetProperty("agy-tools");
        string url = server.GetProperty("serverUrl").GetString()!;
        string header = server.GetProperty("headers").GetProperty("Authorization").GetString()!;
        return new McpCaller(url, header["Bearer ".Length..]);
    }

    public void Dispose() => _http.Dispose();

    public Task<JsonDocument> InitializeAsync(CancellationToken cancellationToken = default) =>
        PostAsync("initialize", "{\"protocolVersion\":\"2025-11-25\"}", cancellationToken);

    public Task<JsonDocument> ListToolsAsync(CancellationToken cancellationToken = default) => PostAsync("tools/list", "{}", cancellationToken);

    public Task<JsonDocument> CallAsync(string tool, string argumentsJson, CancellationToken cancellationToken = default) =>
        PostAsync("tools/call", "{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}", cancellationToken);

    /// <summary>The text of the first content block of a tools/call result, and whether it is marked as an error.</summary>
    public static (string Text, bool IsError) ToolResult(JsonDocument reply)
    {
        JsonElement result = reply.RootElement.GetProperty("result");
        return (result.GetProperty("content")[0].GetProperty("text").GetString()!, result.GetProperty("isError").GetBoolean());
    }

    public async Task<JsonDocument> PostAsync(string method, string paramsJson, CancellationToken cancellationToken = default)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":" + Interlocked.Increment(ref _id) + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(Url, content, cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public HttpClient Http => _http;
}
