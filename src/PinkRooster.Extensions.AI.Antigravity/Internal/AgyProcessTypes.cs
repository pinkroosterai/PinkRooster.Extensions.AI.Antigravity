namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Starts one agy process and reports how it ended. The only seam between the library and the operating system.</summary>
internal interface IAgyProcessRunner
{
    /// <summary>
    /// Runs the process. Timeout and output-limit overflow are reported in the result; cancellation throws
    /// <see cref="OperationCanceledException"/> after the process tree has been ended.
    /// </summary>
    Task<AgyProcessResult> RunAsync(AgyProcessRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the process like <see cref="RunAsync"/> but hands each line of standard output to <paramref name="onStandardOutputLine"/>
    /// as it is read, from a reader thread, and does not keep standard output: <see cref="AgyProcessResult.StandardOutput"/> is empty.
    /// The line count toward <see cref="AgyProcessRequest.MaxOutputBytes"/> like buffered output does.
    /// </summary>
    Task<AgyProcessResult> RunStreamingAsync(
        AgyProcessRequest request,
        Action<string> onStandardOutputLine,
        CancellationToken cancellationToken);
}

/// <summary>What to start.</summary>
internal sealed record AgyProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> EnvironmentOverrides,
    TimeSpan Timeout,
    int MaxOutputBytes);

/// <summary>How the process ended.</summary>
internal sealed record AgyProcessResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool OutputLimitExceeded,
    TimeSpan Elapsed);
