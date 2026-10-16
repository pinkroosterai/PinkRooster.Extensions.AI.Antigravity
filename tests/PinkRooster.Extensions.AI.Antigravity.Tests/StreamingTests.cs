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

    [Fact]
    public async Task The_whole_answer_arrives_as_one_update_with_usage_and_identifiers()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner, o => o.DefaultModelId = "gemini-3.8-flash-low");

        List<ChatResponseUpdate> updates = await Collect(client.GetStreamingResponseAsync(Clients.User()));

        ChatResponseUpdate update = Assert.Single(updates);
        Assert.Equal("pong", update.Text);
        Assert.Equal(ChatRole.Assistant, update.Role);
        Assert.Equal("00000000-0000-0000-0000-000000000001", update.ResponseId);
        Assert.Equal("gemini-3.8-flash-low", update.ModelId);
        Assert.Equal(ChatFinishReason.Stop, update.FinishReason);
        Assert.NotNull(update.CreatedAt);
        UsageContent usage = Assert.Single(update.Contents.OfType<UsageContent>());
        Assert.Equal(16879, usage.Details.TotalTokenCount);
        Assert.Equal(1, update.AdditionalProperties![ResponseMapper.NumTurnsKey]);
    }

    [Fact]
    public async Task The_updates_combine_back_into_the_same_response_as_the_non_streaming_call()
    {
        FakeProcessRunner a = Clients.Runner("success-text.json");
        FakeProcessRunner b = Clients.Runner("success-text.json");
        using AntigravityChatClient streaming = Clients.Create(a);
        using AntigravityChatClient direct = Clients.Create(b);

        ChatResponse combined = (await Collect(streaming.GetStreamingResponseAsync(Clients.User()))).ToChatResponse();
        ChatResponse response = await direct.GetResponseAsync(Clients.User());

        Assert.Equal(response.Text, combined.Text);
        Assert.Equal(response.Usage!.TotalTokenCount, combined.Usage!.TotalTokenCount);
    }

    [Fact]
    public async Task Nothing_runs_until_the_first_update_is_requested()
    {
        FakeProcessRunner runner = Clients.Runner("success-text.json");
        using AntigravityChatClient client = Clients.Create(runner);

        IAsyncEnumerable<ChatResponseUpdate> updates = client.GetStreamingResponseAsync(Clients.User());

        Assert.Empty(runner.Requests);
        await Collect(updates);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task A_failing_run_surfaces_on_the_first_move_next()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Fixture.Result("exit1-empty-prompt.json", exitCode: 1));
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
