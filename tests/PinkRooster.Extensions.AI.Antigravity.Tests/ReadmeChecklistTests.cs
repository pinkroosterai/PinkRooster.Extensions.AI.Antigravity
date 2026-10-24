using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>
/// FR-037 and the completeness rules of the specification, checked against README.md: what the library states about itself must
/// stay in step with what it has.
/// </summary>
public class ReadmeChecklistTests
{
    private static readonly Lazy<string> Readme = new(() =>
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "README.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory, "README.md"));
    });

    private static void AssertMentions(params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            Assert.True(Readme.Value.Contains(fragment, StringComparison.OrdinalIgnoreCase), $"README.md does not mention: {fragment}");
        }
    }

    [Fact]
    public void It_states_the_tested_agy_versions_and_the_minimum()
    {
        AssertMentions("1.3.5 or newer", "1.4.0 and newer", "older than 1.3.5", "Compatibility");
    }

    [Fact]
    public void It_states_the_usage_terms_position_and_that_the_user_is_responsible()
    {
        AssertMentions(
            "not supported under consumer plans", "Additional Terms", "You are responsible", "API key or Enterprise", "does not check which plan");
    }

    [Fact]
    public void It_states_the_side_effect_residue()
    {
        AssertMentions(
            "MCP servers", "stores a transcript", "full prompt and answer", "process list", "settings can widen permissions",
            "`--sandbox` did not stop", "Writes outside the request directory are not blocked");
    }

    [Fact]
    public void It_states_the_token_overhead_and_that_output_limits_are_not_honored()
    {
        AssertMentions("17,000 input tokens", "MaxOutputTokens", "is not honored");
    }

    [Fact]
    public void Every_public_exception_type_has_a_row_in_the_error_table()
    {
        foreach (Type type in typeof(AntigravityException).Assembly.GetExportedTypes().Where(t => typeof(Exception).IsAssignableFrom(t) && t != typeof(AntigravityException)))
        {
            AssertMentions($"`{type.Name}`");
        }
    }

    [Fact]
    public void Every_ChatOptions_member_is_named_with_its_behavior()
    {
        foreach (string name in typeof(ChatOptions).GetProperties().Select(p => p.Name))
        {
            AssertMentions($"`{name}`");
        }
    }

    [Fact]
    public void Every_option_of_the_client_is_documented()
    {
        foreach (string name in typeof(AntigravityChatClientOptions).GetProperties().Select(p => p.Name))
        {
            AssertMentions($"`{name}`");
        }
    }

    [Fact]
    public void It_documents_the_effort_rule_structured_output_limits_and_what_is_not_supported()
    {
        AssertMentions(
            "ExtraHigh", "xhigh", "is **not passed**", "annotation only", "no retry", "one** update", "Not supported in 0.1.0", "Draft 2020-12");
    }

    [Fact]
    public void Its_code_samples_use_names_that_exist()
    {
        AssertMentions("AntigravityChatClient", "AntigravityChatClientOptions", "AddAntigravityChatClient", "GetResponseAsync<T>");
        Assert.NotNull(typeof(AntigravityServiceCollectionExtensions).GetMethod(nameof(AntigravityServiceCollectionExtensions.AddAntigravityChatClient)));
    }
}
