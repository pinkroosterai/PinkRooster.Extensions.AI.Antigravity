using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>What to hand the process runner for one agy call.</summary>
internal sealed record AgyInvocation(
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string?> EnvironmentOverrides,
    bool EffortRequested,
    bool EffortApplied);

/// <summary>
/// The only place that knows agy's command-line flags. Produces only flags verified on agy 1.3.5
/// (docs/specification.md F-02, F-34, F-36, F-37).
/// </summary>
internal static class InvocationBuilder
{
    /// <summary>The variable that tells agy where its home is (Linux; the only platform where isolation is applied).</summary>
    public const string HomeVariable = "HOME";

    /// <summary>True where an agy home is handed to each request (Linux only, the verified platform).</summary>
    public static bool IsolationApplies => OperatingSystem.IsLinux();

    private static readonly string[] LevelSuffixes = ["-low", "-medium", "-high", "-xhigh", "-max"];

    public static AgyInvocation Build(
        string prompt,
        string? modelId,
        ReasoningEffort? effort,
        string? schemaFilePath,
        TimeSpan timeout,
        IEnumerable<string> additionalArguments,
        IEnumerable<KeyValuePair<string, string?>> environmentVariables,
        bool streamJson = false,
        string? homeDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(additionalArguments);
        ArgumentNullException.ThrowIfNull(environmentVariables);

        var arguments = new List<string>
        {
            "--output-format", streamJson ? "stream-json" : "json",
            "--disable-slash-commands",
        };

        if (!string.IsNullOrEmpty(modelId))
        {
            arguments.Add("--model");
            arguments.Add(modelId);
        }

        string? effortFlag = EffortValue(effort);
        bool effortRequested = effortFlag is not null;
        bool effortApplied = effortRequested && !IsLevelQualified(modelId);
        if (effortApplied)
        {
            arguments.Add("--effort");
            arguments.Add(effortFlag!);
        }

        if (schemaFilePath is not null)
        {
            arguments.Add("--json-schema");
            arguments.Add(schemaFilePath);
        }

        // agy's own timeout is only a backstop; the library's timeout fires first.
        long backstopSeconds = (long)Math.Ceiling((timeout + TimeSpan.FromSeconds(10)).TotalSeconds);
        arguments.Add("--print-timeout");
        arguments.Add(FormattableString.Invariant($"{backstopSeconds}s"));

        arguments.AddRange(additionalArguments);

        arguments.Add("-p");
        arguments.Add(prompt);

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> pair in environmentVariables)
        {
            environment[pair.Key] = pair.Value;
        }

        if (homeDirectory is not null)
        {
            // The library's home wins over anything else; OptionsValidator keeps callers from setting it themselves.
            environment[HomeVariable] = homeDirectory;
        }

        return new AgyInvocation(arguments, environment, effortRequested, effortApplied);
    }

    // ContinuationToken and AllowBackgroundResponses are marked experimental (MEAI001) in Microsoft.Extensions.AI.Abstractions.
    // Only their presence is read here, to name them in a debug entry; nothing of them is used or exposed.
#pragma warning disable MEAI001
    /// <summary>The names of ChatOptions members the library ignores and that the caller set. Names only, never values.</summary>
    public static IReadOnlyList<string> IgnoredOptionNames(ChatOptions? options)
    {
        var names = new List<string>();
        if (options is null)
        {
            return names;
        }

        if (options.Temperature is not null) names.Add(nameof(ChatOptions.Temperature));
        if (options.TopP is not null) names.Add(nameof(ChatOptions.TopP));
        if (options.TopK is not null) names.Add(nameof(ChatOptions.TopK));
        if (options.Seed is not null) names.Add(nameof(ChatOptions.Seed));
        if (options.FrequencyPenalty is not null) names.Add(nameof(ChatOptions.FrequencyPenalty));
        if (options.PresencePenalty is not null) names.Add(nameof(ChatOptions.PresencePenalty));
        if (options.StopSequences is { Count: > 0 }) names.Add(nameof(ChatOptions.StopSequences));
        if (options.MaxOutputTokens is not null) names.Add(nameof(ChatOptions.MaxOutputTokens));
        if (options.ConversationId is not null) names.Add(nameof(ChatOptions.ConversationId));
        if (options.ContinuationToken is not null) names.Add(nameof(ChatOptions.ContinuationToken));
        if (options.AllowBackgroundResponses is not null) names.Add(nameof(ChatOptions.AllowBackgroundResponses));
        if (options.AllowMultipleToolCalls is not null) names.Add(nameof(ChatOptions.AllowMultipleToolCalls));
        if (options.RawRepresentationFactory is not null) names.Add(nameof(ChatOptions.RawRepresentationFactory));
        if (options.AdditionalProperties is { Count: > 0 }) names.Add(nameof(ChatOptions.AdditionalProperties));
        return names;
    }
#pragma warning restore MEAI001

    // ExtraHigh maps to agy's "xhigh". agy's "max" has no ReasoningEffort counterpart.
    private static string? EffortValue(ReasoningEffort? effort) => effort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.ExtraHigh => "xhigh",
        _ => null,
    };

    // agy exits 1 when --effort is combined with a model slug that already carries a level (F-37).
    private static bool IsLevelQualified(string? modelId) =>
        modelId is not null && LevelSuffixes.Any(s => modelId.EndsWith(s, StringComparison.OrdinalIgnoreCase));
}
