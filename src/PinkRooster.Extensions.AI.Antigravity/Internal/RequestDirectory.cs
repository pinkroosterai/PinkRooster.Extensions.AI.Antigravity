using System.Text;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// The new, empty directory one agy run uses as its working directory. Created owner-only where the platform allows,
/// deleted on dispose.
/// </summary>
internal sealed class RequestDirectory : IDisposable
{
    public const string SchemaFileName = "schema.json";
    private const string Prefix = "agy-chat-";

    private readonly Action<string, Exception>? _onDeleteFailed;
    private readonly int _deleteAttempts;
    private readonly TimeSpan _retryDelay;
    private bool _disposed;

    private RequestDirectory(string path, Action<string, Exception>? onDeleteFailed, int deleteAttempts, TimeSpan retryDelay)
    {
        Path = path;
        _onDeleteFailed = onDeleteFailed;
        _deleteAttempts = deleteAttempts;
        _retryDelay = retryDelay;
    }

    public string Path { get; }

    /// <summary>
    /// Creates the directory. Failure to create it raises <see cref="AntigravityException"/> naming the root.
    /// </summary>
    /// <param name="root">The directory to create it under; null means the system temporary directory.</param>
    /// <param name="onDeleteFailed">Called with the path and the error when deletion still fails after the retries.</param>
    /// <param name="deleteAttempts">How many times deletion is tried.</param>
    /// <param name="retryDelay">The wait between deletion attempts; default 100 ms.</param>
    public static RequestDirectory Create(
        string? root,
        Action<string, Exception>? onDeleteFailed = null,
        int deleteAttempts = 3,
        TimeSpan? retryDelay = null)
    {
        string path;
        try
        {
            if (root is null)
            {
                // Owner-only (mode 700) on Unix-like systems (Microsoft Learn, Directory.CreateTempSubdirectory).
                path = Directory.CreateTempSubdirectory(Prefix).FullName;
            }
            else
            {
                Directory.CreateDirectory(root);
                path = System.IO.Path.Combine(root, Prefix + Guid.NewGuid().ToString("N"));
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new AntigravityException(
                $"The request directory could not be created under '{root ?? System.IO.Path.GetTempPath()}'.", ex);
        }

        return new RequestDirectory(path, onDeleteFailed, Math.Max(1, deleteAttempts), retryDelay ?? TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Writes the schema where agy can read it by path and returns that path.</summary>
    public string WriteSchemaFile(string schemaText)
    {
        ArgumentNullException.ThrowIfNull(schemaText);
        string file = System.IO.Path.Combine(Path, SchemaFileName);
        File.WriteAllText(file, schemaText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return file;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Exception? last = null;
        for (int attempt = 1; attempt <= _deleteAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                if (attempt < _deleteAttempts)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }

        // The answer is already known, so a leftover directory must not turn a result into a failure (design DL-16).
        _onDeleteFailed?.Invoke(Path, last!);
    }
}
