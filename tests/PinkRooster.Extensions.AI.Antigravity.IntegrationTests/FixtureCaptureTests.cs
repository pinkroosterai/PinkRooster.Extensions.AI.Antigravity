using PinkRooster.Extensions.AI.Antigravity.Internal;
using Xunit.Abstractions;

namespace PinkRooster.Extensions.AI.Antigravity.IntegrationTests;

/// <summary>
/// Records raw agy output for the fixtures of the unit tests. It writes into the directory named by
/// <c>AGY_CAPTURE_DIR</c> (which must also be set) and changes nothing else; copy the files you accept into
/// <c>tests/…Tests/Fixtures</c> yourself (and replace the conversation ids). The first three cases cost no model call.
/// </summary>
public sealed class FixtureCaptureTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "agy-capture-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<AgyProcessResult> Run(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(_work);
        string agy = AgyLocator.Resolve(null);
        return await new AgyProcessRunner().RunAsync(
            new AgyProcessRequest(agy, arguments, _work, new Dictionary<string, string?>(), timeout ?? TimeSpan.FromMinutes(2), 16 * 1024 * 1024),
            CancellationToken.None);
    }

    private void Save(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), content);
        output.WriteLine($"captured {name} ({content.Length} characters)");
    }

    private void SaveResult(string directory, string stem, AgyProcessResult result)
    {
        if (result.StandardOutput.Length > 0)
        {
            Save(directory, stem + ".json", result.StandardOutput);
        }

        if (result.StandardError.Length > 0)
        {
            Save(directory, stem + ".stderr.txt", result.StandardError);
        }

        output.WriteLine($"{stem}: exit {result.ExitCode}, timed out {result.TimedOut}");
    }

    [IntegrationFact]
    public async Task Capture()
    {
        // Switched on by AGY_INTEGRATION_TESTS=1; the target directory is read below.
        string? directory = Environment.GetEnvironmentVariable("AGY_CAPTURE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(directory), "Set AGY_CAPTURE_DIR to an empty directory for the captured files.");
        const string model = "gemini-3.8-flash-low";
        string[] common = ["--output-format", "json", "--disable-slash-commands", "--model", model];

        // Free: agy fails before any model call.
        SaveResult(directory!, "exit1-bad-model", await Run(["--output-format", "json", "--model", "no-such-model", "-p", "hi"]));
        SaveResult(directory!, "exit1-empty-prompt", await Run(["--output-format", "json", "--model", model, "-p", ""]));
        SaveResult(directory!, "exit1-bad-schema", await Run(["--output-format", "json", "--json-schema", "{bad", "-p", "hi"]));

        // A few cents: each is one small model call.
        SaveResult(directory!, "success-text", await Run([.. common, "-p", "Reply with exactly the word: pong. Do not use any tools."]));
        string schema = Path.Combine(_work, "schema.json");
        Directory.CreateDirectory(_work);
        File.WriteAllText(schema, """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"],"additionalProperties":false}""");
        SaveResult(directory!, "success-schema", await Run([.. common, "--json-schema", schema, "-p", "Capital of France? Do not use any tools."]));
        File.WriteAllText(schema, """{"type":"object","properties":{"n":{"type":"integer","minimum":100,"maximum":10}},"required":["n"],"additionalProperties":false}""");
        SaveResult(directory!, "schema-missing-structured", await Run([.. common, "--json-schema", schema, "-p", "Return n = 5. Do not use any tools."]));
        SaveResult(directory!, "timeout-empty-success", await Run([.. common, "--print-timeout", "1s", "-p", "Write a 2000 word essay about the history of tea."]));
        SaveResult(directory!, "denied-empty", await Run([.. common, "-p", "Run the shell command `ls` and tell me the output."]));
    }
}
