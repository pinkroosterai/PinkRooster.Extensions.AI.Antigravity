namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>Base type of every failure the library raises on its own account.</summary>
public class AntigravityException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public AntigravityException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public AntigravityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// What agy wrote to standard error, truncated to 4 KB, when the failure came from a run. Not part of the message;
    /// it may contain text from the caller's prompt, so do not log it unreviewed.
    /// </summary>
    public string? StandardError { get; init; }

    /// <summary>The exit code of the agy process, when there was one.</summary>
    public int? ExitCode { get; init; }
}
