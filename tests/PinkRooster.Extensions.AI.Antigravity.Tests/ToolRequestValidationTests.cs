using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>What a request with tools may contain (spec: Edge cases).</summary>
public class ToolRequestValidationTests
{
    private static List<ChatMessage> User() => [new(ChatRole.User, "hi")];

    private static AIFunction Function(string name) => AIFunctionFactory.Create(() => 1, name);

#pragma warning disable MEAI001 // ApprovalRequiredAIFunction is experimental; the test builds one to prove it is refused.
    [Fact]
    public void An_approval_required_tool_is_refused()
    {
        var options = new ChatOptions { Tools = [new ApprovalRequiredAIFunction(Function("one"))] };

        var ex = Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(User(), options));

        Assert.Contains("approval", ex.Message);
    }
#pragma warning restore MEAI001

    [Fact]
    public void A_declaration_only_tool_is_refused()
    {
        AIFunctionDeclaration declaration = Function("one").AsDeclarationOnly();

        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(User(), new ChatOptions { Tools = [declaration] }));
    }

#pragma warning disable MEAI001 // HostedMcpServerTool is experimental; the test builds one to prove it is refused.
    [Fact]
    public void A_hosted_tool_is_refused()
    {
        Assert.Throws<NotSupportedException>(
            () => RequestValidator.Validate(User(), new ChatOptions { Tools = [new HostedMcpServerTool("docs", "https://example.com/mcp")] }));
    }
#pragma warning restore MEAI001

    [Fact]
    public void Tool_mode_require_specific_is_refused()
    {
        var options = new ChatOptions { Tools = [Function("one")], ToolMode = ChatToolMode.RequireSpecific("one") };

        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(User(), options));
    }

    [Fact]
    public void Tool_mode_auto_is_accepted()
    {
        var options = new ChatOptions { Tools = [Function("one")], ToolMode = ChatToolMode.Auto };

        Assert.True(RequestValidator.Validate(User(), options).HasTools);
    }

    [Fact]
    public void Tool_mode_none_runs_the_request_as_a_plain_request()
    {
        var options = new ChatOptions { Tools = [Function("one")], ToolMode = ChatToolMode.None };

        Assert.False(RequestValidator.Validate(User(), options).HasTools);
    }

    [Fact]
    public void Tools_with_a_json_response_format_are_refused()
    {
        var options = new ChatOptions { Tools = [Function("one")], ResponseFormat = ChatResponseFormat.Json };

        var ex = Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(User(), options));

        Assert.Contains("JSON", ex.Message);
    }

    [Fact]
    public void Two_tools_with_the_same_name_are_refused()
    {
        var options = new ChatOptions { Tools = [Function("same"), Function("same")] };

        Assert.Throws<ArgumentException>(() => RequestValidator.Validate(User(), options));
    }

    [Fact]
    public void Function_content_in_a_message_is_still_refused()
    {
        var messages = new List<ChatMessage> { new(ChatRole.Assistant, [new FunctionCallContent("1", "one")]), new(ChatRole.User, "hi") };

        Assert.Throws<NotSupportedException>(() => RequestValidator.Validate(messages, null));
    }

    [Fact]
    public void MaxToolCalls_must_be_at_least_one()
    {
        Assert.Throws<ArgumentException>(() => new AntigravityChatClient(new AntigravityChatClientOptions { MaxToolCalls = 0 }));
        Assert.Equal(20, new AntigravityChatClientOptions().MaxToolCalls);
    }
}
