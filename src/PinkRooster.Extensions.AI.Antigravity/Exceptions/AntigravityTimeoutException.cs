namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>The request did not finish within the configured timeout.</summary>
public sealed class AntigravityTimeoutException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityTimeoutException(string message, TimeSpan timeout)
        : base(message)
    {
        Timeout = timeout;
    }

    /// <summary>The timeout that applied.</summary>
    public TimeSpan Timeout { get; }
}
