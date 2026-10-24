using System.Text.Json;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class EnvelopeParserTests
{
    private static AgyEnvelope Parse(string text)
    {
        Assert.True(EnvelopeParser.TryParse(text, out AgyEnvelope? envelope));
        return envelope!;
    }

    [Fact]
    public void A_recorded_success_is_read_completely()
    {
        AgyEnvelope e = Parse(Fixture.Text("success-text.json"));

        Assert.Equal("SUCCESS", e.Status);
        Assert.True(e.IsSuccess);
        Assert.Equal("pong\n", e.Response);
        Assert.Equal("00000000-0000-0000-0000-000000000001", e.ConversationId);
        Assert.Equal(1, e.NumTurns);
        Assert.Equal(1.9195324139999999, e.DurationSeconds);
        Assert.Equal(new AgyUsage(16878, 1, 16879, 0, 0), e.Usage);
        Assert.Null(e.Error);
        Assert.Null(e.StructuredOutput);
        Assert.Empty(e.DeniedActionKinds);
    }

    [Fact]
    public void Structured_output_is_kept_as_an_independent_json_element()
    {
        AgyEnvelope e = Parse(Fixture.Text("success-schema.json"));

        Assert.NotNull(e.StructuredOutput);
        Assert.Equal("""{"city":"Paris"}""", e.StructuredOutput!.Value.GetRawText());
        Assert.Contains("toolAction", e.Response); // the free-text field carries extras, which is why it is not used
    }

    [Fact]
    public void A_run_without_structured_output_has_none_even_though_the_schema_is_echoed()
    {
        AgyEnvelope e = Parse(Fixture.Text("schema-missing-structured.json"));

        Assert.True(e.IsSuccess);
        Assert.Null(e.StructuredOutput);
        Assert.False(string.IsNullOrEmpty(e.Response));
        Assert.Equal(["command"], e.DeniedActionKinds);
    }

    [Fact]
    public void An_error_envelope_carries_status_and_error_text()
    {
        AgyEnvelope e = Parse(Fixture.Text("error-safety-block.json"));

        Assert.Equal("ERROR", e.Status);
        Assert.False(e.IsSuccess);
        Assert.StartsWith("Your previous response was blocked", e.Error);
        Assert.NotNull(e.StructuredOutput);
    }

    [Fact]
    public void Raw_element_stays_usable_after_parsing()
    {
        AgyEnvelope e = Parse(Fixture.Text("success-text.json"));

        Assert.Equal("SUCCESS", e.Raw.GetProperty("status").GetString());
        Assert.Equal(16879, e.Raw.GetProperty("usage").GetProperty("total_tokens").GetInt64());
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        AgyEnvelope e = Parse("""{"status":"SUCCESS","response":"x","something_new":{"a":[1,2]},"another":true}""");

        Assert.Equal("x", e.Response);
    }

    [Fact]
    public void Fields_of_an_unexpected_type_read_as_absent_and_do_not_fail_the_parse()
    {
        AgyEnvelope e = Parse("""{"status":"SUCCESS","response":42,"error":[],"conversation_id":false,"duration_seconds":"fast","num_turns":1.5,"usage":"many","denied_actions":"no"}""");

        Assert.Null(e.Response);
        Assert.Null(e.Error);
        Assert.Null(e.ConversationId);
        Assert.Null(e.DurationSeconds);
        Assert.Null(e.NumTurns);
        Assert.Null(e.Usage);
        Assert.Empty(e.DeniedActionKinds);
    }

    [Fact]
    public void Partial_usage_keeps_what_is_there()
    {
        AgyEnvelope e = Parse("""{"status":"SUCCESS","usage":{"input_tokens":7,"total_tokens":9}}""");

        Assert.Equal(new AgyUsage(7, null, 9, null, null), e.Usage);
    }

    [Fact]
    public void Denied_action_kinds_are_listed_once_and_unknown_items_are_named_unknown()
    {
        AgyEnvelope e = Parse("""{"status":"SUCCESS","denied_actions":[{"action":"command"},{"action":"command","display_name":"x"},{"action":"write"},5,{"x":1}]}""");

        Assert.Equal(["command", "write", "unknown"], e.DeniedActionKinds);
    }

    [Fact]
    public void Surrounding_whitespace_is_allowed()
    {
        Assert.Equal("SUCCESS", Parse("  \n{\"status\":\"SUCCESS\"}\r\n\n").Status);
    }

    [Fact]
    public void A_null_structured_output_counts_as_absent()
    {
        Assert.Null(Parse("""{"status":"SUCCESS","structured_output":null}""").StructuredOutput);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"SUCCESS\"")]
    [InlineData("{}")]
    [InlineData("""{"response":"x"}""")]
    [InlineData("""{"status":1}""")]
    [InlineData("""{"status":null}""")]
    [InlineData("""{"status":"SUCCESS"} trailing text""")]
    [InlineData("""{"status":"SUCCESS"}{"status":"SUCCESS"}""")]
    [InlineData("""{"status":"SUCCESS""")]
    public void Output_that_is_not_an_envelope_is_rejected(string? text)
    {
        Assert.False(EnvelopeParser.TryParse(text, out AgyEnvelope? envelope));
        Assert.Null(envelope);
    }

    [Fact]
    public void Every_recorded_envelope_fixture_parses()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            Assert.True(EnvelopeParser.TryParse(File.ReadAllText(file), out _), Path.GetFileName(file));
        }
    }
}
