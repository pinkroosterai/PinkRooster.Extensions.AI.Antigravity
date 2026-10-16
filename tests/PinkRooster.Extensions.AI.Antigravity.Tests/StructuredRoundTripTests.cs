using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>
/// The typed <c>GetResponseAsync&lt;T&gt;</c> extension of Microsoft.Extensions.AI (a reference of the test project only)
/// working against this client: schema in, structured result out, typed object back.
/// </summary>
public class StructuredRoundTripTests
{
    public enum Level { Easy, Hard }

    public sealed record Itinerary(string Destination, string[] Days, Level Level, int Stops, double? Budget);

    private static FakeProcessRunner RunnerAnswering(string structuredOutputJson, Action<string>? seeSchema = null) =>
        new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(request =>
        {
            string schema = File.ReadAllText(request.Arguments[request.Arguments.ToList().IndexOf("--json-schema") + 1]);
            seeSchema?.Invoke(schema);
            return FakeProcessRunner.Ok($$"""{"conversation_id":"c","status":"SUCCESS","response":"{{{structuredOutputJson.Replace("\"", "\\\"")}}} extra text","num_turns":2,"structured_output":{{structuredOutputJson}}}""");
        });

    [Fact]
    public async Task A_record_with_strings_numbers_arrays_and_an_enum_round_trips()
    {
        string? sentSchema = null;
        FakeProcessRunner runner = RunnerAnswering(
            """{"destination":"Tokyo","days":["a","b"],"level":"Hard","stops":3,"budget":null}""", s => sentSchema = s);
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse<Itinerary> response = await client.GetResponseAsync<Itinerary>("Plan a trip to Tokyo");

        Assert.Equal(new[] { "a", "b" }, response.Result.Days);
        Assert.Equal("Tokyo", response.Result.Destination);
        Assert.Equal(Level.Hard, response.Result.Level);
        Assert.Equal(3, response.Result.Stops);
        Assert.Null(response.Result.Budget);
        Assert.NotNull(sentSchema);
        using JsonDocument schema = JsonDocument.Parse(sentSchema);
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task An_integer_is_wrapped_in_an_object_by_the_extension_and_comes_back_unwrapped()
    {
        string? sentSchema = null;
        FakeProcessRunner runner = RunnerAnswering("""{"data":7}""", s => sentSchema = s);
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse<int> response = await client.GetResponseAsync<int>("Pick a number");

        Assert.Equal(7, response.Result);
        Assert.Contains("\"data\"", sentSchema);
    }

    [Fact]
    public async Task A_string_array_is_wrapped_and_comes_back_unwrapped()
    {
        FakeProcessRunner runner = RunnerAnswering("""{"data":["x","y"]}""");
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse<string[]> response = await client.GetResponseAsync<string[]>("List two");

        Assert.Equal(new[] { "x", "y" }, response.Result);
    }

    [Fact]
    public async Task An_enum_is_wrapped_and_comes_back_unwrapped()
    {
        FakeProcessRunner runner = RunnerAnswering("""{"data":"Easy"}""");
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse<Level> response = await client.GetResponseAsync<Level>("How hard?");

        Assert.Equal(Level.Easy, response.Result);
    }

    [Fact]
    public async Task The_typed_extension_works_through_the_plain_json_path_when_it_does_not_send_a_schema()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok("""{"status":"SUCCESS","response":"```json\n{\"destination\":\"Oslo\",\"days\":[],\"level\":\"Easy\",\"stops\":1,\"budget\":2.5}\n```\n"}"""));
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse<Itinerary> response = await client.GetResponseAsync<Itinerary>("Plan Oslo", useJsonSchemaResponseFormat: false);

        Assert.Equal("Oslo", response.Result.Destination);
        Assert.Equal(2.5, response.Result.Budget);
        Assert.DoesNotContain("--json-schema", runner.Requests[1].Arguments);
        Assert.Contains("Respond with a JSON value conforming to the following schema", runner.Requests[1].Arguments[^1]);
    }

    [Fact]
    public async Task A_result_that_breaks_the_generated_schema_raises_InvalidOutput_through_the_extension()
    {
        FakeProcessRunner runner = RunnerAnswering("""{"destination":"Tokyo","days":["a"],"level":"Nope","stops":3,"budget":null}""");
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityInvalidOutputException>(() => client.GetResponseAsync<Itinerary>("Plan"));
    }
}
