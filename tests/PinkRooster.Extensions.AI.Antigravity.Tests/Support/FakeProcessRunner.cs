using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests.Support;

/// <summary>A process runner that returns scripted results and records what it was asked to run.</summary>
internal sealed class FakeProcessRunner : IAgyProcessRunner
{
    private readonly Queue<Func<AgyProcessRequest, AgyProcessResult>> _script = new();

    public List<AgyProcessRequest> Requests { get; } = [];

    public FakeProcessRunner Returns(AgyProcessResult result)
    {
        _script.Enqueue(_ => result);
        return this;
    }

    public FakeProcessRunner Returns(Func<AgyProcessRequest, AgyProcessResult> result)
    {
        _script.Enqueue(result);
        return this;
    }

    public static AgyProcessResult Ok(string stdout, string stderr = "", int exitCode = 0) =>
        new(exitCode, stdout, stderr, TimedOut: false, OutputLimitExceeded: false, TimeSpan.FromMilliseconds(5));

    public Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        if (_script.Count == 0)
        {
            throw new InvalidOperationException("The fake runner was called more often than scripted.");
        }

        return Task.FromResult(_script.Dequeue()(request));
    }
}
