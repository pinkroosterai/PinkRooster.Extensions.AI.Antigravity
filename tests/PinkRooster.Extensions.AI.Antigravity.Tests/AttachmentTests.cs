using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>Image input: validation, prompt, files in the request directory, and the client around them.</summary>
public sealed class AttachmentTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "agy-attach-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static List<ChatMessage> UserWith(params AIContent[] contents) => [new(ChatRole.User, [.. contents])];

    private static string Envelope(string response) => JsonSerializer.Serialize(new { status = "SUCCESS", response, conversation_id = "c-1" });

    // ---- validator ----

    [Fact]
    public void An_image_becomes_an_attachment_with_a_library_chosen_path_and_a_pointer_in_the_turn_text()
    {
        ValidatedRequest request = RequestValidator.Validate(
            UserWith(new TextContent("what is this?"), new DataContent(Png, "image/png"), new DataContent(Png, "IMAGE/JPEG")), null);

        Assert.Equal(["attachments/image-1.png", "attachments/image-2.jpg"], request.Attachments!.Select(a => a.RelativePath));
        Assert.Equal(Png, request.Attachments![0].Data.ToArray());
        Assert.Equal(
            "what is this?\n[attachment: attachments/image-1.png]\n[attachment: attachments/image-2.jpg]",
            request.Turns.Single().Text);
    }

    [Fact]
    public void An_image_alone_is_a_valid_message_and_numbering_runs_across_messages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, [new DataContent(Png, "image/gif")]),
            new(ChatRole.Assistant, "a cat"),
            new(ChatRole.User, [new DataContent(Png, "image/webp")]),
        };

        ValidatedRequest request = RequestValidator.Validate(messages, null);

        Assert.Equal(["attachments/image-1.gif", "attachments/image-2.webp"], request.Attachments!.Select(a => a.RelativePath));
    }

    [Fact]
    public void A_caller_supplied_name_never_reaches_the_path()
    {
        var content = new DataContent(Png, "image/png") { Name = "../../etc/passwd" };

        ValidatedRequest request = RequestValidator.Validate(UserWith(content), null);

        Assert.Equal("attachments/image-1.png", request.Attachments!.Single().RelativePath);
        Assert.DoesNotContain("passwd", request.Turns.Single().Text);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/svg+xml")]
    [InlineData("text/plain")]
    public void Other_media_types_are_refused(string mediaType) =>
        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(UserWith(new DataContent(Png, mediaType)), null));

    [Fact]
    public void Images_in_system_or_assistant_messages_are_refused()
    {
        foreach (ChatRole role in new[] { ChatRole.System, ChatRole.Assistant })
        {
            var messages = new List<ChatMessage> { new(ChatRole.User, "q"), new(role, [new DataContent(Png, "image/png")]) };
            Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(messages, null));
        }
    }

    [Fact]
    public void A_uri_is_refused_with_advice_to_pass_bytes()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => RequestValidator.Validate(UserWith(new UriContent("https://example.com/a.png", "image/png")), null));

        Assert.Contains("DataContent", ex.Message);
    }

    [Fact]
    public void Empty_oversized_and_too_many_images_are_refused()
    {
        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(UserWith(new DataContent(Array.Empty<byte>(), "image/png")), null));
        Assert.Throws<ArgumentException>(
            () => RequestValidator.Validate(UserWith(new DataContent(new byte[RequestValidator.MaxAttachmentBytes + 1], "image/png")), null));
        AIContent[] many = Enumerable.Range(0, RequestValidator.MaxAttachments + 1).Select(_ => (AIContent)new DataContent(Png, "image/png")).ToArray();
        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(UserWith(many), null));

        AIContent[] exactly = many.Take(RequestValidator.MaxAttachments).ToArray();
        Assert.Equal(RequestValidator.MaxAttachments, RequestValidator.Validate(UserWith(exactly), null).Attachments!.Count);
    }

    [Fact]
    public void A_text_only_request_has_no_attachments()
    {
        ValidatedRequest request = RequestValidator.Validate(Clients.User("hi"), null);

        Assert.False(request.HasAttachments);
    }

    // ---- preamble ----

    [Fact]
    public void The_default_preamble_is_swapped_for_requests_with_images_and_a_custom_one_is_kept()
    {
        ValidatedRequest plain = RequestValidator.Validate(Clients.User("hi"), null);
        ValidatedRequest image = RequestValidator.Validate(UserWith(new DataContent(Png, "image/png")), null);
        string standard = AntigravityChatClientOptions.DefaultSystemPreamble;

        Assert.Equal(standard, AgentsFile.EffectivePreamble(plain, standard));
        Assert.Equal(AgentsFile.DefaultAttachmentPreamble, AgentsFile.EffectivePreamble(image, standard));
        Assert.Equal("MINE", AgentsFile.EffectivePreamble(image, "MINE  "));
        Assert.Contains("attachments", AgentsFile.DefaultAttachmentPreamble);
    }

    [Fact]
    public void The_prompt_without_an_agents_file_carries_the_attachment_preamble()
    {
        ValidatedRequest image = RequestValidator.Validate(UserWith(new DataContent(Png, "image/png")), null);

        string prompt = PromptBuilder.Build(image, AntigravityChatClientOptions.DefaultSystemPreamble, "abc123", new PromptLimits(1_000_000, null, null));

        Assert.StartsWith(AgentsFile.DefaultAttachmentPreamble, prompt);
        Assert.Contains("[attachment: attachments/image-1.png]", prompt);
    }

    // ---- request directory ----

    [Fact]
    public void The_files_are_written_with_their_bytes_and_removed_with_the_directory()
    {
        ValidatedRequest image = RequestValidator.Validate(UserWith(new DataContent(Png, "image/png")), null);
        string path;
        using (var directory = RequestDirectory.Create(_root))
        {
            directory.WriteAttachments(image.Attachments!);
            path = Path.Combine(directory.Path, "attachments", "image-1.png");
            Assert.Equal(Png, File.ReadAllBytes(path));
        }

        Assert.False(File.Exists(path));
    }

    // ---- client ----

    [Fact]
    public async Task The_client_writes_the_images_before_agy_starts_and_keeps_their_bytes_out_of_the_arguments()
    {
        byte[]? seen = null;
        string? agents = null;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(run =>
        {
            seen = File.ReadAllBytes(Path.Combine(run.WorkingDirectory, "attachments", "image-1.png"));
            string agentsPath = Path.Combine(run.WorkingDirectory, "AGENTS.md");
            agents = File.Exists(agentsPath) ? File.ReadAllText(agentsPath) : null;
            Assert.Contains("[attachment: attachments/image-1.png]", run.Arguments[^1]);
            return FakeProcessRunner.Ok(Envelope("a cat"));
        });
        using AntigravityChatClient client = Clients.Create(runner, o => o.TemporaryDirectoryRoot = _root);

        ChatResponse response = await client.GetResponseAsync(UserWith(new TextContent("what?"), new DataContent(Png, "image/png")));

        Assert.Equal("a cat", response.Text);
        Assert.Equal(Png, seen);
        Assert.DoesNotContain(Convert.ToBase64String(Png), string.Join(' ', runner.Requests[1].Arguments));
        if (InvocationBuilder.IsolationApplies)
        {
            Assert.Contains(AgentsFile.DefaultAttachmentPreamble, agents);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task A_streaming_request_writes_the_images_too()
    {
        byte[]? seen = null;
        string result = """{"event": "result", "result": {"conversation_id": "c-1", "status": "SUCCESS", "response": "ok\n"}}""";
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(run =>
        {
            seen = File.ReadAllBytes(Path.Combine(run.WorkingDirectory, "attachments", "image-1.png"));
            return FakeProcessRunner.Ok(result + "\n");
        });
        using AntigravityChatClient client = Clients.Create(runner, o => o.TemporaryDirectoryRoot = _root);

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync(UserWith(new DataContent(Png, "image/png"))))
        {
        }

        Assert.Equal(Png, seen);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task An_unsupported_image_type_is_refused_before_any_process_starts()
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner, o => o.TemporaryDirectoryRoot = _root);

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(UserWith(new DataContent(Png, "image/tiff"))));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task The_log_holds_no_image_name_path_or_bytes()
    {
        var logger = new CapturingLogger();
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(FakeProcessRunner.Ok(Envelope("a cat")));
        using AntigravityChatClient client = Clients.Create(runner, o => o.TemporaryDirectoryRoot = _root, logger);

        await client.GetResponseAsync(UserWith(new DataContent(Png, "image/png") { Name = "SECRET-NAME.png" }));

        string all = logger.Everything();
        Assert.DoesNotContain("SECRET-NAME", all);
        Assert.DoesNotContain("image-1", all);
        Assert.DoesNotContain(Convert.ToBase64String(Png), all);
    }
}
