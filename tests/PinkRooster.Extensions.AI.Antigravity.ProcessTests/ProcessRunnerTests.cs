using System.Diagnostics;
using System.Text.Json;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly ScratchDirectory _scratch = new();
    private readonly AgyProcessRunner _runner = new();

    public void Dispose() => _scratch.Dispose();

    private Task<AgyProcessResult> Run(Dictionary<string, string?> env, TimeSpan? timeout = null, int maxOutput = 16 * 1024 * 1024, IReadOnlyList<string>? args = null, CancellationToken ct = default) =>
        _runner.RunAsync(FakeAgyHost.Request(_scratch.Path, env, args, timeout, maxOutput), ct);

    [Fact]
    public async Task Output_error_and_exit_code_are_captured()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT"] = "out text", ["FAKE_AGY_STDERR"] = "err text", ["FAKE_AGY_EXIT"] = "0" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("out text", result.StandardOutput);
        Assert.Equal("err text", result.StandardError);
        Assert.False(result.TimedOut);
        Assert.False(result.OutputLimitExceeded);
        Assert.True(result.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public async Task Non_zero_exit_code_is_reported()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_EXIT"] = "3", ["FAKE_AGY_STDOUT"] = "{}" });

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Each_argument_arrives_unchanged_whatever_it_contains()
    {
        string[] args = ["--output-format", "json", "-p", "line one\nline two \"quoted\" 'single' $(x) ; & | `y` --model evil \U0001F600 é  trailing  "];

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_ECHO_ARGS"] = "1" }, args: args);

        Assert.Equal(args, JsonSerializer.Deserialize<string[]>(result.StandardOutput));
    }

    [Fact]
    public async Task Environment_overrides_add_change_and_remove_variables()
    {
        string removed = "AGY_TEST_REMOVED_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(removed, "present");
        try
        {
            AgyProcessResult set = await Run(new() { ["FAKE_AGY_ECHO_ENV"] = "AGY_TEST_SET", ["AGY_TEST_SET"] = "value" });
            AgyProcessResult gone = await Run(new() { ["FAKE_AGY_ECHO_ENV"] = removed, [removed] = null });
            AgyProcessResult inherited = await Run(new() { ["FAKE_AGY_ECHO_ENV"] = removed });

            Assert.Equal("value", set.StandardOutput);
            Assert.Equal("<unset>", gone.StandardOutput);
            Assert.Equal("present", inherited.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable(removed, null);
        }
    }

    [Fact]
    public async Task Standard_input_is_closed_so_agy_reads_end_of_file_at_once()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_READ_STDIN"] = "1" });

        Assert.Equal("stdin-length:0\n", result.StandardOutput);
    }

    [Fact]
    public async Task A_cancelled_token_starts_no_process()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var request = FakeAgyHost.Request(_scratch.Path, executable: Path.Combine(_scratch.Path, "does-not-exist"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(request, cts.Token));
    }

    [Fact]
    public async Task An_executable_that_cannot_start_raises_ProcessException_without_the_arguments_in_the_message()
    {
        var request = FakeAgyHost.Request(
            _scratch.Path,
            arguments: ["-p", "SECRET-PROMPT"],
            executable: Path.Combine(_scratch.Path, "does-not-exist"));

        var ex = await Assert.ThrowsAsync<AntigravityProcessException>(() => _runner.RunAsync(request, CancellationToken.None));

        Assert.DoesNotContain("SECRET-PROMPT", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task A_process_ended_from_outside_reports_a_non_zero_exit_code_and_no_timeout()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // the self-kill route is the Unix signal; Windows reports another code
        }

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_SELF_KILL"] = "1" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotNull(result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Timeout_ends_the_process_and_is_reported_as_a_timeout_without_an_exit_code()
    {
        var watch = Stopwatch.StartNew();

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_SLEEP_MS"] = "60000" }, timeout: TimeSpan.FromSeconds(1));

        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.False(result.OutputLimitExceeded);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Timeout_keeps_the_output_read_so_far()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT"] = "partial", ["FAKE_AGY_SLEEP_MS"] = "60000" }, timeout: TimeSpan.FromSeconds(2));

        Assert.True(result.TimedOut);
        Assert.Equal("partial", result.StandardOutput);
    }

    [Fact]
    public async Task Cancellation_raises_OperationCanceled_promptly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(new() { ["FAKE_AGY_SLEEP_MS"] = "60000" }, ct: cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Output_over_the_limit_stops_reading_and_ends_the_process()
    {
        var watch = Stopwatch.StartNew();

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT_MB"] = "8", ["FAKE_AGY_SLEEP_MS"] = "60000" }, maxOutput: 1024 * 1024);

        Assert.True(result.OutputLimitExceeded);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.True(result.StandardOutput.Length <= 1024 * 1024);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Two_mebibytes_of_output_are_read_completely_under_a_larger_limit()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT_MB"] = "2" });

        Assert.Equal(2 * 1024 * 1024, result.StandardOutput.Length);
        Assert.False(result.OutputLimitExceeded);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Large_error_output_does_not_block_the_process_and_is_truncated_to_64_kib()
    {
        var watch = Stopwatch.StartNew();

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDERR_MB"] = "2", ["FAKE_AGY_STDOUT"] = "{\"status\":\"SUCCESS\"}" });

        Assert.Equal("{\"status\":\"SUCCESS\"}", result.StandardOutput);
        Assert.Equal(64 * 1024, result.StandardError.Length);
        Assert.Equal(0, result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Large_output_on_both_streams_at_once_completes()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDERR_MB"] = "1", ["FAKE_AGY_STDOUT_MB"] = "1" });

        Assert.Equal(1024 * 1024, result.StandardOutput.Length);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task A_descendant_that_keeps_the_pipes_open_does_not_hold_the_result_back_beyond_the_grace_period()
    {
        string pidFile = Path.Combine(_scratch.Path, "child.pid");
        var watch = Stopwatch.StartNew();
        int childPid = 0;
        try
        {
            AgyProcessResult result = await Run(new()
            {
                ["FAKE_AGY_CHILD_SLEEP_MS"] = "30000",
                ["FAKE_AGY_CHILD_PIDFILE"] = pidFile,
                ["FAKE_AGY_STDOUT"] = "done",
            });
            childPid = await FakeAgyHost.ReadChildPidAsync(pidFile, TimeSpan.FromSeconds(5));

            Assert.Equal("done", result.StandardOutput);
            Assert.Equal(0, result.ExitCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(12), $"took {watch.Elapsed}");
        }
        finally
        {
            // The runner does not end descendants of a process that exited on its own: once the parent is gone they can no longer
            // be found as its children. The test cleans up after itself.
            if (childPid != 0)
            {
                FakeAgyHost.KillQuietly(childPid);
            }
        }
    }
}
