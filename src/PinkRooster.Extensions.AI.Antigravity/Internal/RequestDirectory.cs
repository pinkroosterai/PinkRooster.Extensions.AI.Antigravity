using System.Text;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// The new, empty directory one agy run uses as its working directory, and optionally a sibling directory that agy gets as its
/// home. Both are created owner-only where the platform allows and deleted on dispose.
/// </summary>
internal sealed class RequestDirectory : IDisposable
{
    public const string SchemaFileName = "schema.json";
    private const string Prefix = "agy-chat-";

    private readonly Action<string, Exception>? _onDeleteFailed;
    private readonly int _deleteAttempts;
    private readonly TimeSpan _retryDelay;
    private bool _disposed;

    private RequestDirectory(string path, string? homePath, Action<string, Exception>? onDeleteFailed, int deleteAttempts, TimeSpan retryDelay)
    {
        Path = path;
        HomePath = homePath;
        _onDeleteFailed = onDeleteFailed;
        _deleteAttempts = deleteAttempts;
        _retryDelay = retryDelay;
    }

    public string Path { get; }

    /// <summary>The agy home directory next to the request directory, or null when the request has none.</summary>
    public string? HomePath { get; }

    /// <summary>
    /// Creates the directory. Failure to create it raises <see cref="AntigravityException"/> naming the root.
    /// </summary>
    /// <param name="root">The directory to create it under; null means the system temporary directory.</param>
    /// <param name="onDeleteFailed">Called with the path and the error when deletion still fails after the retries.</param>
    /// <param name="deleteAttempts">How many times deletion is tried.</param>
    /// <param name="retryDelay">The wait between deletion attempts; default 100 ms.</param>
    /// <param name="withHome">True to create an agy home directory next to the request directory.</param>
    public static RequestDirectory Create(
        string? root,
        Action<string, Exception>? onDeleteFailed = null,
        int deleteAttempts = 3,
        TimeSpan? retryDelay = null,
        bool withHome = false)
    {
        string path = string.Empty;
        string? homePath = null;
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
                CreateOwnerOnly(path);
            }

            if (withHome)
            {
                homePath = path + "-home";
                CreateOwnerOnly(homePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The request directory may exist already when the home could not be created; do not leave it behind.
            try
            {
                if (path.Length > 0 && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                onDeleteFailed?.Invoke(path, cleanup);
            }

            throw new AntigravityException(
                $"The request directory could not be created under '{root ?? System.IO.Path.GetTempPath()}'.", ex);
        }

        return new RequestDirectory(path, homePath, onDeleteFailed, Math.Max(1, deleteAttempts), retryDelay ?? TimeSpan.FromMilliseconds(100));
    }

    private static void CreateOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Writes <c>AGENTS.md</c> in the request directory, where agy reads it as a system-level rule.</summary>
    public void WriteAgentsFile(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        File.WriteAllText(System.IO.Path.Combine(Path, AgentsFile.FileName), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Writes the request's images under their library-chosen relative paths, where agy reads them.</summary>
    public void WriteAttachments(IReadOnlyList<Attachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count == 0)
        {
            return;
        }

        CreateOwnerOnly(System.IO.Path.Combine(Path, RequestValidator.AttachmentFolder));
        foreach (Attachment attachment in attachments)
        {
            File.WriteAllBytes(System.IO.Path.Combine(Path, attachment.RelativePath), attachment.Data.ToArray());
        }
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

        Delete(Path);
        if (HomePath is not null)
        {
            Delete(HomePath);
        }
    }

    private void Delete(string path)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= _deleteAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
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
        _onDeleteFailed?.Invoke(path, last!);
    }
}
