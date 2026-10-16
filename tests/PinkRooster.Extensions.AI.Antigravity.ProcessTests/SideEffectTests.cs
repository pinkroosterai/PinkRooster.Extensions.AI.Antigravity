using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>NFR-006: a write by the process lands in the request directory and disappears with it.</summary>
public sealed class SideEffectTests : IDisposable
{
    private readonly ScratchDirectory _scratch = new();
    private readonly AgyProcessRunner _runner = new();

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public async Task A_file_written_by_the_process_stays_inside_the_request_directory_and_is_removed_with_it()
    {
        string callerDirectory = Directory.GetCurrentDirectory();
        string[] before = Directory.GetFileSystemEntries(callerDirectory);
        string path;

        using (var directory = RequestDirectory.Create(_scratch.Path))
        {
            path = directory.Path;
            await _runner.RunAsync(
                FakeAgyHost.Request(directory.Path, new() { ["FAKE_AGY_WRITE_FILE"] = "created-by-agy.txt" }), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(directory.Path, "created-by-agy.txt")));
            Assert.Equal(before, Directory.GetFileSystemEntries(callerDirectory));
        }

        Assert.False(Directory.Exists(path));
        Assert.Equal(before, Directory.GetFileSystemEntries(callerDirectory));
    }
}
