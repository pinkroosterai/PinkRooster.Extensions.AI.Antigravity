using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class ResponseMapperTests
{
    private static AgyEnvelope Envelope(string fixture)
    {
        Assert.True(EnvelopeParser.TryParse(Fixture.Text(fixture), out AgyEnvelope? e));
        return e!;
    }

    [Theory]
    [InlineData("pong\n", "pong")]
    [InlineData("pong\r\n", "pong")]
    [InlineData("pong", "pong")]
    [InlineData("a\n\n", "a\n")]
    [InlineData("line one\nline two\n", "line one\nline two")]
    [InlineData("\n", "")]
    [InlineData("", "")]
    public void Exactly_one_trailing_line_terminator_is_removed(string response, string expected)
    {
        var envelope = new AgyEnvelope("SUCCESS", response, null, null, null, null, null, null, [], default);

        Assert.Equal(expected, ResponseMapper.PlainText(envelope));
    }

    [Fact]
    public void A_missing_response_gives_empty_text()
    {
        var envelope = new AgyEnvelope("SUCCESS", null, null, null, null, null, null, null, [], default);

        Assert.Equal(string.Empty, ResponseMapper.PlainText(envelope));
    }

    [Fact]
    public void The_response_has_one_assistant_message_with_one_text_content()
    {
        AgyEnvelope e = Envelope("success-text.json");

        ChatResponse r = ResponseMapper.Map(e, ResponseMapper.PlainText(e), "gemini-3.8-flash-low", DateTimeOffset.Parse("2026-10-16T12:00:00Z"));

        ChatMessage message = Assert.Single(r.Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);
        TextContent content = Assert.IsType<TextContent>(Assert.Single(message.Contents));
        Assert.Equal("pong", content.Text);
        Assert.Equal("pong", r.Text);
    }

    [Fact]
    public void Identifiers_finish_reason_and_time_are_set()
    {
        AgyEnvelope e = Envelope("success-text.json");
        var now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");

        ChatResponse r = ResponseMapper.Map(e, "x", "m1", now);

        Assert.Equal("00000000-0000-0000-0000-000000000001", r.ResponseId);
        Assert.Equal("m1", r.ModelId);
        Assert.Equal(now, r.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, r.FinishReason);
    }

    [Fact]
    public void Model_id_is_null_when_no_model_was_chosen()
    {
        Assert.Null(ResponseMapper.Map(Envelope("success-text.json"), "x", null, DateTimeOffset.UtcNow).ModelId);
    }

    [Fact]
    public void Token_counts_map_to_usage_details()
    {
        AgyEnvelope e = Envelope("success-schema.json");

        UsageDetails? usage = ResponseMapper.Map(e, "x", null, DateTimeOffset.UtcNow).Usage;

        Assert.NotNull(usage);
        Assert.Equal(19400, usage.InputTokenCount);
        Assert.Equal(315, usage.OutputTokenCount);
        Assert.Equal(19715, usage.TotalTokenCount);
        Assert.Equal(15038, usage.CachedInputTokenCount);
        Assert.Equal(262, usage.ReasoningTokenCount);
    }

    [Fact]
    public void Usage_is_null_when_agy_reported_none()
    {
        var envelope = new AgyEnvelope("SUCCESS", "x", null, null, null, null, null, null, [], default);

        Assert.Null(ResponseMapper.Map(envelope, "x", null, DateTimeOffset.UtcNow).Usage);
    }

    [Fact]
    public void The_raw_representation_is_the_parsed_envelope()
    {
        AgyEnvelope e = Envelope("success-text.json");

        ChatResponse r = ResponseMapper.Map(e, "x", null, DateTimeOffset.UtcNow);

        JsonElement raw = Assert.IsType<JsonElement>(r.RawRepresentation);
        Assert.Equal("SUCCESS", raw.GetProperty("status").GetString());
    }

    [Fact]
    public void Turns_and_duration_are_in_additional_properties_and_denials_only_when_present()
    {
        ChatResponse plain = ResponseMapper.Map(Envelope("success-text.json"), "x", null, DateTimeOffset.UtcNow);
        ChatResponse denied = ResponseMapper.Map(Envelope("denied-with-answer.json"), "x", null, DateTimeOffset.UtcNow);

        Assert.Equal(1, plain.AdditionalProperties![ResponseMapper.NumTurnsKey]);
        Assert.Equal(1.9195324139999999, plain.AdditionalProperties[ResponseMapper.DurationKey]);
        Assert.False(plain.AdditionalProperties.ContainsKey(ResponseMapper.DeniedActionsKey));
        Assert.Equal(new[] { "command" }, Assert.IsAssignableFrom<IEnumerable<string>>(denied.AdditionalProperties![ResponseMapper.DeniedActionsKey]));
    }

    [Fact]
    public void Property_keys_are_the_ones_the_specification_names()
    {
        Assert.Equal("agy.num_turns", ResponseMapper.NumTurnsKey);
        Assert.Equal("agy.duration_seconds", ResponseMapper.DurationKey);
        Assert.Equal("agy.denied_actions", ResponseMapper.DeniedActionsKey);
    }

    [Fact]
    public void An_empty_successful_answer_gives_empty_text()
    {
        AgyEnvelope e = Envelope("timeout-empty-success.json");

        ChatResponse r = ResponseMapper.Map(e, ResponseMapper.PlainText(e), null, DateTimeOffset.UtcNow);

        Assert.Equal(string.Empty, r.Text);
    }
}
