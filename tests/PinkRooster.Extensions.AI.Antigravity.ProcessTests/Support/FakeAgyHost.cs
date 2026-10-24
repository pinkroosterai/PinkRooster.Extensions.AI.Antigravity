using System.Diagnostics;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

/// <summary>Starts the fake agy copied next to the tests through the real process runner.</summary>
internal static class FakeAgyHost
{
    public static string Executable { get; } =
        Path.Combine(AppContext.BaseDirectory, "fakeagy", OperatingSystem.IsWindows() ? "FakeAgy.exe" : "FakeAgy");

    public static AgyProcessRequest Request(
        string workingDirectory,
        Dictionary<string, string?>? environment = null,
        IReadOnlyList<string>? arguments = null,
        TimeSpan? timeout = null,
        int maxOutputBytes = 16 * 1024 * 1024,
        string? executable = null) =>
        new(
            executable ?? Executable,
            arguments ?? ["-p", "prompt"],
            workingDirectory,
            environment ?? [],
            timeout ?? TimeSpan.FromSeconds(60),
            maxOutputBytes);

    public static bool IsAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }

            if (OperatingSystem.IsLinux())
            {
                string stat = File.ReadAllText($"/proc/{pid}/stat");
                int close = stat.LastIndexOf(')');
                return close < 0 || stat[close + 2] != 'Z'; // a zombie is gone for our purposes
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    public static async Task<bool> WaitUntilGoneAsync(int pid, TimeSpan limit)
    {
        DateTime end = DateTime.UtcNow + limit;
        while (IsAlive(pid))
        {
            if (DateTime.UtcNow > end)
            {
                return false;
            }

            await Task.Delay(50);
        }

        return true;
    }

    public static async Task<int> ReadChildPidAsync(string pidFile, TimeSpan limit)
    {
        DateTime end = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < end)
        {
            if (File.Exists(pidFile) && int.TryParse(await File.ReadAllTextAsync(pidFile), out int pid))
            {
                return pid;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The fake agy did not report its child process.");
    }

    public static void KillQuietly(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}

/// <summary>A scratch directory removed at the end of a test.</summary>
internal sealed class ScratchDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agy-process-tests-" + Guid.NewGuid().ToString("N"));

    public ScratchDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
