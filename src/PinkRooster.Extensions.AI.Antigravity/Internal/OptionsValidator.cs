namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Checks <see cref="AntigravityChatClientOptions"/> once, when a client is created.</summary>
internal static class OptionsValidator
{
    // Flag names (without leading dashes) that a caller may not pass through AdditionalArguments:
    // the ones the library sets itself and the ones that break safety or statelessness.
    private static readonly HashSet<string> ForbiddenFlags = new(StringComparer.Ordinal)
    {
        "p", "print", "prompt",
        "output-format", "input-format", "json-schema", "print-timeout",
        "model", "effort", "disable-slash-commands",
        "dangerously-skip-permissions", "add-dir",
        "c", "continue", "conversation",
        "mode", "project", "new-project",
        "i", "prompt-interactive",
        "agent",
    };

    public static void Validate(AntigravityChatClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentException("Timeout must be at least 1 second.", nameof(options));
        }

        if (options.MaxConcurrency < 1)
        {
            throw new ArgumentException("MaxConcurrency must be at least 1.", nameof(options));
        }

        if (options.MaxPromptLength < 1)
        {
            throw new ArgumentException("MaxPromptLength must be at least 1.", nameof(options));
        }

        if (options.MaxOutputBytes < 1)
        {
            throw new ArgumentException("MaxOutputBytes must be at least 1.", nameof(options));
        }

        if (options.MaxToolCalls < 1)
        {
            throw new ArgumentException("MaxToolCalls must be at least 1.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.SystemPreamble))
        {
            throw new ArgumentException("SystemPreamble must not be empty.", nameof(options));
        }

        if (options.EnvironmentVariables is null)
        {
            throw new ArgumentException("EnvironmentVariables must not be null.", nameof(options));
        }

        foreach (string name in options.EnvironmentVariables.Keys)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0'))
            {
                throw new ArgumentException("EnvironmentVariables contains an invalid variable name.", nameof(options));
            }

            if (options.IsolateAgyHome && InvocationBuilder.IsolationApplies && name == InvocationBuilder.HomeVariable)
            {
                throw new ArgumentException(
                    $"EnvironmentVariables may not set {InvocationBuilder.HomeVariable} while IsolateAgyHome is true: the library gives each request its own home.",
                    nameof(options));
            }
        }

        if (options.AdditionalArguments is null)
        {
            throw new ArgumentException("AdditionalArguments must not be null.", nameof(options));
        }

        foreach (string? argument in options.AdditionalArguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("AdditionalArguments must not contain null.", nameof(options));
            }

            string? flag = FlagName(argument);
            if (flag is not null && ForbiddenFlags.Contains(flag))
            {
                throw new ArgumentException($"AdditionalArguments may not contain the flag '{flag}'.", nameof(options));
            }
        }
    }

    // Returns the flag name of "-x", "--x" or "--x=value", or null when the argument is not a flag.
    private static string? FlagName(string argument)
    {
        if (argument.Length < 2 || argument[0] != '-')
        {
            return null;
        }

        string name = argument.TrimStart('-');
        int equals = name.IndexOf('=');
        if (equals >= 0)
        {
            name = name[..equals];
        }

        return name.Length == 0 ? null : name;
    }
}
