using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>NFR-001: after a timeout, cancellation or output overflow no process started by the run is left.</summary>
public sealed class ProcessCleanupTests : IDisposable
{
    private readonly ScratchDirectory _scratch = new();
    private readonly AgyProcessRunner _runner = new();

    public void Dispose() => _scratch.Dispose();

    private Dictionary<string, string?> WithChild(string pidFile, Dictionary<string, string?>? more = null)
    {
        var env = new Dictionary<string, string?>
        {
            ["FAKE_AGY_CHILD_SLEEP_MS"] = "60000",
            ["FAKE_AGY_CHILD_PIDFILE"] = pidFile,
            ["FAKE_AGY_SLEEP_MS"] = "60000",
        };
        if (more is not null)
        {
            foreach (var pair in more)
            {
                env[pair.Key] = pair.Value;
            }
        }

        return env;
    }

    [Fact]
    public async Task Timeout_ends_the_child_process_too()
    {
        string pidFile = Path.Combine(_scratch.Path, "child.pid");

        AgyProcessResult result = await _runner.RunAsync(
            FakeAgyHost.Request(_scratch.Path, WithChild(pidFile), timeout: TimeSpan.FromSeconds(2)), CancellationToken.None);
        int child = await FakeAgyHost.ReadChildPidAsync(pidFile, TimeSpan.FromSeconds(5));

        Assert.True(result.TimedOut);
        Assert.True(await FakeAgyHost.WaitUntilGoneAsync(child, TimeSpan.FromSeconds(10)), "the child process is still running");
    }

    [Fact]
    public async Task Cancellation_ends_the_child_process_too()
    {
        string pidFile = Path.Combine(_scratch.Path, "child.pid");
        using var cts = new CancellationTokenSource();

        Task<AgyProcessResult> run = _runner.RunAsync(FakeAgyHost.Request(_scratch.Path, WithChild(pidFile)), cts.Token);
        int child = await FakeAgyHost.ReadChildPidAsync(pidFile, TimeSpan.FromSeconds(10));
        Assert.True(FakeAgyHost.IsAlive(child));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(await FakeAgyHost.WaitUntilGoneAsync(child, TimeSpan.FromSeconds(10)), "the child process is still running");
    }

    [Fact]
    public async Task Output_overflow_ends_the_child_process_too()
    {
        string pidFile = Path.Combine(_scratch.Path, "child.pid");

        AgyProcessResult result = await _runner.RunAsync(
            FakeAgyHost.Request(_scratch.Path, WithChild(pidFile, new() { ["FAKE_AGY_STDOUT_MB"] = "8" }), maxOutputBytes: 1024 * 1024),
            CancellationToken.None);
        int child = await FakeAgyHost.ReadChildPidAsync(pidFile, TimeSpan.FromSeconds(5));

        Assert.True(result.OutputLimitExceeded);
        Assert.True(await FakeAgyHost.WaitUntilGoneAsync(child, TimeSpan.FromSeconds(10)), "the child process is still running");
    }

    [Fact]
    public async Task Request_directory_is_removed_when_the_run_is_cancelled()
    {
        string path;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        using (var directory = RequestDirectory.Create(_scratch.Path))
        {
            path = directory.Path;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _runner.RunAsync(FakeAgyHost.Request(directory.Path, new() { ["FAKE_AGY_SLEEP_MS"] = "60000" }), cts.Token));
        }

        Assert.False(Directory.Exists(path));
    }
}
