using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>The loopback tool endpoint, driven over real sockets with a standard HTTP client.</summary>
public sealed class ToolBridgeTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<(string Name, bool Failed)> _calls = [];
    private int _runs;
    private ToolBridge? _bridge;

    public async ValueTask DisposeAsync()
    {
        if (_bridge is not null)
        {
            await _bridge.DisposeAsync();
        }

        _stop.Dispose();
    }

    private ToolBridge Start(int maxCalls = 20, params AIFunction[] tools)
    {
        AIFunction[] all = tools.Length > 0 ? tools : [Code()];
        var server = new McpToolServer(all, maxCalls, SchemaValidator.Instance, (name, _, failed) => { lock (_calls) { _calls.Add((name, failed)); } }, _stop.Token);
        _bridge = ToolBridge.Start(server, _stop);
        return _bridge;
    }

    private AIFunction Code() => AIFunctionFactory.Create((string person) =>
    {
        Interlocked.Increment(ref _runs);
        return "ZEBRA for " + person;
    }, "get_code", "Returns the secret code of a person.");

    private static string Fail(string person) => throw new InvalidOperationException("SECRET-" + person);

    private McpCaller Caller(ToolBridge bridge) => new(bridge.Url, bridge.Token);

    [Fact]
    public async Task Initialize_list_and_call_work_with_the_token()
    {
        using McpCaller caller = Caller(Start());

        using JsonDocument init = await caller.InitializeAsync();
        using JsonDocument list = await caller.ListToolsAsync();
        using JsonDocument call = await caller.CallAsync("get_code", "{\"person\":\"Ada\"}");

        Assert.Equal("2025-11-25", init.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        JsonElement tool = list.RootElement.GetProperty("result").GetProperty("tools")[0];
        Assert.Equal("get_code", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.Equal(("ZEBRA for Ada", false), McpCaller.ToolResult(call));
        Assert.Equal(1, _runs);
        Assert.Equal([("get_code", false)], _calls);
    }

    [Fact]
    public async Task A_wrong_or_missing_token_never_reaches_the_tool_body()
    {
        ToolBridge bridge = Start();
        using var http = new HttpClient();
        string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"get_code\",\"arguments\":{\"person\":\"Ada\"}}}";

        foreach (string? header in new string?[] { null, "Bearer wrong", "Bearer ", bridge.Token, "bearer " + bridge.Token })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, bridge.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (header is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", header);
            }

            using HttpResponseMessage response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(0, _runs);
    }

    [Fact]
    public async Task A_foreign_origin_or_host_is_rejected_and_the_tool_body_does_not_run()
    {
        ToolBridge bridge = Start();
        using var http = new HttpClient();
        string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"get_code\",\"arguments\":{\"person\":\"Ada\"}}}";

        using var foreignOrigin = new HttpRequestMessage(HttpMethod.Post, bridge.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreignOrigin.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bridge.Token);
        foreignOrigin.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        using var foreignHost = new HttpRequestMessage(HttpMethod.Post, bridge.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreignHost.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bridge.Token);
        foreignHost.Headers.Host = "evil.example";

        using HttpResponseMessage a = await http.SendAsync(foreignOrigin);
        using HttpResponseMessage b = await http.SendAsync(foreignHost);

        Assert.Equal(HttpStatusCode.Forbidden, a.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, b.StatusCode);
        Assert.Equal(0, _runs);
    }

    [Fact]
    public async Task A_loopback_origin_is_accepted()
    {
        ToolBridge bridge = Start();
        using McpCaller caller = Caller(bridge);
        caller.Http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", $"http://127.0.0.1:{bridge.Port}");

        using JsonDocument call = await caller.CallAsync("get_code", "{\"person\":\"Ada\"}");

        Assert.False(McpCaller.ToolResult(call).IsError);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"person\":5}")]
    public async Task Arguments_that_do_not_match_the_schema_never_reach_the_tool_body(string arguments)
    {
        using McpCaller caller = Caller(Start());

        using JsonDocument call = await caller.CallAsync("get_code", arguments);

        (string text, bool isError) = McpCaller.ToolResult(call);
        Assert.True(isError);
        Assert.DoesNotContain("Ada", text);
        Assert.Equal(0, _runs);
    }

    // A function whose schema is stricter than anything the argument binding checks, to see the bridge's own validation.
    private sealed class CappedFunction(Action onRun) : AIFunction
    {
        public override string Name => "capped";

        public override JsonElement JsonSchema { get; } = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{\"n\":{\"type\":\"integer\",\"maximum\":5}},\"required\":[\"n\"]}").RootElement.Clone();

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            onRun();
            return new ValueTask<object?>("ran");
        }
    }

    [Fact]
    public async Task A_schema_violation_that_the_argument_binding_would_accept_never_reaches_the_tool_body()
    {
        using McpCaller caller = Caller(Start(20, new CappedFunction(() => Interlocked.Increment(ref _runs))));

        using JsonDocument tooBig = await caller.CallAsync("capped", "{\"n\":99}");
        using JsonDocument fine = await caller.CallAsync("capped", "{\"n\":3}");

        Assert.True(McpCaller.ToolResult(tooBig).IsError);
        Assert.False(McpCaller.ToolResult(fine).IsError);
        Assert.Equal(1, _runs);
    }

    [Fact]
    public async Task An_unknown_tool_gets_an_error_result_and_the_bridge_goes_on()
    {
        using McpCaller caller = Caller(Start());

        using JsonDocument unknown = await caller.CallAsync("nope", "{}");
        using JsonDocument known = await caller.CallAsync("get_code", "{\"person\":\"Ada\"}");

        Assert.True(McpCaller.ToolResult(unknown).IsError);
        Assert.False(McpCaller.ToolResult(known).IsError);
    }

    [Fact]
    public async Task Calls_beyond_the_cap_get_an_error_result_without_running_the_tool()
    {
        using McpCaller caller = Caller(Start(maxCalls: 2));

        var results = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            using JsonDocument call = await caller.CallAsync("get_code", "{\"person\":\"Ada\"}");
            results.Add(McpCaller.ToolResult(call).IsError);
        }

        Assert.Equal([false, false, true, true], results);
        Assert.Equal(2, _runs);
    }

    [Fact]
    public async Task A_throwing_tool_gives_a_generic_error_that_never_carries_the_exception_text()
    {
        AIFunction throwing = AIFunctionFactory.Create((string person) => Fail(person), "boom");
        using McpCaller caller = Caller(Start(20, throwing));

        using JsonDocument call = await caller.CallAsync("boom", "{\"person\":\"Ada\"}");

        (string text, bool isError) = McpCaller.ToolResult(call);
        Assert.True(isError);
        Assert.DoesNotContain("SECRET", text);
        Assert.Equal([("boom", true)], _calls);
    }

    [Fact]
    public async Task Ending_the_bridge_cancels_a_running_tool_body()
    {
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        AIFunction slow = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }

            return "never";
        }, "slow");
        ToolBridge bridge = Start(20, slow);
        using McpCaller caller = Caller(bridge);

        Task<JsonDocument> pending = caller.CallAsync("slow", "{}");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await bridge.DisposeAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(() => pending);
    }

    [Fact]
    public async Task Calls_may_run_at_the_same_time()
    {
        using McpCaller caller = Caller(Start());

        JsonDocument[] replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => caller.CallAsync("get_code", "{\"person\":\"p" + i + "\"}")));

        Assert.All(replies, r => Assert.False(McpCaller.ToolResult(r).IsError));
        Assert.Equal(8, _runs);
        foreach (JsonDocument reply in replies)
        {
            reply.Dispose();
        }
    }

    [Fact]
    public async Task Agy_probes_get_the_answers_agy_was_observed_to_accept()
    {
        ToolBridge bridge = Start();
        using McpCaller caller = Caller(bridge);

        using JsonDocument discover = await caller.PostAsync("server/discover", "{}");
        using HttpResponseMessage get = await caller.Http.GetAsync(bridge.Url);
        using var plain = new HttpClient();
        using HttpResponseMessage wellKnown = await plain.GetAsync($"http://127.0.0.1:{bridge.Port}/.well-known/oauth-protected-resource/mcp");
        using var notification = new HttpRequestMessage(HttpMethod.Post, bridge.Url)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}", Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage accepted = await caller.Http.SendAsync(notification);

        Assert.Equal(-32601, discover.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wellKnown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [Fact]
    public async Task Two_requests_on_one_connection_and_a_chunked_body_are_served()
    {
        ToolBridge bridge = Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, bridge.Port);
        NetworkStream stream = tcp.GetStream();
        string json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}";
        string head = $"Host: 127.0.0.1:{bridge.Port}\r\nAuthorization: Bearer {bridge.Token}\r\nContent-Type: application/json\r\n";
        string plain = $"POST /mcp HTTP/1.1\r\n{head}Content-Length: {json.Length}\r\n\r\n{json}";
        string chunked = $"POST /mcp HTTP/1.1\r\n{head}Transfer-Encoding: chunked\r\n\r\n{json.Length:x}\r\n{json}\r\n0\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(plain + chunked));
        string reply = await ReadAsync(stream, expectedResponses: 2);

        Assert.Equal(2, reply.Split("HTTP/1.1 200 OK").Length - 1);
        Assert.Equal(2, reply.Split("get_code").Length - 1);
    }

    private static async Task<string> ReadAsync(NetworkStream stream, int expectedResponses)
    {
        var sb = new StringBuilder();
        byte[] buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (sb.ToString().Split("HTTP/1.1 ").Length - 1 < expectedResponses || !sb.ToString().TrimEnd().EndsWith('}'))
        {
            int read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                break;
            }

            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (sb.ToString().Split("get_code").Length - 1 >= expectedResponses)
            {
                break;
            }
        }

        return sb.ToString();
    }

    [Fact]
    public async Task After_it_ends_the_port_is_closed_and_each_bridge_has_its_own_port_and_token()
    {
        ToolBridge first = Start();
        int port = first.Port;
        string token = first.Token;
        await first.DisposeAsync();
        _bridge = null;

        await Assert.ThrowsAsync<SocketException>(async () =>
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
        });

        using var otherStop = new CancellationTokenSource();
        var server = new McpToolServer([Code()], 20, SchemaValidator.Instance, null, otherStop.Token);
        await using ToolBridge second = ToolBridge.Start(server, otherStop);
        Assert.NotEqual(token, second.Token);
        Assert.Equal(64, second.Token.Length);
    }
}
