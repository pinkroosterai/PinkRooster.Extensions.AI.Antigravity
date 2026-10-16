using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>NFR-008: behavior does not depend on the host process's current directory.</summary>
[Collection("current directory")]
public sealed class WorkingDirectoryTests : IDisposable
{
    private readonly ScratchDirectory _scratch = new();
    private readonly string _original = Directory.GetCurrentDirectory();

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_original);
        _scratch.Dispose();
    }

    [Fact]
    public async Task The_process_runs_in_the_request_directory_whatever_the_host_directory_is()
    {
        string host = Path.Combine(_scratch.Path, "host-cwd");
        string request = Path.Combine(_scratch.Path, "request-cwd");
        Directory.CreateDirectory(host);
        Directory.CreateDirectory(request);
        Directory.SetCurrentDirectory(host);

        AgyProcessResult result = await new AgyProcessRunner().RunAsync(
            FakeAgyHost.Request(request, new() { ["FAKE_AGY_ECHO_CWD"] = "1" }), CancellationToken.None);

        Assert.Equal(Path.GetFullPath(request), Path.GetFullPath(result.StandardOutput).TrimEnd(Path.DirectorySeparatorChar), ignoreCase: OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }
}

[CollectionDefinition("current directory", DisableParallelization = true)]
public sealed class CurrentDirectoryCollection;
