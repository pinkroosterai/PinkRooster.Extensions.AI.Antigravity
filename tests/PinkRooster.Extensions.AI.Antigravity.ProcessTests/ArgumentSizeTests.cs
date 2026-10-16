using System.Text.Json;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;
using Xunit.Abstractions;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>
/// Probes how large one command-line argument may be on the machine the tests run on, and checks that the library's own
/// ceiling (<see cref="PromptLimits"/>) is below it. The measured limit is written to the test output for research.md.
/// </summary>
public sealed class ArgumentSizeTests(ITestOutputHelper output) : IDisposable
{
    private readonly ScratchDirectory _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private async Task<(bool Started, int Echoed)> TryAsync(int length)
    {
        string argument = new('a', length);
        try
        {
            AgyProcessResult result = await new AgyProcessRunner().RunAsync(
                FakeAgyHost.Request(_scratch.Path, new() { ["FAKE_AGY_ECHO_ARGS"] = "1" }, ["-p", argument]), CancellationToken.None);
            string[]? echoed = JsonSerializer.Deserialize<string[]>(result.StandardOutput);
            return (true, echoed![1].Length);
        }
        catch (AntigravityProcessException)
        {
            return (false, 0);
        }
    }

    [Fact]
    public async Task The_librarys_prompt_ceiling_is_below_the_largest_argument_the_system_accepts()
    {
        PromptLimits limits = PromptLimits.ForCurrentPlatform(int.MaxValue);
        int ceiling = limits.MaxUtf8Bytes ?? limits.MaxUtf16Units!.Value;

        (bool started, int echoed) = await TryAsync(ceiling);

        output.WriteLine($"{Environment.OSVersion}: argument of {ceiling} characters started={started}");
        Assert.True(started);
        Assert.Equal(ceiling, echoed);
    }

    [Fact]
    public async Task Measure_the_largest_single_argument()
    {
        // Binary search between a size known to work and one that is large enough to fail on every system.
        int low = 1_000;
        int high = 4_000_000;
        while (low + 1 < high)
        {
            int middle = low + ((high - low) / 2);
            (bool started, _) = await TryAsync(middle);
            if (started)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        output.WriteLine($"{Environment.OSVersion}: largest single argument that started: {low} characters; first that failed: {high}");
        Assert.True(low >= PromptLimits.ForCurrentPlatform(int.MaxValue).MaxUtf8Bytes.GetValueOrDefault(0));
        Assert.True(low >= PromptLimits.ForCurrentPlatform(int.MaxValue).MaxUtf16Units.GetValueOrDefault(0));
    }
}
