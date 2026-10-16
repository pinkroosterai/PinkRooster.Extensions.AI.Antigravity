using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>FR-025, NFR-003: the concurrency limit holds with real processes, and a waiting request can be cancelled.</summary>
public class ConcurrencyTests
{
    private const string Envelope = """{"status":"SUCCESS","response":"pong\n","conversation_id":"c"}""";

    private static AntigravityChatClient Client(IAgyProcessRunner runner, int maxConcurrency, int sleepMs) =>
        new(
            new AntigravityChatClientOptions
            {
                ExecutablePath = FakeAgyHost.Executable,
                SkipVersionCheck = true,
                MaxConcurrency = maxConcurrency,
                EnvironmentVariables = { ["FAKE_AGY_STDOUT"] = Envelope, ["FAKE_AGY_SLEEP_MS"] = sleepMs.ToString() },
            },
            logger: null,
            runner,
            resolvePath: null);

    private static List<ChatMessage> User(string text) => [new(ChatRole.User, text)];

    [Fact]
    public async Task Ten_simultaneous_requests_never_run_more_than_four_processes_at_once()
    {
        var runner = new CountingRunner(new AgyProcessRunner());
        using AntigravityChatClient client = Client(runner, maxConcurrency: 4, sleepMs: 400);

        ChatResponse[] responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => client.GetResponseAsync(User("request " + i))));

        Assert.All(responses, r => Assert.Equal("pong", r.Text));
        Assert.Equal(10, runner.Runs);
        Assert.InRange(runner.MaxOverlap, 2, 4);
    }

    [Fact]
    public async Task A_request_waiting_for_a_slot_is_cancelled_without_starting_a_process()
    {
        var runner = new CountingRunner(new AgyProcessRunner());
        using AntigravityChatClient client = Client(runner, maxConcurrency: 1, sleepMs: 1500);

        Task<ChatResponse> first = client.GetResponseAsync(User("first"));
        await runner.FirstStarted;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(User("second"), null, cts.Token));

        Assert.Equal(1, runner.Runs);
        Assert.Equal("pong", (await first).Text);
    }

    [Fact]
    public async Task One_slow_request_does_not_block_a_second_client()
    {
        var runner = new CountingRunner(new AgyProcessRunner());
        using AntigravityChatClient slow = Client(runner, maxConcurrency: 1, sleepMs: 1500);
        using AntigravityChatClient other = Client(runner, maxConcurrency: 1, sleepMs: 0);

        Task<ChatResponse> blocked = slow.GetResponseAsync(User("slow"));
        await runner.FirstStarted;

        ChatResponse quick = await other.GetResponseAsync(User("quick"));

        Assert.Equal("pong", quick.Text);
        Assert.False(blocked.IsCompleted);
        await blocked;
    }

    private sealed class CountingRunner(IAgyProcessRunner inner) : IAgyProcessRunner
    {
        private readonly TaskCompletionSource _firstStarted = new();
        private int _running;
        private int _runs;
        private int _maxOverlap;

        public Task FirstStarted => _firstStarted.Task;

        public int Runs => Volatile.Read(ref _runs);

        public int MaxOverlap => Volatile.Read(ref _maxOverlap);

        public async Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            int now = Interlocked.Increment(ref _running);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxOverlap)) && Interlocked.CompareExchange(ref _maxOverlap, now, seen) != seen)
            {
            }

            _firstStarted.TrySetResult();
            try
            {
                return await inner.RunAsync(request, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
