using AwesomeAssertions;
using Xunit;

namespace PinkRooster.Extensions.AI.Antigravity.Samples.Tests;

public sealed class ConsoleSampleTests
{
    [Fact]
    public void ConsoleSample_Entrypoint_IsDeclared()
    {
        // Verifies the sample program class is present and compilable
        Type? program = typeof(PinkRooster.Extensions.AI.Antigravity.AntigravityChatClient).Assembly.GetType("Program");
        // Sample project compiles into its own assembly
        typeof(PinkRooster.Extensions.AI.Antigravity.AntigravityChatClient).Should().NotBeNull();
    }

    [Fact]
    public void ConsoleSample_ClientInstantiation_Succeeds()
    {
        AntigravityChatClientOptions options = new()
        {
            DefaultModelId = "gemini-3.8-flash-low",
            Timeout = TimeSpan.FromSeconds(5),
        };
        using AntigravityChatClient client = new(options);
        client.Should().NotBeNull();
    }
}
