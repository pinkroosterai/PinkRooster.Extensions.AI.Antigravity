using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public sealed class StructuredOutputTests
{
    private const string CitySchema = """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"],"additionalProperties":false}""";

    private static ChatOptions Schema(string schema = CitySchema, string? name = null, string? description = null) =>
        new() { ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonDocument.Parse(schema).RootElement.Clone(), name, description) };

    private static AgyEnvelope Envelope(string json)
    {
        Assert.True(EnvelopeParser.TryParse(json, out AgyEnvelope? e));
        return e!;
    }

    private static ICompiledSchema Compiled(string schema = CitySchema) => SchemaValidator.Instance.Compile(schema);

    // Checker

    [Fact]
    public void A_valid_structured_result_becomes_compact_json_taken_from_structured_output_not_from_the_response_text()
    {
        AgyEnvelope e = Envelope(Fixture.Text("success-schema.json"));

        string text = StructuredResultChecker.CheckSchemaResult(e, Compiled());

        Assert.Equal("""{"city":"Paris"}""", text);
        Assert.DoesNotContain("toolAction", text);
    }

    [Fact]
    public void Whitespace_in_the_structured_result_is_removed_and_non_ascii_text_is_kept_readable()
    {
        AgyEnvelope e = Envelope("""{"status":"SUCCESS","structured_output":{ "city" : "Zürich ☃" }}""");

        Assert.Equal("""{"city":"Zürich ☃"}""", StructuredResultChecker.CheckSchemaResult(e, Compiled()));
    }

    [Fact]
    public void A_missing_structured_result_raises_InvalidOutput_carrying_the_free_text()
    {
        AgyEnvelope e = Envelope(Fixture.Text("schema-missing-structured.json"));

        var ex = Assert.Throws<AntigravityInvalidOutputException>(() => StructuredResultChecker.CheckSchemaResult(e, Compiled()));

        Assert.Contains("no structured result", ex.Message);
        Assert.Contains("\"n\":5", ex.RawOutput);
        Assert.DoesNotContain("\"n\":5", ex.Message);
    }

    [Fact]
    public void A_structured_result_that_does_not_match_raises_InvalidOutput_carrying_that_result()
    {
        AgyEnvelope e = Envelope("""{"status":"SUCCESS","structured_output":{"city":42}}""");

        var ex = Assert.Throws<AntigravityInvalidOutputException>(() => StructuredResultChecker.CheckSchemaResult(e, Compiled()));

        Assert.Equal("""{"city":42}""", ex.RawOutput);
        Assert.DoesNotContain("42", ex.Message);
    }

    [Theory]
    [InlineData("""{"city":"Paris","extra":1}""")]
    [InlineData("""{}""")]
    [InlineData("""["Paris"]""")]
    [InlineData("\"Paris\"")]
    public void Other_mismatches_are_rejected(string result)
    {
        AgyEnvelope e = Envelope($$"""{"status":"SUCCESS","structured_output":{{result}}}""");

        Assert.Throws<AntigravityInvalidOutputException>(() => StructuredResultChecker.CheckSchemaResult(e, Compiled()));
    }

    // Plain JSON

    [Theory]
    [InlineData("""{"a":1}""", """{"a":1}""")]
    [InlineData("  [1, 2]  \n", "[1, 2]")]
    [InlineData("42", "42")]
    [InlineData("\"text\"", "\"text\"")]
    [InlineData("null", "null")]
    [InlineData("```json\n{\"a\":1}\n```", """{"a":1}""")]
    [InlineData("```\n{\"a\":1}\n```", """{"a":1}""")]
    [InlineData("```JSON\n[1]\n```", "[1]")]
    public void A_json_answer_is_accepted_with_one_surrounding_code_fence_removed(string answer, string expected)
    {
        Assert.Equal(expected, StructuredResultChecker.CheckPlainJson(answer));
    }

    [Theory]
    [InlineData("Sure! {\"a\":1}")]
    [InlineData("{\"a\":1} and more")]
    [InlineData("{\"a\":1}{\"b\":2}")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("```python\n{\"a\":1}\n```")]
    [InlineData("```json{\"a\":1}```")]
    [InlineData("{\"a\":")]
    public void An_answer_that_is_not_exactly_one_json_value_raises_InvalidOutput_with_the_answer_on_the_property(string answer)
    {
        var ex = Assert.Throws<AntigravityInvalidOutputException>(() => StructuredResultChecker.CheckPlainJson(answer));

        Assert.Equal(answer, ex.RawOutput);
        if (answer.Length > 3)
        {
            Assert.DoesNotContain(answer, ex.Message);
        }
    }

    // Through the client

    [Fact]
    public async Task A_schema_request_delivers_the_schema_by_file_and_returns_the_compact_result()
    {
        string? schemaArgument = null;
        string? fileContent = null;
        bool fileExistedDuringRun = false;
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(request =>
        {
            int i = request.Arguments.ToList().IndexOf("--json-schema");
            schemaArgument = request.Arguments[i + 1];
            fileExistedDuringRun = File.Exists(schemaArgument);
            fileContent = File.ReadAllText(schemaArgument);
            return Fixture.Result("success-schema.json");
        });
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User("Capital of France?"), Schema(name: "City", description: "A city."));

        Assert.Equal("""{"city":"Paris"}""", response.Text);
        Assert.True(fileExistedDuringRun);
        Assert.Equal(CitySchema, fileContent);
        Assert.True(Path.IsPathRooted(schemaArgument));
        Assert.EndsWith("schema.json", schemaArgument);
        Assert.False(File.Exists(schemaArgument)); // the request directory is gone
        Assert.Contains("(named City)", runner.Requests[1].Arguments[^1]);
        Assert.Contains("Schema description: A city.", runner.Requests[1].Arguments[^1]);
    }

    [Fact]
    public async Task The_schema_text_never_appears_on_the_command_line()
    {
        FakeProcessRunner runner = Clients.Runner("success-schema.json");
        using AntigravityChatClient client = Clients.Create(runner);

        await client.GetResponseAsync(Clients.User("q"), Schema());

        Assert.DoesNotContain(runner.Requests[1].Arguments, a => a.Contains("additionalProperties"));
    }

    [Fact]
    public async Task A_run_without_a_structured_result_raises_InvalidOutput_once_and_is_not_retried()
    {
        FakeProcessRunner runner = Clients.Runner("schema-missing-structured.json");
        using AntigravityChatClient client = Clients.Create(runner);

        var ex = await Assert.ThrowsAsync<AntigravityInvalidOutputException>(() => client.GetResponseAsync(Clients.User(), Schema()));

        Assert.NotNull(ex.RawOutput);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task A_mismatching_result_raises_InvalidOutput_and_is_not_retried()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok("""{"status":"SUCCESS","response":"x","structured_output":{"city":7}}"""));
        using AntigravityChatClient client = Clients.Create(runner);

        var ex = await Assert.ThrowsAsync<AntigravityInvalidOutputException>(() => client.GetResponseAsync(Clients.User(), Schema()));

        Assert.Equal("""{"city":7}""", ex.RawOutput);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task Status_ERROR_wins_even_when_a_valid_structured_result_is_present()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok("""{"status":"ERROR","error":"blocked","structured_output":{"city":"Paris"}}"""));
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityRunException>(() => client.GetResponseAsync(Clients.User(), Schema()));
    }

    [Fact]
    public async Task An_empty_answer_with_a_denial_is_a_run_error_not_an_invalid_output()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5")).Returns(Fixture.Result("denied-empty.json"));
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<AntigravityRunException>(() => client.GetResponseAsync(Clients.User(), Schema()));
    }

    [Theory]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"type":"array","items":{"type":"integer"}}""")]
    public async Task A_schema_that_is_not_an_object_schema_is_refused_before_any_process_starts(string schema)
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(Clients.User(), Schema(schema)));

        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("""{"type":"object","required":"a"}""")]
    [InlineData("""{"type":"object","properties":{"a":{"type":5}}}""")]
    public async Task A_schema_the_validator_cannot_use_is_refused_before_any_process_starts(string schema)
    {
        FakeProcessRunner runner = Clients.Runner();
        using AntigravityChatClient client = Clients.Create(runner);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(Clients.User(), Schema(schema)));

        Assert.StartsWith("The response schema cannot be used", ex.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_schema_defect_that_only_shows_when_checking_the_answer_raises_ArgumentException_after_one_run()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok("""{"status":"SUCCESS","response":"x","structured_output":{"a":"text"}}"""));
        using AntigravityChatClient client = Clients.Create(runner);
        const string schema = """{"type":"object","properties":{"a":{"type":"string","pattern":"("}}}""";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(Clients.User(), Schema(schema)));

        Assert.StartsWith("The response schema cannot be used", ex.Message);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task A_plain_json_request_passes_no_schema_and_returns_the_json_without_a_fence()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok("{\"status\":\"SUCCESS\",\"response\":\"```json\\n{\\\"a\\\":1}\\n```\\n\"}"));
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User(), new ChatOptions { ResponseFormat = ChatResponseFormat.Json });

        Assert.Equal("""{"a":1}""", response.Text);
        Assert.DoesNotContain("--json-schema", runner.Requests[1].Arguments);
        Assert.Contains("Reply with one JSON value", runner.Requests[1].Arguments[^1]);
    }

    [Fact]
    public async Task A_plain_json_request_whose_answer_is_not_json_raises_InvalidOutput()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"))
            .Returns(FakeProcessRunner.Ok("""{"status":"SUCCESS","response":"Sure, here you go: {}\n"}"""));
        using AntigravityChatClient client = Clients.Create(runner);

        var ex = await Assert.ThrowsAsync<AntigravityInvalidOutputException>(
            () => client.GetResponseAsync(Clients.User(), new ChatOptions { ResponseFormat = ChatResponseFormat.Json }));

        Assert.Contains("Sure, here you go", ex.RawOutput);
    }

    [Fact]
    public async Task A_schema_request_still_reports_usage_identifiers_and_the_raw_envelope()
    {
        FakeProcessRunner runner = Clients.Runner("success-schema.json");
        using AntigravityChatClient client = Clients.Create(runner);

        ChatResponse response = await client.GetResponseAsync(Clients.User(), Schema());

        Assert.Equal(19715, response.Usage!.TotalTokenCount);
        Assert.Equal("00000000-0000-0000-0000-000000000002", response.ResponseId);
        Assert.Equal("Paris", Assert.IsType<JsonElement>(response.RawRepresentation).GetProperty("structured_output").GetProperty("city").GetString());
    }
}
