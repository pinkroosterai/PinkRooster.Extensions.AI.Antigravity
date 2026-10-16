using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>The whole client over the real process runner and the fake agy.</summary>
public class EndToEndTests
{
    [Fact]
    public async Task A_plain_request_through_the_real_runner_and_the_fake_agy_returns_the_expected_response()
    {
        const string envelope = """{"conversation_id":"c-1","status":"SUCCESS","response":"pong\n","duration_seconds":0.5,"num_turns":1,"usage":{"input_tokens":10,"output_tokens":2,"thinking_tokens":1,"cache_read_tokens":3,"total_tokens":12}}""";
        using var client = new AntigravityChatClient(new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            SkipVersionCheck = true,
            DefaultModelId = "gemini-3.8-flash-low",
            EnvironmentVariables = { ["FAKE_AGY_STDOUT"] = envelope },
        });

        ChatResponse response = await client.GetResponseAsync("Say pong");

        Assert.Equal("pong", response.Text);
        Assert.Equal("c-1", response.ResponseId);
        Assert.Equal("gemini-3.8-flash-low", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(12, response.Usage!.TotalTokenCount);
        Assert.Equal(3, response.Usage.CachedInputTokenCount);
    }

    [Fact]
    public async Task The_version_check_runs_through_the_real_runner()
    {
        using var client = new AntigravityChatClient(new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            EnvironmentVariables = { ["FAKE_AGY_STDOUT"] = """{"status":"SUCCESS","response":"ok"}""" },
        });

        ChatResponse response = await client.GetResponseAsync("hi");

        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task A_too_old_fake_agy_is_refused_by_the_version_check()
    {
        using var client = new AntigravityChatClient(new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            EnvironmentVariables = { ["FAKE_AGY_VERSION"] = "1.2.0" },
        });

        var ex = await Assert.ThrowsAsync<AntigravityVersionException>(() => client.GetResponseAsync("hi"));

        Assert.Equal("1.2.0", ex.FoundVersion);
    }

    private static (string Home, string Cwd, string Agents, string Args) ParseContext(string text)
    {
        string Value(string key) => text.Split('\n').First(l => l.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
        string agents = text[(text.IndexOf("agents=", StringComparison.Ordinal) + "agents=".Length)..text.IndexOf("\nargs=", StringComparison.Ordinal)];
        return (Value("home"), Value("cwd"), agents, text[(text.IndexOf("args=", StringComparison.Ordinal) + 5)..]);
    }

    [Fact]
    public async Task Every_request_runs_with_a_fresh_home_and_the_system_text_in_agents_md_through_the_real_runner()
    {
        if (!Internal.InvocationBuilder.IsolationApplies)
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "agy-e2e-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new AntigravityChatClient(new AntigravityChatClientOptions
            {
                ExecutablePath = FakeAgyHost.Executable,
                SkipVersionCheck = true,
                TemporaryDirectoryRoot = root,
                EnvironmentVariables = { ["FAKE_AGY_ECHO_CONTEXT"] = "1" },
            });

            var first = ParseContext((await client.GetResponseAsync("Say pong", new ChatOptions { Instructions = "SECRET-SYSTEM" })).Text);
            var second = ParseContext((await client.GetResponseAsync("Say pong again")).Text);

            Assert.NotEqual(Environment.GetEnvironmentVariable("HOME"), first.Home);
            Assert.NotEqual(first.Home, second.Home);
            Assert.StartsWith(root, first.Home, StringComparison.Ordinal);
            Assert.Contains("SECRET-SYSTEM", first.Agents);
            Assert.Contains(AntigravityChatClientOptions.DefaultSystemPreamble, first.Agents);
            Assert.DoesNotContain("SECRET-SYSTEM", first.Args);
            Assert.DoesNotContain(AntigravityChatClientOptions.DefaultSystemPreamble, first.Args);
            Assert.False(Directory.Exists(first.Home));
            Assert.False(Directory.Exists(first.Cwd));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
