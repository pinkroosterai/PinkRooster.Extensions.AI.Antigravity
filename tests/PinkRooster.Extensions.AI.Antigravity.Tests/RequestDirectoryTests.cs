using System.Text;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public sealed class RequestDirectoryTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "agy-tests-" + Guid.NewGuid().ToString("N"));

    public RequestDirectoryTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try
        {
            MakeWritable(_scratch);
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void MakeWritable(string path)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(path))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Prepend(path))
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Default_root_gives_a_new_empty_directory_with_the_library_prefix()
    {
        using var directory = RequestDirectory.Create(null);

        Assert.True(Directory.Exists(directory.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
        Assert.StartsWith("agy-chat-", Path.GetFileName(directory.Path));
    }

    [Fact]
    public void Two_directories_never_share_a_path()
    {
        using var a = RequestDirectory.Create(null);
        using var b = RequestDirectory.Create(null);

        Assert.NotEqual(a.Path, b.Path);
    }

    [Fact]
    public void Default_directory_is_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = RequestDirectory.Create(null);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory.Path));
    }

    [Fact]
    public void Custom_root_is_created_when_missing_and_holds_an_owner_only_child()
    {
        string root = Path.Combine(_scratch, "does", "not", "exist");

        using var directory = RequestDirectory.Create(root);

        Assert.True(Directory.Exists(root));
        Assert.Equal(root, Path.GetDirectoryName(directory.Path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory.Path));
        }
    }

    [Fact]
    public void Dispose_deletes_the_directory_and_everything_in_it()
    {
        var directory = RequestDirectory.Create(_scratch);
        Directory.CreateDirectory(Path.Combine(directory.Path, "sub"));
        File.WriteAllText(Path.Combine(directory.Path, "sub", "file.txt"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "top.txt"), "y");

        directory.Dispose();

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public void Dispose_twice_is_harmless()
    {
        var directory = RequestDirectory.Create(_scratch);

        directory.Dispose();
        directory.Dispose();

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public void Dispose_succeeds_when_the_directory_is_already_gone()
    {
        var directory = RequestDirectory.Create(_scratch);
        Directory.Delete(directory.Path);

        directory.Dispose();
    }

    [Fact]
    public void Schema_file_is_written_as_utf8_without_a_byte_order_mark()
    {
        using var directory = RequestDirectory.Create(_scratch);
        const string schema = """{"type":"object","description":"é\u00e9"}""";

        string file = directory.WriteSchemaFile(schema);

        Assert.Equal(Path.Combine(directory.Path, RequestDirectory.SchemaFileName), file);
        byte[] bytes = File.ReadAllBytes(file);
        Assert.Equal(Encoding.UTF8.GetBytes(schema), bytes);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void Root_that_is_a_file_raises_AntigravityException_naming_the_root()
    {
        string file = Path.Combine(_scratch, "not-a-directory");
        File.WriteAllText(file, "x");

        var ex = Assert.Throws<AntigravityException>(() => RequestDirectory.Create(file));

        Assert.Contains(file, ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Failed_deletion_is_reported_through_the_callback_after_the_retries_and_does_not_throw()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var failures = new List<string>();
        var directory = RequestDirectory.Create(_scratch, (path, _) => failures.Add(path), deleteAttempts: 3, retryDelay: TimeSpan.FromMilliseconds(1));
        string locked = Path.Combine(directory.Path, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "file.txt"), "x");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            if (Environment.UserName == "root")
            {
                return; // root ignores permissions, so the failure cannot be provoked
            }

            directory.Dispose();

            Assert.Equal([directory.Path], failures);
            Assert.True(Directory.Exists(directory.Path));
        }
        finally
        {
            MakeWritable(directory.Path);
        }
    }

    [Fact]
    public void Successful_deletion_does_not_call_the_failure_callback()
    {
        bool called = false;
        var directory = RequestDirectory.Create(_scratch, (_, _) => called = true);

        directory.Dispose();

        Assert.False(called);
    }
}
