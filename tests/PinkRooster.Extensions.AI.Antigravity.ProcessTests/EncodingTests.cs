using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

public sealed class EncodingTests : IDisposable
{
    private readonly ScratchDirectory _scratch = new();
    private readonly AgyProcessRunner _runner = new();

    public void Dispose() => _scratch.Dispose();

    private Task<AgyProcessResult> Run(Dictionary<string, string?> env, IReadOnlyList<string>? args = null) =>
        _runner.RunAsync(FakeAgyHost.Request(_scratch.Path, env, args), CancellationToken.None);

    [Fact]
    public async Task Text_outside_the_basic_multilingual_plane_round_trips_in_output_and_error()
    {
        const string text = "héllo \U0001F600 日本語 Привет";

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT"] = text, ["FAKE_AGY_STDERR"] = text });

        Assert.Equal(text, result.StandardOutput);
        Assert.Equal(text, result.StandardError);
    }

    [Fact]
    public async Task Text_outside_the_basic_multilingual_plane_round_trips_in_arguments()
    {
        string[] args = ["-p", "\U0001F600 日本語 é"];

        AgyProcessResult result = await Run(new() { ["FAKE_AGY_ECHO_ARGS"] = "1" }, args);

        Assert.Equal("[\"-p\",\"\\uD83D\\uDE00 \\u65E5\\u672C\\u8A9E \\u00E9\"]", result.StandardOutput);
    }

    [Fact]
    public async Task Invalid_bytes_are_replaced_and_do_not_fail_the_run()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT_HEX"] = "FFFE41" });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains('A', result.StandardOutput);
        Assert.Contains('\uFFFD', result.StandardOutput);
    }

    [Fact]
    public async Task A_byte_order_mark_at_the_start_is_removed()
    {
        AgyProcessResult result = await Run(new() { ["FAKE_AGY_STDOUT_HEX"] = "EFBBBF7B7D" });

        Assert.Equal("{}", result.StandardOutput);
    }
}
