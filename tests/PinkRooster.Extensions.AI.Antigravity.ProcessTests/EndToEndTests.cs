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
}
