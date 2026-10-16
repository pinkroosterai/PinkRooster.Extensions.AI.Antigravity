using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests.Support;

internal static class Clients
{
    public const string FakeAgyPath = "/fake/agy";

    /// <summary>A client over a fake runner. The version check, when not skipped, consumes the first scripted result.</summary>
    public static AntigravityChatClient Create(
        IAgyProcessRunner runner,
        Action<AntigravityChatClientOptions>? configure = null,
        ILogger? logger = null)
    {
        var options = new AntigravityChatClientOptions();
        configure?.Invoke(options);
        return new AntigravityChatClient(options, logger, runner, _ => FakeAgyPath);
    }

    /// <summary>A runner that answers the version check and then each request with the given fixtures.</summary>
    public static FakeProcessRunner Runner(params string[] stdoutFixtures)
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5\n"));
        foreach (string fixture in stdoutFixtures)
        {
            runner.Returns(Fixture.Result(fixture));
        }

        return runner;
    }

    public static List<ChatMessage> User(string text = "hello") => [new(ChatRole.User, text)];
}
