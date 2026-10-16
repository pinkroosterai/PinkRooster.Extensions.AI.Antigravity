namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>agy ended with a non-zero exit code, or without one.</summary>
public sealed class AntigravityProcessException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityProcessException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the exception that caused it.</summary>
    public AntigravityProcessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
