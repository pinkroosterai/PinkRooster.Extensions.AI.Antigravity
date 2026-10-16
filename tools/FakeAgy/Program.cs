// FakeAgy imitates the agy command line for tests of the process runner. Its behavior is chosen with environment
// variables, so the arguments the library passes (which this program ignores unless asked to echo them) stay realistic.
//
//   FAKE_AGY_VERSION         text printed for "--version" (default "1.3.5")
//   FAKE_AGY_STDOUT          text written to standard output
//   FAKE_AGY_STDOUT_HEX      bytes (hex digits) written to standard output, for invalid UTF-8
//   FAKE_AGY_STDERR          text written to standard error
//   FAKE_AGY_STDOUT_MB       this many MiB of 'x' written to standard output
//   FAKE_AGY_STDERR_MB       this many MiB of 'e' written to standard error, before anything else
//   FAKE_AGY_SLEEP_MS        wait this long before exiting
//   FAKE_AGY_EXIT            exit code (default 0)
//   FAKE_AGY_ECHO_ARGS=1     write the received arguments as a JSON array to standard output
//   FAKE_AGY_ECHO_CWD=1      write the working directory to standard output
//   FAKE_AGY_ECHO_ENV=NAME   write the value of that environment variable (or "<unset>") to standard output
//   FAKE_AGY_READ_STDIN=1    read standard input to its end and write "stdin-length:N"
//   FAKE_AGY_WRITE_FILE=NAME create that file in the working directory
//   FAKE_AGY_CHILD_SLEEP_MS  start a child that sleeps this long and shares the standard streams
//   FAKE_AGY_CHILD_PIDFILE   file that receives the child's process id
//   FAKE_AGY_SELF_KILL=1     end this process abruptly, as an outside signal would

using System.Diagnostics;
using System.Text;
using System.Text.Json;

if (args.Length >= 2 && args[0] == "--fake-sleep")
{
    Thread.Sleep(int.Parse(args[1]));
    return 0;
}

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Stream stdout = Console.OpenStandardOutput();
Stream stderr = Console.OpenStandardError();

static string? Env(string name) => Environment.GetEnvironmentVariable(name);

void Write(Stream stream, string text)
{
    byte[] bytes = utf8.GetBytes(text);
    stream.Write(bytes, 0, bytes.Length);
    stream.Flush();
}

void WriteMegabytes(Stream stream, int megabytes, byte fill)
{
    byte[] chunk = new byte[64 * 1024];
    Array.Fill(chunk, fill);
    for (long written = 0; written < megabytes * 1024L * 1024L; written += chunk.Length)
    {
        stream.Write(chunk, 0, chunk.Length);
    }

    stream.Flush();
}

if (args.Contains("--version"))
{
    Write(stdout, (Env("FAKE_AGY_VERSION") ?? "1.3.5") + "\n");
    return 0;
}

if (Env("FAKE_AGY_SELF_KILL") == "1")
{
    Process.GetCurrentProcess().Kill();
}

if (int.TryParse(Env("FAKE_AGY_CHILD_SLEEP_MS"), out int childSleep))
{
    string host = Environment.ProcessPath!;
    var start = new ProcessStartInfo(host) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    {
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
    }

    start.ArgumentList.Add("--fake-sleep");
    start.ArgumentList.Add(childSleep.ToString());
    Process child = Process.Start(start)!;
    string? pidFile = Env("FAKE_AGY_CHILD_PIDFILE");
    if (pidFile is not null)
    {
        File.WriteAllText(pidFile, child.Id.ToString());
    }
}

if (int.TryParse(Env("FAKE_AGY_STDERR_MB"), out int stderrMb))
{
    WriteMegabytes(stderr, stderrMb, (byte)'e');
}

if (Env("FAKE_AGY_WRITE_FILE") is { } fileName)
{
    File.WriteAllText(fileName, "written by FakeAgy");
}

if (Env("FAKE_AGY_READ_STDIN") == "1")
{
    string input = Console.In.ReadToEnd();
    Write(stdout, "stdin-length:" + input.Length + "\n");
}

if (Env("FAKE_AGY_ECHO_ARGS") == "1")
{
    Write(stdout, JsonSerializer.Serialize(args));
}

if (Env("FAKE_AGY_ECHO_CWD") == "1")
{
    Write(stdout, Directory.GetCurrentDirectory());
}

if (Env("FAKE_AGY_ECHO_ENV") is { } variable)
{
    Write(stdout, Environment.GetEnvironmentVariable(variable) ?? "<unset>");
}

if (Env("FAKE_AGY_STDOUT") is { } text)
{
    Write(stdout, text);
}

if (Env("FAKE_AGY_STDOUT_HEX") is { } hex)
{
    byte[] bytes = Convert.FromHexString(hex);
    stdout.Write(bytes, 0, bytes.Length);
    stdout.Flush();
}

if (int.TryParse(Env("FAKE_AGY_STDOUT_MB"), out int stdoutMb))
{
    WriteMegabytes(stdout, stdoutMb, (byte)'x');
}

if (Env("FAKE_AGY_STDERR") is { } errorText)
{
    Write(stderr, errorText);
}

if (int.TryParse(Env("FAKE_AGY_SLEEP_MS"), out int sleep))
{
    Thread.Sleep(sleep);
}

return int.TryParse(Env("FAKE_AGY_EXIT"), out int exitCode) ? exitCode : 0;
