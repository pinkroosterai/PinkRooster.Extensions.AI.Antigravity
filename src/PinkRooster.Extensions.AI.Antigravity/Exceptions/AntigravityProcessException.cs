namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>agy ended with a non-zero exit code, or without one.</summary>
public sealed class AntigravityProcessException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityProcessException(string message)
        : base(message)
    {
    }
}
