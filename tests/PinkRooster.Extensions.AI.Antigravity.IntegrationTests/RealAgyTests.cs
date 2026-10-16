using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace PinkRooster.Extensions.AI.Antigravity.IntegrationTests;

/// <summary>
/// The tests of docs/software-design.md section 8.3, against the real agy. Each prints what it observed, because a failure is
/// as informative as a pass. They use the cheapest listed model and tiny prompts, and still cost about 17,000 input tokens a call.
/// </summary>
public sealed class RealAgyTests : IDisposable
{
    private const string Model = "gemini-3.8-flash-low";

    private readonly ITestOutputHelper _output;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "agy-integration-" + Guid.NewGuid().ToString("N"));

    public RealAgyTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private AntigravityChatClient Client(Action<AntigravityChatClientOptions>? configure = null)
    {
        var options = new AntigravityChatClientOptions { DefaultModelId = Model, TemporaryDirectoryRoot = _scratch };
        configure?.Invoke(options);
        return new AntigravityChatClient(options);
    }

    private void Describe(ChatResponse response) =>
        _output.WriteLine(
            $"answer: {Truncate(response.Text)} | id {response.ResponseId} | tokens in {response.Usage?.InputTokenCount} out {response.Usage?.OutputTokenCount} | "
            + $"turns {response.AdditionalProperties?["agy.num_turns"]}");

    private static string Truncate(string text) => text.Length <= 160 ? text : text[..160] + "…";

    public sealed record Capital(string City, string Country);

    [IntegrationFact]
    public async Task Plain_prompt_returns_text()
    {
        using AntigravityChatClient client = Client();

        ChatResponse response = await client.GetResponseAsync("Reply with exactly the word: pong. Do not use any tools.");

        Describe(response);
        Assert.Contains("pong", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(response.ResponseId));
        Assert.True(response.Usage!.InputTokenCount > 0);
        Assert.Equal(Model, response.ModelId);
    }

    [IntegrationFact]
    public async Task Schema_prompt_roundtrips_into_T()
    {
        using AntigravityChatClient client = Client();

        ChatResponse<Capital> response = await client.GetResponseAsync<Capital>("What is the capital of France? Do not use any tools.");

        _output.WriteLine($"typed: {response.Result.City}, {response.Result.Country}");
        Assert.Equal("Paris", response.Result.City);
    }

    [IntegrationFact]
    public async Task Unsatisfiable_schema_throws_InvalidOutput()
    {
        using AntigravityChatClient client = Client();
        using JsonDocument schema = JsonDocument.Parse(
            """{"type":"object","properties":{"n":{"type":"integer","minimum":100,"maximum":10}},"required":["n"],"additionalProperties":false}""");
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement.Clone()) };

        // agy 1.3.5 returned SUCCESS without a structured result for this schema (spec fact F-35).
        AntigravityException ex = await Assert.ThrowsAnyAsync<AntigravityException>(
            () => client.GetResponseAsync("Return n = 5. Do not use any tools.", options));

        _output.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        Assert.IsType<AntigravityInvalidOutputException>(ex);
    }

    [IntegrationFact]
    public async Task File_creation_request_leaves_no_trace()
    {
        string callerDirectory = Directory.GetCurrentDirectory();
        string[] before = Directory.GetFileSystemEntries(callerDirectory);
        const string fileName = "agy-integration-created.txt";
        using AntigravityChatClient client = Client();

        try
        {
            ChatResponse response = await client.GetResponseAsync($"Create a file named {fileName} containing 'hi' in the current directory.");
            Describe(response);
        }
        catch (AntigravityException ex)
        {
            _output.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        }

        Assert.False(File.Exists(Path.Combine(callerDirectory, fileName)));
        Assert.Equal(before, Directory.GetFileSystemEntries(callerDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(_scratch)); // every request directory is gone
    }

    [IntegrationFact]
    public async Task Leading_slash_is_not_a_command()
    {
        using AntigravityChatClient client = Client();

        ChatResponse response = await client.GetResponseAsync("/help");

        Describe(response);
        // agy's built-in command list has lines like "/changelog<TAB>Show release notes and changes".
        Assert.DoesNotContain("/changelog\tShow release notes", response.Text);
    }

    [IntegrationFact]
    public async Task Preamble_blocks_tool_use_sample()
    {
        // Informational: five repeats of a request that invites a file write; the output shows how often agy still acted.
        // The assertion only guards the caller's directory.
        string callerDirectory = Directory.GetCurrentDirectory();
        string[] before = Directory.GetFileSystemEntries(callerDirectory);
        using AntigravityChatClient client = Client();
        int denied = 0;
        int multiTurn = 0;

        for (int i = 1; i <= 5; i++)
        {
            try
            {
                ChatResponse response = await client.GetResponseAsync($"Create a file named repeat-{i}.txt containing 'hi' in the current directory.");
                bool hadDenial = response.AdditionalProperties?.ContainsKey("agy.denied_actions") == true;
                bool manyTurns = Convert.ToInt32(response.AdditionalProperties?["agy.num_turns"] ?? 1) > 1;
                denied += hadDenial ? 1 : 0;
                multiTurn += manyTurns ? 1 : 0;
                _output.WriteLine($"run {i}: denied={hadDenial} turns>1={manyTurns} answer={Truncate(response.Text)}");
            }
            catch (AntigravityException ex)
            {
                _output.WriteLine($"run {i}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine($"summary: {denied} of 5 runs had a denied action, {multiTurn} of 5 used more than one turn");
        Assert.Equal(before, Directory.GetFileSystemEntries(callerDirectory));
    }

    [IntegrationFact]
    public async Task Unicode_roundtrip()
    {
        using AntigravityChatClient client = Client();
        const string text = "héllo 日本語 😀";

        ChatResponse response = await client.GetResponseAsync($"Repeat exactly this text and nothing else: {text}. Do not use any tools.");

        Describe(response);
        Assert.Contains(text, response.Text);
    }

    [IntegrationFact]
    public async Task Streaming_yields_text_updates_that_join_to_the_answer()
    {
        using AntigravityChatClient client = Client();
        var updates = new List<ChatResponseUpdate>();
        var arrivals = new List<TimeSpan>();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            "Write the numbers one to twenty as words, separated by commas. Do not use any tools."))
        {
            updates.Add(update);
            arrivals.Add(watch.Elapsed);
        }

        ChatResponse combined = updates.ToChatResponse();
        _output.WriteLine($"{updates.Count} updates; first at {arrivals[0]}, last at {arrivals[^1]}; text: {Truncate(combined.Text)}");
        _output.WriteLine($"usage: in {combined.Usage?.InputTokenCount} out {combined.Usage?.OutputTokenCount}; id {combined.ResponseId}");
        Assert.Contains("twenty", combined.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(updates.Count >= 2, "expected at least one text update and the final update");
        Assert.NotNull(combined.Usage);
        Assert.False(string.IsNullOrEmpty(combined.ResponseId));
    }

    [IntegrationFact]
    public async Task A_real_agy_describes_an_attached_image()
    {
        using AntigravityChatClient client = Client();
        byte[] photo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "rooster-profile.jpg"));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, [new TextContent("Which animal is in the attached image? Answer with a single English word."), new DataContent(photo, "image/jpeg")]),
        };

        ChatResponse response = await client.GetResponseAsync(messages);

        Describe(response);
        object? denied = null;
        response.AdditionalProperties?.TryGetValue("agy.denied_actions", out denied);
        _output.WriteLine($"denied actions: {(denied is IEnumerable<string> names ? string.Join(", ", names) : "none")}");
        Assert.Matches("(?i)rooster|chicken|cock|hen", response.Text);
    }

    [IntegrationFact]
    public async Task A_real_agy_calls_a_caller_tool_through_the_bridge_without_skip_permissions()
    {
        int runs = 0;
        AIFunction code = AIFunctionFactory.Create((string person) =>
        {
            Interlocked.Increment(ref runs);
            return "ZEBRA-4417 for " + person;
        }, "get_secret_code", "Returns the secret code of the day for a person.");
        using AntigravityChatClient client = Client(o => o.IsolateAgyHome = true);

        ChatResponse response = await client.GetResponseAsync(
            "Call the get_secret_code tool for the person Ada and tell me exactly what it returned.",
            new ChatOptions { Tools = [code] });

        Describe(response);
        _output.WriteLine($"tool body ran {runs} time(s)");
        Assert.True(runs >= 1, "agy never called the tool (a denied permission shows as an AntigravityRunException or an empty answer)");
        Assert.Contains("ZEBRA-4417", response.Text);
    }

    [IntegrationFact]
    public async Task Isolated_home_keeps_sign_in_and_follows_an_agents_md_rule()
    {
        using AntigravityChatClient client = Client(o => o.IsolateAgyHome = true);
        var options = new ChatOptions { Instructions = "End every answer with the exact line: PINEAPPLE-7" };

        ChatResponse response = await client.GetResponseAsync("In one sentence, explain what a hash map is.", options);

        Describe(response);
        // Sign-in survived the home redirect (no exception above) and the system text reached the model through AGENTS.md.
        Assert.Contains("PINEAPPLE-7", response.Text);
        // The user's own global rules must not leak into the answer (a rule in ~/.gemini/config/GEMINI.md can add suffixes).
        _output.WriteLine("Check by eye: the answer above must not end with lines from your own global agy rules.");
    }

    [IntegrationFact]
    public async Task Reasoning_effort_with_base_model()
    {
        using AntigravityChatClient baseModel = Client(o => o.DefaultModelId = "gemini-3.8-flash");
        using AntigravityChatClient levelModel = Client();
        var options = new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low } };

        ChatResponse a = await baseModel.GetResponseAsync("Reply with exactly: ok. Do not use any tools.", options);
        Describe(a);
        // A level-qualified model name plus an effort makes agy exit 1; the library must leave the effort out.
        ChatResponse b = await levelModel.GetResponseAsync("Reply with exactly: ok. Do not use any tools.", options);
        Describe(b);

        Assert.Contains("ok", a.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok", b.Text, StringComparison.OrdinalIgnoreCase);
    }

    [IntegrationFactWhen("AGY_TEST_UNAUTHENTICATED")]
    public async Task Not_signed_in_signal()
    {
        // Run this only when agy is NOT signed in on this machine (the owner arranges that). It records the real signal.
        using AntigravityChatClient client = Client();

        Exception? caught = null;
        try
        {
            await client.GetResponseAsync("Reply with exactly: ok.");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        _output.WriteLine($"type: {caught.GetType().Name}");
        _output.WriteLine($"message: {caught.Message}");
        if (caught is AntigravityException agy)
        {
            _output.WriteLine($"exit code: {agy.ExitCode}");
            _output.WriteLine($"standard error: {agy.StandardError}");
        }

        // If this fails, the output above is the real wording to add to AuthFailureDetector.
        Assert.IsType<AntigravityNotAuthenticatedException>(caught);
    }
}
