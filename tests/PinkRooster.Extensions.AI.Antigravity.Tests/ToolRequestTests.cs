using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>Requests with tools, end to end through the client with a runner that plays agy toward the bridge.</summary>
public sealed class ToolRequestTests : IDisposable
{
    private const string SecretArgument = "SECRET-ARGUMENT";
    private const string SecretResult = "SECRET-RESULT";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "agy-tool-tests-" + Guid.NewGuid().ToString("N"));
    private int _runs;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AIFunction Lookup() => AIFunctionFactory.Create((string person) =>
    {
        Interlocked.Increment(ref _runs);
        return SecretResult + " " + person;
    }, "lookup", "Looks a person up.");

    private static string Envelope(string response) => JsonSerializer.Serialize(new { status = "SUCCESS", response, conversation_id = "c-1" });

    // A runner that does what agy does with a bridge: read the config from the home, then call the tools.
    private static Func<AgyProcessRequest, AgyProcessResult> Agy(Action<McpCaller> act, Action<AgyProcessRequest>? inspect = null) =>
        run =>
        {
            inspect?.Invoke(run);
            using McpCaller caller = McpCaller.FromHome(run.EnvironmentOverrides["HOME"]!);
            act(caller);
            return FakeProcessRunner.Ok(Envelope("done"));
        };

    private AntigravityChatClient Client(FakeProcessRunner runner, Action<AntigravityChatClientOptions>? configure = null, ILogger? logger = null) =>
        Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; configure?.Invoke(o); }, logger);

    private static ChatOptions Tools(params AIFunction[] tools) => new() { Tools = [.. tools] };

    [Fact]
    public async Task A_tool_request_writes_the_bridge_files_and_the_tool_runs_when_agy_calls_it()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        string? config = null, settings = null, agents = null, prompt = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Agy(
            caller =>
            {
                caller.InitializeAsync().GetAwaiter().GetResult().Dispose();
                using JsonDocument call = caller.CallAsync("lookup", "{\"person\":\"Ada\"}").GetAwaiter().GetResult();
                Assert.Equal(SecretResult + " Ada", McpCaller.ToolResult(call).Text);
            },
            run =>
            {
                string home = run.EnvironmentOverrides["HOME"]!;
                config = File.ReadAllText(Path.Combine(home, AgyHomeFiles.McpConfigRelativePath));
                settings = File.ReadAllText(Path.Combine(home, AgyHomeFiles.SettingsRelativePath));
                agents = File.ReadAllText(Path.Combine(run.WorkingDirectory, "AGENTS.md"));
                prompt = run.Arguments[^1];
            }));
        using AntigravityChatClient client = Client(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User("who is Ada?"), Tools(Lookup()));

        Assert.Equal("done", response.Text);
        Assert.Equal(1, _runs);
        using (JsonDocument c = JsonDocument.Parse(config!))
        {
            JsonElement server = c.RootElement.GetProperty("mcpServers").GetProperty("agy-tools");
            Assert.StartsWith("http://127.0.0.1:", server.GetProperty("serverUrl").GetString());
            Assert.StartsWith("Bearer ", server.GetProperty("headers").GetProperty("Authorization").GetString());
        }

        using (JsonDocument s = JsonDocument.Parse(settings!))
        {
            Assert.Equal(["mcp(agy-tools/*)"], s.RootElement.GetProperty("permissions").GetProperty("allow").EnumerateArray().Select(e => e.GetString()));
        }

        Assert.Contains("agy-tools", agents);
        Assert.DoesNotContain("Do not use any tools", agents);
        Assert.DoesNotContain("agy-tools", prompt);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task A_caller_supplied_preamble_is_kept_for_tool_requests()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        string? agents = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Agy(_ => { }, run =>
            agents = File.ReadAllText(Path.Combine(run.WorkingDirectory, "AGENTS.md"))));
        using AntigravityChatClient client = Client(runner, o => o.SystemPreamble = "MY-OWN-PREAMBLE");

        await client.GetResponseAsync(Clients.User(), Tools(Lookup()));

        Assert.Contains("MY-OWN-PREAMBLE", agents);
    }

    [Fact]
    public async Task A_real_function_invoking_chat_client_over_it_runs_the_tool_once_and_sees_no_call()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Agy(caller =>
        {
            using JsonDocument call = caller.CallAsync("lookup", "{\"person\":\"Ada\"}").GetAwaiter().GetResult();
        }));
        using AntigravityChatClient inner = Client(runner);
        using IChatClient client = new ChatClientBuilder(inner).UseFunctionInvocation().Build();

        ChatResponse response = await client.GetResponseAsync(Clients.User(), Tools(Lookup()));

        Assert.Equal(1, _runs); // run by the bridge; the wrapper never invoked it
        Assert.Equal(2, runner.Requests.Count); // the version check and one agy run: the wrapper did not call again
        Assert.DoesNotContain(response.Messages.SelectMany(m => m.Contents), c => c is FunctionCallContent or FunctionResultContent);
    }

    [Fact]
    public async Task Tools_are_refused_when_the_home_is_not_isolated_and_no_process_starts()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner, o => o.IsolateAgyHome = false);

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(Clients.User(), Tools(Lookup())));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Tool_mode_none_runs_a_plain_request_without_a_bridge()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        bool configExists = true;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(run =>
        {
            configExists = File.Exists(Path.Combine(run.EnvironmentOverrides["HOME"]!, AgyHomeFiles.McpConfigRelativePath));
            return FakeProcessRunner.Ok(Envelope("plain"));
        });
        using AntigravityChatClient client = Client(runner);
        var options = Tools(Lookup());
        options.ToolMode = ChatToolMode.None;

        ChatResponse response = await client.GetResponseAsync(Clients.User(), options);

        Assert.Equal("plain", response.Text);
        Assert.False(configExists);
    }

    [Fact]
    public async Task The_cap_comes_from_the_options()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        var errors = new List<bool>();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Agy(caller =>
        {
            for (int i = 0; i < 3; i++)
            {
                using JsonDocument call = caller.CallAsync("lookup", "{\"person\":\"Ada\"}").GetAwaiter().GetResult();
                errors.Add(McpCaller.ToolResult(call).IsError);
            }
        }));
        using AntigravityChatClient client = Client(runner, o => o.MaxToolCalls = 2);

        await client.GetResponseAsync(Clients.User(), Tools(Lookup()));

        Assert.Equal([false, false, true], errors);
        Assert.Equal(2, _runs);
    }

    [Fact]
    public async Task After_the_request_the_bridge_port_is_closed_and_the_home_is_gone()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        string? url = null, home = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Agy(caller => url = caller.Url, run => home = run.EnvironmentOverrides["HOME"]));
        using AntigravityChatClient client = Client(runner);

        await client.GetResponseAsync(Clients.User(), Tools(Lookup()));

        Assert.NotNull(url);
        Assert.False(Directory.Exists(home));
        await AssertPortClosedAsync(url!);
    }

    // The listener is gone: a new connection is refused (not accepted and left unanswered).
    private static async Task AssertPortClosedAsync(string url)
    {
        var uri = new Uri(url);
        await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(async () =>
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(System.Net.IPAddress.Loopback, uri.Port);
        });
    }

    [Fact]
    public async Task A_failing_run_still_stops_the_bridge()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        string? url = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(run =>
        {
            using McpCaller caller = McpCaller.FromHome(run.EnvironmentOverrides["HOME"]!);
            url = caller.Url;
            return Fixture.Result("exit1-empty-prompt.json", exitCode: 1);
        });
        using AntigravityChatClient client = Client(runner);

        await Assert.ThrowsAsync<AntigravityProcessException>(() => client.GetResponseAsync(Clients.User(), Tools(Lookup())));

        await AssertPortClosedAsync(url!);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task Tool_content_never_reaches_the_log_but_the_tool_name_does()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        var logger = new CapturingLogger();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Agy(caller =>
        {
            using JsonDocument call = caller.CallAsync("lookup", "{\"person\":\"" + SecretArgument + "\"}").GetAwaiter().GetResult();
        }));
        using AntigravityChatClient client = Client(runner, logger: logger);

        await client.GetResponseAsync(Clients.User(), Tools(Lookup()));

        string everything = logger.Everything();
        Assert.Contains("lookup", everything);
        Assert.DoesNotContain(SecretArgument, everything);
        Assert.DoesNotContain(SecretResult, everything);
    }

    [Fact]
    public async Task Streaming_with_tools_yields_text_and_no_call_content_even_when_agy_reports_tool_steps()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        string stream =
            "{\"event\":\"init\",\"conversation_id\":\"c-1\",\"init\":{\"model\":\"m\"}}\n" +
            "{\"event\":\"step_update\",\"step_update\":{\"step_index\":2,\"state\":\"ACTIVE\",\"step_type\":\"tool\",\"tool_name\":\"call_mcp_tool\",\"tool_info\":{\"name\":\"call_mcp_tool\",\"parameters\":{\"ServerName\":\"agy-tools\",\"ToolName\":\"lookup\"}}}}\n" +
            "{\"event\":\"step_update\",\"step_update\":{\"step_index\":2,\"state\":\"DONE\",\"step_type\":\"tool\",\"tool_name\":\"call_mcp_tool\",\"tool_info\":{\"name\":\"call_mcp_tool\",\"output\":\"" + SecretResult + "\"}}}\n" +
            "{\"event\":\"step_update\",\"step_update\":{\"step_index\":3,\"state\":\"ACTIVE\",\"step_type\":\"agent_response\",\"text_delta\":\"The answer\"}}\n" +
            "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"The answer\",\"conversation_id\":\"c-1\"}}\n";
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(run =>
        {
            using McpCaller caller = McpCaller.FromHome(run.EnvironmentOverrides["HOME"]!);
            using JsonDocument call = caller.CallAsync("lookup", "{\"person\":\"Ada\"}").GetAwaiter().GetResult();
            return FakeProcessRunner.Ok(stream);
        });
        using AntigravityChatClient client = Client(runner);
        var updates = new List<ChatResponseUpdate>();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(Clients.User(), Tools(Lookup())))
        {
            updates.Add(update);
        }

        Assert.Equal("The answer", updates.ToChatResponse().Text);
        Assert.DoesNotContain(updates.SelectMany(u => u.Contents), c => c is FunctionCallContent or FunctionResultContent);
        Assert.Equal(1, _runs);
    }

    [Fact]
    public async Task Cancelling_the_request_cancels_the_running_tool_body_and_removes_everything()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        AIFunction slow = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            return "never";
        }, "slow");
        var agy = new CallingRunner();
        using AntigravityChatClient client = new(
            new AntigravityChatClientOptions { TemporaryDirectoryRoot = _root, IsolateAgyHome = true, SkipVersionCheck = true },
            null,
            agy,
            _ => Clients.FakeAgyPath);
        using var cts = new CancellationTokenSource();

        Task<ChatResponse> request = client.GetResponseAsync(Clients.User(), Tools(slow), cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    // An agy that calls the first tool it is offered and then waits for the request to be cancelled.
    private sealed class CallingRunner : IAgyProcessRunner
    {
        public async Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken)
        {
            using McpCaller caller = McpCaller.FromHome(request.EnvironmentOverrides["HOME"]!);
            using JsonDocument call = await caller.CallAsync("slow", "{}", cancellationToken);
            return FakeProcessRunner.Ok(Envelope("done"));
        }

        public Task<AgyProcessResult> RunStreamingAsync(AgyProcessRequest request, Action<string> onStandardOutputLine, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
