using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class LoggingTests
{
    private const string SecretPrompt = "SECRET-PROMPT";
    private const string SecretSystem = "SECRET-SYSTEM";
    private const string SecretAnswer = "SECRET-ANSWER";
    private const string SecretStderr = "SECRET-STDERR";
    private const string SecretEnvironment = "SECRET-ENV";
    private const string SecretSchema = "SECRET-SCHEMA";

    private static AntigravityChatClient Client(IAgyProcessRunner runner, CapturingLogger? logger, bool structured = false) =>
        Clients.Create(runner, o =>
        {
            o.EnvironmentVariables["SOME_TOKEN"] = SecretEnvironment;
            o.DefaultModelId = "gemini-3.8-flash-low";
        }, logger);

    private static ChatOptions Options() => new()
    {
        Instructions = SecretSystem,
        Temperature = 0.1f,
        AdditionalProperties = new AdditionalPropertiesDictionary { ["k"] = SecretSchema },
    };

    private static AgyProcessResult Answer(string text, string stderr = "", int exit = 0) =>
        FakeProcessRunner.Ok("{\"status\":\"SUCCESS\",\"response\":\"" + text + "\",\"conversation_id\":\"c\",\"usage\":{\"input_tokens\":3,\"output_tokens\":4,\"total_tokens\":7}}", stderr, exit);

    private static void AssertClean(CapturingLogger logger)
    {
        string everything = logger.Everything();
        foreach (string secret in new[] { SecretPrompt, SecretSystem, SecretAnswer, SecretStderr, SecretEnvironment, SecretSchema })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    [Fact]
    public async Task A_successful_request_logs_a_summary_without_any_content()
    {
        var logger = new CapturingLogger();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Answer(SecretAnswer, SecretStderr));
        using AntigravityChatClient client = Client(runner, logger);

        ChatResponse response = await client.GetResponseAsync(Clients.User(SecretPrompt), Options());

        Assert.Equal(SecretAnswer, response.Text);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("agy request completed"));
        AssertClean(logger);
    }

    [Fact]
    public async Task A_streaming_request_logs_a_summary_without_any_content()
    {
        var logger = new CapturingLogger();
        string stdout = """
            {"event":"init","conversation_id":"c","init":{"model":"m"}}
            {"event":"step_update","step_update":{"step_type":"agent_response","text_delta":"@ANSWER@"}}
            {"event":"result","result":{"status":"SUCCESS","response":"@ANSWER@","usage":{"input_tokens":3,"output_tokens":4,"total_tokens":7}}}
            """.Replace("@ANSWER@", SecretAnswer);
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(FakeProcessRunner.Ok(stdout, SecretStderr));
        using AntigravityChatClient client = Client(runner, logger);

        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(Clients.User(SecretPrompt), Options()))
        {
            updates.Add(update);
        }

        Assert.Equal(SecretAnswer, updates.ToChatResponse().Text);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("agy request completed"));
        AssertClean(logger);
    }

    [Fact]
    public async Task The_completion_entry_carries_model_time_prompt_length_and_token_counts()
    {
        var logger = new CapturingLogger();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Answer("x"));
        using AntigravityChatClient client = Client(runner, logger);

        await client.GetResponseAsync(Clients.User("q"));

        LogEntry entry = Assert.Single(logger.Entries, e => e.Message.Contains("agy request completed"));
        Assert.Contains("gemini-3.8-flash-low", entry.Message);
        Assert.Contains("3 input and 4 output tokens", entry.Message);
        Assert.Matches(@"prompt \d+ characters", entry.Message);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("process")]
    [InlineData("run")]
    [InlineData("output")]
    [InlineData("auth")]
    public async Task Failures_log_only_the_exception_type_and_numbers(string kind)
    {
        var logger = new CapturingLogger();
        AgyProcessResult failure = kind switch
        {
            "timeout" => new AgyProcessResult(null, "", SecretStderr, TimedOut: true, OutputLimitExceeded: false, TimeSpan.FromSeconds(1)),
            "process" => FakeProcessRunner.Ok("", SecretStderr, exitCode: 1),
            "run" => FakeProcessRunner.Ok("{\"status\":\"ERROR\",\"error\":\"" + SecretStderr + "\"}"),
            "output" => FakeProcessRunner.Ok("not json " + SecretAnswer, SecretStderr),
            _ => FakeProcessRunner.Ok("", "authentication required " + SecretStderr, exitCode: 1),
        };
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(failure);
        using AntigravityChatClient client = Client(runner, logger);

        var ex = await Assert.ThrowsAnyAsync<AntigravityException>(() => client.GetResponseAsync(Clients.User(SecretPrompt), Options()));

        Assert.Contains(logger.Entries, e => e.Message.Contains("agy request failed") && e.Message.Contains(ex.GetType().Name));
        AssertClean(logger);
        Assert.DoesNotContain(SecretPrompt, ex.Message);
        Assert.DoesNotContain(SecretSystem, ex.Message);
        Assert.DoesNotContain(SecretEnvironment, ex.ToString());
    }

    [Fact]
    public async Task Refused_input_logs_nothing_with_content()
    {
        var logger = new CapturingLogger();
        using AntigravityChatClient client = Client(Clients.Runner(), logger);
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => SecretSchema, "secret_tool")] };

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(Clients.User(SecretPrompt), options));

        AssertClean(logger);
    }

    [Fact]
    public async Task No_logger_means_nothing_is_written_and_nothing_fails()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Answer("fine"));
        using AntigravityChatClient client = Client(runner, logger: null);

        ChatResponse response = await client.GetResponseAsync(Clients.User());

        Assert.Equal("fine", response.Text);
    }

    [Fact]
    public async Task The_library_never_logs_at_error_or_critical_level()
    {
        var logger = new CapturingLogger();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(FakeProcessRunner.Ok("", "boom", exitCode: 2));
        using AntigravityChatClient client = Client(runner, logger);

        await Assert.ThrowsAsync<AntigravityProcessException>(() => client.GetResponseAsync(Clients.User()));

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }
}
