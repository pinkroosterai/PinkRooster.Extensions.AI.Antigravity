// A small tour of the library. It is built with the solution but never run by the build or the tests: every run calls agy,
// which spends model tokens. Run it yourself with `dotnet run --project samples/ConsoleSample` once agy is installed
// (1.3.5 or newer) and signed in.

using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity;

using IChatClient client = new AntigravityChatClient(new AntigravityChatClientOptions
{
    DefaultModelId = "gemini-3.8-flash-low", // `agy models` lists the choices
    Timeout = TimeSpan.FromMinutes(2),
});

try
{
    // Plain text.
    ChatResponse reply = await client.GetResponseAsync("Reply with exactly one word: pong.");
    Console.WriteLine($"Answer: {reply.Text}");
    Console.WriteLine($"Tokens: {reply.Usage?.InputTokenCount} in, {reply.Usage?.OutputTokenCount} out (agy's own overhead is included)");

    // Streaming: the text is written as agy produces it. The last update carries usage and no text.
    Console.Write("Streamed: ");
    await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("Count from one to five in words, separated by commas."))
    {
        Console.Write(update.Text);
        if (update.Contents.OfType<UsageContent>().FirstOrDefault() is { } usage)
        {
            Console.WriteLine($"{Environment.NewLine}Tokens: {usage.Details.InputTokenCount} in, {usage.Details.OutputTokenCount} out");
        }
    }

    // A tool: agy calls it while it works (Linux, IsolateAgyHome). The call is not returned to you, only the answer is.
    var options = new ChatOptions { Tools = [AIFunctionFactory.Create((string city) => $"Forecast for {city}: light rain.", "forecast")] };
    ChatResponse withTool = await client.GetResponseAsync("Will it rain in Utrecht? Use the forecast tool.", options);
    Console.WriteLine($"With a tool: {withTool.Text}");

    // An image: the bytes go in as DataContent (png, jpeg, gif or webp, in a user message); the library writes them as a file for agy.
    byte[] photo = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "rooster-profile.jpg"));
    ChatResponse seen = await client.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, [new TextContent("Which animal is in this photo, and what color is its comb?"), new DataContent(photo, "image/jpeg")]),
    ]);
    Console.WriteLine($"Image: {seen.Text}");

    // A typed answer constrained by a JSON schema.
    ChatResponse<Capital> typed = await client.GetResponseAsync<Capital>("What is the capital of France?");
    Console.WriteLine($"Typed: {typed.Result.City} ({typed.Result.Country})");
}
catch (AntigravityNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
}
catch (AntigravityNotAuthenticatedException ex)
{
    Console.Error.WriteLine(ex.Message);
}
catch (AntigravityException ex)
{
    // Messages never contain the prompt or the answer; ex.StandardError may hold more detail and should be treated as sensitive.
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
}

internal sealed record Capital(string City, string Country);
