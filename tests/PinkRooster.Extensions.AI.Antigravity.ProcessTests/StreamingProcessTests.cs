using System.Diagnostics;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>Streaming runs over the real process runner and the fake agy.</summary>
public sealed class StreamingProcessTests : IDisposable
{
    private const string Init = """{"event":"init","conversation_id":"c-1","init":{"model":"m"}}""";
    private const string Delta = """{"event":"step_update","step_update":{"conversation_id":"c-1","step_index":1,"state":"ACTIVE","step_type":"agent_response","text_delta":"hello"}}""";
    private const string Result = """{"event":"result","result":{"conversation_id":"c-1","status":"SUCCESS","response":"hello\n","usage":{"total_tokens":3}}}""";

    private readonly ScratchDirectory _scratch = new();
    private readonly AgyProcessRunner _runner = new();

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public async Task Lines_reach_the_caller_while_the_process_is_still_running()
    {
        var env = new Dictionary<string, string?>
        {
            ["FAKE_AGY_LINES"] = $"{Init}\n{Delta}\n{Result}",
            ["FAKE_AGY_LINE_DELAY_MS"] = "700",
        };
        var arrivals = new List<(string Line, TimeSpan At)>();
        var watch = Stopwatch.StartNew();

        AgyProcessResult result = await _runner.RunStreamingAsync(
            FakeAgyHost.Request(_scratch.Path, env),
            line =>
            {
                lock (arrivals)
                {
                    arrivals.Add((line, watch.Elapsed));
                }
            },
            CancellationToken.None);
        TimeSpan finished = watch.Elapsed;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal([Init, Delta, Result], arrivals.Select(a => a.Line));
        Assert.True(finished - arrivals[0].At > TimeSpan.FromMilliseconds(1000), $"first line at {arrivals[0].At}, finished at {finished}");
    }

    [Fact]
    public async Task Output_over_the_limit_is_reported_and_not_kept()
    {
        long bytes = 0;
        var env = new Dictionary<string, string?> { ["FAKE_AGY_STDOUT_MB"] = "4" };

        AgyProcessResult result = await _runner.RunStreamingAsync(
            FakeAgyHost.Request(_scratch.Path, env, maxOutputBytes: 1024 * 1024),
            line => Interlocked.Add(ref bytes, line.Length),
            CancellationToken.None);

        Assert.True(result.OutputLimitExceeded);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.True(bytes <= 1024 * 1024, $"{bytes} bytes were handed over");
    }

    [Fact]
    public async Task A_timeout_is_reported_and_the_lines_before_it_were_delivered()
    {
        var env = new Dictionary<string, string?> { ["FAKE_AGY_LINES"] = Init, ["FAKE_AGY_SLEEP_MS"] = "60000" };
        var lines = new List<string>();

        AgyProcessResult result = await _runner.RunStreamingAsync(
            FakeAgyHost.Request(_scratch.Path, env, timeout: TimeSpan.FromSeconds(2)),
            line => { lock (lines) { lines.Add(line); } },
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Equal([Init], lines);
    }

    [Fact]
    public async Task Breaking_out_of_the_enumeration_ends_agy_and_removes_the_request_directory()
    {
        string pidFile = Path.Combine(_scratch.Path, "child.pid");
        string root = Path.Combine(_scratch.Path, "requests");
        Directory.CreateDirectory(root);
        using var client = new AntigravityChatClient(new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            SkipVersionCheck = true,
            TemporaryDirectoryRoot = root,
            EnvironmentVariables =
            {
                ["FAKE_AGY_LINES"] = $"{Init}\n{Delta}",
                ["FAKE_AGY_CHILD_SLEEP_MS"] = "60000",
                ["FAKE_AGY_CHILD_PIDFILE"] = pidFile,
                ["FAKE_AGY_SLEEP_MS"] = "60000",
            },
        });

        var watch = Stopwatch.StartNew();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("hi"))
        {
            Assert.Equal("hello", update.Text);
            break;
        }

        // The fake agy would run for 60 s on its own: returning sooner means the library ended it.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"stopping took {watch.Elapsed}");
        int child = await FakeAgyHost.ReadChildPidAsync(pidFile, TimeSpan.FromSeconds(10));
        Assert.True(await FakeAgyHost.WaitUntilGoneAsync(child, TimeSpan.FromSeconds(10)), "the child process is still running");
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public async Task The_client_streams_text_before_the_fake_agy_exits()
    {
        using var client = new AntigravityChatClient(new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            SkipVersionCheck = true,
            EnvironmentVariables =
            {
                ["FAKE_AGY_LINES"] = $"{Init}\n{Delta}\n{Result}",
                ["FAKE_AGY_LINE_DELAY_MS"] = "700",
            },
        });
        var watch = Stopwatch.StartNew();
        TimeSpan? firstText = null;
        var updates = new List<ChatResponseUpdate>();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("hi"))
        {
            firstText ??= update.Contents.OfType<TextContent>().Any() ? watch.Elapsed : null;
            updates.Add(update);
        }

        Assert.NotNull(firstText);
        Assert.True(watch.Elapsed - firstText.Value > TimeSpan.FromMilliseconds(500), $"first text at {firstText}, done at {watch.Elapsed}");
        Assert.Equal("hello", updates.ToChatResponse().Text);
        Assert.Equal(3, updates.Last().Contents.OfType<UsageContent>().Single().Details.TotalTokenCount);
    }
}
