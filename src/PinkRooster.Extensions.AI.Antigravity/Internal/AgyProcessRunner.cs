using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// Starts agy and reports how it ended. This is the only code that touches <see cref="Process"/>.
/// </summary>
internal sealed class AgyProcessRunner : IAgyProcessRunner
{
    private const int StandardErrorKeepBytes = 64 * 1024;
    private static readonly TimeSpan ReaderGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitGuard = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken) =>
        RunCoreAsync(request, null, cancellationToken);

    public Task<AgyProcessResult> RunStreamingAsync(
        AgyProcessRequest request,
        Action<string> onStandardOutputLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onStandardOutputLine);
        return RunCoreAsync(request, onStandardOutputLine, cancellationToken);
    }

    private static async Task<AgyProcessResult> RunCoreAsync(
        AgyProcessRequest request,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string?> pair in request.EnvironmentOverrides)
        {
            if (pair.Value is null)
            {
                startInfo.Environment.Remove(pair.Key);
            }
            else
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using Process process = new Process { StartInfo = startInfo };
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // The message of these exceptions names the file, never the arguments, so it cannot carry the prompt.
            throw new AntigravityProcessException($"agy could not be started: {ex.Message}", ex);
        }

        bool outputOverflow = false;
        using CancellationTokenSource timeout = new CancellationTokenSource(request.Timeout);
        using CancellationTokenSource overflow = new CancellationTokenSource();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token, overflow.Token);
        using CancellationTokenSource readers = new CancellationTokenSource();

        Task<byte[]> stdoutTask = Task.FromResult(Array.Empty<byte>());
        Task<byte[]> stderrTask = stdoutTask;
        bool exited = false;
        try
        {
            CloseStandardInput(process);

            stdoutTask = ReadAsync(
                process.StandardOutput.BaseStream,
                request.MaxOutputBytes,
                stopAtLimit: true,
                () =>
                {
                    outputOverflow = true;
                    overflow.Cancel();
                },
                readers.Token,
                onLine);
            stderrTask = ReadAsync(process.StandardError.BaseStream, StandardErrorKeepBytes, stopAtLimit: false, null, readers.Token);

            try
            {
                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
                exited = true;
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the caller, by the timeout, or by the output limit: end the whole tree below.
            }

            if (!exited)
            {
                await KillTreeAsync(process).ConfigureAwait(false);
            }

            await DrainAsync(stdoutTask, stderrTask, readers).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExitedSafe())
            {
                await KillTreeAsync(process).ConfigureAwait(false);
            }

            readers.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();

        bool timedOut = !exited && !outputOverflow && timeout.IsCancellationRequested;
        stopwatch.Stop();

        string standardOutput = Decode(await SafeResultAsync(stdoutTask).ConfigureAwait(false));
        string standardError = Decode(await SafeResultAsync(stderrTask).ConfigureAwait(false));
        int? exitCode = exited ? process.ExitCode : null;

        return new AgyProcessResult(exitCode, standardOutput, standardError, timedOut, outputOverflow, stopwatch.Elapsed);
    }

    // Splits UTF-8 bytes into lines on '\n' (a byte that never occurs inside a multi-byte sequence) and decodes each line.
    [SuppressMessage("Usage", "CA1001", Justification = "MemoryStream holds no unmanaged resource; the splitter lives for one read.")]
    private sealed class LineSplitter(Action<string> onLine)
    {
        private readonly MemoryStream _pending = new();

        public void Add(byte[] buffer, int count)
        {
            int start = 0;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] != (byte)'\n')
                {
                    continue;
                }

                _pending.Write(buffer, start, i - start);
                Emit();
                start = i + 1;
            }

            _pending.Write(buffer, start, count - start);
        }

        public void Finish()
        {
            if (_pending.Length > 0)
            {
                Emit();
            }
        }

        private void Emit()
        {
            string text = Decode(_pending.ToArray());
            _pending.SetLength(0);
            onLine(text.EndsWith('\r') ? text[..^1] : text);
        }
    }

    private static void CloseStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process may already have exited; there is nothing left to close.
        }
    }

    private static async Task<byte[]> ReadAsync(
        Stream stream,
        int limit,
        bool stopAtLimit,
        Action? onLimit,
        CancellationToken cancellationToken,
        Action<string>? onLine = null)
    {
        MemoryStream kept = new MemoryStream();
        LineSplitter? line = onLine is null ? null : new LineSplitter(onLine);
        long total = 0;
        byte[] buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    line?.Finish();
                    break;
                }

                if (line is not null)
                {
                    // Lines are handed on and not kept; the limit counts every byte read.
                    long left = limit - total;
                    int usable = (int)Math.Min(read, Math.Max(left, 0));
                    line.Add(buffer, usable);
                    total += usable;
                    if (usable < read)
                    {
                        onLimit?.Invoke();
                        break;
                    }

                    continue;
                }

                long room = limit - kept.Length;
                if (read > room)
                {
                    if (room > 0)
                    {
                        kept.Write(buffer, 0, (int)room);
                    }

                    if (stopAtLimit)
                    {
                        onLimit?.Invoke();
                        break;
                    }

                    // Keep draining so a chatty agy never blocks on a full pipe; the surplus is dropped.
                    continue;
                }

                kept.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
            // The reader was told to give up (after a kill, or for a pipe a grandchild keeps open); return what was read.
        }
        catch (IOException)
        {
            // The pipe was closed under the reader by the end of the process.
        }
        catch (ObjectDisposedException)
        {
        }

        return kept.ToArray();
    }

    // After the process is gone the readers normally finish at once; a grandchild that inherited the pipe can keep it open,
    // so wait a bounded time and then tell the readers to stop.
    private static async Task DrainAsync(Task<byte[]> stdoutTask, Task<byte[]> stderrTask, CancellationTokenSource readers)
    {
        Task both = Task.WhenAll(stdoutTask, stderrTask);
        Task finished = await Task.WhenAny(both, Task.Delay(ReaderGrace)).ConfigureAwait(false);
        if (finished != both)
        {
            readers.Cancel();
            await both.ConfigureAwait(false);
        }
    }

    private static async Task KillTreeAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return; // already exited
        }
        catch (Exception ex) when (ex is Win32Exception or AggregateException or NotSupportedException)
        {
            try
            {
                process.Kill();
            }
            catch (Exception inner) when (inner is Win32Exception or InvalidOperationException)
            {
            }
        }

        // WaitForExit reports the root process only; descendants may still be exiting when it returns.
        using CancellationTokenSource guard = new CancellationTokenSource(ExitGuard);
        try
        {
            await process.WaitForExitAsync(guard.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<byte[]> SafeResultAsync(Task<byte[]> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return [];
        }
    }

    private static string Decode(byte[] bytes)
    {
        string text = Utf8.GetString(bytes); // invalid sequences become U+FFFD
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
}

internal static class ProcessExtensions
{
    public static bool HasExitedSafe(this Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
