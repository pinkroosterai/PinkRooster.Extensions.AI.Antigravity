namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// Decides from a finished process whether the run succeeded and, if not, which exception applies
/// (docs/software-design.md section 4.4, rules 1 to 7 and 10; the structured-output rules run in the structured-result checker).
/// </summary>
internal static class ErrorClassifier
{
    private const int MessageDetailLength = 200;
    private const int StandardErrorKeepLength = 4096;
    private const string AgyTimeoutNotice = "print timeout after";

    /// <summary>Returns the envelope of a successful run, or throws the exception that describes the failure.</summary>
    public static AgyEnvelope Classify(AgyProcessResult result, TimeSpan configuredTimeout)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? stderr = Truncate(result.StandardError, StandardErrorKeepLength);

        // 1. The output limit was hit.
        if (result.OutputLimitExceeded)
        {
            throw new AntigravityOutputException("agy wrote more output than the configured limit (MaxOutputBytes); the run was ended.")
            {
                StandardError = stderr,
            };
        }

        // 2. Timeout: ours, or agy's own notice (which comes with exit code 0 and an empty answer).
        if (result.TimedOut || result.StandardError.Contains(AgyTimeoutNotice, StringComparison.OrdinalIgnoreCase))
        {
            throw new AntigravityTimeoutException(
                $"The agy request did not finish within {FormatDuration(configuredTimeout)}.",
                configuredTimeout)
            {
                StandardError = stderr,
            };
        }

        bool parsed = EnvelopeParser.TryParse(result.StandardOutput, out AgyEnvelope? envelope);
        bool failed = result.ExitCode != 0 || !parsed || !envelope!.IsSuccess;

        // 3. Not signed in.
        if (failed && AuthFailureDetector.Matches(result.StandardError, parsed ? envelope!.Error : null))
        {
            throw new AntigravityNotAuthenticatedException(
                "agy is not signed in. Start 'agy' once interactively to sign in, then try again.")
            {
                StandardError = stderr,
                ExitCode = result.ExitCode,
            };
        }

        // 4. Non-zero exit code, or none.
        if (result.ExitCode != 0)
        {
            string detail = FirstLine(parsed ? envelope!.Error : null) ?? FirstLine(result.StandardError) ?? "no further detail";
            string code = result.ExitCode is int exit ? $"code {exit}" : "no exit code";
            throw new AntigravityProcessException($"agy ended with {code}: {Truncate(detail, MessageDetailLength)}")
            {
                StandardError = stderr,
                ExitCode = result.ExitCode,
            };
        }

        // 5. Output that is not an envelope.
        if (!parsed)
        {
            throw new AntigravityOutputException("agy's output could not be read as the expected JSON envelope.")
            {
                StandardError = stderr,
                ExitCode = result.ExitCode,
            };
        }

        // 6. Exit code 0 is not success: the envelope status decides.
        if (!envelope!.IsSuccess)
        {
            string detail = FirstLine(envelope.Error) ?? "no further detail";
            throw new AntigravityRunException($"agy finished with status {envelope.Status}: {Truncate(detail, MessageDetailLength)}", envelope.DeniedActionKinds)
            {
                StandardError = stderr,
                ExitCode = result.ExitCode,
            };
        }

        // 7. An empty answer because agy denied an action.
        if (string.IsNullOrEmpty(envelope.Response) && envelope.StructuredOutput is null && envelope.DeniedActionKinds.Count > 0)
        {
            throw new AntigravityRunException(
                "agy produced no answer because it denied an action: " + string.Join(", ", envelope.DeniedActionKinds) + ".",
                envelope.DeniedActionKinds)
            {
                StandardError = stderr,
                ExitCode = result.ExitCode,
            };
        }

        // 10. Success.
        return envelope;
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalSeconds < 120 ? $"{span.TotalSeconds:0.#} seconds" : $"{span.TotalMinutes:0.#} minutes";

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        int end = trimmed.IndexOfAny(['\r', '\n']);
        return end < 0 ? trimmed : trimmed[..end];
    }

    private static string? Truncate(string? text, int length) =>
        text is null || text.Length <= length ? text : text[..length];
}
