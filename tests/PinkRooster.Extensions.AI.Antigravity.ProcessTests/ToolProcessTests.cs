using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>Tool requests through the real process runner, with the fake agy acting as an MCP client over HTTP.</summary>
public sealed class ToolProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agy-tool-e2e-" + Guid.NewGuid().ToString("N"));
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
        return "ZEBRA-" + person;
    }, "lookup", "Looks a person up.");

    private AntigravityChatClient Client(string calls, Action<AntigravityChatClientOptions>? configure = null, Dictionary<string, string?>? more = null)
    {
        var options = new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            SkipVersionCheck = true,
            TemporaryDirectoryRoot = _root,
            IsolateAgyHome = true,
            EnvironmentVariables = { ["FAKE_AGY_MCP_CALLS"] = calls },
        };
        if (more is not null)
        {
            foreach (KeyValuePair<string, string?> pair in more)
            {
                options.EnvironmentVariables[pair.Key] = pair.Value;
            }
        }

        configure?.Invoke(options);
        return new AntigravityChatClient(options);
    }

    [Fact]
    public async Task A_tool_body_runs_when_the_fake_agy_calls_it_and_the_result_reaches_the_answer()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        using AntigravityChatClient client = Client("lookup|{\"person\":\"Ada\"}");

        ChatResponse response = await client.GetResponseAsync("who?", new ChatOptions { Tools = [Lookup()] });

        Assert.Equal("tools: ZEBRA-Ada", response.Text);
        Assert.Equal(1, _runs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task A_real_function_invoking_chat_client_does_not_run_the_tool_a_second_time()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        using AntigravityChatClient inner = Client("lookup|{\"person\":\"Ada\"}");
        using IChatClient client = new ChatClientBuilder(inner).UseFunctionInvocation().Build();

        ChatResponse response = await client.GetResponseAsync("who?", new ChatOptions { Tools = [Lookup()] });

        Assert.Equal(1, _runs);
        Assert.DoesNotContain(response.Messages.SelectMany(m => m.Contents), c => c is FunctionCallContent or FunctionResultContent);
    }

    [Fact]
    public async Task Calls_beyond_the_cap_are_refused_by_the_bridge()
    {
        if (!InvocationBuilder.IsolationApplies) { return; }

        using AntigravityChatClient client = Client("lookup|{\"person\":\"A\"}\nlookup|{\"person\":\"B\"}", o => o.MaxToolCalls = 1);

        ChatResponse response = await client.GetResponseAsync("who?", new ChatOptions { Tools = [Lookup()] });

        Assert.Contains("ZEBRA-A", response.Text);
        Assert.Contains("limit", response.Text);
        Assert.Equal(1, _runs);
    }

    [Fact]
    public async Task Cancelling_ends_the_fake_agy_and_the_tool_body_and_leaves_nothing_behind()
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
        using AntigravityChatClient client = Client("slow|{}");
        using var cts = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();

        Task<ChatResponse> request = client.GetResponseAsync("go", new ChatOptions { Tools = [slow] }, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }
}
