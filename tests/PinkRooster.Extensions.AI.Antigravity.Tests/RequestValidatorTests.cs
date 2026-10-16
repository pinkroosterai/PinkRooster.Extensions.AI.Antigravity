using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class RequestValidatorTests
{
    private static List<ChatMessage> OneUserMessage(string text = "hello") => [new(ChatRole.User, text)];

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void Null_messages_throw_ArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => RequestValidator.Validate(null!, null));
    }

    [Fact]
    public void Empty_message_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => RequestValidator.Validate([], null));
    }

    [Fact]
    public void Messages_without_any_text_are_rejected()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "") , new(ChatRole.Assistant, [new TextReasoningContent("thinking")]) };

        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(messages, null));
    }

    [Fact]
    public void Text_turns_and_system_text_are_collected_in_order()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "be brief"),
            new(ChatRole.User, "one"),
            new(ChatRole.Assistant, "two"),
            new(ChatRole.System, "also polite"),
            new(ChatRole.User, "three"),
        };

        ValidatedRequest result = RequestValidator.Validate(messages, new ChatOptions { Instructions = "from options" });

        Assert.Equal(["be brief", "also polite", "from options"], result.SystemTexts);
        Assert.Equal(
            [new PromptTurn(PromptRole.User, "one"), new PromptTurn(PromptRole.Assistant, "two"), new PromptTurn(PromptRole.User, "three")],
            result.Turns);
        Assert.Equal(ResponseKind.Text, result.ResponseKind);
    }

    [Fact]
    public void Several_text_parts_of_one_message_are_joined_unchanged()
    {
        var message = new ChatMessage(ChatRole.User, [new TextContent("a  b"), new TextContent("c\n d")]);

        ValidatedRequest result = RequestValidator.Validate([message], null);

        Assert.Equal("a  b\nc\n d", Assert.Single(result.Turns).Text);
    }

    [Fact]
    public void Reasoning_content_in_history_is_dropped()
    {
        var message = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("hidden"), new TextContent("visible")]);

        ValidatedRequest result = RequestValidator.Validate([new(ChatRole.User, "q"), message], null);

        Assert.Equal("visible", result.Turns[1].Text);
    }

    [Fact]
    public void Invocable_tools_are_accepted_and_kept_in_order()
    {
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => 1, "one"), AIFunctionFactory.Create(() => 2, "two")] };

        ValidatedRequest request = RequestValidator.Validate(OneUserMessage(), options);

        Assert.True(request.HasTools);
        Assert.Equal(["one", "two"], request.Tools!.Select(t => t.Name));
    }

    [Fact]
    public void Empty_tools_list_is_accepted()
    {
        RequestValidator.Validate(OneUserMessage(), new ChatOptions { Tools = [] });
    }

    [Theory]
    [MemberData(nameof(RejectedToolModes))]
    public void Tool_modes_other_than_none_are_rejected(ChatToolMode mode)
    {
        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(OneUserMessage(), new ChatOptions { ToolMode = mode }));
    }

    public static IEnumerable<object[]> RejectedToolModes()
    {
        yield return [ChatToolMode.RequireAny];
        yield return [ChatToolMode.RequireSpecific("one")];
    }

    [Fact]
    public void Tool_mode_none_and_null_are_accepted()
    {
        RequestValidator.Validate(OneUserMessage(), new ChatOptions { ToolMode = ChatToolMode.None });
        RequestValidator.Validate(OneUserMessage(), new ChatOptions { ToolMode = null });
    }

    [Theory]
    [MemberData(nameof(UnsupportedContents))]
    public void Non_text_content_is_rejected(AIContent content)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, [new TextContent("q"), content]) };

        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(messages, null));
    }

    public static IEnumerable<object[]> UnsupportedContents()
    {
        yield return [new DataContent(new byte[] { 1, 2, 3 }, "application/pdf")];
        yield return [new UriContent("https://example.com/a.png", "image/png")];
        yield return [new FunctionCallContent("id", "fn", new Dictionary<string, object?>())];
        yield return [new FunctionResultContent("id", "result")];
    }

    [Fact]
    public void Tool_role_message_with_function_result_is_rejected_as_unsupported_content()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "q"), new(ChatRole.Tool, [new FunctionResultContent("id", "x")]) };

        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(messages, null));
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("developer")]
    public void Unknown_roles_are_rejected(string role)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "q"), new(new ChatRole(role), "text") };

        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(messages, null));
    }

    [Fact]
    public void Text_and_plain_json_formats_map_to_their_kinds()
    {
        Assert.Equal(ResponseKind.Text, RequestValidator.Validate(OneUserMessage(), new ChatOptions { ResponseFormat = ChatResponseFormat.Text }).ResponseKind);
        Assert.Equal(ResponseKind.Json, RequestValidator.Validate(OneUserMessage(), new ChatOptions { ResponseFormat = ChatResponseFormat.Json }).ResponseKind);
    }

    [Fact]
    public void Object_schema_is_accepted_and_carried_with_name_and_description()
    {
        JsonElement schema = Json("""{"type":"object","properties":{"a":{"type":"string"}},"required":["a"]}""");
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "Thing", "A thing.") };

        ValidatedRequest result = RequestValidator.Validate(OneUserMessage(), options);

        Assert.Equal(ResponseKind.JsonSchema, result.ResponseKind);
        Assert.Equal(schema.GetRawText(), result.SchemaText);
        Assert.Equal("Thing", result.SchemaName);
        Assert.Equal("A thing.", result.SchemaDescription);
    }

    [Theory]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"type":"array","items":{"type":"integer"}}""")]
    [InlineData("""{"properties":{"a":{"type":"string"}}}""")]
    [InlineData("""{"type":["object","null"]}""")]
    [InlineData("true")]
    [InlineData("[]")]
    public void Schemas_that_are_not_object_schemas_are_rejected(string schemaText)
    {
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Json(schemaText)) };

        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(OneUserMessage(), options));
    }

    [Fact]
    public void Undefined_schema_element_is_rejected()
    {
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(default(JsonElement)) };

        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(OneUserMessage(), options));
    }

    [Fact]
    public void Caller_options_are_not_modified()
    {
        var options = new ChatOptions { Instructions = "i", ModelId = "m" };

        RequestValidator.Validate(OneUserMessage(), options);

        Assert.Equal("i", options.Instructions);
        Assert.Equal("m", options.ModelId);
        Assert.Null(options.ResponseFormat);
    }
}
