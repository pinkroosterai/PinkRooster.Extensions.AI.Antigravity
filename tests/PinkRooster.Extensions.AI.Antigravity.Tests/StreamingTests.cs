using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class StreamingTests
{
    private static async Task<List<ChatResponseUpdate>> Collect(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var list = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in updates)
        {
            list.Add(update);
        }

        return list;
    }

    private const string Init = """{"event": "init", "conversation_id": "c-1", "init": {"model": "gemini-3.8-flash-low", "cwd": "/x", "tools": []}}""";
    private const string Result =
        """{"event": "result", "result": {"conversation_id": "c-1", "status": "SUCCESS", "response": "ab\n", "duration_seconds": 1.5, "num_turns": 1, "usage": {"input_tokens": 10, "output_tokens": 2, "total_tokens": 12}}}""";

    private static string Delta(string text, string stepType = "agent_response", string state = "ACTIVE") =>
        JsonSerializer.Serialize(new
        {
            @event = "step_update",
            step_update = new { conversation_id = "c-1", step_index = 1, state, step_type = stepType, text_delta = text },
        });

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static FakeProcessRunner StreamRunner(string stdout, string stderr = "", int exitCode = 0) =>
        new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5\n")).Returns(FakeProcessRunner.Ok(stdout, stderr, exitCode));

    // The result event holds the fields of the JSON envelope, so the same run can be answered through the buffered call.
    private static string EnvelopeOf(string ndjson)
    {
        string last = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
        using JsonDocument document = JsonDocument.Parse(last);
        return document.RootElement.GetProperty("result").GetRawText();
    }

    [Theory]
    [InlineData("stream-one-step.ndjson")]
    [InlineData("stream-with-tool.ndjson")]
    public async Task Joined_text_updates_equal_the_buffered_answer_for_the_captured_runs(string fixture)
    {
        string ndjson = Fixture.Text(fixture);
        using AntigravityChatClient streaming = Clients.Create(StreamRunner(ndjson));
        using AntigravityChatClient direct = Clients.Create(StreamRunner(EnvelopeOf(ndjson)));

        ChatResponse combined = (await Collect(streaming.GetStreamingResponseAsync(Clients.User()))).ToChatResponse();
        ChatResponse response = await direct.GetResponseAsync(Clients.User());

        Assert.Equal(response.Text, combined.Text);
        Assert.Equal(response.Usage!.TotalTokenCount, combined.Usage!.TotalTokenCount);
        Assert.Equal(response.ResponseId, combined.ResponseId);
    }

    [Fact]
    public async Task Text_arrives_in_several_updates_and_steps_without_text_yield_nothing()
    {
        using AntigravityChatClient client = Clients.Create(StreamRunner(Fixture.Text("stream-with-tool.ndjson")));

        List<ChatResponseUpdate> updates = await Collect(client.GetStreamingResponseAsync(Clients.User()));

        List<ChatResponseUpdate> text = updates.Where(u => u.Contents.OfType<TextContent>().Any()).ToList();
        Assert.True(text.Count > 3, $"expected several text updates, got {text.Count}");
        Assert.All(text, u => Assert.Equal("afc9c574-9a85-4349-8881-5bed85a68a5d", u.ResponseId));
        Assert.StartsWith("I created", text[0].Text, StringComparison.Ordinal);
        Assert.Equal(text.Count + 1, updates.Count);
    }

    [Fact]
    public async Task The_final_update_carries_usage_identifiers_and_metadata_and_no_text()
    {
        using AntigravityChatClient client = Clients.Create(StreamRunner(Lines(Init, Delta("ab"), Delta("\n", state: "DONE"), Result)));

        List<ChatResponseUpdate> updates = await Collect(client.GetStreamingResponseAsync(Clients.User()));

        Assert.Equal(2, updates.Count);
        Assert.Equal("ab", updates[0].Text);
        Assert.Equal(ChatRole.Assistant, updates[0].Role);
        Assert.Null(updates[0].FinishReason);
        ChatResponseUpdate last = updates[1];
        Assert.Empty(last.Contents.OfType<TextContent>());
        Assert.Equal("c-1", last.ResponseId);
        Assert.Equal("gemini-3.8-flash-low", last.ModelId);
        Assert.Equal(ChatFinishReason.Stop, last.FinishReason);
        Assert.NotNull(last.CreatedAt);
        Assert.Equal(12, Assert.Single(last.Contents.OfType<UsageContent>()).Details.TotalTokenCount);
        Assert.Equal(1, last.AdditionalProperties![ResponseMapper.NumTurnsKey]);
    }

    [Theory]
    [InlineData("a\n", "\n", "a\n")]
    [InlineData("a\r\n", "", "a")]
    [InlineData("a\r", "\n", "a")]
    [InlineData("a\r", "", "a\r")]
    [InlineData("a", "b", "ab")]
    [InlineData("\n", "", "")]
    public void The_hold_withholds_exactly_one_trailing_terminator(string first, string second, string expectedJoined)
    {
        var hold = new TrailingTerminatorHold();

        string joined = hold.Push(first) + (second.Length > 0 ? hold.Push(second) : string.Empty) + hold.Finish();

        Assert.Equal(expectedJoined, joined);
    }

    [Fact]
    public async Task A_streaming_request_asks_agy_for_stream_json_and_a_buffered_one_does_not()
    {
        FakeProcessRunner streamRunner = StreamRunner(Lines(Init, Result));
        using AntigravityChatClient streaming = Clients.Create(streamRunner);
        FakeProcessRunner directRunner = StreamRunner(EnvelopeOf(Lines(Init, Result)));
        using AntigravityChatClient direct = Clients.Create(directRunner);

        await Collect(streaming.GetStreamingResponseAsync(Clients.User()));
        await direct.GetResponseAsync(Clients.User());

        Assert.Equal("stream-json", ArgumentAfter(streamRunner.Requests[1], "--output-format"));
        Assert.Equal("json", ArgumentAfter(directRunner.Requests[1], "--output-format"));
    }

    private static string? ArgumentAfter(AgyProcessRequest request, string flag)
    {
        int index = request.Arguments.ToList().IndexOf(flag);
        return index < 0 ? null : request.Arguments[index + 1];
    }

    [Fact]
    public async Task A_lone_trailing_carriage_return_is_text_and_is_still_streamed()
    {
        using AntigravityChatClient client = Clients.Create(StreamRunner(Lines(Init, Delta("a\r"), Result)));

        List<ChatResponseUpdate> updates = await Collect(client.GetStreamingResponseAsync(Clients.User()));

        Assert.Equal("a\r", updates.ToChatResponse().Text);
    }

    [Fact]
    public async Task A_run_without_text_gives_one_final_update()
    {
        using AntigravityChatClient client = Clients.Create(StreamRunner(Lines(Init, Result.Replace("ab\\n", string.Empty))));

        ChatResponseUpdate update = Assert.Single(await Collect(client.GetStreamingResponseAsync(Clients.User())));

        Assert.Empty(update.Contents.OfType<TextContent>());
        Assert.Single(update.Contents.OfType<UsageContent>());
    }

    [Fact]
    public async Task Unknown_events_and_blank_lines_are_ignored()
    {
        string stdout = Lines(Init, "", """{"event": "future_thing", "future_thing": {}}""", Delta("x", stepType: "tool"), Delta("ab"), Result);
        using AntigravityChatClient client = Clients.Create(StreamRunner(stdout));

        List<ChatResponseUpdate> updates = await Collect(client.GetStreamingResponseAsync(Clients.User()));

        Assert.Equal("ab", updates.ToChatResponse().Text);
    }

    [Fact]
    public async Task Schema_and_plain_json_requests_stay_on_the_buffered_path_and_yield_one_update()
    {
        FakeProcessRunner runner = StreamRunner("""{"status": "SUCCESS", "response": "{\"a\":1}\n", "conversation_id": "c-9"}""");
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponseUpdate update = Assert.Single(
            await Collect(client.GetStreamingResponseAsync(Clients.User(), new ChatOptions { ResponseFormat = ChatResponseFormat.Json })));

        Assert.Equal("""{"a":1}""", update.Text);
        Assert.Equal("json", ArgumentAfter(runner.Requests[1], "--output-format"));
    }

    [Fact]
    public async Task A_timeout_notice_after_text_throws_on_the_next_move_next_and_earlier_text_stands()
    {
        FakeProcessRunner runner = StreamRunner(Lines(Init, Delta("ab"), Result), stderr: "[agy] print timeout after 8s with turn in progress; returning partial output\n");
        using AntigravityChatClient client = Clients.Create(runner);
        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = client.GetStreamingResponseAsync(Clients.User()).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("ab", enumerator.Current.Text);
        await Assert.ThrowsAsync<AntigravityTimeoutException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task Our_own_timeout_throws_the_timeout_exception()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Fixture.Result(string.Empty, timedOut: true));
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityTimeoutException>(() => Collect(client.GetStreamingResponseAsync(Clients.User())));
    }

    [Fact]
    public async Task Output_over_the_limit_throws_the_output_exception()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Fixture.Result(string.Empty, overflow: true));
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityOutputException>(() => Collect(client.GetStreamingResponseAsync(Clients.User())));
    }

    [Fact]
    public async Task An_error_status_in_the_result_event_throws_the_run_exception_on_the_first_move_next()
    {
        string error = Result.Replace("\"SUCCESS\"", "\"ERROR\"").Replace("\"response\": \"ab\\n\"", "\"error\": \"blocked\"");
        using AntigravityChatClient client = Clients.Create(StreamRunner(Lines(Init, error)));
        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = client.GetStreamingResponseAsync(Clients.User()).GetAsyncEnumerator();

        await Assert.ThrowsAsync<AntigravityRunException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task A_denied_action_with_no_answer_throws_the_run_exception()
    {
        string denied = Result.Replace("\"response\": \"ab\\n\"", "\"response\": \"\", \"denied_actions\": [{\"action\": \"command\"}]");
        using AntigravityChatClient client = Clients.Create(StreamRunner(Lines(Init, denied)));

        await Assert.ThrowsAsync<AntigravityRunException>(() => Collect(client.GetStreamingResponseAsync(Clients.User())));
    }

    [Fact]
    public async Task A_line_that_is_not_json_throws_the_output_exception_and_never_quotes_it()
    {
        using AntigravityChatClient client = Clients.Create(StreamRunner(Lines(Init, "this is not json SECRET-ANSWER", Result)));

        AntigravityOutputException ex = await Assert.ThrowsAsync<AntigravityOutputException>(() => Collect(client.GetStreamingResponseAsync(Clients.User())));

        Assert.DoesNotContain("SECRET-ANSWER", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_that_ends_without_a_result_event_fails_by_its_exit_code()
    {
        using AntigravityChatClient zero = Clients.Create(StreamRunner(Lines(Init, Delta("ab"))));
        using AntigravityChatClient one = Clients.Create(StreamRunner(Lines(Init, Delta("ab")), "boom", exitCode: 1));

        await Assert.ThrowsAsync<AntigravityOutputException>(() => Collect(zero.GetStreamingResponseAsync(Clients.User())));
        await Assert.ThrowsAsync<AntigravityProcessException>(() => Collect(one.GetStreamingResponseAsync(Clients.User())));
    }

    [Fact]
    public async Task Stopping_early_releases_the_concurrency_slot()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok(Lines(Init, Delta("a"), Delta("b"), Result)))
            .Returns(FakeProcessRunner.Ok(Lines(Init, Delta("c"), Result)));
        using AntigravityChatClient client = Clients.Create(runner, o => o.MaxConcurrency = 1);

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync(Clients.User()))
        {
            break;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        List<ChatResponseUpdate> second = await Collect(client.GetStreamingResponseAsync(Clients.User(), cancellationToken: cts.Token));
        Assert.Equal("c", second.ToChatResponse().Text);
    }

    [Fact]
    public async Task Nothing_runs_until_the_first_update_is_requested()
    {
        FakeProcessRunner runner = StreamRunner(Lines(Init, Result));
        using AntigravityChatClient client = Clients.Create(runner);

        IAsyncEnumerable<ChatResponseUpdate> updates = client.GetStreamingResponseAsync(Clients.User());

        Assert.Empty(runner.Requests);
        await Collect(updates);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task A_failing_run_surfaces_on_the_first_move_next()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Fixture.Result(string.Empty, exitCode: 1));
        using AntigravityChatClient client = Clients.Create(runner);

        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = client.GetStreamingResponseAsync(Clients.User()).GetAsyncEnumerator();

        await Assert.ThrowsAsync<AntigravityProcessException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task Refused_input_surfaces_on_the_first_move_next_not_when_the_call_is_made()
    {
        using AntigravityChatClient client = Clients.Create(Clients.Runner());
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => 1, "one")] };

        IAsyncEnumerable<ChatResponseUpdate> updates = client.GetStreamingResponseAsync(Clients.User(), options);

        await Assert.ThrowsAsync<NotSupportedException>(() => Collect(updates));
    }

    [Fact]
    public async Task Use_after_dispose_surfaces_on_the_first_move_next()
    {
        AntigravityChatClient client = Clients.Create(Clients.Runner());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => Collect(client.GetStreamingResponseAsync(Clients.User())));
    }

    [Fact]
    public async Task The_cancellation_token_given_to_the_enumerator_is_honored()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using IAsyncEnumerator<ChatResponseUpdate> enumerator =
            client.GetStreamingResponseAsync(Clients.User()).GetAsyncEnumerator(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());

        Assert.Empty(runner.Requests);
    }
}
