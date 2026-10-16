// FakeAgy imitates the agy command line for tests of the process runner. Behavior is chosen with environment
// variables so the arguments the library passes (which this program ignores) can stay realistic.
//
//   FAKE_AGY_VERSION   text printed for "--version" (default "1.3.5")
//   FAKE_AGY_STDOUT    text written to standard output for any other call
//   FAKE_AGY_STDERR    text written to standard error
//   FAKE_AGY_EXIT      exit code (default 0)
//
// More behaviors (delays, child processes, large output, invalid bytes) are added in the process-runner phase.

using System.Text;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
using var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };

if (args.Contains("--version"))
{
    stdout.WriteLine(Environment.GetEnvironmentVariable("FAKE_AGY_VERSION") ?? "1.3.5");
    return 0;
}

string? standardOutput = Environment.GetEnvironmentVariable("FAKE_AGY_STDOUT");
if (standardOutput is not null)
{
    stdout.Write(standardOutput);
}

string? standardError = Environment.GetEnvironmentVariable("FAKE_AGY_STDERR");
if (standardError is not null)
{
    stderr.Write(standardError);
}

return int.TryParse(Environment.GetEnvironmentVariable("FAKE_AGY_EXIT"), out int exitCode) ? exitCode : 0;
