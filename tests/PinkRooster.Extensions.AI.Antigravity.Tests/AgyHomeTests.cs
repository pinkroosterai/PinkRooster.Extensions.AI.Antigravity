using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>Every request runs in its own agy home with the system text in AGENTS.md (Linux, the verified platform).</summary>
public sealed class AgyHomeTests : IDisposable
{
    private const string SecretSystem = "SECRET-SYSTEM-TEXT";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "agy-home-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static bool Applies => InvocationBuilder.IsolationApplies;

    private static ChatOptions WithInstructions() => new() { Instructions = SecretSystem };

    private sealed record Seen(string WorkingDirectory, string? Home, bool HomeExists, string? Agents, string Prompt, IReadOnlyDictionary<string, string?> Environment);

    private static Func<AgyProcessRequest, AgyProcessResult> Capture(List<Seen> seen, string fixture = "success-text.json") =>
        run =>
        {
            run.EnvironmentOverrides.TryGetValue("HOME", out string? home);
            string agentsPath = Path.Combine(run.WorkingDirectory, "AGENTS.md");
            seen.Add(new Seen(
                run.WorkingDirectory,
                home,
                home is not null && Directory.Exists(home),
                File.Exists(agentsPath) ? File.ReadAllText(agentsPath) : null,
                run.Arguments[^1],
                run.EnvironmentOverrides));
            return Fixture.Result(fixture);
        };

    [Fact]
    public async Task An_isolated_request_gets_a_fresh_home_and_the_system_text_in_agents_md_and_not_in_the_prompt()
    {
        if (!Applies) { return; }

        var seen = new List<Seen>();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Capture(seen));
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; });

        await client.GetResponseAsync(Clients.User("Say pong"), WithInstructions());

        Seen run = Assert.Single(seen);
        Assert.NotNull(run.Home);
        Assert.True(run.HomeExists);
        Assert.NotEqual(Environment.GetEnvironmentVariable("HOME"), run.Home);
        Assert.StartsWith(_root, run.Home, StringComparison.Ordinal);
        Assert.NotEqual(run.WorkingDirectory, run.Home);
        Assert.NotNull(run.Agents);
        Assert.Contains(AntigravityChatClientOptions.DefaultSystemPreamble, run.Agents);
        Assert.Contains(SecretSystem, run.Agents);
        Assert.DoesNotContain(SecretSystem, run.Prompt);
        Assert.DoesNotContain(AntigravityChatClientOptions.DefaultSystemPreamble, run.Prompt);
        Assert.Contains("Say pong", run.Prompt);
        Assert.False(Directory.Exists(run.Home));
        Assert.False(Directory.Exists(run.WorkingDirectory));
    }

    [Fact]
    public async Task Each_request_gets_its_own_home()
    {
        if (!Applies) { return; }

        var seen = new List<Seen>();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Capture(seen)).Returns(Capture(seen));
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; });

        await client.GetResponseAsync(Clients.User());
        await client.GetResponseAsync(Clients.User());

        Assert.Equal(2, seen.Select(s => s.Home).Distinct().Count());
    }

    [Fact]
    public async Task Without_isolation_nothing_changes_the_system_text_stays_in_the_prompt_and_agy_keeps_the_real_home()
    {
        var seen = new List<Seen>();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Capture(seen));
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = false; });

        await client.GetResponseAsync(Clients.User(), WithInstructions());

        Seen run = Assert.Single(seen);
        Assert.Null(run.Home);
        Assert.Null(run.Agents);
        Assert.Contains(SecretSystem, run.Prompt);
        Assert.Contains(AntigravityChatClientOptions.DefaultSystemPreamble, run.Prompt);
    }

    [Fact]
    public async Task The_home_is_removed_when_the_run_fails()
    {
        if (!Applies) { return; }

        string? home = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(r => { home = r.EnvironmentOverrides["HOME"]; return Fixture.Result("exit1-empty-prompt.json", exitCode: 1); });
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; });

        await Assert.ThrowsAsync<AntigravityProcessException>(() => client.GetResponseAsync(Clients.User()));

        Assert.NotNull(home);
        Assert.False(Directory.Exists(home));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task The_streaming_path_uses_the_home_and_agents_md_too()
    {
        if (!Applies) { return; }

        var seen = new List<Seen>();
        string stream = "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"x\"}}\n";
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(r =>
            {
                Capture(seen)(r);
                return FakeProcessRunner.Ok(stream);
            });
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; });

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync(Clients.User(), WithInstructions()))
        {
        }

        Seen run = Assert.Single(seen);
        Assert.True(run.HomeExists);
        Assert.Contains(SecretSystem, run.Agents);
        Assert.DoesNotContain(SecretSystem, run.Prompt);
    }

    [Fact]
    public async Task System_text_over_24000_bytes_is_refused_before_any_process_starts_and_bytes_are_what_count()
    {
        if (!Applies) { return; }

        var runner = new FakeProcessRunner();
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; });
        // 9,000 three-byte characters: 9,000 characters but 27,000 bytes.
        var tooBig = new ChatOptions { Instructions = new string('日', 9_000) };

        var ex = await Assert.ThrowsAsync<AntigravityPromptTooLargeException>(() => client.GetResponseAsync(Clients.User(), tooBig));

        Assert.Empty(runner.Requests);
        Assert.DoesNotContain("日", ex.Message);
    }

    [Fact]
    public async Task System_text_just_under_the_limit_is_accepted()
    {
        if (!Applies) { return; }

        var seen = new List<Seen>();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Capture(seen));
        using AntigravityChatClient client = Clients.Create(runner, o => { o.TemporaryDirectoryRoot = _root; o.IsolateAgyHome = true; });
        var options = new ChatOptions { Instructions = new string('日', 5_000) }; // 15,000 bytes plus the preamble

        await client.GetResponseAsync(Clients.User(), options);

        Assert.Single(seen);
    }

    [Fact]
    public void A_caller_cannot_set_HOME_while_the_home_is_isolated()
    {
        if (!Applies) { return; }

        var options = new AntigravityChatClientOptions { IsolateAgyHome = true };
        options.EnvironmentVariables["HOME"] = "/somewhere";

        Assert.Throws<ArgumentException>(() => new AntigravityChatClient(options));
    }

    [Fact]
    public void A_caller_may_set_HOME_when_isolation_is_off()
    {
        var options = new AntigravityChatClientOptions { IsolateAgyHome = false, SkipVersionCheck = true };
        options.EnvironmentVariables["HOME"] = "/somewhere";

        using var client = new AntigravityChatClient(options);

        Assert.NotNull(client);
    }

    [Fact]
    public void The_default_is_isolation_on_linux_and_off_elsewhere()
    {
        Assert.Equal(OperatingSystem.IsLinux(), new AntigravityChatClientOptions().IsolateAgyHome);
    }

    [Fact]
    public void The_prompt_builder_leaves_out_the_preamble_and_system_turns_when_they_travel_in_the_file()
    {
        var request = new ValidatedRequest([SecretSystem], [new PromptTurn(PromptRole.User, "hello")], ResponseKind.Text, null, null, null);

        string prompt = PromptBuilder.Build(request, "/starts with a slash", "abc123", PromptLimits.ForCurrentPlatform(100_000), systemTextInFile: true);

        Assert.DoesNotContain(SecretSystem, prompt);
        Assert.DoesNotContain("starts with a slash", prompt);
        Assert.DoesNotContain("[system|", prompt);
        Assert.StartsWith("Every turn below", prompt);
        Assert.Contains("[user|abc123]", prompt);
    }
}
