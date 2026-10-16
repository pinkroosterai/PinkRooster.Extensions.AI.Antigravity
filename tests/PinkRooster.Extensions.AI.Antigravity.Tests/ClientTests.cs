using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public sealed class ClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agy-client-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string PromptOf(AgyProcessRequest request) => request.Arguments[^1];

    // The prompt carries a random six-digit code in its turn headers; blank it so two prompts can be compared.
    private static string WithoutNonce(string prompt)
    {
        string nonce = System.Text.RegularExpressions.Regex.Match(prompt, @"\[user\|([0-9a-f]{6})\]").Groups[1].Value;
        Assert.NotEmpty(nonce);
        return prompt.Replace(nonce, "NONCE", StringComparison.Ordinal);
    }

    // A plain request

    [Fact]
    public async Task A_plain_request_returns_the_answer_as_one_assistant_message()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User("Say pong"));

        Assert.Equal("pong", response.Text);
        Assert.Equal(ChatRole.Assistant, Assert.Single(response.Messages).Role);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(16879, response.Usage!.TotalTokenCount);
    }

    [Fact]
    public async Task The_prompt_reaches_agy_as_the_last_argument_after_p()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner);

        await client.GetResponseAsync(Clients.User("Say pong"));

        AgyProcessRequest run = runner.Requests[1];
        Assert.Equal(Clients.FakeAgyPath, run.ExecutablePath);
        Assert.Equal("-p", run.Arguments[^2]);
        Assert.Contains("Say pong", PromptOf(run));
        Assert.StartsWith(AntigravityChatClientOptions.DefaultSystemPreamble, PromptOf(run));
    }

    [Fact]
    public async Task The_version_is_checked_once_for_the_first_request_only_and_each_request_runs_agy_once()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json", "success-text.json");
        using AntigravityChatClient client = Clients.Create(runner);

        await client.GetResponseAsync(Clients.User());
        await client.GetResponseAsync(Clients.User());

        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal(["--version"], runner.Requests[0].Arguments);
        Assert.DoesNotContain("--version", runner.Requests[1].Arguments);
        Assert.DoesNotContain("--version", runner.Requests[2].Arguments);
    }

    [Fact]
    public async Task Each_request_runs_in_its_own_empty_directory_that_is_gone_afterwards()
    {
        var seen = new List<string>();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(r => { seen.Add(r.WorkingDirectory); Assert.Empty(Directory.EnumerateFileSystemEntries(r.WorkingDirectory)); return Fixture.Result("success-text.json"); })
            .Returns(r => { seen.Add(r.WorkingDirectory); return Fixture.Result("success-text.json"); });
        using AntigravityChatClient client = Clients.Create(runner, o => o.TemporaryDirectoryRoot = _root);

        await client.GetResponseAsync(Clients.User());
        await client.GetResponseAsync(Clients.User());

        Assert.Equal(2, seen.Distinct().Count());
        Assert.All(seen, d => Assert.StartsWith(_root, d));
        Assert.All(seen, d => Assert.False(Directory.Exists(d)));
    }

    [Fact]
    public async Task The_request_directory_is_removed_when_the_run_fails()
    {
        string? directory = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(r => { directory = r.WorkingDirectory; return Fixture.Result("exit1-empty-prompt.json", exitCode: 1); });
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityProcessException>(() => client.GetResponseAsync(Clients.User()));

        Assert.NotNull(directory);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Timeout_and_output_limit_options_reach_the_runner()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o => { o.Timeout = TimeSpan.FromSeconds(42); o.MaxOutputBytes = 1234; });

        await client.GetResponseAsync(Clients.User());

        Assert.Equal(TimeSpan.FromSeconds(42), runner.Requests[1].Timeout);
        Assert.Equal(1234, runner.Requests[1].MaxOutputBytes);
    }

    [Fact]
    public async Task Environment_variables_and_extra_arguments_reach_the_run()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o =>
        {
            o.EnvironmentVariables["AGY_ADC_AUTH"] = "true";
            o.AdditionalArguments.Add("--log-file");
            o.AdditionalArguments.Add("agy.log");
        });

        await client.GetResponseAsync(Clients.User());

        Assert.Equal("true", runner.Requests[1].EnvironmentOverrides["AGY_ADC_AUTH"]);
        Assert.Contains("--log-file", runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task Empty_success_gives_empty_text()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok(Fixture.Text("timeout-empty-success.json")));
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User());

        Assert.Equal(string.Empty, response.Text);
    }

    // Model and effort

    [Fact]
    public async Task The_request_model_beats_the_default_and_is_reported_back()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o => o.DefaultModelId = "default-model");

        ChatResponse response = await client.GetResponseAsync(Clients.User(), new ChatOptions { ModelId = "request-model" });

        int index = runner.Requests[1].Arguments.ToList().IndexOf("--model");
        Assert.Equal("request-model", runner.Requests[1].Arguments[index + 1]);
        Assert.Equal("request-model", response.ModelId);
    }

    [Fact]
    public async Task The_default_model_is_used_when_the_request_names_none()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o => o.DefaultModelId = "default-model");

        ChatResponse response = await client.GetResponseAsync(Clients.User());

        Assert.Equal("default-model", response.ModelId);
        Assert.Contains("default-model", runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task No_model_means_no_model_flag_and_no_model_id_in_the_response()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User());

        Assert.DoesNotContain("--model", runner.Requests[1].Arguments);
        Assert.Null(response.ModelId);
    }

    [Fact]
    public async Task Reasoning_effort_from_the_request_is_passed_for_a_base_model_and_the_default_is_the_fallback()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json", "success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o => { o.DefaultModelId = "gemini-3.8-flash"; o.DefaultReasoningEffort = ReasoningEffort.Low; });

        await client.GetResponseAsync(Clients.User(), new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High } });
        await client.GetResponseAsync(Clients.User());

        Assert.Equal("high", runner.Requests[1].Arguments[runner.Requests[1].Arguments.ToList().IndexOf("--effort") + 1]);
        Assert.Equal("low", runner.Requests[2].Arguments[runner.Requests[2].Arguments.ToList().IndexOf("--effort") + 1]);
    }

    [Fact]
    public async Task Reasoning_effort_is_left_out_with_a_debug_entry_for_a_level_qualified_model()
    {
        var logger = new CapturingLogger();
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o => o.DefaultModelId = "gemini-3.8-flash-low", logger);

        await client.GetResponseAsync(Clients.User(), new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High } });

        Assert.DoesNotContain("--effort", runner.Requests[1].Arguments);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Reasoning effort not passed"));
    }

    // Ignored options, caller's options

    [Fact]
    public async Task Ignored_options_change_nothing_on_the_command_line_and_are_named_in_one_debug_entry()
    {
        var logger = new CapturingLogger();
        FakeProcessRunner plain = Clients.Runner("success-text.json");
        FakeProcessRunner tuned = Clients.Runner("success-text.json");
        using AntigravityChatClient a = Clients.Create(plain);
        using AntigravityChatClient b = Clients.Create(tuned, logger: logger);

        await a.GetResponseAsync(Clients.User("same"));
        await b.GetResponseAsync(Clients.User("same"), new ChatOptions { Temperature = 0.1f, TopP = 0.5f, MaxOutputTokens = 5, StopSequences = ["x"], ConversationId = "SECRET-ID" });

        Assert.Equal(plain.Requests[1].Arguments.SkipLast(1), tuned.Requests[1].Arguments.SkipLast(1));
        Assert.Equal(WithoutNonce(PromptOf(plain.Requests[1])), WithoutNonce(PromptOf(tuned.Requests[1])));
        LogEntry entry = Assert.Single(logger.Entries, e => e.Message.StartsWith("Ignored ChatOptions"));
        Assert.Contains("Temperature", entry.Message);
        Assert.DoesNotContain("SECRET-ID", logger.Everything());
    }

    [Fact]
    public async Task The_callers_options_instance_is_not_modified()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner);
        var options = new ChatOptions { ModelId = "m", Instructions = "i", Temperature = 0.3f };

        await client.GetResponseAsync(Clients.User(), options);

        Assert.Equal("m", options.ModelId);
        Assert.Equal("i", options.Instructions);
        Assert.Equal(0.3f, options.Temperature);
        Assert.Null(options.ResponseFormat);
        Assert.Null(options.Tools);
    }

    [Fact]
    public async Task Changing_the_options_after_construction_has_no_effect()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        var options = new AntigravityChatClientOptions { DefaultModelId = "before" };
        using var client = new AntigravityChatClient(options, null, runner, _ => Clients.FakeAgyPath);

        options.DefaultModelId = "after";
        options.EnvironmentVariables["X"] = "late";
        options.AdditionalArguments.Add("--late");
        await client.GetResponseAsync(Clients.User());

        Assert.Contains("before", runner.Requests[1].Arguments);
        Assert.DoesNotContain("--late", runner.Requests[1].Arguments);
        Assert.False(runner.Requests[1].EnvironmentOverrides.ContainsKey("X"));
    }

    [Fact]
    public void Invalid_options_are_rejected_when_the_client_is_created()
    {
        Assert.Throws<ArgumentException>(() => new AntigravityChatClient(new AntigravityChatClientOptions { MaxConcurrency = 0 }));
        Assert.Throws<ArgumentException>(() => new AntigravityChatClient(new AntigravityChatClientOptions { AdditionalArguments = { "--dangerously-skip-permissions" } }));
    }

    // Refused before anything runs

    [Fact]
    public async Task Tools_are_refused_before_any_process_starts()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => 1, "one")] };

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(Clients.User(), options));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Non_text_content_is_refused_before_any_process_starts()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);
        var messages = new List<ChatMessage> { new(ChatRole.User, [new TextContent("q"), new DataContent(new byte[] { 1 }, "image/png")]) };

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(messages));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Empty_input_and_unknown_roles_are_refused_before_any_process_starts()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync([new ChatMessage(new ChatRole("tool"), "x")]));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Null_messages_throw_ArgumentNull()
    {
        using AntigravityChatClient client = Clients.Create(Clients.Runner());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetResponseAsync((IEnumerable<ChatMessage>)null!));
    }

    [Fact]
    public async Task A_prompt_over_the_limit_is_refused_before_any_process_starts()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner, o => o.MaxPromptLength = 50);

        var ex = await Assert.ThrowsAsync<AntigravityPromptTooLargeException>(() => client.GetResponseAsync(Clients.User(new string('x', 500))));

        Assert.Equal(50, ex.Limit);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_missing_executable_is_reported_before_any_process_starts()
    {
        var runner = new FakeProcessRunner();
        using var client = new AntigravityChatClient(null, null, runner, _ => throw new AntigravityNotFoundException("missing", ["/x"]));

        await Assert.ThrowsAsync<AntigravityNotFoundException>(() => client.GetResponseAsync(Clients.User()));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_too_old_agy_stops_the_request_after_the_version_run()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.0.0"));
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityVersionException>(() => client.GetResponseAsync(Clients.User()));

        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task A_cancelled_token_starts_nothing()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(Clients.User(), null, cts.Token));

        Assert.Empty(runner.Requests);
    }

    // Failures are never retried

    [Theory]
    [InlineData("exit1-empty-prompt.json", 1, typeof(AntigravityProcessException))]
    [InlineData("error-safety-block.json", 0, typeof(AntigravityRunException))]
    [InlineData("denied-empty.json", 0, typeof(AntigravityRunException))]
    public async Task A_failed_run_is_reported_once_and_never_retried(string fixture, int exitCode, Type expected)
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Fixture.Result(fixture, exitCode));
        using AntigravityChatClient client = Clients.Create(runner);

        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync(Clients.User()));

        Assert.IsType(expected, ex);
        Assert.Equal(2, runner.Requests.Count); // the version check and one run
    }

    [Fact]
    public async Task A_timeout_is_reported_once_and_never_retried()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(new AgyProcessResult(null, "", "", TimedOut: true, OutputLimitExceeded: false, TimeSpan.FromSeconds(1)));
        using AntigravityChatClient client = Clients.Create(runner, o => o.Timeout = TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<AntigravityTimeoutException>(() => client.GetResponseAsync(Clients.User()));

        Assert.Equal(2, runner.Requests.Count);
    }

    // Warnings and information

    [Fact]
    public async Task An_answer_with_denied_actions_succeeds_with_one_warning_and_the_denial_in_additional_properties()
    {
        var logger = new CapturingLogger();
        FakeProcessRunner runner = Clients.Runner("denied-with-answer.json");
        using AntigravityChatClient client = Clients.Create(runner, logger: logger);

        ChatResponse response = await client.GetResponseAsync(Clients.User());

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.True(response.AdditionalProperties!.ContainsKey(ResponseMapper.DeniedActionsKey));
    }

    [Fact]
    public async Task A_newer_agy_than_tested_is_logged_once_at_information_level()
    {
        var logger = new CapturingLogger();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.9.0"))
            .Returns(Fixture.Result("success-text.json")).Returns(Fixture.Result("success-text.json"));
        using AntigravityChatClient client = Clients.Create(runner, logger: logger);

        await client.GetResponseAsync(Clients.User());
        await client.GetResponseAsync(Clients.User());

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("newer than the newest"));
    }

    [Fact]
    public async Task A_directory_that_cannot_be_deleted_is_a_warning_and_the_answer_is_still_returned()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        var logger = new CapturingLogger();
        string? locked = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(r =>
            {
                locked = Path.Combine(r.WorkingDirectory, "locked");
                Directory.CreateDirectory(locked);
                File.WriteAllText(Path.Combine(locked, "f"), "x");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(locked, UnixFileMode.None);
                }

                return Fixture.Result("success-text.json");
            });
        using AntigravityChatClient client = Clients.Create(runner, o => o.TemporaryDirectoryRoot = _root, logger);

        try
        {
            ChatResponse response = await client.GetResponseAsync(Clients.User());

            Assert.Equal("pong", response.Text);
            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("could not be deleted"));
        }
        finally
        {
            if (locked is not null && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    // Waiting for a slot

    [Fact]
    public async Task A_request_waiting_for_a_slot_can_be_cancelled_without_starting_a_process()
    {
        var release = new TaskCompletionSource();
        var runner = new BlockingRunner(release.Task);
        using AntigravityChatClient client = Clients.Create(runner, o => o.MaxConcurrency = 1);

        Task<ChatResponse> first = client.GetResponseAsync(Clients.User("first"));
        await runner.FirstRunStarted;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(Clients.User("second"), null, cts.Token));

        Assert.Equal(1, runner.RunCount);
        release.SetResult();
        Assert.Equal("pong", (await first).Text);
    }

    [Fact]
    public async Task No_more_than_the_configured_number_of_runs_overlap()
    {
        var runner = new BlockingRunner(Task.Delay(100));
        using AntigravityChatClient client = Clients.Create(runner, o => o.MaxConcurrency = 3);

        await Task.WhenAll(Enumerable.Range(0, 9).Select(i => client.GetResponseAsync(Clients.User("r" + i))));

        Assert.Equal(9, runner.RunCount);
        Assert.InRange(runner.MaxOverlap, 2, 3);
    }

    // Disposal and services

    [Fact]
    public async Task Use_after_dispose_throws_and_dispose_twice_is_harmless()
    {
        FakeProcessRunner runner = Clients.Runner();
        AntigravityChatClient client = Clients.Create(runner);

        client.Dispose();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetResponseAsync(Clients.User()));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void GetService_returns_metadata_and_the_client_and_nothing_else()
    {
        using AntigravityChatClient client = Clients.Create(Clients.Runner(), o => o.DefaultModelId = "gemini-3.8-flash-low");

        var metadata = Assert.IsType<ChatClientMetadata>(client.GetService(typeof(ChatClientMetadata)));
        Assert.Equal("antigravity", metadata.ProviderName);
        Assert.Null(metadata.ProviderUri);
        Assert.Equal("gemini-3.8-flash-low", metadata.DefaultModelId);
        Assert.Same(client, client.GetService(typeof(AntigravityChatClient)));
        Assert.Null(client.GetService(typeof(string)));
        Assert.Null(client.GetService(typeof(ChatClientMetadata), "key"));
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
    }

    [Fact]
    public void The_minimum_version_constant_is_the_specified_one()
    {
        Assert.Equal("1.3.5", AntigravityChatClient.MinimumAgyVersion);
    }

    /// <summary>A runner whose runs finish after a gate task, counting how many ran and how many overlapped.</summary>
    private sealed class BlockingRunner(Task gate) : IAgyProcessRunner
    {
        private readonly TaskCompletionSource _firstRunStarted = new();
        private int _running;
        private int _runCount;
        private int _maxOverlap;

        public Task FirstRunStarted => _firstRunStarted.Task;

        public int RunCount => Volatile.Read(ref _runCount);

        public int MaxOverlap => Volatile.Read(ref _maxOverlap);

        public Task<AgyProcessResult> RunStreamingAsync(AgyProcessRequest request, Action<string> onStandardOutputLine, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken)
        {
            if (request.Arguments.Contains("--version"))
            {
                return FakeProcessRunner.Ok("1.3.5");
            }

            Interlocked.Increment(ref _runCount);
            int now = Interlocked.Increment(ref _running);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxOverlap)) && Interlocked.CompareExchange(ref _maxOverlap, now, seen) != seen)
            {
            }

            _firstRunStarted.TrySetResult();
            try
            {
                await gate.WaitAsync(cancellationToken);
                return Fixture.Result("success-text.json");
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
