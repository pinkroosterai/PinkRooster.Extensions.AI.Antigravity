using System.Diagnostics;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>
/// NFR-010: the runner adds little to the cost of starting the process. Shared CI machines are noisy, so the check is a
/// loose ceiling and the measurement is written to the test output.
/// </summary>
public sealed class OverheadTests(ITestOutputHelper output) : IDisposable
{
    private readonly ScratchDirectory _scratch = new();

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public async Task Runner_overhead_over_a_bare_process_start_stays_small()
    {
        const int rounds = 7;
        var runner = new AgyProcessRunner();
        var request = FakeAgyHost.Request(_scratch.Path, new() { ["FAKE_AGY_STDOUT"] = "{}" });

        await runner.RunAsync(request, CancellationToken.None); // warm up

        var bare = new List<double>();
        var viaRunner = new List<double>();
        for (int i = 0; i < rounds; i++)
        {
            var watch = Stopwatch.StartNew();
            using (var process = Process.Start(new ProcessStartInfo(FakeAgyHost.Executable) { UseShellExecute = false, RedirectStandardOutput = true, WorkingDirectory = _scratch.Path, Environment = { ["FAKE_AGY_STDOUT"] = "{}" } })!)
            {
                await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
            }

            bare.Add(watch.Elapsed.TotalMilliseconds);

            watch.Restart();
            await runner.RunAsync(request, CancellationToken.None);
            viaRunner.Add(watch.Elapsed.TotalMilliseconds);
        }

        double overhead = Median(viaRunner) - Median(bare);
        output.WriteLine($"median bare start {Median(bare):F0} ms, via runner {Median(viaRunner):F0} ms, overhead {overhead:F0} ms");
        Assert.True(overhead < 250, $"overhead {overhead:F0} ms");
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }
}
