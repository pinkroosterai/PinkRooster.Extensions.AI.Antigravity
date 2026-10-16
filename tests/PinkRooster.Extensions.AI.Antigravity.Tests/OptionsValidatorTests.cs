using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class OptionsValidatorTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        OptionsValidator.Validate(new AntigravityChatClientOptions());
    }

    [Fact]
    public void Defaults_match_the_specification()
    {
        var options = new AntigravityChatClientOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
        Assert.Equal(4, options.MaxConcurrency);
        Assert.Equal(OperatingSystem.IsWindows() ? 30_000 : 100_000, options.MaxPromptLength);
        Assert.Equal(16 * 1024 * 1024, options.MaxOutputBytes);
        Assert.False(options.SkipVersionCheck);
        Assert.Null(options.ExecutablePath);
        Assert.Empty(options.EnvironmentVariables);
        Assert.Empty(options.AdditionalArguments);
        Assert.Equal(AntigravityChatClientOptions.DefaultSystemPreamble, options.SystemPreamble);
    }

    [Fact]
    public void Null_options_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => OptionsValidator.Validate(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(-1)]
    public void Timeout_below_one_second_is_rejected(int milliseconds)
    {
        var options = new AntigravityChatClientOptions { Timeout = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(options));
    }

    [Fact]
    public void Timeout_of_one_second_is_accepted()
    {
        OptionsValidator.Validate(new AntigravityChatClientOptions { Timeout = TimeSpan.FromSeconds(1) });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Concurrency_below_one_is_rejected(int value)
    {
        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(new AntigravityChatClientOptions { MaxConcurrency = value }));
    }

    [Fact]
    public void Non_positive_prompt_length_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(new AntigravityChatClientOptions { MaxPromptLength = 0 }));
    }

    [Fact]
    public void Non_positive_output_limit_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(new AntigravityChatClientOptions { MaxOutputBytes = 0 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_preamble_is_rejected(string? preamble)
    {
        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(new AntigravityChatClientOptions { SystemPreamble = preamble! }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A=B")]
    [InlineData("A\0B")]
    public void Invalid_environment_variable_names_are_rejected(string name)
    {
        var options = new AntigravityChatClientOptions();
        options.EnvironmentVariables[name] = "x";

        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(options));
    }

    [Fact]
    public void Environment_variable_with_null_value_is_accepted()
    {
        var options = new AntigravityChatClientOptions();
        options.EnvironmentVariables["REMOVE_ME"] = null;

        OptionsValidator.Validate(options);
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--print")]
    [InlineData("--prompt")]
    [InlineData("--output-format")]
    [InlineData("--output-format=text")]
    [InlineData("-output-format")]
    [InlineData("--input-format")]
    [InlineData("--json-schema")]
    [InlineData("--print-timeout")]
    [InlineData("--model")]
    [InlineData("--model=gemini-3.8-flash-low")]
    [InlineData("--effort")]
    [InlineData("--disable-slash-commands")]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("--add-dir")]
    [InlineData("-c")]
    [InlineData("--continue")]
    [InlineData("--conversation")]
    [InlineData("--conversation=abc")]
    [InlineData("--mode")]
    [InlineData("--project")]
    [InlineData("--new-project")]
    [InlineData("-i")]
    [InlineData("--prompt-interactive")]
    [InlineData("--agent")]
    public void Forbidden_flags_in_additional_arguments_are_rejected(string flag)
    {
        var options = new AntigravityChatClientOptions();
        options.AdditionalArguments.Add(flag);

        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(options));
    }

    [Fact]
    public void Forbidden_flag_after_an_allowed_one_is_found()
    {
        var options = new AntigravityChatClientOptions();
        options.AdditionalArguments.Add("--log-file");
        options.AdditionalArguments.Add("agy.log");
        options.AdditionalArguments.Add("--dangerously-skip-permissions");

        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(options));
    }

    [Fact]
    public void Allowed_flags_and_values_are_accepted()
    {
        var options = new AntigravityChatClientOptions();
        options.AdditionalArguments.Add("--log-file");
        options.AdditionalArguments.Add("agy.log");
        options.AdditionalArguments.Add("--sandbox");

        OptionsValidator.Validate(options);
    }

    [Fact]
    public void Null_additional_argument_is_rejected()
    {
        var options = new AntigravityChatClientOptions();
        options.AdditionalArguments.Add(null!);

        Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(options));
    }
}
