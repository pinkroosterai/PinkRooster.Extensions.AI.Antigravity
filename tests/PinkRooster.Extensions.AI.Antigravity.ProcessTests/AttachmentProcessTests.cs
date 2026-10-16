using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.ProcessTests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.ProcessTests;

/// <summary>Image input over the real process runner: the bytes reach the working directory of the fake agy.</summary>
public sealed class AttachmentProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agy-attach-e2e-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task The_images_are_files_in_the_working_directory_when_agy_runs_and_gone_afterwards()
    {
        using var client = new AntigravityChatClient(new AntigravityChatClientOptions
        {
            ExecutablePath = FakeAgyHost.Executable,
            SkipVersionCheck = true,
            TemporaryDirectoryRoot = _root,
            EnvironmentVariables = { ["FAKE_AGY_ECHO_ATTACHMENTS"] = "1" },
        });
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, [new TextContent("what?"), new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 9 }, "image/png"), new DataContent(new byte[] { 0xFF, 0xD8 }, "image/jpeg")]),
        };

        ChatResponse response = await client.GetResponseAsync(messages);

        Assert.Contains("files=image-1.png:5:89504E47,image-2.jpg:2:FFD8", response.Text);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }
}
