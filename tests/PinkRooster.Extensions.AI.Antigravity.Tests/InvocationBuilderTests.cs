using Microsoft.Extensions.AI;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class InvocationBuilderTests
{
    private static AgyInvocation Build(
        string prompt = "PROMPT",
        string? model = null,
        ReasoningEffort? effort = null,
        string? schema = null,
        TimeSpan? timeout = null,
        string[]? extra = null,
        Dictionary<string, string?>? env = null) =>
        InvocationBuilder.Build(prompt, model, effort, schema, timeout ?? TimeSpan.FromMinutes(5), extra ?? [], env ?? []);

    [Fact]
    public void Minimal_call_has_the_fixed_flags_and_the_prompt_last()
    {
        AgyInvocation call = Build();

        Assert.Equal(
            ["--output-format", "json", "--disable-slash-commands", "--print-timeout", "310s", "-p", "PROMPT"],
            call.Arguments);
    }

    [Fact]
    public void Prompt_is_a_single_argument_whatever_it_contains()
    {
        string prompt = "line one\n--model evil \"quoted\" $(rm -rf /) ; & | `x` \U0001F600";

        AgyInvocation call = Build(prompt);

        Assert.Equal(prompt, call.Arguments[^1]);
        Assert.Equal("-p", call.Arguments[^2]);
        Assert.Single(call.Arguments, a => a == prompt);
    }

    [Fact]
    public void Model_is_passed_when_set()
    {
        AgyInvocation call = Build(model: "gemini-3.8-flash-low");

        int index = call.Arguments.ToList().IndexOf("--model");
        Assert.True(index >= 0);
        Assert.Equal("gemini-3.8-flash-low", call.Arguments[index + 1]);
    }

    [Fact]
    public void No_model_flag_when_no_model_is_set()
    {
        Assert.DoesNotContain("--model", Build().Arguments);
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.ExtraHigh, "xhigh")]
    public void Effort_is_mapped_for_a_base_model(ReasoningEffort effort, string expected)
    {
        AgyInvocation call = Build(model: "gemini-3.8-flash", effort: effort);

        int index = call.Arguments.ToList().IndexOf("--effort");
        Assert.True(index >= 0);
        Assert.Equal(expected, call.Arguments[index + 1]);
        Assert.True(call.EffortRequested);
        Assert.True(call.EffortApplied);
    }

    [Fact]
    public void Effort_is_passed_when_no_model_is_set()
    {
        Assert.Contains("--effort", Build(effort: ReasoningEffort.Low).Arguments);
    }

    [Theory]
    [InlineData("gemini-3.8-flash-low")]
    [InlineData("gemini-3.8-flash-medium")]
    [InlineData("gemini-3.8-flash-high")]
    [InlineData("some-model-xhigh")]
    [InlineData("some-model-max")]
    [InlineData("Gemini-3.8-Flash-LOW")]
    public void Effort_is_ignored_for_level_qualified_models(string model)
    {
        AgyInvocation call = Build(model: model, effort: ReasoningEffort.High);

        Assert.DoesNotContain("--effort", call.Arguments);
        Assert.True(call.EffortRequested);
        Assert.False(call.EffortApplied);
    }

    [Fact]
    public void Effort_none_and_null_pass_nothing()
    {
        Assert.DoesNotContain("--effort", Build(effort: ReasoningEffort.None).Arguments);
        Assert.DoesNotContain("--effort", Build(effort: null).Arguments);
        Assert.False(Build(effort: ReasoningEffort.None).EffortRequested);
    }

    [Fact]
    public void Schema_is_passed_by_file_path()
    {
        AgyInvocation call = Build(schema: "/tmp/x/schema.json");

        int index = call.Arguments.ToList().IndexOf("--json-schema");
        Assert.True(index >= 0);
        Assert.Equal("/tmp/x/schema.json", call.Arguments[index + 1]);
    }

    [Theory]
    [InlineData(1, "11s")]
    [InlineData(300, "310s")]
    [InlineData(300.2, "311s")]
    public void Print_timeout_is_the_library_timeout_plus_ten_seconds_rounded_up(double seconds, string expected)
    {
        AgyInvocation call = Build(timeout: TimeSpan.FromSeconds(seconds));

        int index = call.Arguments.ToList().IndexOf("--print-timeout");
        Assert.Equal(expected, call.Arguments[index + 1]);
    }

    [Fact]
    public void Additional_arguments_come_before_the_prompt()
    {
        AgyInvocation call = Build(extra: ["--log-file", "agy.log"]);

        List<string> args = call.Arguments.ToList();
        Assert.True(args.IndexOf("--log-file") < args.IndexOf("-p"));
        Assert.Equal("agy.log", args[args.IndexOf("--log-file") + 1]);
    }

    [Theory]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("--add-dir")]
    [InlineData("--continue")]
    [InlineData("-c")]
    [InlineData("--conversation")]
    [InlineData("--mode")]
    [InlineData("--project")]
    [InlineData("--new-project")]
    [InlineData("-i")]
    [InlineData("--prompt-interactive")]
    [InlineData("--input-format")]
    [InlineData("--sandbox")]
    [InlineData("--agent")]
    public void The_library_itself_never_produces_the_flags_it_forbids(string flag)
    {
        AgyInvocation call = Build(
            prompt: "p", model: "gemini-3.8-flash", effort: ReasoningEffort.High, schema: "/s.json",
            timeout: TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(flag, call.Arguments);
    }

    [Fact]
    public void Slash_commands_are_always_switched_off()
    {
        Assert.Contains("--disable-slash-commands", Build().Arguments);
    }

    [Fact]
    public void Output_format_is_always_json_and_never_built_from_input()
    {
        AgyInvocation call = Build(prompt: "text", model: "m");

        int index = call.Arguments.ToList().IndexOf("--output-format");
        Assert.Equal("json", call.Arguments[index + 1]);
        Assert.Single(call.Arguments, a => a == "--output-format");
    }

    [Fact]
    public void Environment_overrides_are_copied_and_nothing_is_added()
    {
        var env = new Dictionary<string, string?> { ["A"] = "1", ["REMOVE"] = null };

        AgyInvocation call = Build(env: env);
        env["A"] = "changed";

        Assert.Equal(2, call.EnvironmentOverrides.Count);
        Assert.Equal("1", call.EnvironmentOverrides["A"]);
        Assert.Null(call.EnvironmentOverrides["REMOVE"]);
    }

    [Fact]
    public void Ignored_options_are_listed_by_name_only()
    {
        var options = new ChatOptions
        {
            Temperature = 0.2f,
            TopP = 0.9f,
            TopK = 3,
            Seed = 7,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.1f,
            StopSequences = ["x"],
            MaxOutputTokens = 10,
            ConversationId = "SECRET-ID",
            AllowMultipleToolCalls = true,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["k"] = "SECRET-VALUE" },
        };

        IReadOnlyList<string> names = InvocationBuilder.IgnoredOptionNames(options);

        Assert.Equal(
            ["Temperature", "TopP", "TopK", "Seed", "FrequencyPenalty", "PresencePenalty", "StopSequences", "MaxOutputTokens",
             "ConversationId", "AllowMultipleToolCalls", "AdditionalProperties"],
            names);
        Assert.DoesNotContain(names, n => n.Contains("SECRET"));
    }

    [Fact]
    public void Honored_options_and_defaults_are_not_listed_as_ignored()
    {
        Assert.Empty(InvocationBuilder.IgnoredOptionNames(null));
        Assert.Empty(InvocationBuilder.IgnoredOptionNames(new ChatOptions { ModelId = "m", Instructions = "i", ResponseFormat = ChatResponseFormat.Json }));
    }

    [Fact]
    public void Ignored_options_never_change_the_command_line()
    {
        AgyInvocation plain = Build();
        AgyInvocation again = Build();

        Assert.Equal(plain.Arguments, again.Arguments);
    }
}
